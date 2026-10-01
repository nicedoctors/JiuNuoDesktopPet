using System.Diagnostics;
using System.IO;
using System.Text.Json;
using SoftMochiPet.Core;
using SoftMochiPet.Interop;
using SoftMochiPet.Services;
using DrawingRectangle = System.Drawing.Rectangle;
using Point = System.Windows.Point;

namespace SoftMochiPet;

public partial class MainWindow
{
    private readonly FeatureDemoSession? _featureSession;
    private FeatureTestTargetClient? _featureTarget;
    private CancellationTokenSource? _featureCancellation;
    private TaskCompletionSource<bool>? _featureRetry;
    private bool _featureDemoRunning;
    private bool _featureStopRequested;
    private string _featureStopReason = "UserStopped";
    private int _featurePranksCompleted;
    private string? _featurePrankFailure;
    private FeatureDemoStep? _featureCurrentStep;
    private FeatureDemoProof? _featureProof;
    private string? _featurePhaseHint;
    private Stopwatch? _featureManualWait;
    private FeatureManualRestoreObservation? _featureManualRestore;
    private bool _featureHatlessInterruptionObserved;
    private bool? _featureRemainsStoredBeforeReturn;
    private readonly List<FeatureDemoResult> _featureResults = [];
    private MonitorGeometry? _featureMonitor;
    private DrawingRectangle _featureWindowBounds;

    private sealed record FeatureDemoProof(string Operation, string Window, uint ProcessId,
        int ShiftX, int ShiftY, bool MinimizeConfirmed, bool RestoreConfirmed, bool HatRecovered,
        bool? HatlessInterruptionObserved, bool? RemainsStoredBeforeReturn);
    private sealed record FeatureDemoResult(string Step, string Title, int Attempt, string Result,
        double Seconds, string? Reason, string Phase, FeatureDemoProof? Evidence,
        FeatureManualRestoreObservation? ManualRestore);
    private sealed record FeatureManualRestoreObservation(string State, string Window, uint ProcessId,
        double HeldSeconds, DateTimeOffset? RequestedAt);
    public bool IsFeatureTestMode => _featureSession is not null;

