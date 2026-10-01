using SoftMochiPet.Core;
using SoftMochiPet.Interop;
using SoftMochiPet.Services;
using DrawingPoint = System.Drawing.Point;

namespace SoftMochiPet;

public partial class MainWindow
{
    private readonly BehaviorControlSession? _behaviorSession;
    private (BehaviorControlAction Action, string Character)? _pendingBehavior;
    private BehaviorControlAction? _activeBehavior;
    private bool _behaviorAutonomyEnabled;
    private bool _behaviorHatOnly;
    private bool _behaviorRequestFailed;
    public bool IsBehaviorControlMode => _behaviorSession is not null;

    private void InitializeBehaviorControl()
    {
        if (!IsBehaviorControlMode) return;
        _trayIcon.BehaviorControlRequested += QueueBehaviorControl;
        _trayIcon.BehaviorControlStopRequested += () => QueueBehaviorControl(BehaviorControlAction.Stop);
        _trayIcon.BehaviorAutonomyChanged += enabled =>
        {
            _behaviorAutonomyEnabled = enabled;
            ScheduleNextAutonomousDecision(4, 4);
            SetBehaviorControlStatus(enabled ? "自主日常已开启" : "自主日常已关闭，可直接点选行为");
        };
        SetBehaviorControlStatus("右键选择行为 · 自主日常默认关闭");
    }

    private void QueueBehaviorControl(BehaviorControlAction action)
    {
        if (!IsBehaviorControlMode || _isClosing) return;
        _pendingBehavior = (action, _character.Id);
        SetBehaviorControlStatus("关菜单后执行：" + BehaviorControlCatalog.Get(action).Title);
    }

    private void SetBehaviorControlStatus(string text)
    {
        if (IsBehaviorControlMode) _trayIcon.SetBehaviorControlStatus(text);
    }

    private WindowSurface? BehaviorSupport()
    {
        var foot = PrankFoot();
        return _surfaceProvider.Surfaces.FirstOrDefault(surface =>
            surface.SourceHandle == _supportHandle && surface.IsDesktopFloor == _supportIsDesktopFloor &&
            surface.ContainsX((int)Math.Round(foot.X)) && Math.Abs(surface.Top - foot.Y) < 20);
    }

    private string? BehaviorUnavailableReason(BehaviorControlAction action)
    {
        var item = BehaviorControlCatalog.Get(action);
        if (!item.Supports(_character)) return "当前角色没有这个行为";
        if (action == BehaviorControlAction.Stop) return null;
        if (_sessionLocked || _isClosing) return "当前会话不可用";
        if (_isDragging) return "先松开桌宠";
        if (action == BehaviorControlAction.ReturnWindows)
            return !HasHeldPrank ? "帽子里没有窗口" : _manualRestoreInProgress ? "正在归还窗口" : null;
        if (_prankPhase != PrankPhase.None || _prankAwaitingSystem) return "先停止当前捣蛋，或等她收好帽子";
        if (_animator.Hatless) return "等她捡回帽子";
        if (_settings.QuietMode && action is not (BehaviorControlAction.Stand or BehaviorControlAction.Sleep or BehaviorControlAction.Wake))
            return "请先关闭安静模式";
        var needs = item.Requirements;
        if (needs.HasFlag(BehaviorControlRequirements.Awake) && _state is PetState.Sleeping or PetState.Waking)
            return "先叫醒她";
        if (needs.HasFlag(BehaviorControlRequirements.Sleeping) && _state != PetState.Sleeping)
            return "她现在没有睡觉";
        if (needs.HasFlag(BehaviorControlRequirements.EmptyHat) && HasHeldPrank)
            return "帽子已有窗口，请先归还";
        if (needs.HasFlag(BehaviorControlRequirements.OutsideEnclosure) && _enclosureHandle != IntPtr.Zero)
            return "先把她拖到窗口外";
        if (needs.HasFlag(BehaviorControlRequirements.FoodEnabled) && _settings.FastingMode)
            return "请先关闭辟谷模式";
        if (needs.HasFlag(BehaviorControlRequirements.GroundSupport) && BehaviorSupport() is null)
            return "先让她站稳";
        if (needs.HasFlag(BehaviorControlRequirements.WindowPlatform) && _supportIsDesktopFloor)
            return "需要先站在真实窗口顶边";
        return null;
    }

