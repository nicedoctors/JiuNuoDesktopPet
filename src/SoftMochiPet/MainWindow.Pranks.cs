using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using SoftMochiPet.Core;
using SoftMochiPet.Interop;
using SoftMochiPet.Services;
using Point = System.Windows.Point;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;

namespace SoftMochiPet;

public partial class MainWindow
{
    private enum PrankKind { Kick, Punch, Charge, Hat, Shatter, Tear }
    private enum PrankPhase
    {
        None, BurstCall, Approach, Preparing, Strike, HatOpen, HatStore, HatReturn,
        ThrowHat, TearGrow, BigStrike, CollectRemains, Reassemble, Restoring, FetchHat, PickHat, WearHat,
    }

    private WindowPrankController? _windowPranks;
    private WindowPrankTarget? _prankTarget;
    private WindowPrankSnapshot? _prankSnapshot;
    private CancellationTokenSource? _prankCancellation;
    private MischiefController _mischief = new();
    private MischiefStateStore? _mischiefStore;
    private PrankArtwork? _prankArtwork;
    private HatPropWindow? _hatProp;
    private PrankKind _prankKind;
    private PrankPhase _prankPhase;
    private double _prankPhaseElapsed;
    private double _prankPhaseDuration;
    private double _nextPrankSearch;
    private double _hatFlightElapsed;
    private double _hatPhysicalWidth;
    private double _hatThrowWidth;
    private double _hatLandingWidth;
    private double _hatThrowRotation;
    private double _hatLandingRotation;
    private double _hatArcHeight;
    private Point _prankStartFoot;
    private Point _prankApproachFoot;
    private int _prankApproachDirection = 1;
    private Point _prankContactFoot;
    private Point _screenStrikeContact;
    private Point _hatStart;
    private Point _hatRest;
    private Point _hatPickupFoot;
    private Point _prankHatMouth;
    private double _prankHatMouthWidth;
    private int _prankGeneration;
    private int _manualPrankIndex;
    private int _burstPrankIndex;
    private bool _prankLarge;
    private bool _prankContactApplied;
    private bool _prankAwaitingSystem;
    private bool _prankSystemEngaged;
    private bool _tearHandsReleased;
    private bool _sessionLocked;
    private bool _needsHatRecovery;
    private bool _sessionEventsSubscribed;
    private bool _manualRestoreInProgress;
    private bool _prankPresentationFinished;
    private bool _returnPrankRemainsFromHat;
    // A tray/menu request can arrive while the pet is completing a landing or
    // waking animation. Keep that explicit request until the normal idle gate
    // is reached instead of dropping it silently.
    private bool _manualMischiefPending;
    private double _prankImpactElapsed = -1;

    private bool IsPrankStrikePhase => _prankPhase is PrankPhase.Strike or PrankPhase.BigStrike;
    private bool IsPrankTimelineControlled => IsPrankStrikePhase || _prankPhase == PrankPhase.CollectRemains ||
        (_prankPhase == PrankPhase.HatReturn && _returnPrankRemainsFromHat);
    private PrankStrikeTiming? CurrentStrikeTiming => IsPrankStrikePhase ? PrankStrikeTiming.Find(_animator.CurrentAssetFolder) : null;
    private bool IsPrankAwaitingManualRestore => HasHeldPrank && _prankPhase == PrankPhase.None;

