using System.Drawing;
using SoftMochiPet.Core;
using SoftMochiPet.Interop;
using SoftMochiPet.Services;

namespace SoftMochiPet;

public partial class MainWindow
{
    private const double IconKickContactSeconds = 0.84;
    private const double IconKickFlightSeconds = 0.80;

    private PairIconKickLayout? _pairIconLayout;
    private Point _pairIconOrigin;
    private double _pairIconApproachSeconds;
    private double _pairIconRetryAt;
    private bool _pairIconLaunched;
    private bool _pairIconConsumed;
    private bool _pairIconWaiting;
    private double _pairIconWaitElapsed;

    private bool TryStartPairIconMeal(
        MainWindow nuonuo, DeletedItemInfo meal, GhostIconWindow ghost, Point target)
    {
        if (_primaryWindow is not null || _companionWindow is not { } companion) return false;
        bool Skip(string reason)
        {
            DiagnosticsLog.WriteEvent("PairIconKickSkipped", ("QueueId", meal.QueueId), ("Reason", reason));
            return false;
        }
        if (_pairScene is not null || _clock.Elapsed.TotalSeconds < _pairIconRetryAt ||
            _isClosing || companion._isClosing || _settings.QuietMode ||
            !_settings.ReactToDeletes)
            return Skip("ModeOrInteractionBusy");
        if (!meal.ExactDesktopAnchor || !ghost.IsGhostVisible)
            return Skip(!meal.ExactDesktopAnchor ? "NoExactDesktopAnchor" : "GhostUnavailable");

        var feibi = _character.UsesMischief ? this : companion;
        var readiness = feibi.IconPartnerReadinessNow();
        if (!ReferenceEquals(nuonuo, _character.SupportsFood ? this : companion) ||
            readiness == IconPartnerReadiness.Unavailable ||
            nuonuo._state is not (PetState.Idle or PetState.Curious or PetState.Licking or
                PetState.Running or PetState.Waking or PetState.Satisfied or PetState.Landing) ||
            nuonuo._isDragging ||
            nuonuo._sessionLocked || feibi._sessionLocked ||
            nuonuo._windowHandle == IntPtr.Zero || feibi._windowHandle == IntPtr.Zero ||
            !NativeMethods.GetWindowRect(nuonuo._windowHandle, out var nBounds) ||
            !NativeMethods.GetWindowRect(feibi._windowHandle, out var fBounds))
            return Skip($"PetUnavailable:{nuonuo._state}/{feibi._state}/{feibi._prankPhase}");

        var monitor = DisplayGeometry.FromPoint(target, nearest: false);
        if (monitor is null ||
            DisplayGeometry.MonitorHandleFromWindow(nuonuo._windowHandle) != monitor.Handle ||
            DisplayGeometry.MonitorHandleFromWindow(feibi._windowHandle) != monitor.Handle)
            return Skip("DifferentMonitor");

        var nSize = DisplayGeometry.ProjectDipSize(nuonuo.Width, nuonuo.Height, monitor);
        var fSize = DisplayGeometry.ProjectDipSize(feibi.Width, feibi.Height, monitor);
        var mouthAnchor = PairSpriteGeometry.Map(nuonuo._character, "pair_feed", 9, 290, 240);
        var kickAnchor = PairSpriteGeometry.Map(feibi._character, "pair_icon_kick", 8, 80, 370);
        var mouth = nuonuo.ProjectCanvasAnchorOffset(mouthAnchor.X, mouthAnchor.Y, monitor);
        var kick = feibi.ProjectCanvasAnchorOffset(kickAnchor.X, kickAnchor.Y, monitor);
        var gap = (int)Math.Round(1.12 * Math.Max(nSize.Width, fSize.Width));
        var layout = PairIconKickPlanner.Plan(monitor.WorkArea, target, nSize, fSize,
            mouth, kick, gap);
        if (layout is null)
        {
            return Skip("NoSafePlacement");
        }

        CancelPairDialogue();
        _pairIconLayout = layout;
        _pairIconOrigin = target;
        _pairIconLaunched = false;
        _pairIconConsumed = false;
        _pairNuonuo = nuonuo;
        _pairFeibi = feibi;
        _pairNuonuoPosition = new PairPosition(nBounds.Left, nBounds.Top,
            layout.NuonuoTopLeft.X, layout.NuonuoTopLeft.Y);
        _pairFeibiPosition = new PairPosition(fBounds.Left, fBounds.Top,
            layout.FeibiTopLeft.X, layout.FeibiTopLeft.Y);
        _pairSceneElapsed = 0;
        _pairActionStarted = false;
        _pairScene = PairScene.IconKick;
        nuonuo._currentMealNutritionApplied = false;
        _pairIconWaitElapsed = 0;
        _pairIconWaiting = readiness == IconPartnerReadiness.Wait;
        if (_pairIconWaiting)
        {
            PreparePairActor(nuonuo, "pair_feed_wait");
            if (feibi._state == PetState.Sleeping) feibi.BeginWake();
            DiagnosticsLog.WriteEvent("PairIconKickWaiting", ("QueueId", meal.QueueId),
                ("FeibiState", feibi._state), ("TimeoutSeconds", PairInteractionPolicy.IconPartnerWaitSeconds));
        }
        else BeginPairIconApproach(monitor.Scale);
        return true;
    }