    private void RefreshBehaviorControlAvailability()
    {
        if (!IsBehaviorControlMode) return;
        _trayIcon.SetBehaviorControlAvailability(BehaviorControlCatalog.ForCharacter(_character)
            .ToDictionary(item => item.Action, item => BehaviorUnavailableReason(item.Action) is null));
    }

    private void ExecutePendingBehavior()
    {
        if (!IsBehaviorControlMode || _trayIcon.IsMenuOpen || _pendingBehavior is not { } request) return;
        _pendingBehavior = null;
        if (request.Character != _character.Id || _pendingCharacter is not null || _isClosing) return;
        var action = request.Action;
        if (BehaviorUnavailableReason(action) is { } unavailable)
        {
            RejectBehavior(unavailable);
            return;
        }
        _behaviorRequestFailed = false;
        try
        {
            if (action == BehaviorControlAction.Stop)
            {
                StopBehaviorControl();
                return;
            }
            if (action == BehaviorControlAction.ReturnWindows)
            {
                _activeBehavior = action;
                RequestWindowReturn();
                SetBehaviorControlStatus("正在归还原窗口");
                return;
            }
            // Commands replace ordinary movement, never another native-window
            // operation. The latter requires the explicit stop/return entries.
            if (action != BehaviorControlAction.Wake) ResetOrdinaryBehavior();
            _activeBehavior = action;
            var started = StartControlledBehavior(action);
            if (!started)
            {
                _activeBehavior = null;
                if (!_behaviorRequestFailed) RejectBehavior("暂时无法执行，请检查支撑面或目标");
                return;
            }
            if (!_behaviorRequestFailed)
                SetBehaviorControlStatus("执行：" + BehaviorControlCatalog.Get(action).Title);
            DiagnosticsLog.WriteEvent("BehaviorControlStarted", ("Action", action), ("Character", _character.Id),
                ("State", _state), ("Animation", _animator.CurrentClip));
        }
        catch (Exception exception)
        {
            CancelWindowPrank("BehaviorControlStop", false, false);
            ClearFoodInteractions();
            _activeBehavior = null;
            ReturnToIdle();
            RejectBehavior("行为未完成，请查看日志");
            DiagnosticsLog.WriteEvent("BehaviorControlFailed", ("Action", action), ("Reason", exception.GetType().Name));
        }
    }

    private bool StartControlledBehavior(BehaviorControlAction action)
    {
        switch (action)
        {
            case BehaviorControlAction.Stand: return true;
            case BehaviorControlAction.Walk: return StartGroundMove(MovementPurpose.Wander);
            case BehaviorControlAction.Run:
                if (!StartGroundMove(MovementPurpose.Wander)) return false;
                _animator.Play("run");
                return true;
            case BehaviorControlAction.Explore: return StartGroundMove(MovementPurpose.Explore);
            case BehaviorControlAction.Patrol: return StartContinuousPatrol(preferClimb: false);
            case BehaviorControlAction.Curious: BeginCurious(false); return true;
            case BehaviorControlAction.Hop: return BeginTerrainHop();
            case BehaviorControlAction.Roll: return BeginRoll();
            case BehaviorControlAction.Climb: return StartControlledClimb();
            case BehaviorControlAction.SlideDown: return StartControlledEdge(slide: true);
            case BehaviorControlAction.JumpDown: return StartControlledEdge(slide: false);
            case BehaviorControlAction.Sleep: BeginSleep(); return _state == PetState.Sleeping;
            case BehaviorControlAction.Wake: BeginWake(); return true;
            case BehaviorControlAction.FreeFall: return StartControlledFall(toss: false);
            case BehaviorControlAction.Toss: return StartControlledFall(toss: true);
            case BehaviorControlAction.Hungry:
                _lifeState.Hunger = 100;
                _lifeState.Fullness = 0;
                BeginCurious(false, hungryAppeal: true);
                SaveLifeState();
                UpdateHungerDisplay(force: true);
                return true;
            case BehaviorControlAction.LickIcon:
                if (TryBeginIconLick(manualRequest: true)) return true;
                RejectBehavior("没有可见桌面图标，请露出桌面后重试");
                return false;
            case BehaviorControlAction.EatIcon: return StartControlledIconMeal();
            case BehaviorControlAction.Satisfied:
                _state = PetState.Satisfied;
                _stateElapsed = 0;
                _animator.Play("satisfied");
                return true;
            case BehaviorControlAction.Kick: return StartControlledPrank(PrankKind.Kick);
            case BehaviorControlAction.Punch: return StartControlledPrank(PrankKind.Punch);
            case BehaviorControlAction.Charge: return StartControlledPrank(PrankKind.Charge);
            case BehaviorControlAction.HatStore: return StartControlledPrank(PrankKind.Hat);
            case BehaviorControlAction.Shatter: return StartControlledPrank(PrankKind.Shatter);
            case BehaviorControlAction.Tear: return StartControlledPrank(PrankKind.Tear);
            case BehaviorControlAction.Tease:
                UpdateMischief(0);
                if (!_mischief.Tease()) { RejectBehavior("逗弄或大招仍在冷却"); return false; }
                PlayMischiefVoice(MischiefCue.Teased);
                SaveMischiefState();
                return true;
            case BehaviorControlAction.FillMischief:
                if (!_mischief.FillForDirectControl()) return false;
                SaveMischiefState();
                return true;
            case BehaviorControlAction.ThrowAndRecoverHat: return StartControlledHat();
            default: return false;
        }
    }