    private async Task RunFeatureDemoAsync()
    {
        if (!IsFeatureTestMode || _featureDemoRunning || _featureStopRequested || _isClosing) return;
        _featureDemoRunning = true;
        _featureCancellation = new CancellationTokenSource();
        var token = _featureCancellation.Token;
        var originalSize = _preferredPetSize;
        try
        {
            _featureMonitor = DisplayGeometry.FromWindow(_windowHandle)
                ?? throw new InvalidOperationException("monitor_unavailable");
            var area = _featureMonitor.WorkArea;
            var scale = _featureMonitor.Scale;
            ChangePetSize(Math.Min(originalSize, Math.Clamp(area.Width / scale * 0.19, 132, 220)));
            var width = (int)Math.Clamp(area.Width * 0.42, 300 * scale, 650 * scale);
            var height = (int)Math.Clamp(area.Height * 0.28, 210 * scale, 320 * scale);
            _featureWindowBounds = new DrawingRectangle(area.Left + (int)(area.Width * 0.10),
                area.Bottom - height - (int)(60 * scale), width, height);
            DiagnosticsLog.WriteEvent("FeatureDemoStarted", ("Revision", FeatureDemoPlan.Revision),
                ("Steps", FeatureDemoPlan.Steps.Count), ("ReportDirectory", _featureSession!.DirectoryPath));

            foreach (var step in FeatureDemoPlan.Steps)
            {
                _featureCurrentStep = step;
                var attempt = 0;
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    attempt++;
                    _featureProof = null;
                    _featurePhaseHint = null;
                    _featureManualWait = null;
                    _featureManualRestore = null;
                    _featureHatlessInterruptionObserved = false;
                    _featureRemainsStoredBeforeReturn = null;
                    _featurePrankFailure = null;
                    var ordinal = FeatureDemoPlan.Steps.ToList().IndexOf(step) + 1;
                    var label = $"{ordinal}/{FeatureDemoPlan.Steps.Count} {step.Title}";
                    _trayIcon.SetFeatureTestProgress(label, true);
                    var watch = Stopwatch.StartNew();
                    DiagnosticsLog.WriteEvent("FeatureDemoStepStarted", ("Step", step.Id), ("Attempt", attempt));
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                    timeout.CancelAfter(TimeSpan.FromSeconds(step.TimeoutSeconds));
                    var result = "Completed";
                    string? failure = null;
                    try
                    {
                        await PrepareFeatureStageAsync(step, timeout.Token);
                        _featureTarget?.SetStep(label);
                        if (step.Action == FeatureDemoAction.Tease)
                            await FillFeatureMeterAsync(timeout.Token);
                        else
                            await DemonstrateFeaturePrankAsync(step.Action, timeout);
                        result = FeatureDemoPlan.ConfirmedStepResult(step.Action, _featureHatlessInterruptionObserved,
                            _featureRemainsStoredBeforeReturn);
                        if (result == "NeedsReview") failure = _featureRemainsStoredBeforeReturn == false
                            ? "manual_restore_before_remains_stored" : "manual_restore_interrupted_hatless_observation";
                    }
                    catch (OperationCanceledException)
                    {
                        result = token.IsCancellationRequested ? "Stopped" : "Failed";
                        failure = token.IsCancellationRequested ? _featureStopReason : "step_timeout";
                    }
                    catch (Exception exception) when (exception is InvalidOperationException or TimeoutException
                        or System.ComponentModel.Win32Exception)
                    {
                        result = "Failed";
                        failure = exception is InvalidOperationException ? exception.Message : exception.GetType().Name;
                    }

                    var phase = _prankPhase.ToString();
                    _featureResults.Add(new(step.Id, step.Title, attempt, result,
                        Math.Round(watch.Elapsed.TotalSeconds, 2), failure, phase, _featureProof,
                        CurrentFeatureManualRestore()));
                    _featureManualWait?.Stop();
                    DiagnosticsLog.WriteEvent("FeatureDemoStepFinished", ("Step", step.Id), ("Attempt", attempt),
                        ("Result", result), ("Reason", failure), ("Phase", phase));
                    if (token.IsCancellationRequested) break;
                    if (result is "Completed" or "NeedsReview")
                    {
                        WriteFeatureReport("Running");
                        await Task.Delay(1100, token);
                        break;
                    }
                    await PauseFeatureForRetryAsync(failure!, token);
                }
                if (token.IsCancellationRequested) break;
            }
            if (!token.IsCancellationRequested) _featureCurrentStep = null;
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _featureResults.Add(new("Session", "测试准备或运行", 1, "Failed", 0,
                exception.GetType().Name, _prankPhase.ToString(), null, CurrentFeatureManualRestore()));
            DiagnosticsLog.Write("Feature demo stopped safely.", exception);
        }
        finally
        {
            _featureManualWait?.Stop();
            _featureDemoRunning = false;
            _featureRetry = null;
            if (!_isClosing) CancelWindowPrank("FeatureDemoFinished", false, false);
            _featureTarget?.Dispose();
            _featureTarget = null;
            _featureCancellation?.Dispose();
            _featureCancellation = null;
            if (!_isClosing)
            {
                _voice.Stop();
                ChangePetSize(originalSize);
                RefreshSurfaceMap(force: true);
                PlaceFeatureOnFloor();
            }
            var completed = _featureResults.Where(item => item.Result == "Completed")
                .Select(item => item.Step).Distinct().Count();
            var failed = _featureResults.Count(item => item.Result == "Failed");
            var review = _featureResults.Where(item => item.Result == "NeedsReview")
                .Select(item => item.Step).Distinct().Count();
            var status = _featureStopRequested ? "Stopped"
                : completed + review != FeatureDemoPlan.Steps.Count ? "Failed"
                : review > 0 ? "NeedsReview"
                : failed > 0 ? "CompletedAfterRetry" : "Completed";
            WriteFeatureReport(status);
            if (!_isClosing)
            {
                _trayIcon.SetFeatureTestProgress(_featureStopRequested ? "测试已停止"
                    : review > 0 ? "测试结束：无帽中断表演待观察"
                    : $"专属测试完成 {completed}/{FeatureDemoPlan.Steps.Count}", false);
            }
            DiagnosticsLog.WriteEvent("FeatureDemoFinished", ("Result", status), ("CompletedSteps", completed),
                ("NeedsReviewSteps", review), ("FailedAttempts", failed), ("ReportDirectory", _featureSession!.DirectoryPath));
        }
    }

    private async Task PauseFeatureForRetryAsync(string reason, CancellationToken token)
    {
        CancelWindowPrank("FeatureDemoStepFailed", false, false);
        _voice.Stop();
        PlaceFeatureOnFloor();
        _featureRetry = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var hint = FeatureDemoPlan.FailureHint(reason);
        _trayIcon.SetFeatureTestProgress($"{_featureCurrentStep!.Title}：{hint}", true, canRetry: true);
        _featureTarget?.SetStep(hint);
        WriteFeatureReport("PausedOnFailure");
        try { await _featureRetry.Task.WaitAsync(token); }
        finally { _featureRetry = null; }
        await Task.Delay(400, token);
    }

    private void RetryFeatureStep() => _featureRetry?.TrySetResult(true);

    private void UpdateFeatureHatProgress()
    {
        if (!_featureDemoRunning || _featureStopRequested ||
            _featureCurrentStep?.RequiresManualRestore != true) return;
        var hint = _prankPhase switch
        {
            PrankPhase.HatOpen => "正在摘帽",
            PrankPhase.HatStore => "正在把窗口装进帽子",
            PrankPhase.HatReturn => "正在取出窗口",
            PrankPhase.CollectRemains => "正在把碎片收进帽子",
            PrankPhase.Reassemble => "正在拼回完整窗口",
            PrankPhase.FetchHat => "正在跑去捡帽子",
            PrankPhase.PickHat => "正在捡起帽子",
            PrankPhase.Restoring => "正在恢复真实窗口",
            PrankPhase.WearHat => "正在戴回帽子",
            _ => null,
        };
        if (hint is null || hint == _featurePhaseHint) return;
        _featurePhaseHint = hint;
        var ordinal = FeatureDemoPlan.Steps.ToList().IndexOf(_featureCurrentStep) + 1;
        var label = $"{ordinal}/{FeatureDemoPlan.Steps.Count} {_featureCurrentStep.Title}：{hint}";
        _trayIcon.SetFeatureTestProgress(label, true);
        _featureTarget?.SetStep(label);
        DiagnosticsLog.WriteEvent("FeatureDemoPhase", ("Step", _featureCurrentStep.Id),
            ("Phase", _prankPhase), ("Progress", hint));
    }

    private FeatureManualRestoreObservation? CurrentFeatureManualRestore() => _featureManualRestore is null ? null
        : _featureManualRestore with { HeldSeconds = Math.Round(_featureManualWait?.Elapsed.TotalSeconds
            ?? _featureManualRestore.HeldSeconds, 2) };

    private void NotifyFeatureManualRestoreRequested()
    {
        if (!_featureDemoRunning || _featureStopRequested || _featureCurrentStep?.RequiresManualRestore != true ||
            _featureTarget?.IsAlive != true || _featureManualRestore?.RequestedAt is not null) return;
        if (_windowPranks?.ControlledHandle != _featureTarget.Handle)
        {
            _featurePrankFailure = "manual_restore_identity_mismatch";
            return;
        }
        _featureManualWait?.Stop();
        if (_prankLarge) _featureRemainsStoredBeforeReturn = _windowPranks.IsCollected;
        _featureManualRestore = new("ManualRestoreRequested", FormatHandle(_featureTarget.Handle),
            _featureTarget.ProcessId, Math.Round(_featureManualWait?.Elapsed.TotalSeconds ?? 0, 2), DateTimeOffset.UtcNow);
        SetFeatureManualRestoreProgress("已收到右键恢复请求，正在确认真实窗口");
        WriteFeatureReport("ManualRestoreRequested");
        DiagnosticsLog.WriteEvent("FeatureDemoManualRestoreRequested", ("Step", _featureCurrentStep.Id),
            ("Window", _featureManualRestore.Window), ("ProcessId", _featureManualRestore.ProcessId),
            ("HeldSeconds", _featureManualRestore.HeldSeconds));
    }

    private void SetFeatureManualRestoreProgress(string hint)
    {
        _featurePhaseHint = hint;
        var ordinal = FeatureDemoPlan.Steps.ToList().IndexOf(_featureCurrentStep!) + 1;
        var label = $"{ordinal}/{FeatureDemoPlan.Steps.Count} {_featureCurrentStep!.Title}：{hint}";
        _trayIcon.SetFeatureTestProgress(label, true);
        _featureTarget?.SetStep(label);
    }

    private void CheckFeatureWindowHeld()
    {
        if (_featureManualRestore?.RequestedAt is not null) return;
        if (_featureTarget is null || _windowPranks?.IsHoldingWindow != true ||
            _windowPranks.ControlledHandle != _featureTarget.Handle || !NativeMethods.IsIconic(_featureTarget.Handle))
            throw new InvalidOperationException("unexpected_restore_before_manual_request");
    }

    private async Task WaitForFeatureManualRestoreAsync(CancellationTokenSource timeout)
    {
        var token = timeout.Token;
        token.ThrowIfCancellationRequested();
        // Human observation is unbounded; only animation and native restoration retain deadlines.
        timeout.CancelAfter(Timeout.InfiniteTimeSpan);
        if (_featureManualRestore?.RequestedAt is null)
        {
            CheckFeatureWindowHeld();
            _featureManualWait = Stopwatch.StartNew();
            _featureManualRestore = new("WaitingForManualRestore", FormatHandle(_featureTarget!.Handle),
                _featureTarget.ProcessId, 0, null);
            DiagnosticsLog.WriteEvent("FeatureDemoWaitingForManualRestore", ("Step", _featureCurrentStep!.Id),
                ("Window", _featureManualRestore.Window), ("ProcessId", _featureManualRestore.ProcessId));
            while (_featureManualRestore.RequestedAt is null)
            {
                CheckFeatureTarget(token);
                CheckFeatureWindowHeld();
                if (_featurePrankFailure is not null) throw new InvalidOperationException(_featurePrankFailure);
                var hint = FeatureDemoPlan.ManualRestoreHint(_featureManualWait.Elapsed.TotalSeconds);
                if (_featurePhaseHint != hint)
                {
                    SetFeatureManualRestoreProgress(hint);
                    WriteFeatureReport("WaitingForManualRestore");
                }
                await Task.Delay(30, token);
            }
        }
        token.ThrowIfCancellationRequested();
        timeout.CancelAfter(TimeSpan.FromSeconds(FeatureDemoPlan.RestoreTimeoutSeconds));
        await WaitForFeatureAsync(() => !_windowPranks!.HasActiveOperation && _prankPhase == PrankPhase.None &&
            !NativeMethods.IsIconic(_featureTarget!.Handle) && NativeMethods.IsWindowVisible(_featureTarget.Handle), token);
        _featureManualRestore = _featureManualRestore! with { State = "Restored" };
    }

    private void StopFeatureDemo(string reason)
    {
        if (!IsFeatureTestMode || _featureStopRequested || (!_featureDemoRunning && _featureResults.Count > 0)) return;
        _featureStopRequested = true;
        _featureStopReason = reason;
        _featureCancellation?.Cancel();
        WriteFeatureReport("Stopped");
        _voice.Stop();
        CancelWindowPrank("FeatureDemoStopped", false, false);
        _featureTarget?.Dispose();
        if (!_isClosing) _trayIcon.SetFeatureTestProgress("测试已停止", false);
    }

    private void WriteFeatureReport(string status)
    {
        if (_featureSession is null) return;
        var inspection = _windowPranks?.LastInspection;
        var report = new
        {
            Status = status,
            Revision = FeatureDemoPlan.Revision,
            PlannedSteps = FeatureDemoPlan.Steps.Select(step => step.Id),
            ActiveStep = _featureCurrentStep?.Id,
            PhaseHint = _featurePhaseHint,
            ManualRestore = CurrentFeatureManualRestore(),
            StopReason = _featureStopRequested ? _featureStopReason : null,
            UpdatedAt = DateTimeOffset.UtcNow,
            Note = "Completed requires actual window results; appearance and sound still require human review.",
            Steps = _featureResults,
            LastTargetCheck = inspection is null ? null : new
            {
                inspection.Reason,
                Window = FormatHandle(inspection.Handle),
                Blocker = FormatHandle(inspection.BlockingWindow),
                inspection.NativeError,
            },
            ManualChecks = FeatureDemoPlan.ManualChecks,
        };
        try
        {
            File.WriteAllText(Path.Combine(_featureSession.DirectoryPath, "feature-test-report.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        { DiagnosticsLog.WriteEvent("FeatureDemoReportFailed", ("Reason", exception.GetType().Name)); }
    }

    private async Task PrepareFeatureStageAsync(FeatureDemoStep step, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        CancelWindowPrank("FeatureDemoStageBoundary", false, false);
        _settings.QuietMode = false;
        _settings.AllowMischief = false;
        PlaceFeatureOnFloor();
        _previousWindowBodies.Clear();
        _featurePrankFailure = null;
        if (!step.RequiresWindowTarget) return;
        if (_featureTarget?.IsAlive != true)
        {
            _featureTarget?.Dispose();
            _featureTarget = await FeatureTestTargetClient.StartAsync(token);
        }
        await _featureTarget.PrepareAsync(_featureWindowBounds, token);
        RefreshSurfaceMap(force: true);
        var watch = Stopwatch.StartNew();
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (InspectFeatureTarget(includePetOcclusion: true) is not null)
            {
                _featurePrankFailure = null;
                DiagnosticsLog.WriteEvent("FeatureDemoTargetReady", ("Step", step.Id),
                    ("Window", FormatHandle(_featureTarget.Handle)));
                return;
            }
            if (_featurePrankFailure != "occluded" || watch.Elapsed.TotalSeconds >= 2)
                throw new InvalidOperationException(_featurePrankFailure ?? "target_not_ready");
            await Task.Delay(60, token);
        }
    }

    private WindowPrankTarget? InspectFeatureTarget(bool includePetOcclusion)
    {
        if (_windowPranks is null || _featureTarget?.IsAlive != true)
        {
            _featurePrankFailure = "test_window_closed";
            return null;
        }
        var inspection = _windowPranks.InspectTarget(_featureTarget.Handle, _featureTarget.ProcessId, includePetOcclusion);
        var target = inspection.Target;
        if (target is null) _featurePrankFailure = inspection.Reason;
        else if (target.MonitorArea != _featureMonitor?.MonitorArea)
        {
            _featurePrankFailure = "test_window_wrong_monitor";
            target = null;
        }
        else
        {
            // Keep the feature harness on the same adaptive approach rule as
            // production: a target at the right edge may still be approached
            // from its left side, and vice versa.  A right-only check made
            // valid edge targets fail before the real prank state machine ran.
            var petWidth = Math.Max(1, (int)Math.Round(512 * PetCanvasPhysicalScale()));
            if (WindowPrankApproachPolicy.Choose(target.VisibleBounds, target.WorkArea,
                    petWidth, (int)Math.Round(PrankFoot().X)) is null)
            {
                _featurePrankFailure = "insufficient_approach_space";
                target = null;
            }
        }
        return target;
    }

    private void PlaceFeatureOnFloor()
    {
        if (_featureMonitor is null) return;
        ClearWindowEnclosure();
        _velocityX = _velocityY = 0;
        _movementPixelRemainderX = _movementPixelRemainderY = _fallPixelRemainder = 0;
        ResetSupportMotionTracking();
        ResetAirbornePlatformSupport();
        var area = _featureMonitor.WorkArea;
        var petWidth = 512 * PetCanvasPhysicalScale();
        MovePrankFoot(new Point(Math.Clamp(_featureWindowBounds.Right + petWidth * 0.62 + 16,
            area.Left + petWidth * 0.6, area.Right - petWidth * 0.6), area.Bottom - 1));
        _supportHandle = _featureMonitor.Handle;
        _supportIsDesktopFloor = true;
        ReturnToIdle();
    }

    private void CheckFeatureTarget(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (_isClosing || _sessionLocked) throw new OperationCanceledException(token);
        if (_featureTarget?.IsAlive != true) throw new InvalidOperationException("test_window_closed");
    }

    private async Task WaitForFeatureAsync(Func<bool> condition, CancellationToken token, bool requireHeld = false)
    {
        while (true)
        {
            CheckFeatureTarget(token);
            if (requireHeld) CheckFeatureWindowHeld();
            if (_featurePrankFailure is not null) throw new InvalidOperationException(_featurePrankFailure);
            if (condition()) return;
            await Task.Delay(30, token);
        }
    }

    private async Task<bool> WaitForFeatureGestureDelayAsync(int milliseconds, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            CheckFeatureTarget(token);
            if (_featureManualRestore?.RequestedAt is not null) return false;
            CheckFeatureWindowHeld();
            if (_featurePrankFailure is not null) throw new InvalidOperationException(_featurePrankFailure);
            var remaining = milliseconds - watch.ElapsedMilliseconds;
            if (remaining <= 0) return true;
            await Task.Delay((int)Math.Min(15, remaining), token);
        }
    }

    private async Task<bool> DemonstrateHatlessThrowAsync(CancellationToken token)
    {
        if (_featureManualRestore?.RequestedAt is not null) return false;
        var origin = PrankFoot();
        var tracker = new DragMotionTracker();
        tracker.Reset(ToDrawing(origin), 0);
        _state = PetState.Dragging;
        _animator.Play("drag");
        _voice.Play(VoiceCue.Aggrieved, VoicePriority.Normal);
        if (!await WaitForFeatureGestureDelayAsync(950, token)) return false;
        var scale = PetPhysicalScale();
        for (var frame = 1; frame <= 12; frame++)
        {
            CheckFeatureTarget(token);
            if (_featureManualRestore?.RequestedAt is not null) return false;
            CheckFeatureWindowHeld();
            var position = new Point(origin.X - frame * 5 * scale, origin.Y - frame * 10 * scale);
            MovePrankFoot(position);
            tracker.Add(ToDrawing(position), frame / 60.0);
            if (!await WaitForFeatureGestureDelayAsync(16, token)) return false;
        }
        if (!_animator.Hatless || _hatProp is null) throw new InvalidOperationException("hatless_drag_lost_hat");
        var release = tracker.Release(ToDrawing(PrankFoot()), 12 / 60.0, scale);
        if (!release.HasInertia) throw new InvalidOperationException("throw_gesture_has_no_inertia");
        _voice.Play(VoiceCue.Scream, VoicePriority.Important, TimeSpan.FromSeconds(8));
        BeginFall(release.VelocityY, "DragThrow", release.VelocityX);
        await WaitForFeatureAsync(() => _featureManualRestore?.RequestedAt is not null ||
            (_state == PetState.Idle && !_animator.Hatless && _prankPhase == PrankPhase.None),
            token, requireHeld: true);
        return _featureManualRestore?.RequestedAt is null;
    }

    private async Task FillFeatureMeterAsync(CancellationToken token)
    {
        _mischief = new MischiefController();
        for (var index = 0; index < 9; index++)
        {
            token.ThrowIfCancellationRequested();
            _mischief.Tick(1.6, false);
            if (!_mischief.Tease()) throw new InvalidOperationException("tease_did_not_charge");
            UpdateMischief(0);
            await Task.Delay(230, token);
        }
        if (_mischief.Value != 100) throw new InvalidOperationException("meter_did_not_fill");
    }

    private async Task DemonstrateFeaturePrankAsync(FeatureDemoAction action, CancellationTokenSource timeout)
    {
        var token = timeout.Token;
        var large = action is FeatureDemoAction.Shatter or FeatureDemoAction.Tear or FeatureDemoAction.HatlessInterruption;
        if (large) await FillFeatureMeterAsync(token);
        else _mischief = new MischiefController(new MischiefState { Value = 50 });
        _manualPrankIndex = action switch { FeatureDemoAction.Punch => 1, FeatureDemoAction.Charge => 2,
            FeatureDemoAction.HatStoreReturn => 3, _ => 0 };
        _burstPrankIndex = action == FeatureDemoAction.Tear ? 1 : 0;
        var completed = _featurePranksCompleted;
        var sequence = _windowPranks!.CompletionSequence;
        var initialBounds = _featureTarget!.Bounds;
        TryStartWindowPrank(true);
        if (_prankPhase == PrankPhase.None)
            throw new InvalidOperationException(_featurePrankFailure ?? "prank_did_not_start");
        if (action == FeatureDemoAction.HatlessInterruption)
        {
            await WaitForFeatureAsync(() => _featureManualRestore?.RequestedAt is not null ||
                (_animator.Hatless && _prankPhase == PrankPhase.BigStrike &&
                 _prankSystemEngaged && _windowPranks.SnapshotMinimized), token);
            if (_featureManualRestore?.RequestedAt is null)
            {
                if (_hatProp is null) throw new InvalidOperationException("hat_was_not_thrown");
                CancelWindowPrank("FeatureDemoHatlessPickup", false, true);
                _featurePrankFailure = null;
                CheckFeatureWindowHeld();
                _featureHatlessInterruptionObserved = await DemonstrateHatlessThrowAsync(token);
            }
        }
        if (_featureCurrentStep!.RequiresManualRestore)
        {
            await WaitForFeatureAsync(() => (_featurePranksCompleted > completed && IsPrankAwaitingManualRestore) ||
                _featureManualRestore?.RequestedAt is not null, token);
            if (large && _featureManualRestore?.RequestedAt is null && !_windowPranks.IsCollected)
                throw new InvalidOperationException("remains_not_stored");
            await WaitForFeatureManualRestoreAsync(timeout);
        }
        else
            await WaitForFeatureAsync(() => _featurePranksCompleted > completed && _prankPhase == PrankPhase.None, token);
        if (_windowPranks.HasActiveOperation || NativeMethods.IsIconic(_featureTarget.Handle))
            throw new InvalidOperationException("window_not_returned");
        if (_animator.Hatless || _needsHatRecovery || _hatProp is not null)
            throw new InvalidOperationException("hat_not_recovered");

        var evidence = _windowPranks.LastCompletion;
        if (!FeatureDemoEvidencePolicy.Matches(_featureCurrentStep.CompletionAction, sequence,
                _featureTarget.Handle, _featureTarget.ProcessId, evidence))
            throw new InvalidOperationException("native_completion_not_confirmed");
        if (_featureCurrentStep.RequiresManualRestore &&
            !WindowPrankGeometry.SameBounds(initialBounds, _featureTarget.Bounds))
            throw new InvalidOperationException("restored_window_moved");
        _featureProof = new(evidence!.Operation, FormatHandle(evidence.Handle), evidence.ProcessId,
            evidence.FinalOuterBounds.X - evidence.InitialOuterBounds.X,
            evidence.FinalOuterBounds.Y - evidence.InitialOuterBounds.Y,
            evidence.MinimizeConfirmed, evidence.RestoreConfirmed, true,
            action == FeatureDemoAction.HatlessInterruption ? _featureHatlessInterruptionObserved : null,
            _featureRemainsStoredBeforeReturn);
    }
}