    private static bool HasCharacterAssets(string runtimeDirectory, PetCharacterProfile character)
    {
        if (!SpriteAnimator.HasCompleteAssets(runtimeDirectory, character)) return false;
        if (!character.UsesMischief) return true;
        try { _ = new PrankArtwork(Path.GetDirectoryName(runtimeDirectory)!); return true; }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            DiagnosticsLog.WriteEvent("PrankArtworkUnavailable", ("Reason", exception.GetType().Name));
            return false;
        }
    }

    private void NormalizeCharacterNeeds()
    {
        _lifeState.Hunger = _character.SupportsFood
            ? PetModePolicy.ResolveHunger(_lifeState.Hunger, EffectiveFastingMode) : 0;
        if (!_character.SupportsFood) _lifeState.Fullness = 0;
    }

    private void ClearFoodInteractions()
    {
        _foodQueue.Clear(CloseGhostWindow);
        CloseCurrentGhost();
        _currentFood = null;
        _manualLickRequested = _wakeForMeal = _wakeForManualLick = false;
        _currentMealNutritionApplied = false;
        _buffetUntil = DateTimeOffset.MinValue;
        FoodVisual.Visibility = Visibility.Collapsed;
        BurpPuff.Opacity = 0;
    }

    private void ConfigureFoodMonitoring()
    {
        if (_deletionWatcher is not null)
        {
            _deletionWatcher.ItemDeleted -= DeletionWatcher_ItemDeleted;
            _deletionWatcher.Dispose();
            _deletionWatcher = null;
        }
        _mouseMonitor.Dispose();
        _mouseMonitor = new MouseMonitor();
        if (!_character.SupportsFood || IsBehaviorControlMode) { ClearFoodInteractions(); return; }
        _mouseMonitor.Start();
        _deletionWatcher = new FileDeletionWatcher();
        _deletionWatcher.ItemDeleted += DeletionWatcher_ItemDeleted;
        _deletionWatcher.Start(_settings.WatchedFolders);
        if (IsLoaded) _deletionWatcher.BeginIconCacheWarmup();
    }

    private void InitializeMischief()
    {
        if (_primaryWindow is null)
        {
            _trayIcon.AllowMischiefChanged += enabled => Dispatcher.Invoke(() =>
            {
                var pet = MischiefPet;
                if (pet is null) return;
                pet._settings.AllowMischief = enabled;
                pet._settingsStore.Save(pet._settings);
                if (!enabled) pet.CancelWindowPrank("AutonomyDisabled", true, false);
                pet.UpdateMischief(0);
            });
            _trayIcon.MischiefRequested += () => Dispatcher.Invoke(() =>
            {
                var pet = MischiefPet;
                if (pet is null) return;
                DiagnosticsLog.WriteEvent("MischiefManualRequested",
                    ("State", pet._state), ("Animation", pet._animator.CurrentClip),
                    ("MenuOpen", _trayIcon.IsMenuOpen));
                CancelPairScene("ManualMischief");
                CancelPairDialogue();
                pet.TryStartWindowPrank(manual: true);
            });
            _trayIcon.TeaseRequested += () => Dispatcher.Invoke(() =>
            {
                var pet = MischiefPet;
                if (pet is null) return;
                pet.UpdateMischief(0);
                if (pet._mischief.Tease())
                {
                    pet.PlayMischiefVoice(MischiefCue.Teased);
                    pet.SaveMischiefState();
                }
            });
            _trayIcon.ReturnWindowsRequested += () => Dispatcher.Invoke(() =>
            {
                var pet = MischiefPet;
                if (pet is null) return;
                if (pet.IsBehaviorControlMode) pet.QueueBehaviorControl(BehaviorControlAction.ReturnWindows);
                else
                {
                    CancelPairScene("ManualWindowReturn");
                    pet.RequestWindowReturn();
                }
            });
        }
        LoadCharacterMischief();
    }

    private void InitializeWindowPranks()
    {
        _windowPranks ??= new WindowPrankController(_windowHandle);
        if (_sessionEventsSubscribed) return;
        SystemEvents.SessionSwitch += OnPrankSessionSwitch;
        SystemEvents.PowerModeChanged += OnPrankPowerModeChanged;
        _sessionEventsSubscribed = true;
    }

    private void OnPrankSessionSwitch(object sender, SessionSwitchEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (_isClosing) return;
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.SessionLogoff or
            SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect)
        {
            _sessionLocked = true;
            StopFeatureDemo("SessionUnavailable");
            CancelPairScene("SessionUnavailable");
            if (_cheekPinch.Active) ReturnToIdle();
            CancelWindowPrank("SessionUnavailable", true, false);
        }
        else if (e.Reason is SessionSwitchReason.SessionUnlock or SessionSwitchReason.SessionLogon or
                 SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect)
            _sessionLocked = false;
        UpdateMischief(0);
    });

    private void OnPrankPowerModeChanged(object sender, PowerModeChangedEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (_isClosing) return;
        if (e.Mode == PowerModes.Suspend)
        {
            _sessionLocked = true;
            StopFeatureDemo("Suspend");
            CancelPairScene("Suspend");
            if (_cheekPinch.Active) ReturnToIdle();
            CancelWindowPrank("Suspend", true, false);
            SaveMischiefState();
        }
        else if (e.Mode == PowerModes.Resume)
        {
            _sessionLocked = false;
            _nextPrankSearch = _clock.Elapsed.TotalSeconds + 30;
        }
        UpdateMischief(0);
    });

    private void LoadCharacterMischief()
    {
        _mischiefWaitingForTarget = false;
        _lastMischiefStatusReason = null;
        _prankArtwork = null;
        _mischiefStore = _character.UsesMischief ? new MischiefStateStore(_lifeStateStore.StateDirectory) : null;
        _mischief = new MischiefController(_mischiefStore?.Load());
        _trayIcon.SetAllowMischief(_settings.AllowMischief);
        if (_character.UsesMischief) _trayIcon.SetWindowReturnAvailable(false);
        if (_character.UsesMischief)
        {
            try
            {
                var directory = Path.GetDirectoryName(Path.Combine(AppContext.BaseDirectory,
                    _character.RuntimeRelativeDirectory))!;
                _prankArtwork = new PrankArtwork(directory);
            }
            catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
            {
                DiagnosticsLog.WriteEvent("PrankArtworkUnavailable", ("Reason", exception.GetType().Name));
            }
        }
        UpdateMischief(0);
    }

    private void SaveMischiefState()
    {
        if (!_character.UsesMischief || _mischiefStore is null) return;
        try { _mischiefStore.Save(_mischief.CaptureState()); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { DiagnosticsLog.WriteEvent("MischiefSaveSkipped", ("Reason", exception.GetType().Name)); }
    }

    private void UpdateMischief(double deltaSeconds)
    {
        UpdateProgressDisplayVisibility();
        if (!_character.UsesMischief) return;
        _mischief.Tick(deltaSeconds, _settings.AllowMischief,
            sleeping: _state is PetState.Sleeping or PetState.Waking,
            quiet: _settings.QuietMode, sessionLocked: _sessionLocked, burstBlocked: HasHeldPrank);
        var width = Math.Max(1, MischiefBadge.ActualWidth - 53);
        MischiefBarFill.Width = width * _mischief.Value / 100;
        var activeBrush = MischiefActiveBrush;
        MischiefBarFill.Background = _settings.AllowMischief ? activeBrush : System.Windows.Media.Brushes.Gray;
        MischiefBadge.Opacity = _settings.AllowMischief ? 1 : 0.6;
        var pulse = _mischief.Value >= 90 && _settings.AllowMischief && !_settings.QuietMode && !_sessionLocked
            ? 1 + Math.Sin(_clock.Elapsed.TotalSeconds * 3.2) * 0.028 : 1;
        MischiefBadgeScale.ScaleX = MischiefBadgeScale.ScaleY = pulse;
        _trayIcon.SetWindowReturnAvailable(HasHeldPrank);
        if ((!IsBehaviorControlMode || _activeBehavior is null) &&
            (_primaryWindow ?? this)._pairScene != PairScene.IconKick &&
            _mischief.AutonomousActionDue && _clock.Elapsed.TotalSeconds >= _nextPrankSearch &&
            _state is PetState.Idle or PetState.Curious or PetState.Running &&
            !_isDragging && !_animator.Hatless && _enclosureHandle == IntPtr.Zero &&
            (_windowPranks?.HasActiveOperation != true || _windowPranks.IsHeld))
            TryStartWindowPrank(manual: false);
        UpdateMischiefStatus();
    }

    private void PlayMischiefVoice(MischiefCue cue)
    {
        if (!_settings.QuietMode && !_sessionLocked)
            _voice.PlayMischief(cue);
    }

    private bool IsMischiefDeferredState => _state is PetState.Falling or PetState.Landing or
        PetState.Sleeping or PetState.Waking;

    private void QueueManualMischief()
    {
        if (_manualMischiefPending) return;
        _manualMischiefPending = true;
        DiagnosticsLog.WriteEvent("MischiefManualDeferred",
            ("State", _state), ("Animation", _animator.CurrentClip),
            ("MenuOpen", _trayIcon.IsMenuOpen));

        // Sleeping pets should begin waking immediately; landing/falling pets
        // are allowed to finish their physics/impact animation first.
        if (_state is PetState.Sleeping or PetState.Waking)
            BeginWake();
    }

    private bool TryRunQueuedManualMischief()
    {
        if (!_manualMischiefPending || IsBehaviorControlMode || _state != PetState.Idle)
            return false;

        _manualMischiefPending = false;
        DiagnosticsLog.WriteEvent("MischiefManualDeferredExecuting",
            ("State", _state), ("Animation", _animator.CurrentClip));
        TryStartWindowPrank(manual: true);
        return true;
    }

    private void TryStartWindowPrank(bool manual, PrankKind? directKind = null)
    {
        if (directKind is not null && !IsBehaviorControlMode) return;
        var deferredState = IsMischiefDeferredState;
        if (!_character.UsesMischief || _isClosing || _sessionLocked || _settings.QuietMode || _isDragging ||
            _prankPhase != PrankPhase.None || deferredState || _state is PetState.Climbing or PetState.Sliding or
                PetState.Rolling or PetState.Interacting or PetState.Pinching || _animator.Hatless)
        {
            if (manual && directKind is null && !_isClosing && !_sessionLocked && !_settings.QuietMode &&
                !IsBehaviorControlMode && deferredState)
                QueueManualMischief();
            if (IsFeatureTestMode) _featurePrankFailure = $"pet_not_ready_{_state}";
            return;
        }
        if (_prankArtwork is null || _windowPranks is null)
        {
            if (IsFeatureTestMode) _featurePrankFailure = "prank_assets_or_controller_unavailable";
            _nextPrankSearch = _clock.Elapsed.TotalSeconds + 60;
            return;
        }
        ParkHeldPrank();
        if (_windowPranks.HasActiveOperation) return;
        if (HasStoredPrank && directKind is PrankKind.Hat or PrankKind.Shatter or PrankKind.Tear)
        {
            RejectBehavior("帽子已有窗口，请先选择归还");
            return;
        }
        var foot = PrankFoot();
        var physicalPetWidth = Math.Abs(CanvasPointToScreen(512, 0).X - CanvasPointToScreen(0, 0).X);
        var petMonitor = DisplayGeometry.FromWindow(_windowHandle)?.MonitorArea;
        // The right-side staging space keeps both the capture and the readable anticipation unobstructed.
        var target = IsFeatureTestMode ? InspectFeatureTarget(includePetOcclusion: false)
            : _windowPranks.TrySelectTarget(ToDrawing(foot), rejectionReason: candidate =>
            candidate.MonitorArea != petMonitor ? "different_monitor" :
            WindowPrankApproachPolicy.Choose(candidate.VisibleBounds, candidate.WorkArea,
                (int)Math.Round(physicalPetWidth), (int)Math.Round(foot.X)) is null
                ? "insufficient_approach_space" : null,
                preferredHandle: manual ? _menuForegroundHandle : IntPtr.Zero);
        if (target is null)
        {
            _mischief.TargetUnavailable();
            _nextPrankSearch = _clock.Elapsed.TotalSeconds + MischiefController.TargetRetrySeconds;
            _mischiefWaitingForTarget = true;
            DiagnosticsLog.WriteEvent("MischiefTargetWaiting", ("Value", Math.Round(_mischief.Value, 2)),
                ("RetrySeconds", MischiefController.TargetRetrySeconds), ("HatOccupied", HasStoredPrank),
                ("Reasons", _windowPranks.LastSelectionRejections));
            if (manual) PlayMischiefVoice(MischiefCue.NoTarget);
            if (directKind is not null) RejectBehavior("没有合适窗口：请同屏、保持非最大化并留出右侧空间");
            return;
        }
        DiagnosticsLog.WriteEvent("MischiefTargetSelected",
            ("Window", FormatHandle(target.Handle)),
            ("Process", target.ProcessId),
            ("Manual", manual),
            ("Preferred", manual && target.Handle == _menuForegroundHandle),
            ("Foreground", target.Handle == NativeMethods.GetForegroundWindow()));
        bool started;
        if (directKind is { } requestedKind)
        {
            _prankLarge = requestedKind is PrankKind.Shatter or PrankKind.Tear;
            _mischief.Tick(0, _settings.AllowMischief, quiet: _settings.QuietMode, sessionLocked: _sessionLocked);
            started = _mischief.TryStartDirect(_prankLarge);
        }
        else started = _mischief.TryStart(manual, out _prankLarge, allowLarge: !HasStoredPrank);
        if (!started)
        {
            if (IsFeatureTestMode) _featurePrankFailure = "mischief_not_ready";
            return;
        }
        _mischiefWaitingForTarget = false;
        _prankKind = directKind ?? (_prankLarge ? (_burstPrankIndex++ % 2 == 0 ? PrankKind.Shatter : PrankKind.Tear)
            : (PrankKind)(HasStoredPrank ? (manual ? _manualPrankIndex++ % 3 : Random.Shared.Next(3))
                : manual ? _manualPrankIndex++ % 4 : Math.Min(3, Random.Shared.Next(7) / 2)));
        _manualRestoreInProgress = false;
        _prankPresentationFinished = false;
        _returnPrankRemainsFromHat = false;
        _prankTarget = target;
        _prankCancellation?.Dispose();
        _prankCancellation = new CancellationTokenSource();
        _prankGeneration++;
        ClearWindowEnclosure();
        _pendingClimb = _activeClimb = null;
        _slideSurface = null;
        _movementPurpose = MovementPurpose.None;
        _velocityX = _velocityY = 0;
        ResetBodyDeformation();
        FacingTransform.ScaleX = 1;
        _prankStartFoot = foot;
        var approach = WindowPrankApproachPolicy.Choose(target.VisibleBounds, target.WorkArea,
            (int)Math.Round(physicalPetWidth), (int)Math.Round(foot.X));
        if (approach is null)
        {
            RejectBehavior("窗口两侧都没有足够靠近空间");
            _mischief.Interrupt();
            return;
        }
        _prankApproachFoot = new Point(approach.Value.FootX, foot.Y);
        _prankApproachDirection = approach.Value.Direction;
        _prankApproachFoot.Y = Math.Clamp(_prankApproachFoot.Y,
            target.WorkArea.Top + physicalPetWidth, target.WorkArea.Bottom - 1);
        DiagnosticsLog.WriteEvent("PrankStarted", ("Kind", _prankKind), ("Large", _prankLarge), ("Manual", manual));
        if (_prankLarge) _ = AnnounceBurstThenApproachAsync();
        else BeginPrankApproach();
    }

    private void BeginPrankApproach()
    {
        var duration = Math.Clamp(Math.Abs(_prankStartFoot.X - _prankApproachFoot.X) / (280 * PetPhysicalScale()), 0.45, 4);
        BeginPrankPhase(PrankPhase.Approach, _prankKind == PrankKind.Hat ? "prank_sneak" : "run", duration);
        var approachDirection = _prankApproachFoot.X < _prankStartFoot.X ? -1 : 1;
        FacingTransform.ScaleX = _prankKind == PrankKind.Hat ? -approachDirection : approachDirection;
        if (_prankKind == PrankKind.Hat) PlayMischiefVoice(MischiefCue.Sneak);
    }

    private async Task AnnounceBurstThenApproachAsync()
    {
        var generation = _prankGeneration;
        var token = _prankCancellation!.Token;
        BeginPrankPhase(PrankPhase.BurstCall, "idle");
        try
        {
            var completed = await _voice.PlayBurstCallAsync(token);
            if (generation != _prankGeneration || token.IsCancellationRequested || _isClosing) return;
            if (_prankTarget is not { } target || _windowPranks is null) return;
            var inspection = _windowPranks.InspectTarget(target.Handle, target.ProcessId);
            if (inspection.Target is not { } current ||
                !WindowPrankGeometry.SameBounds(target.OuterBounds, current.OuterBounds))
            {
                CancelWindowPrank(inspection.Eligible ? "target_moved_during_burst_call" : inspection.Reason, true, false);
                return;
            }
            DiagnosticsLog.WriteEvent("PrankBurstCallFinished", ("VoiceCompleted", completed), ("Kind", _prankKind));
            BeginPrankApproach();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (generation != _prankGeneration || _isClosing) return;
            DiagnosticsLog.WriteEvent("PrankBurstCallFailed", ("Reason", exception.GetType().Name));
            CancelWindowPrank("burst_call_failed", true, false);
        }
    }

    private void BeginPrankPhase(PrankPhase phase, string clip, double? duration = null)
    {
        _state = PetState.Pranking;
        _stateElapsed = 0;
        _prankPhase = phase;
        _prankPhaseElapsed = 0;
        _prankContactApplied = false;
        FacingTransform.ScaleX = 1;
        _animator.Play(clip);
        _prankPhaseDuration = duration ?? _animator.DurationSeconds;
        DiagnosticsLog.WriteEvent("PrankPhaseChanged", ("Phase", phase), ("Clip", clip),
            ("Duration", Math.Round(_prankPhaseDuration, 3)));
        UpdatePrankForeground();
        UpdateFeatureHatProgress();
    }

    private void UpdateWindowPrank(double delta)
    {
        if (_prankPhase == PrankPhase.None) { ReturnToIdle(); return; }
        var strikeTiming = CurrentStrikeTiming;
        var nextElapsed = _prankPhaseElapsed + delta;
        _windowPranks?.Update(strikeTiming?.MotionDelta(_prankPhaseElapsed, nextElapsed) ?? delta);
        if (_prankSystemEngaged && !_prankAwaitingSystem && _windowPranks?.CancelReason is { } reason)
        {
            CancelWindowPrank(reason, true, keepHatless: true);
            return;
        }
        if (_prankAwaitingSystem) return;
        _prankPhaseElapsed = nextElapsed;
        var progress = strikeTiming?.SourceProgress(_prankPhaseElapsed)
            ?? Math.Clamp(_prankPhaseElapsed / Math.Max(0.01, _prankPhaseDuration), 0, 1);
        if (IsPrankTimelineControlled)
            _animator.AdvanceTo(progress * _animator.DurationSeconds + 0.00000001);
        switch (_prankPhase)
        {
            case PrankPhase.Approach:
                MovePrankFoot(new Point(Lerp(_prankStartFoot.X, _prankApproachFoot.X, SmoothStep(progress)),
                    Lerp(_prankStartFoot.Y, _prankApproachFoot.Y, SmoothStep(progress))));
                if (progress >= 1) _ = PreparePrankAsync();
                break;
            case PrankPhase.Strike:
            case PrankPhase.BigStrike:
                UpdateStrikePose(progress);
                if (_prankKind == PrankKind.Tear && !_tearSnapshotRequested && progress >= 3 / 16d)
                {
                    _tearSnapshotRequested = true;
                    _ = EnterSnapshotAsync();
                    if (_prankPhase != PrankPhase.BigStrike) return;
                }
                if (!_prankContactApplied && progress >= ContactFraction())
                {
                    _prankContactApplied = true;
                    if (_prankKind == PrankKind.Tear) ShowPrankImpact();
                    else if (_prankLarge) _ = EnterSnapshotAsync();
                    else ApplyWindowStrike();
                    if (_prankPhase is not (PrankPhase.Strike or PrankPhase.BigStrike)) return;
                }
                if (_prankLarge && _windowPranks?.SnapshotMinimized == true)
                {
                    if (_prankKind == PrankKind.Tear && !_tearHandsReleased)
                    {
                        UpdateTearHands(Math.Min(9, _animator.CurrentFrameIndex));
                        _tearHandsReleased = _animator.CurrentFrameIndex >= 9;
                    }
                    if (_prankKind == PrankKind.Tear)
                        _windowPranks.SetTearTension(Math.Clamp((progress * 16 - 3) / 5, 0, 1));
                    _windowPranks.SetEffectProgress(StrikeEffectProgress(progress), ToDrawing(_screenStrikeContact));
                }
                if (progress >= 1 && !_prankAwaitingSystem)
                {
                    if (_prankLarge)
                    {
                        if (HoldPrankResult()) StartHatRecovery();
                    }
                    else if (_windowPranks?.IsMoving != true) FinishWindowPrank();
                }
                break;
            case PrankPhase.HatOpen:
                if (progress >= 1)
                {
                    if (_manualRestoreInProgress) BeginPrankReturn();
                    else BeginPrankPhase(PrankPhase.HatStore, "hat_store");
                }
                break;
            case PrankPhase.HatStore:
                if (!_prankContactApplied)
                {
                    _prankContactApplied = true;
                    _ = EnterSnapshotAsync();
                    if (_prankPhase != PrankPhase.HatStore) return;
                }
                UpdateHatOpening();
                if (_windowPranks?.SnapshotMinimized == true)
                    _windowPranks.SetEffectProgress(progress, ToDrawing(_prankHatMouth), _prankHatMouthWidth);
                if (progress >= 1 && !_prankAwaitingSystem)
                {
                    PlayMischiefVoice(MischiefCue.HoldingWindow);
                    if (HoldPrankResult()) BeginPrankPhase(PrankPhase.WearHat, "hat_wear");
                }
                break;
            case PrankPhase.HatReturn:
                UpdateHatOpening();
                if (_returnPrankRemainsFromHat)
                    _windowPranks?.SetCollectionProgress(1 - progress, ToDrawing(_prankHatMouth), _prankHatMouthWidth);
                else _windowPranks?.SetEffectProgress(1 - progress, ToDrawing(_prankHatMouth), _prankHatMouthWidth);
                if (progress >= 1)
                {
                    if (_returnPrankRemainsFromHat) BeginPrankPhase(PrankPhase.Reassemble, "hat_hold", 1.8);
                    else _ = RestorePrankAsync();
                }
                break;
            case PrankPhase.CollectRemains:
                UpdateHatOpening();
                _windowPranks?.SetCollectionProgress(progress, ToDrawing(_prankHatMouth), _prankHatMouthWidth);
                if (progress >= 1)
                {
                    DiagnosticsLog.WriteEvent("PrankRemainsStored", ("Kind", _prankKind));
                    _windowPranks?.SetForeground(null, DrawingRectangle.Empty);
                    PlayMischiefVoice(MischiefCue.HoldingWindow);
                    BeginPrankPhase(PrankPhase.WearHat, "hat_wear");
                }
                break;
            case PrankPhase.ThrowHat:
                if (!_prankContactApplied && progress >= ContactFraction())
                {
                    _prankContactApplied = true;
                    ThrowPrankHat();
                }
                if (progress >= 1)
                {
                    _animator.SetHatless(true);
                    if (_behaviorHatOnly) StartHatRecovery();
                    else if (_prankKind == PrankKind.Tear) BeginTearGrowth();
                    else BeginStrike(large: true);
                }
                break;
            case PrankPhase.TearGrow:
                UpdateTearGrowth(progress);
                if (progress >= 1) BeginStrike(large: true);
                break;
            case PrankPhase.Reassemble:
                _windowPranks?.SetEffectProgress(1 - SmoothStep(progress), ToDrawing(_screenStrikeContact));
                if (progress >= 1) _ = RestorePrankAsync();
                break;
            case PrankPhase.FetchHat:
                MovePrankFoot(new Point(Lerp(_prankStartFoot.X, _hatPickupFoot.X, SmoothStep(progress)),
                    Lerp(_prankStartFoot.Y, _hatPickupFoot.Y, SmoothStep(progress))));
                if (progress >= 1) BeginPrankPhase(PrankPhase.PickHat, "hat_pickup");
                break;
            case PrankPhase.PickHat:
                if (!_prankContactApplied && progress >= ContactFraction())
                { _prankContactApplied = true; CloseHatProp(); }
                if (progress >= 1) BeginPrankCollectionOrWearHat();
                break;
            case PrankPhase.WearHat:
                if (progress >= 1)
                {
                    _animator.SetHatless(false);
                    _needsHatRecovery = false;
                    FinishWindowPrank();
                }
                break;
        }
        UpdatePrankForeground();
    }

    private async Task PreparePrankAsync()
    {
        if (_windowPranks is null || _prankTarget is null) return;
        BeginPrankPhase(PrankPhase.Preparing, "idle");
        var generation = _prankGeneration;
        var token = _prankCancellation!.Token;
        _prankAwaitingSystem = true;
        try
        {
            if (_prankLarge || _prankKind == PrankKind.Hat)
            {
                var captured = await _windowPranks.CaptureAsync(_prankTarget, token);
                if (generation != _prankGeneration) { captured?.Dispose(); return; }
                if (captured is null) { CancelWindowPrank(_windowPranks.LastInspection is { Eligible: false } rejected
                    ? rejected.Reason : _windowPranks.LastResult, true, false); return; }
                _prankSnapshot = captured;
            }
            if (_prankLarge) BeginPrankPhase(PrankPhase.ThrowHat, "hat_throw");
            else if (_prankKind == PrankKind.Hat)
            {
                PlayMischiefVoice(MischiefCue.HideWindow);
                BeginPrankPhase(PrankPhase.HatOpen, "hat_open");
            }
            else BeginStrike(large: false);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (generation != _prankGeneration) return;
            DiagnosticsLog.WriteEvent("PrankPreparationFailed", ("Reason", exception.GetType().Name));
            CancelWindowPrank("PreparationFailed", true, false);
        }
        finally { if (generation == _prankGeneration) _prankAwaitingSystem = false; }
    }

    private void BeginStrike(bool large)
    {
        var clip = _prankKind switch
        {
            PrankKind.Punch => "punch", PrankKind.Charge => "charge",
            PrankKind.Shatter => "bare_kick", PrankKind.Tear => "bare_tear", _ => "kick",
        };
        BeginPrankPhase(large ? PrankPhase.BigStrike : PrankPhase.Strike, clip, PrankStrikeTiming.Find(clip)?.DurationSeconds);
        _tearHandsReleased = false;
        var artwork = _prankArtwork!.Clip(clip);
        var anchor = artwork.ContactAt(artwork.ContactFrame);
        if (_prankKind == PrankKind.Tear)
        {
            var upper = artwork.TearUpperHandAt(3);
            var lower = artwork.TearLowerHandAt(3);
            anchor = new Point((upper.X + lower.X) / 2, (upper.Y + lower.Y) / 2);
        }
        var bounds = _prankTarget!.VisibleBounds;
        var contactY = Math.Clamp(_prankApproachFoot.Y - 55 * PetPhysicalScale(), bounds.Top + 35, bounds.Bottom - 20);
        var contactX = _prankApproachDirection < 0 ? bounds.Left : bounds.Right;
        _screenStrikeContact = new Point(contactX, contactY);
        var scale = PetCanvasPhysicalScale();
        _prankContactFoot = new Point(contactX + (_character.Geometry.FootCanvasX - anchor.X) * scale,
            contactY + (_character.Geometry.FootCanvasY - anchor.Y) * scale);
        if (_prankKind == PrankKind.Tear)
        {
            _prankContactFoot = _tearStageFoot;
            _screenStrikeContact = TearCanvasPointToScreen(anchor.X, anchor.Y);
        }
        PlayMischiefVoice(_prankKind switch
        {
            PrankKind.Punch => MischiefCue.Punch, PrankKind.Charge => MischiefCue.Charge,
            PrankKind.Shatter => MischiefCue.Smash, PrankKind.Tear => MischiefCue.Tear, _ => MischiefCue.Kick,
        });
    }

    private void UpdateStrikePose(double progress)
    {
        if (_prankKind == PrankKind.Tear)
        {
            UpdateTearPresentation(progress);
            return;
        }
        var amount = CurrentStrikeTiming!.PoseWeight(progress);
        MovePrankFoot(new Point(Lerp(_prankApproachFoot.X, _prankContactFoot.X, amount),
            Lerp(_prankApproachFoot.Y, _prankContactFoot.Y, amount)));
    }

    private double ContactFraction()
    {
        var artwork = _prankArtwork?.Clip(_animator.CurrentAssetFolder);
        var frame = artwork?.HatTransferFrame ?? artwork?.ContactFrame ?? 8;
        return Math.Clamp(frame / 16d, 0.1, 0.9);
    }

    private double StrikeEffectProgress(double progress)
    {
        var finishedAt = _prankKind == PrankKind.Tear ? 9 / 16d : 1;
        return Math.Clamp((progress - ContactFraction()) / Math.Max(0.01, finishedAt - ContactFraction()), 0, 1);
    }

    private void ApplyWindowStrike()
    {
        var kind = _prankKind switch
        {
            PrankKind.Punch => WindowPrankMotionKind.Punch,
            PrankKind.Charge => WindowPrankMotionKind.Charge, _ => WindowPrankMotionKind.Kick,
        };
        if (_prankTarget is null || _windowPranks?.BeginMotion(_prankTarget, kind, -_prankApproachDirection) != true)
            CancelWindowPrank("StrikeRejected", true, false);
        else
        {
            _prankSystemEngaged = true;
            ShowPrankImpact();
        }
    }

    private async Task EnterSnapshotAsync()
    {
        if (_prankSnapshot is null || _windowPranks is null) { CancelWindowPrank("SnapshotMissing", true, true); return; }
        var generation = _prankGeneration;
        _prankAwaitingSystem = true;
        var waitStarted = _clock.Elapsed.TotalSeconds;
        try
        {
            var kind = _prankKind switch
            {
                PrankKind.Shatter => WindowPrankVisualKind.Shatter,
                PrankKind.Tear => WindowPrankVisualKind.Tear, _ => WindowPrankVisualKind.Hat,
            };
            var entered = await _windowPranks.BeginSnapshotAsync(_prankSnapshot, kind, _prankCancellation!.Token);
            if (generation != _prankGeneration) return;
            if (!entered) { CancelWindowPrank("SnapshotRejected", true, true); return; }
            _prankSystemEngaged = true;
            if (_prankKind != PrankKind.Tear && CurrentStrikeTiming is { } timing)
            {
                _prankPhaseElapsed = timing.ConsumeImpactPause(_prankPhaseElapsed, _clock.Elapsed.TotalSeconds - waitStarted);
                ShowPrankImpact();
            }
            if (_prankKind == PrankKind.Tear) UpdateTearHands(3);
            UpdatePrankForeground();
            _trayIcon.SetWindowReturnAvailable(true);
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (generation != _prankGeneration) return;
            DiagnosticsLog.WriteEvent("PrankSnapshotFailed", ("Reason", exception.GetType().Name));
            CancelWindowPrank("SnapshotFailed", true, true);
        }
        finally { if (generation == _prankGeneration) _prankAwaitingSystem = false; }
    }

    private void UpdateHatOpening()
    {
        var clip = _prankArtwork!.Clip(_animator.CurrentAssetFolder);
        var mouth = clip.HatMouthAt(_animator.CurrentFrameIndex);
        _prankHatMouth = CanvasPointToScreen(mouth.X, mouth.Y);
        _prankHatMouthWidth = clip.HatWidthAt(_animator.CurrentFrameIndex) * PetCanvasPhysicalScale();
    }

    private void UpdateTearHands(int frame)
    {
        var clip = _prankArtwork!.Clip("bare_tear");
        var upper = clip.TearUpperHandAt(frame);
        var lower = clip.TearLowerHandAt(frame);
        _windowPranks?.SetTearHands(ToDrawing(TearCanvasPointToScreen(upper.X, upper.Y)),
            ToDrawing(TearCanvasPointToScreen(lower.X, lower.Y)));
    }

    private void BeginPrankReturn()
    {
        BeginPrankPhase(PrankPhase.HatReturn, "hat_return", _returnPrankRemainsFromHat ? 2.4 : null);
    }

    private void RequestWindowReturn()
    {
        CheckStoredPrankSafety();
        if (_storedPrank is not null) TakeStoredPrankForReturn();
        if (_windowPranks?.IsHoldingWindow != true || _manualRestoreInProgress) return;
        if (!_windowPranks.IsHeld && !HoldPrankResult()) return;
        var returnRemainsFromHat = _prankLarge && _windowPranks.IsCollected;
        NotifyFeatureManualRestoreRequested();
        CancelWindowPrank("ManualRestorePreparation", false, keepHatless: false);
        _prankCancellation = new CancellationTokenSource();
        _manualRestoreInProgress = true;
        _returnPrankRemainsFromHat = returnRemainsFromHat;
        _prankSystemEngaged = true;
        _velocityX = _velocityY = 0;
        ClearWindowEnclosure();
        ResetBodyDeformation();
        PlayMischiefVoice(MischiefCue.ReturnWindow);
        if (_prankTarget?.MonitorArea != DisplayGeometry.FromWindow(_windowHandle)?.MonitorArea)
        {
            BeginPrankPhase(PrankPhase.Restoring, "idle");
            _ = RestorePrankAsync();
        }
        else if (_prankKind == PrankKind.Hat || _returnPrankRemainsFromHat)
            BeginPrankPhase(PrankPhase.HatOpen, "hat_open");
        else
        {
            _windowPranks.ResetCollection();
            BeginPrankPhase(PrankPhase.Reassemble, "idle", 1.8);
        }
    }

    private bool HoldPrankResult()
    {
        if (_windowPranks?.HoldSnapshot() != true)
        {
            CancelWindowPrank("HoldUnconfirmed", true, keepHatless: true);
            return false;
        }
        _trayIcon.SetWindowReturnAvailable(true);
        return true;
    }

    private void UpdateHeldPrank(double delta)
    {
        CheckStoredPrankSafety();
        if (_state == PetState.Pranking || _windowPranks?.IsHeld != true) return;
        _windowPranks.Update(delta);
        if (_windowPranks.IsHeld) return;
        _prankSnapshot?.Dispose();
        _prankSnapshot = null;
        _prankTarget = null;
        _trayIcon.SetWindowReturnAvailable(HasHeldPrank);
        if (_featureDemoRunning) _featurePrankFailure = _windowPranks.CancelReason ?? "held_window_released";
        DiagnosticsLog.WriteEvent("PrankHeldWindowReleased", ("Reason", _windowPranks.CancelReason));
    }

    private async Task RestorePrankAsync()
    {
        var generation = _prankGeneration;
        var wearHatAfterReturn = _animator.CurrentClip is "hat_return" or "hat_hold";
        _prankPhase = PrankPhase.Restoring;
        _prankAwaitingSystem = true;
        UpdateFeatureHatProgress();
        try
        {
            var restored = _windowPranks is null || await _windowPranks.RestoreAsync(_prankCancellation!.Token);
            if (generation != _prankGeneration) return;
            _trayIcon.SetWindowReturnAvailable(false);
            if (!restored) { CancelWindowPrank("RestoreUnconfirmed", true, true); return; }
            _prankSnapshot?.Dispose();
            _prankSnapshot = null;
            if (wearHatAfterReturn) BeginPrankPhase(PrankPhase.WearHat, "hat_wear");
            else FinishWindowPrank();
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (generation != _prankGeneration) return;
            DiagnosticsLog.WriteEvent("PrankRestoreFailed", ("Reason", exception.GetType().Name));
            CancelWindowPrank("RestoreFailed", true, true);
        }
        finally { if (generation == _prankGeneration) _prankAwaitingSystem = false; }
    }

    private void ThrowPrankHat()
    {
        var throwing = _prankArtwork!.Clip("hat_throw");
        var pickup = _prankArtwork.Clip("hat_pickup");
        var anchor = throwing.HatTransferAt(_animator.CurrentFrameIndex);
        _hatStart = CanvasPointToScreen(anchor.X, anchor.Y);
        var scale = PetCanvasPhysicalScale();
        _hatThrowWidth = throwing.HatTransferWidth * scale;
        _hatLandingWidth = pickup.HatTransferWidth * scale;
        _hatThrowRotation = throwing.HatTransferRotation;
        _hatLandingRotation = pickup.HatTransferRotation;
        _hatPhysicalWidth = _hatThrowWidth;
        _hatArcHeight = Math.Max(_hatThrowWidth * 1.15, 512 * scale * 0.45);
        var area = _prankTarget?.WorkArea ?? DisplayGeometry.FromWindow(_windowHandle)!.WorkArea;
        var margin = Math.Max(_hatThrowWidth, _hatLandingWidth);
        _hatRest = new Point(Math.Clamp(_prankApproachFoot.X + 512 * scale * 0.65,
            area.Left + margin, area.Right - margin * 0.6),
            Math.Min(area.Bottom - _hatLandingWidth * 0.25 - 1, _prankApproachFoot.Y - _hatLandingWidth * 0.22));
        _hatFlightElapsed = 0;
        _hatProp = new HatPropWindow(_prankArtwork.Hat);
        _needsHatRecovery = true;
        _animator.SetHatless(true);
        UpdateThrownHat(0);
    }

    private void UpdateThrownHat(double delta)
    {
        if (_hatProp is null) return;
        _hatFlightElapsed += delta;
        var t = Math.Clamp(_hatFlightElapsed / 0.85, 0, 1);
        _hatPhysicalWidth = Lerp(_hatThrowWidth, _hatLandingWidth, SmoothStep(t));
        var lift = Math.Sin(Math.PI * t) * _hatArcHeight;
        var bounce = _hatFlightElapsed is > 0.85 and < 1.1
            ? Math.Sin((_hatFlightElapsed - 0.85) / 0.25 * Math.PI) * _hatPhysicalWidth * 0.10 : 0;
        _hatProp.Place(new Point(Lerp(_hatStart.X, _hatRest.X, t), Lerp(_hatStart.Y, _hatRest.Y, t) - lift - bounce),
            _hatPhysicalWidth, Lerp(_hatThrowRotation, _hatLandingRotation, t));
    }

    private void StartHatRecovery()
    {
        ResetTearPresentation();
        if (!_animator.Hatless) { FinishWindowPrank(); return; }
        _prankStartFoot = PrankFoot();
        if (_hatProp is null)
        {
            BeginPrankCollectionOrWearHat();
            return;
        }
        var pickup = _prankArtwork!.Clip("hat_pickup");
        var anchor = pickup.HatTransferAt(pickup.HatTransferFrame ?? 4);
        var scale = PetCanvasPhysicalScale();
        _hatPickupFoot = new Point(_hatRest.X + (_character.Geometry.FootCanvasX - anchor.X) * scale,
            _hatRest.Y + (_character.Geometry.FootCanvasY - anchor.Y) * scale);
        var duration = Math.Clamp(Math.Abs(_prankStartFoot.X - _hatPickupFoot.X) / (250 * PetPhysicalScale()), 0.4, 3);
        BeginPrankPhase(PrankPhase.FetchHat, "bare_run", duration);
        FacingTransform.ScaleX = _hatPickupFoot.X < _prankStartFoot.X ? 1 : -1;
    }

    private bool TryResumeHatRecovery()
    {
        if (!_needsHatRecovery || !_character.UsesMischief || _isClosing || _prankArtwork is null || _sessionLocked)
            return false;
        StartHatRecovery();
        return true;
    }

    private void BeginPrankCollectionOrWearHat()
    {
        // A large prank is still collectable as long as the minimized snapshot
        // is owned by this controller.  IsHeld can briefly be false while the
        // hat-pickup animation yields to the safety check; gating on it made
        // tear/shatter remnants skip the hat and jump straight to hat_wear.
        if (_prankLarge && !_manualRestoreInProgress && _windowPranks is
            { SnapshotMinimized: true, IsCollected: false })
        {
            if (!_windowPranks.IsHeld && !HoldPrankResult())
            {
                DiagnosticsLog.WriteEvent("PrankRemainsCollectionSkipped", ("Reason", "hold_unconfirmed"));
                return;
            }
            DiagnosticsLog.WriteEvent("PrankRemainsCollectionStarted",
                ("Kind", _prankKind), ("WindowHeld", _windowPranks.IsHeld));
            BeginPrankPhase(PrankPhase.CollectRemains, "hat_store", 3.0);
            UpdateHatOpening();
            _windowPranks.SetCollectionProgress(0, ToDrawing(_prankHatMouth), _prankHatMouthWidth);
        }
        else BeginPrankPhase(PrankPhase.WearHat, "hat_wear");
    }

    private void FinishWindowPrank()
    {
        _behaviorHatOnly = false;
        ResetTearPresentation();
        ClearPrankImpact();
        if (!_prankPresentationFinished)
        {
            if (_featureDemoRunning) _featurePranksCompleted++;
            _mischief.Complete(_prankLarge);
            _prankPresentationFinished = true;
        }
        var held = _windowPranks?.IsHeld == true;
        var restored = _manualRestoreInProgress;
        _manualRestoreInProgress = false;
        _returnPrankRemainsFromHat = false;
        _prankPhase = PrankPhase.None;
        _prankSystemEngaged = false;
        if (!held)
        {
            _prankTarget = null;
            _prankSnapshot?.Dispose();
            _prankSnapshot = null;
        }
        _prankCancellation?.Dispose();
        _prankCancellation = null;
        CloseHatProp();
        _animator.SetHatless(false);
        _needsHatRecovery = false;
        _windowPranks?.SetForeground(null, DrawingRectangle.Empty);
        _previousWindowBodies.Clear();
        ResetSupportMotionTracking();
        ResetAirbornePlatformSupport();
        _trayIcon.SetWindowReturnAvailable(HasHeldPrank);
        _nextPrankSearch = _clock.Elapsed.TotalSeconds;
        SaveMischiefState();
        ReturnToIdle();
        if (!TryResolveGroundSupport(allowSnap: false)) BeginFall(reason: "PrankFinished");
        if (!restored) PlayMischiefVoice(MischiefCue.Finished);
        DiagnosticsLog.WriteEvent(restored ? "PrankManualRestoreCompleted" : "PrankCompleted",
            ("Kind", _prankKind), ("Value", _mischief.Value), ("AwaitingManualRestore", HasHeldPrank));
    }

    private void CancelWindowPrank(string reason, bool returnToIdle, bool keepHatless)
    {
        _behaviorHatOnly = false;
        ResetTearPresentation();
        ClearPrankImpact();
        if (WindowPrankHoldPolicy.MustReturnStoredResult(reason)) ReleaseStoredPrank();
        var wasActive = _prankPhase != PrankPhase.None || _windowPranks?.HasActiveOperation == true;
        var keepResult = WindowPrankHoldPolicy.KeepResultOnPetInterruption(reason) &&
            _windowPranks?.SnapshotMinimized == true && _windowPranks.HoldSnapshot();
        if (keepResult)
        {
            _windowPranks!.SetEffectProgress(1, ToDrawing(_prankKind == PrankKind.Hat ? _prankHatMouth : _screenStrikeContact),
                _prankHatMouthWidth);
            if (_prankLarge && reason != "ManualRestorePreparation" &&
                (_manualRestoreInProgress || _prankPhase == PrankPhase.CollectRemains || !keepHatless))
                _windowPranks.SetCollectionProgress(1, ToDrawing(_prankHatMouth), _prankHatMouthWidth);
        }
        if (wasActive && _featureDemoRunning && reason != "ManualRestorePreparation") _featurePrankFailure = reason;
        if (wasActive && _character.UsesMischief) _voice.Stop();
        _prankGeneration++;
        _prankCancellation?.Cancel();
        _prankCancellation?.Dispose();
        _prankCancellation = null;
        if (!keepResult)
        {
            _windowPranks?.Abort(reason);
            _prankSnapshot?.Dispose();
            _prankSnapshot = null;
            _prankTarget = null;
        }
        _windowPranks?.SetForeground(null, DrawingRectangle.Empty);
        _manualRestoreInProgress = false;
        _returnPrankRemainsFromHat = false;
        _prankPhase = PrankPhase.None;
        _prankAwaitingSystem = false;
        _prankSystemEngaged = false;
        _prankContactApplied = false;
        if (!keepHatless)
        {
            _needsHatRecovery = false;
            _animator.SetHatless(false);
            CloseHatProp();
        }
        _trayIcon?.SetWindowReturnAvailable(HasHeldPrank);
        if (!wasActive) return;
        if (!_prankPresentationFinished) _mischief.Interrupt();
        _previousWindowBodies.Clear();
        ResetSupportMotionTracking();
        _nextPrankSearch = _clock.Elapsed.TotalSeconds + 30;
        DiagnosticsLog.WriteEvent("PrankInterrupted", ("Reason", reason), ("WindowStillHeld", HasHeldPrank));
        if (IsBehaviorControlMode && _activeBehavior is not null && reason != "ManualRestorePreparation")
        {
            _behaviorRequestFailed = true;
            SetBehaviorControlStatus("动作已中断：" + reason + (HasHeldPrank ? " · 帽中窗口仍保留" : ""));
        }
        SaveMischiefState();
        if (returnToIdle && !_isClosing)
        {
            ReturnToIdle();
            if (_state == PetState.Idle && !TryResolveGroundSupport(false)) BeginFall(reason: "PrankInterrupted");
        }
    }

    private bool IsPrankControlledWindow(IntPtr handle) =>
        handle != IntPtr.Zero && (handle == _windowPranks?.ControlledHandle || handle == _prankTarget?.Handle ||
            handle == _storedPrank?.Controller.ControlledHandle);

    private void UpdatePrankForeground()
    {
        var overlayOwnsTear = _animator.CurrentAssetFolder == "bare_tear" &&
            _windowPranks is { SnapshotMinimized: true, IsHeld: false };
        PetSprite.Opacity = overlayOwnsTear ? 0 : 1;
        if (_prankArtwork is null || _windowPranks?.SnapshotMinimized != true ||
            (_windowPranks.IsHeld && !_manualRestoreInProgress && _prankPhase != PrankPhase.CollectRemains)) return;
        var foreground = _animator.CurrentAssetFolder == "bare_tear" ? _animator.CurrentFrame
            : _prankArtwork.Foreground(_animator.CurrentAssetFolder, _animator.CurrentFrameIndex);
        var topLeft = overlayOwnsTear ? TearCanvasPointToScreen(0, 0) : CanvasPointToScreen(0, 0);
        var bottomRight = overlayOwnsTear ? TearCanvasPointToScreen(512, 512) : CanvasPointToScreen(512, 512);
        _windowPranks.SetForeground(foreground, DrawingRectangle.FromLTRB((int)topLeft.X, (int)topLeft.Y,
            (int)bottomRight.X, (int)bottomRight.Y));
    }

    private void CloseHatProp()
    {
        var prop = _hatProp;
        _hatProp = null;
        if (prop is null) return;
        try { prop.CloseSafely(); }
        catch (InvalidOperationException exception)
        {
            DiagnosticsLog.Write("Hat prop was already closing.", exception);
        }
    }

    private void ShowPrankImpact()
    {
        _prankImpactElapsed = 0;
        PrankImpact.Visibility = Visibility.Visible;
        UpdatePrankImpact(0);
        DiagnosticsLog.WriteEvent("PrankImpact", ("Kind", _prankKind),
            ("PauseSeconds", CurrentStrikeTiming?.ImpactPauseSeconds));
    }

    private void UpdatePrankImpact(double delta)
    {
        if (_prankImpactElapsed < 0) return;
        _prankImpactElapsed += delta;
        var progress = _prankImpactElapsed / 0.18;
        if (progress >= 1) { ClearPrankImpact(); return; }
        var origin = CanvasPointToScreen(0, 0);
        var scale = Math.Max(0.01, PetCanvasPhysicalScale());
        PrankImpactTranslate.X = (_screenStrikeContact.X - origin.X) / scale;
        PrankImpactTranslate.Y = (_screenStrikeContact.Y - origin.Y) / scale;
        if (_prankKind == PrankKind.Tear && _tearNormalSize > 0)
        {
            var contact = PetSprite.PointFromScreen(_screenStrikeContact);
            PrankImpactTranslate.X = contact.X;
            PrankImpactTranslate.Y = contact.Y;
        }
        PrankImpactScale.ScaleX = PrankImpactScale.ScaleY = 0.8 + 0.5 * progress;
        PrankImpact.Opacity = 1 - progress * progress;
    }

    private void ClearPrankImpact()
    {
        _prankImpactElapsed = -1;
        if (PrankImpact is not null) PrankImpact.Visibility = Visibility.Collapsed;
    }
    private Point PrankFoot() => CanvasPointToScreen(_character.Geometry.FootCanvasX, _character.Geometry.FootCanvasY);
    private double PetPhysicalScale() => DisplayGeometry.FromWindow(_windowHandle)?.Scale ?? 1;
    private double PetCanvasPhysicalScale() => Math.Abs(CanvasPointToScreen(512, 0).X - CanvasPointToScreen(0, 0).X) / 512;
    private static DrawingPoint ToDrawing(Point point) => new((int)Math.Round(point.X), (int)Math.Round(point.Y));

    private void MovePrankFoot(Point desired)
    {
        var current = PrankFoot();
        DisplayGeometry.OffsetWindowPhysical(_windowHandle,
            (int)Math.Round(desired.X - current.X), (int)Math.Round(desired.Y - current.Y));
    }

    private void DisposeWindowPranks()
    {
        ReleaseStoredPrank();
        _windowPranks?.Dispose();
        _windowPranks = null;
        CloseHatProp();
        if (!_sessionEventsSubscribed) return;
        SystemEvents.SessionSwitch -= OnPrankSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPrankPowerModeChanged;
        _sessionEventsSubscribed = false;
    }
}