    private void ResetOrdinaryBehavior()
    {
        _activeBehavior = null;
        ClearFoodInteractions();
        _velocityX = _velocityY = 0;
        _movementPixelRemainderX = _movementPixelRemainderY = 0;
        _wakeForMeal = _wakeForManualLick = false;
        ReturnToIdle();
    }

    private void StopBehaviorControl()
    {
        _pendingBehavior = null;
        _activeBehavior = null;
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();
        }
        CancelWindowPrank("BehaviorControlStop", false, false);
        ResetOrdinaryBehavior();
        if (!TryResolveGroundSupport(false)) BeginFall(reason: "BehaviorControlStop");
        _voice.Stop();
        SetBehaviorControlStatus(HasHeldPrank ? "已停止 · 帽中窗口仍保留，可右键归还" : "已停止，等待选择行为");
        DiagnosticsLog.WriteEvent("BehaviorControlStopped", ("HatOccupied", HasHeldPrank));
    }

    private void RejectBehavior(string reason)
    {
        _behaviorRequestFailed = true;
        SetBehaviorControlStatus(reason);
        DiagnosticsLog.WriteEvent("BehaviorControlUnavailable", ("Reason", reason));
    }

    private void UpdateBehaviorControlCompletion()
    {
        if (!IsBehaviorControlMode || _activeBehavior is not { } action || _state != PetState.Idle || _prankPhase != PrankPhase.None) return;
        _activeBehavior = null;
        if (_behaviorRequestFailed) return;
        SetBehaviorControlStatus("已完成：" + BehaviorControlCatalog.Get(action).Title + (HasHeldPrank ? " · 窗口留在帽中" : ""));
        DiagnosticsLog.WriteEvent("BehaviorControlCompleted", ("Action", action), ("HatOccupied", HasHeldPrank));
    }

    private bool StartControlledClimb()
    {
        RefreshSurfaceMap(force: true);
        var surface = BehaviorSupport();
        if (surface is null) return false;
        var foot = PrankFoot();
        var plan = WindowClimbPlanner.Choose(_surfaceProvider.Surfaces, surface, (int)Math.Round(foot.X), PetPhysicalScale(), 0);
        if (plan is null) { RejectBehavior("附近没有能够到的窗口边缘"); return false; }
        _pendingClimb = plan;
        _movementPurpose = MovementPurpose.ClimbApproach;
        _targetScreenX = plan.ApproachX;
        _targetScreenY = surface.Top;
        _state = PetState.Running;
        _stateElapsed = 0;
        _animator.Play("walk");
        return true;
    }

    private bool StartControlledEdge(bool slide)
    {
        RefreshSurfaceMap(force: true);
        var surface = BehaviorSupport();
        if (surface is null || surface.IsDesktopFloor) { RejectBehavior("先把她放到真实窗口的顶边"); return false; }
        var foot = PrankFoot();
        _patrolDirection = foot.X - surface.Left < surface.Right - foot.X ? -1 : 1;
        _targetScreenX = _patrolDirection < 0 ? surface.Left + 20 : surface.Right - 20;
        _targetScreenY = surface.Top;
        _movementPurpose = slide ? MovementPurpose.EdgeSlideApproach : MovementPurpose.EdgeJumpApproach;
        _state = PetState.Running;
        _stateElapsed = 0;
        _animator.Play("walk");
        return true;
    }

    private bool StartControlledFall(bool toss)
    {
        var monitor = DisplayGeometry.FromWindow(_windowHandle);
        if (monitor is null) return false;
        if (!toss)
        {
            var available = PrankFoot().Y - monitor.WorkArea.Top - Height * monitor.Scale;
            if (available < 36) { RejectBehavior("上方空间不足，请把她放低一些"); return false; }
            DisplayGeometry.OffsetWindowPhysical(_windowHandle, 0, -(int)Math.Min(available, 220 * monitor.Scale));
        }
        BeginFall(toss ? -650 * monitor.Scale : 0, toss ? "BehaviorControlToss" : "BehaviorControlFall",
            toss ? (FacingTransform.ScaleX < 0 ? -1 : 1) * 90 * monitor.Scale : 0);
        if (toss) _voice.Play(VoiceCue.Scream, VoicePriority.Important, TimeSpan.FromSeconds(8));
        return true;
    }

    private bool StartControlledPrank(PrankKind kind)
    {
        TryStartWindowPrank(manual: true, directKind: kind);
        return _prankPhase != PrankPhase.None;
    }

    private bool StartControlledHat()
    {
        if (_prankArtwork is null || DisplayGeometry.FromWindow(_windowHandle) is null) return false;
        _behaviorHatOnly = true;
        _prankKind = PrankKind.Hat;
        _prankLarge = false;
        _prankTarget = null;
        _prankPresentationFinished = true;
        _prankSystemEngaged = false;
        _prankAwaitingSystem = false;
        _prankStartFoot = _prankApproachFoot = PrankFoot();
        _prankGeneration++;
        _prankCancellation = new CancellationTokenSource();
        BeginPrankPhase(PrankPhase.ThrowHat, "hat_throw");
        return true;
    }

    private bool StartControlledIconMeal()
    {
        var origin = PrankFoot();
        var icon = DesktopIconLocator.GetAllIcons()
            .Where(candidate => DisplayGeometry.FromPoint(candidate.Center, nearest: false) is not null &&
                DesktopIconLocator.FindListViewAt(candidate.Center) != IntPtr.Zero)
            .OrderBy(candidate => Math.Pow(candidate.Center.X - origin.X, 2) + Math.Pow(candidate.Center.Y - origin.Y, 2))
            .FirstOrDefault();
        if (icon is null) { RejectBehavior("没有可见桌面图标，请露出桌面后重试"); return false; }
        var bitmap = DesktopIconCopyCapture.Capture(icon);
        if (bitmap is null) { RejectBehavior("图标被遮挡或无法读取，请换一个位置重试"); return false; }
        var target = DisplayGeometry.ClampPointToNearestWorkArea(
            new DrawingPoint(icon.Center.X + icon.PixelSize.Width + 24, icon.Center.Y), 36);
        var meal = new DeletedItemInfo("behavior-icon-copy.snack", "图标副本", DateTimeOffset.UtcNow, false,
            CachedIcon: bitmap, ScreenX: target.X, ScreenY: target.Y,
            IconPixelWidth: bitmap.PixelWidth, IconPixelHeight: bitmap.PixelHeight,
            QueueId: Interlocked.Increment(ref _nextMealQueueId), AnchorSource: "BehaviorControlIconCopy");
        _foodQueue.Enqueue(meal, CreateWaitingGhost);
        StartNextMeal();
        return _state == PetState.Running && _movementPurpose == MovementPurpose.Meal;
    }
}