    private IconPartnerReadiness IconPartnerReadinessNow() => PairInteractionPolicy.IconPartner(
        _state, _movementPurpose, _isDragging, _animator.Hatless || _needsHatRecovery,
        _prankPhase != PrankPhase.None || _manualMischiefPending,
        _windowPranks?.HasActiveOperation == true, _windowPranks?.IsHeld == true);

    private void BeginPairIconApproach(double scale)
    {
        var nuonuo = _pairNuonuo!;
        var feibi = _pairFeibi!;
        var layout = _pairIconLayout!;
        _pairIconWaiting = false;
        _pairIconApproachSeconds = PairApproachDuration(_pairNuonuoPosition!, _pairFeibiPosition!, scale);
        _pairSceneElapsed = 0;
        feibi.ParkHeldPrank();
        PreparePairActor(nuonuo, "run");
        PreparePairActor(feibi, "run");
        DiagnosticsLog.WriteEvent("PairIconKickStarted", ("QueueId", nuonuo._currentFood?.QueueId),
            ("WaitedSeconds", _pairIconWaitElapsed),
            ("Rebound", layout.Rebound), ("Stage", $"{layout.Stage.X},{layout.Stage.Y}"),
            ("Mouth", $"{layout.Mouth.X},{layout.Mouth.Y}"));
    }

    private void UpdatePairIconWait(double delta)
    {
        if (!_pairIconWaiting || _pairNuonuo is not { } nuonuo || _pairFeibi is not { } feibi) return;
        _pairIconWaitElapsed += delta;
        var readiness = feibi.IconPartnerReadinessNow();
        if (nuonuo._isClosing || feibi._isClosing || nuonuo._isDragging || nuonuo._sessionLocked || feibi._sessionLocked ||
            readiness == IconPartnerReadiness.Unavailable || nuonuo._currentGhostIconWindow?.IsGhostVisible != true)
        {
            CancelPairIconMeal("PartnerUnavailable");
            return;
        }
        if (_pairIconWaitElapsed >= PairInteractionPolicy.IconPartnerWaitSeconds)
        {
            CancelPairIconMeal("PartnerWaitTimeout");
            return;
        }
        nuonuo._animator.Tick(FrameTiming.FromElapsed(delta).AnimationSeconds);
        if (readiness != IconPartnerReadiness.Ready) return;
        var monitor = DisplayGeometry.FromPoint(_pairIconOrigin, nearest: false);
        if (monitor is null ||
            DisplayGeometry.MonitorHandleFromWindow(nuonuo._windowHandle) != monitor.Handle ||
            DisplayGeometry.MonitorHandleFromWindow(feibi._windowHandle) != monitor.Handle ||
            !NativeMethods.GetWindowRect(nuonuo._windowHandle, out var nBounds) ||
            !NativeMethods.GetWindowRect(feibi._windowHandle, out var fBounds))
        {
            CancelPairIconMeal("PlacementChanged");
            return;
        }
        _pairNuonuoPosition = _pairNuonuoPosition! with { StartLeft = nBounds.Left, StartTop = nBounds.Top };
        _pairFeibiPosition = _pairFeibiPosition! with { StartLeft = fBounds.Left, StartTop = fBounds.Top };
        BeginPairIconApproach(monitor.Scale);
    }

