using SoftMochiPet.Core;
using SoftMochiPet.Interop;
using SoftMochiPet.Services;

namespace SoftMochiPet;

public partial class MainWindow
{
    private enum PairScene { Cheek, Sleep, Feed, IconKick, Follow, Nuzzle, Ball, Watch }

    private sealed record PairPosition(int StartLeft, int StartTop, int TargetLeft, int TargetTop);

    private PairScene? _pairScene;
    private MainWindow? _pairNuonuo;
    private MainWindow? _pairFeibi;
    private PairPosition? _pairNuonuoPosition;
    private PairPosition? _pairFeibiPosition;
    private GhostIconWindow? _pairTreatWindow;
    private string? _pairTreatName;
    private double _pairSceneElapsed;
    private double _nextPairSceneAt = PairInteractionPolicy.FirstInvitationSeconds;
    private double _pairApproachSeconds;
    private CompanionActivity? _previousPairActivity;
    private bool _pairActionStarted;
    private bool _pairSleepHolding;

    private static double PairEase(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    private void TryStartAutonomousPair(double now)
    {
        if (_primaryWindow is not null || _companionWindow is null || _pairScene is not null ||
            _pairDialogueStep != 0 || _manualPairDialogueUntil > now ||
            IsFeatureTestMode || IsBehaviorControlMode) return;
        var nuonuo = _character.SupportsFood ? this : _companionWindow;
        var feibi = _character.UsesMischief ? this : _companionWindow;
        ObserveCompanionWindows(now);
        var needsRestOrFood = Math.Max(nuonuo._lifeState.Sleepiness, feibi._lifeState.Sleepiness) >= 70 ||
            !EffectiveFastingMode && nuonuo._lifeState.Hunger >= 55;
        if (!needsRestOrFood && TryStartContextualPair(now, nuonuo, feibi)) return;
        if (now < _nextPairSceneAt) return;
        var activity = PairInteractionPolicy.ChooseActivity(_settings.QuietMode, EffectiveFastingMode,
            nuonuo._lifeState.Hunger, nuonuo._lifeState.Sleepiness, feibi._lifeState.Sleepiness,
            _previousPairActivity, Random.Shared.NextDouble());
        var scene = activity switch
        {
            CompanionActivity.Feed => PairScene.Feed,
            CompanionActivity.Sleep => PairScene.Sleep,
            _ => PairScene.Cheek,
        };
        if (!_settings.QuietMode && !needsRestOrFood && now >= _nextPairBallAt &&
            TryStartPairScene(PairScene.Ball)) return;
        if (!TryStartPairScene(scene)) _nextPairSceneAt = now + PairInteractionPolicy.InvitationRetrySeconds;
    }

    private void StartPairSceneFromMenu(string sceneName)
    {
        var owner = _primaryWindow ?? this;
        var scene = sceneName switch
        {
            "cheek" => PairScene.Cheek,
            "sleep" => PairScene.Sleep,
            "feed" => PairScene.Feed,
            "follow" => PairScene.Follow,
            "nuzzle" => PairScene.Nuzzle,
            "ball" => PairScene.Ball,
            "watch" => PairScene.Watch,
            _ => throw new ArgumentOutOfRangeException(nameof(sceneName)),
        };
        if (!owner.TryStartPairScene(scene))
            DiagnosticsLog.WriteEvent("PairSceneRejected", ("Scene", scene),
                ("Reason", "BothPetsMustBeAvailable"));
    }

    private bool TryStartPairScene(PairScene scene)
    {
        if (_primaryWindow is not null || _companionWindow is not { } companion || _pairScene is not null ||
            _isClosing || companion._isClosing || _settings.QuietMode && scene != PairScene.Sleep)
            return false;
        var nuonuo = _character.SupportsFood ? this : companion;
        var feibi = _character.UsesMischief ? this : companion;
        if (!PairInteractionPolicy.CanInvite(nuonuo._state, nuonuo._movementPurpose, scene == PairScene.Sleep) ||
            !PairInteractionPolicy.CanInvite(feibi._state, feibi._movementPurpose, scene == PairScene.Sleep) ||
            nuonuo._isDragging || feibi._isDragging || nuonuo._foodQueue.Count > 0 ||
            nuonuo._currentFood is not null || feibi._manualMischiefPending ||
            feibi._animator.Hatless || feibi._needsHatRecovery ||
            feibi._prankPhase != PrankPhase.None ||
            feibi._windowPranks is { HasActiveOperation: true, IsHeld: false } ||
            nuonuo._windowHandle == IntPtr.Zero || feibi._windowHandle == IntPtr.Zero ||
            nuonuo._sessionLocked || feibi._sessionLocked ||
            !NativeMethods.GetWindowRect(nuonuo._windowHandle, out var nBounds) ||
            !NativeMethods.GetWindowRect(feibi._windowHandle, out var fBounds))
        {
            DiagnosticsLog.WriteEventThrottled("pair-invitation-wait", TimeSpan.FromSeconds(20),
                "PairSceneWaiting", ("Scene", scene), ("NuonuoState", nuonuo._state),
                ("FeibiState", feibi._state), ("Meals", nuonuo._foodQueue.Count),
                ("PrankPhase", feibi._prankPhase));
            return false;
        }

        if (scene == PairScene.Feed) return StartRemotePairFeed(nuonuo, feibi, nBounds, fBounds);
        var monitor = DisplayGeometry.FromWindow(_windowHandle);
        if (monitor is null || DisplayGeometry.MonitorHandleFromWindow(companion._windowHandle) != monitor.Handle)
            return false;
        if (IsPairLifeScene(scene))
            return StartPairLifeScene(scene, nuonuo, feibi, nBounds, fBounds, monitor);
        var nFoot = nuonuo.ProjectCanvasAnchorOffset(nuonuo._character.Geometry.FootCanvasX,
            nuonuo._character.Geometry.FootCanvasY, monitor);
        var fFoot = feibi.ProjectCanvasAnchorOffset(feibi._character.Geometry.FootCanvasX,
            feibi._character.Geometry.FootCanvasY, monitor);
        var gap = (int)Math.Round(nuonuo.Width * monitor.Scale * (scene switch
        {
            PairScene.Sleep => 0.42,
            _ => 0.55,
        }));
        var centerMin = monitor.WorkArea.Left + gap / 2 + nFoot.X;
        var feibiWidth = (int)Math.Round(feibi.Width * monitor.Scale);
        var centerMax = monitor.WorkArea.Right - gap / 2 - (feibiWidth - fFoot.X);
        if (centerMin > centerMax) return false;
        CancelPairDialogue();
        var center = Math.Clamp((nBounds.Left + fBounds.Left) / 2 + nFoot.X,
            centerMin, centerMax);
        var floorY = monitor.WorkArea.Bottom - 1;
        _pairNuonuoPosition = new PairPosition(nBounds.Left, nBounds.Top,
            center - gap / 2 - nFoot.X, floorY - nFoot.Y);
        _pairFeibiPosition = new PairPosition(fBounds.Left, fBounds.Top,
            center + gap / 2 - fFoot.X, floorY - fFoot.Y);
        _pairNuonuo = nuonuo;
        _pairFeibi = feibi;
        _pairScene = scene;
        _previousPairActivity = scene switch
        {
            PairScene.Sleep => CompanionActivity.Sleep,
            _ => CompanionActivity.Cheek,
        };
        _pairApproachSeconds = PairApproachDuration(_pairNuonuoPosition, _pairFeibiPosition, monitor.Scale);
        _pairSceneElapsed = 0;
        _pairActionStarted = false;
        _pairSleepHolding = false;
        _pairTreatName = null;
        feibi.ParkHeldPrank();
        PreparePairActor(nuonuo, "run");
        PreparePairActor(feibi, "run");
        DiagnosticsLog.WriteEvent("PairSceneStarted", ("Scene", scene), ("Treat", _pairTreatName));
        return true;
    }

    private static double PairApproachDuration(PairPosition nuonuo, PairPosition feibi, double scale)
    {
        static double Distance(PairPosition p) => Math.Sqrt(
            Math.Pow(p.StartLeft - p.TargetLeft, 2) + Math.Pow(p.StartTop - p.TargetTop, 2));
        return PairInteractionPolicy.ApproachSeconds(Math.Max(Distance(nuonuo), Distance(feibi)), scale);
    }

    private static void PreparePairActor(MainWindow pet, string clip)
    {
        pet._state = PetState.Interacting;
        pet._movementPurpose = MovementPurpose.None;
        pet._stateElapsed = 0;
        pet._velocityX = pet._velocityY = 0;
        pet._movementPixelRemainderX = pet._movementPixelRemainderY = pet._fallPixelRemainder = 0;
        pet._supportHandle = IntPtr.Zero;
        pet._supportIsDesktopFloor = false;
        pet._pendingClimb = pet._activeClimb = null;
        pet._slideSurface = null;
        pet.ClearWindowEnclosure();
        pet.ResetSupportMotionTracking();
        pet.ResetAirbornePlatformSupport();
        pet.SquashTransform.ScaleX = pet.SquashTransform.ScaleY = 1;
        pet.LeanTransform.Angle = 0;
        pet.FacingTransform.ScaleX = 1;
        pet._animator.Play(clip);
    }

    private void UpdatePairScene(double delta)
    {
        if (_pairScene is not { } scene || _pairNuonuo is not { } nuonuo ||
            _pairFeibi is not { } feibi || _pairNuonuoPosition is not { } nPosition ||
            _pairFeibiPosition is not { } fPosition) return;
        foreach (var pet in new[] { nuonuo, feibi })
        {
            if (pet._state != PetState.Interacting) continue;
            pet._lifeState.Advance(TimeSpan.FromSeconds(delta), scene == PairScene.Sleep && _pairSleepHolding);
            pet.NormalizeCharacterNeeds();
            pet.UpdateHungerDisplay();
        }
        if (scene == PairScene.IconKick)
        {
            UpdatePairIconMeal(delta);
            return;
        }
        if (nuonuo._isClosing || feibi._isClosing || nuonuo._isDragging || feibi._isDragging)
        {
            CancelPairScene("PetUnavailable");
            return;
        }
        if (scene == PairScene.Feed)
        {
            UpdateRemotePairFeed(delta, nuonuo, feibi);
            return;
        }
        if (IsPairLifeScene(scene))
        {
            UpdatePairLifeScene(delta, scene, nuonuo, feibi);
            return;
        }
        _pairSceneElapsed += delta;
        var approach = Math.Min(1, _pairSceneElapsed / _pairApproachSeconds);
        if (approach < 1)
        {
            nuonuo._animator.Tick(delta);
            feibi._animator.Tick(delta);
            MovePairPet(nuonuo, nPosition, approach);
            MovePairPet(feibi, fPosition, approach);
            return;
        }
        if (!_pairActionStarted)
        {
            MovePairPet(nuonuo, nPosition, 1);
            MovePairPet(feibi, fPosition, 1);
            var clip = scene == PairScene.Cheek ? "pair_cheek" : "pair_sleep_enter";
            nuonuo._animator.Play(clip);
            feibi._animator.Play(clip);
            _pairActionStarted = true;
        }
        var actionTime = _pairSceneElapsed - _pairApproachSeconds;
        if (scene == PairScene.Sleep && actionTime >= 2.64 && !_pairSleepHolding)
        {
            nuonuo._animator.Play("pair_sleep_hold");
            feibi._animator.Play("pair_sleep_hold");
            _pairSleepHolding = true;
        }
        var playback = _pairSleepHolding ? actionTime - 2.64 : actionTime;
        nuonuo._animator.AdvanceTo(playback);
        feibi._animator.AdvanceTo(playback);
        ApplyPairPose(scene, actionTime, nuonuo, feibi);
        var duration = scene == PairScene.Cheek ? 2.7 : 18.2;
        if (actionTime < duration) return;
        CancelPairScene("Completed");
    }

    private static void ApplyPairPose(PairScene scene, double time, MainWindow nuonuo, MainWindow feibi)
    {
        if (scene == PairScene.Sleep)
        {
            var breath = Math.Sin(Math.Max(0, time - 2.64) * 2.8);
            nuonuo.SquashTransform.ScaleY = 1 + .012 * breath;
            feibi.SquashTransform.ScaleY = 1 + .012 * breath;
            return;
        }
        if (scene == PairScene.Cheek)
        {
            var pull = Math.Sin(Math.PI * Math.Clamp((time - .75) / 1.55, 0, 1));
            nuonuo.SquashTransform.ScaleX = 1 + .038 * pull;
            nuonuo.SquashTransform.ScaleY = 1 - .025 * pull;
            feibi.LeanTransform.Angle = -2.2 * pull;
            return;
        }
    }

    private static void MovePairPet(MainWindow pet, PairPosition position, double progress)
    {
        pet.FacingTransform.ScaleX = progress < 1 && position.TargetLeft < position.StartLeft ? -1 : 1;
        var eased = PairEase(progress);
        var left = (int)Math.Round(position.StartLeft + (position.TargetLeft - position.StartLeft) * eased);
        var top = (int)Math.Round(position.StartTop + (position.TargetTop - position.StartTop) * eased);
        DisplayGeometry.MoveWindowPhysical(pet._windowHandle, left, top);
    }

    private System.Windows.Point PairPointToScreen(string folder, int frame, double x, double y)
    {
        var point = PairSpriteGeometry.Map(_character, folder, frame, x, y);
        return PetSprite.PointToScreen(point);
    }

    private void CancelPairScene(string reason)
    {
        var owner = _primaryWindow ?? this;
        if (!ReferenceEquals(owner, this)) { owner.CancelPairScene(reason); return; }
        if (_pairScene is null) return;
        if (_pairScene == PairScene.IconKick)
        {
            CancelPairIconMeal(reason);
            return;
        }
        var treat = _pairTreatWindow;
        _pairTreatWindow = null;
        CloseGhostWindow(treat);
        _feedNuonuoSupport = _feedFeibiSupport = null;
        _pairLifeLane = null;
        _nextPairContextAt = _clock.Elapsed.TotalSeconds + CompanionLifePolicy.ContextGap;
        var nuonuo = _pairNuonuo;
        var feibi = _pairFeibi;
        DiagnosticsLog.WriteEvent("PairSceneEnded", ("Scene", _pairScene), ("Reason", reason));
        _pairScene = null;
        _pairNuonuo = null;
        _pairFeibi = null;
        _pairNuonuoPosition = null;
        _pairFeibiPosition = null;
        _pairTreatName = null;
        _nextPairSceneAt = _clock.Elapsed.TotalSeconds + PairInteractionPolicy.NextInvitationDelay(Random.Shared.NextDouble());
        // Clear scene ownership before ReturnToIdle can start a queued deletion.
        foreach (var pet in new[] { feibi, nuonuo })
            if (pet is { _isClosing: false, _state: PetState.Interacting })
            {
                pet.ReturnToIdle();
                if (pet._state == PetState.Idle && !pet.TryResolveGroundSupport(allowSnap: false))
                    pet.BeginFall(reason: "PairSceneEnded");
                if (reason == "Completed")
                {
                    pet._nextAutonomousDecision = pet._clock.Elapsed.TotalSeconds + 6;
                    pet._nextPrankSearch = Math.Max(pet._nextPrankSearch, pet._clock.Elapsed.TotalSeconds + 6);
                }
                pet.SaveLifeState();
            }
    }
}