    private void UpdatePairIconMeal(double delta)
    {
        if (_pairIconWaiting) return;
        if (_pairNuonuo is not { } nuonuo || _pairFeibi is not { } feibi ||
            _pairNuonuoPosition is not { } nPosition ||
            _pairFeibiPosition is not { } fPosition ||
            _pairIconLayout is not { } layout ||
            !_pairIconConsumed && nuonuo._currentGhostIconWindow?.IsGhostVisible != true)
        {
            CancelPairIconMeal("VisualUnavailable");
            return;
        }
        if (nuonuo._isClosing || feibi._isClosing || nuonuo._isDragging || feibi._isDragging)
        {
            CancelPairIconMeal("PetUnavailable");
            return;
        }

        try
        {
            _pairSceneElapsed += delta;
            var approach = Math.Min(1, _pairSceneElapsed / _pairIconApproachSeconds);
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
                nuonuo._animator.Play("pair_feed_wait");
                feibi._animator.Play("pair_icon_kick");
                _pairActionStarted = true;
            }

            var time = _pairSceneElapsed - _pairIconApproachSeconds;
            feibi._animator.AdvanceTo(time);
            if (time < 0.55)
                nuonuo._animator.Tick(delta);
            else if (time < 1.04)
            {
                if (nuonuo._animator.CurrentClip != "pair_feed_ready")
                    nuonuo._animator.Play("pair_feed_ready");
                nuonuo._animator.AdvanceTo(time - 0.55);
            }
            else
            {
                if (nuonuo._animator.CurrentClip != "pair_feed_catch")
                    nuonuo._animator.Play("pair_feed_catch");
                nuonuo._animator.AdvanceTo(time - 1.04);
            }

            // This catch clip already includes swallowing and chewing. Retain
            // ownership after the ghost closes instead of restarting solo suction.
            if (_pairIconConsumed)
            {
                if (time >= 1.04 + 10 * .175) CancelPairIconMeal("Consumed", consumed: true);
                return;
            }
            var ghost = nuonuo._currentGhostIconWindow!;
            if (time < IconKickContactSeconds)
            {
                var settle = PairEase(Math.Clamp(time / 0.65, 0, 1));
                var stagedX = _pairIconOrigin.X + (layout.Stage.X - _pairIconOrigin.X) * settle;
                var y = _pairIconOrigin.Y + (layout.Stage.Y - _pairIconOrigin.Y) * settle;
                ghost.SetVisual(stagedX, y, 1, 1, 0, 1);
                feibi.SquashTransform.ScaleY = 1 - 0.055 *
                    Math.Sin(Math.PI * Math.Clamp(time / IconKickContactSeconds, 0, 1));
                return;
            }

            if (!_pairIconLaunched)
            {
                _pairIconLaunched = true;
                ghost.SetLabelOpacity(0);
                DiagnosticsLog.WriteEvent("PairIconKickImpact",
                    ("QueueId", nuonuo._currentFood?.QueueId), ("Rebound", layout.Rebound));
            }
            var flight = Math.Clamp((time - IconKickContactSeconds) / IconKickFlightSeconds, 0, 1);
            double x;
            if (layout.Rebound && flight < 0.27)
                x = layout.Stage.X + (layout.ReboundX - layout.Stage.X) * PairEase(flight / 0.27);
            else if (layout.Rebound)
                x = layout.ReboundX + (layout.Mouth.X - layout.ReboundX) *
                    PairEase((flight - 0.27) / 0.73);
            else
                x = layout.Stage.X + (layout.Mouth.X - layout.Stage.X) * PairEase(flight);
            var yFlight = layout.Stage.Y - 46 * Math.Sin(Math.PI * flight);
            var scale = 1 - 0.58 * PairEase(flight);
            ghost.SetVisual(x, yFlight, scale, scale, -290 * flight, 1);
            feibi.LeanTransform.Angle = -3 * Math.Sin(Math.PI * flight);

            if (flight >= 1)
            {
                _pairIconConsumed = true;
                nuonuo.CompleteMealConsumption();
                nuonuo._voice.Play(VoiceCue.Positive, VoicePriority.Important, TimeSpan.FromSeconds(6));
                feibi.LeanTransform.Angle = 0;
            }
        }
        catch (InvalidOperationException exception)
        {
            DiagnosticsLog.Write("Pair icon visual became unavailable; returning to solo meal.", exception);
            CancelPairIconMeal("VisualClosed");
        }
    }

    private void CancelPairIconMeal(string reason, bool consumed = false)
    {
        consumed |= _pairIconConsumed;
        var nuonuo = _pairNuonuo;
        var feibi = _pairFeibi;
        var queueId = nuonuo?._currentFood?.QueueId;
        var ghost = nuonuo?._currentGhostIconWindow;
        _pairScene = null;
        _pairNuonuo = null;
        _pairFeibi = null;
        _pairNuonuoPosition = null;
        _pairFeibiPosition = null;
        _pairIconLayout = null;
        _pairIconLaunched = false;
        _pairIconConsumed = false;
        _pairIconWaiting = false;
        _pairActionStarted = false;
        _nextPairSceneAt = _clock.Elapsed.TotalSeconds + PairInteractionPolicy.NextInvitationDelay(Random.Shared.NextDouble());
        if (!consumed) _pairIconRetryAt = _clock.Elapsed.TotalSeconds + 8;

        if (feibi is { _isClosing: false } && feibi._state == PetState.Interacting)
            feibi.ReturnToIdle();
        if (nuonuo is { _isClosing: false } && nuonuo._currentFood is { } meal)
        {
            if (consumed)
            {
                if (nuonuo._state == PetState.Interacting) nuonuo.ReturnToIdle();
            }
            else
            {
                try
                {
                    if (ghost?.IsGhostVisible != true)
                    {
                        CloseGhostWindow(ghost);
                        ghost = nuonuo.CreateWaitingGhost(meal);
                    }
                    ghost?.SetLabelOpacity(1);
                    ghost?.SetVisual(_pairIconOrigin.X, _pairIconOrigin.Y, 1, 1, 0, 1);
                }
                catch (InvalidOperationException)
                {
                    CloseGhostWindow(ghost);
                    ghost = null;
                }
                nuonuo._foodQueue.EnqueueFirst(meal, ghost);
                nuonuo._currentFood = null;
                nuonuo._currentGhostIconWindow = null;
                nuonuo._usingGhostIcon = false;
                nuonuo._currentMealNutritionApplied = false;
                if (nuonuo._state == PetState.Interacting) nuonuo.ReturnToIdle();
            }
        }
        else if (!consumed)
        {
            CloseGhostWindow(ghost);
            if (nuonuo is { _isClosing: false, _state: PetState.Interacting })
                nuonuo.ReturnToIdle();
        }
        DiagnosticsLog.WriteEvent("PairIconKickEnded", ("QueueId", queueId),
            ("Reason", reason), ("Consumed", consumed));
    }
}
