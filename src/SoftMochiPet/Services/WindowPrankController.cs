using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SoftMochiPet.Core;
using SoftMochiPet.Interop;

namespace SoftMochiPet.Services;

public sealed record WindowPrankTarget(
    IntPtr Handle, uint ProcessId, uint ThreadId, Rectangle VisibleBounds, Rectangle OuterBounds,
    Rectangle WorkArea, Rectangle MonitorArea)
{
    internal PrankNativeMethods.WindowPlacement Placement { get; init; }
}

public sealed record WindowPrankInspection(IntPtr Handle, uint ProcessId, WindowPrankTarget? Target,
    string Reason, IntPtr BlockingWindow = default, int NativeError = 0)
{
    public bool Eligible => Target is not null;
}

public sealed record WindowPrankCompletionEvidence(long Sequence, string Operation, IntPtr Handle,
    uint ProcessId, Rectangle InitialOuterBounds, Rectangle FinalOuterBounds,
    bool MinimizeConfirmed, bool RestoreConfirmed);

public sealed class WindowPrankSnapshot : IDisposable
{
    public WindowPrankTarget Target { get; }
    internal BitmapSource? Image { get; private set; }
    internal long CreatedAt { get; } = Stopwatch.GetTimestamp();
    internal WindowPrankSnapshot(WindowPrankTarget target, BitmapSource image) { Target = target; Image = image; }
    public void Dispose() => Image = null;
}

/// <summary>One external window at a time. Invoked on the pet UI thread; native mutations are asynchronous and verified.</summary>
public sealed class WindowPrankController : IDisposable
{
    private readonly IntPtr _petHandle;
    private readonly Dispatcher _dispatcher;
    private WindowPrankTarget? _target;
    private WindowPrankSnapshot? _snapshot;
    private PrankEffectWindow? _overlay;
    private Rectangle _expectedOuter;
    private Rectangle? _pendingOuter;
    private Rectangle _motionDestination;
    private double _motionTime;
    private double _motionDuration;
    private double _pendingTime;
    private bool _motion;
    private bool _minimizeRequested;
    private bool _restoring;
    private IntPtr _foregroundAtBegin;
    // A manually selected foreground window is an intentional target. Keep
    // its initial foreground state from being mistaken for user takeover;
    // later foreground changes are still handled by the normal safety checks.
    private bool _targetWasForegroundAtBegin;
    private bool _disposed;
    private int _generation;
    private WindowPrankVisualKind _kind;
    private string _operation = string.Empty;

    public WindowPrankController(IntPtr petHandle)
    {
        _petHandle = petHandle;
        _dispatcher = Dispatcher.CurrentDispatcher;
    }

    public bool HasActiveOperation => _target is not null;
    public bool IsMoving => _motion;
    public bool SnapshotMinimized { get; private set; }
    public bool IsHoldingWindow => SnapshotMinimized;
    public bool IsHeld { get; private set; }
    public bool IsCollected => _overlay?.IsCollected == true;
    public IntPtr ControlledHandle => _target?.Handle ?? IntPtr.Zero;
    public string LastResult { get; private set; } = "idle";
    public string? CancelReason { get; private set; }
    public WindowPrankInspection? LastInspection { get; private set; }
    public string LastSelectionRejections { get; private set; } = string.Empty;
    public long CompletionSequence { get; private set; }
    public WindowPrankCompletionEvidence? LastCompletion { get; private set; }
    public double MotionProgress => _motionDuration <= 0 ? 0 : Math.Clamp(_motionTime / _motionDuration, 0, 1);

    public WindowPrankInspection InspectTarget(IntPtr handle, uint expectedProcessId, bool includePetOcclusion = false)
    {
        _dispatcher.VerifyAccess();
        var result = _disposed
            ? new WindowPrankInspection(handle, 0, null, "controller_disposed")
            : ReadCandidate(handle, includePetOcclusion, expectedProcessId);
        LastInspection = result;
        LogInspection(result);
        return result;
    }

    public WindowPrankTarget? TrySelectTarget(Point petPoint, Func<WindowPrankTarget, bool>? predicate = null,
        Func<WindowPrankTarget, string?>? rejectionReason = null, IntPtr preferredHandle = default)
    {
        if (_disposed || HasActiveOperation)
        {
            LastResult = _disposed ? "controller_disposed" : "operation_active";
            return null;
        }
        WindowPrankTarget? best = null;
        var distance = double.PositiveInfinity;
        var bestIsPreferred = false;
        var bestIsForeground = false;
        var foreground = NativeMethods.GetForegroundWindow();
        var rejected = new Dictionary<string, int>(StringComparer.Ordinal);
        NativeMethods.EnumWindows((handle, _) =>
        {
            var inspection = ReadCandidate(handle, includePetOcclusion: false,
                allowNonOrdinary: handle == preferredHandle || handle == foreground);
            var candidate = inspection.Target;
            var callerReason = candidate is null ? null : rejectionReason?.Invoke(candidate);
            if (candidate is null || callerReason is not null || predicate?.Invoke(candidate) == false)
            {
                var reason = candidate is null ? inspection.Reason : callerReason ?? "caller_constraint";
                rejected[reason] = rejected.GetValueOrDefault(reason) + 1;
                return true;
            }
            var point = new Point(candidate.VisibleBounds.Right, Math.Clamp(petPoint.Y, candidate.VisibleBounds.Top, candidate.VisibleBounds.Bottom));
            var dx = (double)point.X - petPoint.X;
            var dy = (double)point.Y - petPoint.Y;
            var next = dx * dx + dy * dy;
            var isPreferred = preferredHandle != IntPtr.Zero && handle == preferredHandle;
            var isForeground = handle == foreground;
            if (best is null ||
                isPreferred && !bestIsPreferred ||
                isPreferred == bestIsPreferred && isForeground && !bestIsForeground ||
                isPreferred == bestIsPreferred && isForeground == bestIsForeground && next < distance)
            {
                best = candidate;
                bestIsPreferred = isPreferred;
                bestIsForeground = isForeground;
                distance = next;
            }
            return true;
        }, IntPtr.Zero);
        LastSelectionRejections = string.Join(",", rejected.OrderBy(pair => pair.Key)
            .Select(pair => $"{pair.Key}:{pair.Value}"));
        if (best is null)
        {
            LastResult = "no_eligible_window";
            DiagnosticsLog.WriteEventThrottled("prank.selection.rejected", TimeSpan.FromSeconds(15),
                "prank.selection.rejected", ("Reasons", LastSelectionRejections));
        }
        return best;
    }

    public bool BeginMotion(WindowPrankTarget target, WindowPrankMotionKind kind, int direction)
    {
        _dispatcher.VerifyAccess();
        if (_disposed || HasActiveOperation || !StillEligible(target, includePetOcclusion: false)) return Reject("motion_target_changed");
        var destination = WindowPrankGeometry.MotionDestination(target.VisibleBounds, target.WorkArea, kind, direction);
        if (WindowPrankGeometry.SameBounds(destination, target.VisibleBounds, 3)) return Reject("window_at_edge");
        _target = target;
        _operation = kind.ToString();
        _expectedOuter = target.OuterBounds;
        _pendingOuter = null;
        _motionDestination = destination;
        _motionDuration = WindowPrankGeometry.MotionDuration(kind);
        _motionTime = 0;
        _pendingTime = 0;
        _motion = true;
        _foregroundAtBegin = NativeMethods.GetForegroundWindow();
        _targetWasForegroundAtBegin = _foregroundAtBegin == target.Handle;
        CancelReason = null;
        LastResult = "moving";
        DiagnosticsLog.WriteEvent("prank.motion.begin", ("kind", kind), ("duration", _motionDuration));
        return true;
    }

    public async Task<WindowPrankSnapshot?> CaptureAsync(WindowPrankTarget target, CancellationToken cancellationToken = default)
    {
        _dispatcher.VerifyAccess();
        if (_disposed || HasActiveOperation || !StillEligible(target, includePetOcclusion: true))
        { Reject("capture_target_occluded_or_changed"); return null; }
        try
        {
            var image = await Task.Run(() => CaptureVisiblePixels(target.VisibleBounds, cancellationToken), cancellationToken);
            if (image is null || _disposed || cancellationToken.IsCancellationRequested ||
                !StillEligible(target, includePetOcclusion: true))
            { Reject(image is null ? "capture_unusable_pixels" : "capture_target_changed"); return null; }
            LastResult = "snapshot_ready";
            return new WindowPrankSnapshot(target, image);
        }
        catch (OperationCanceledException) { Reject("capture_cancelled"); }
        catch (Exception exception) when (exception is ExternalException or ArgumentException or InvalidOperationException)
        { Reject("capture_unavailable"); }
        return null;
    }

    public async Task<bool> BeginSnapshotAsync(WindowPrankTarget target, WindowPrankVisualKind kind, CancellationToken cancellationToken = default)
    {
        var snapshot = await CaptureAsync(target, cancellationToken);
        return snapshot is not null && await BeginSnapshotAsync(snapshot, kind, cancellationToken);
    }

    public async Task<bool> BeginSnapshotAsync(WindowPrankSnapshot snapshot, WindowPrankVisualKind kind, CancellationToken cancellationToken = default)
    {
        _dispatcher.VerifyAccess();
        var target = snapshot.Target;
        if (_disposed || HasActiveOperation || snapshot.Image is null ||
            Stopwatch.GetElapsedTime(snapshot.CreatedAt).TotalSeconds > 15 || !StillEligible(target, includePetOcclusion: false))
        { snapshot.Dispose(); return Reject("snapshot_target_changed"); }
        _target = target;
        _operation = kind.ToString();
        _snapshot = snapshot;
        _expectedOuter = target.OuterBounds;
        _kind = kind;
        _foregroundAtBegin = NativeMethods.GetForegroundWindow();
        _targetWasForegroundAtBegin = _foregroundAtBegin == target.Handle;
        CancelReason = null;
        var generation = ++_generation;
        try
        {
            _overlay = new PrankEffectWindow(snapshot.Image, target.VisibleBounds, target.MonitorArea, kind, _petHandle);
            _overlay.Show();
            _overlay.SetProgress(0, new Point(target.VisibleBounds.Right, target.VisibleBounds.Bottom));
            await _dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
            if (cancellationToken.IsCancellationRequested || generation != _generation || !SameLiveIdentity(target) ||
                !HasExpectedOuter(target.OuterBounds))
            { Abort("snapshot_cancelled_before_minimize"); return false; }
            _minimizeRequested = true;
            if (!PrankNativeMethods.ShowWindowAsync(target.Handle, PrankNativeMethods.SwMinimizeNoActivate))
            { Abort("minimize_rejected"); return false; }
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed.TotalMilliseconds < 900)
            {
                if (generation != _generation) return false;
                if (cancellationToken.IsCancellationRequested || !SameLiveIdentity(target))
                { Abort("minimize_cancelled"); return false; }
                if (NativeMethods.IsIconic(target.Handle))
                {
                    SnapshotMinimized = true;
                    // The expected foreground transition is complete. From
                    // this point on, re-activating the target is takeover.
                    _foregroundAtBegin = NativeMethods.GetForegroundWindow();
                    _targetWasForegroundAtBegin = false;
                    LastResult = "snapshot_minimized";
                    DiagnosticsLog.WriteEvent("prank.snapshot.begin", ("kind", kind), ("capture", "visible_pixels"),
                        ("Window", FormatHandle(target.Handle)), ("Process", target.ProcessId), ("MinimizedConfirmed", true));
                    return true;
                }
                await Task.Delay(15);
            }
            Abort("minimize_not_confirmed");
        }
        catch (Exception exception) when (exception is InvalidOperationException or ExternalException)
        { Abort("snapshot_overlay_unavailable"); }
        return false;
    }

    public void SetEffectProgress(double progress, Point hatMouth, double hatWidth = 60)
    {
        _dispatcher.VerifyAccess();
        if (SnapshotMinimized && _overlay is not null)
            _overlay.SetProgress(progress, hatMouth, hatWidth);
    }

    public void SetForeground(BitmapSource? foreground, Rectangle spriteBounds, bool mirror = false)
    {
        _dispatcher.VerifyAccess();
        _overlay?.SetForeground(foreground, spriteBounds, mirror);
    }

    public void SetCollectionProgress(double progress, Point hatMouth, double hatWidth = 60)
    {
        _dispatcher.VerifyAccess();
        if (SnapshotMinimized) _overlay?.SetCollectionProgress(progress, hatMouth, hatWidth);
    }

    public void ResetCollection()
    {
        _dispatcher.VerifyAccess();
        _overlay?.ResetCollection();
    }

    public void SetTearHands(Point? upperHand, Point? lowerHand)
    {
        _dispatcher.VerifyAccess();
        _overlay?.SetTearHands(upperHand, lowerHand);
    }

    public void SetTearTension(double tension)
    {
        _dispatcher.VerifyAccess();
        if (SnapshotMinimized) _overlay?.SetTearTension(tension);
    }

    public bool HoldSnapshot()
    {
        _dispatcher.VerifyAccess();
        if (_disposed || _target is not { } target ||
            !WindowPrankHoldPolicy.CanHold(SnapshotMinimized, _restoring, SameLiveIdentity(target),
                NativeMethods.IsIconic(target.Handle), NativeMethods.GetForegroundWindow() == target.Handle,
                PrankNativeMethods.IsZoomed(target.Handle), PlacementUnchanged(target)))
            return Reject("snapshot_not_ready_for_hold");
        if (IsHeld) return true;
        IsHeld = true;
        _overlay?.SetForeground(null, Rectangle.Empty);
        _overlay?.SetClickThrough(true);
        LastResult = "snapshot_held";
        DiagnosticsLog.WriteEvent("prank.snapshot.held", ("kind", _kind),
            ("Window", FormatHandle(target.Handle)), ("Process", target.ProcessId));
        return true;
    }

    public void CheckSafety()
    {
        _dispatcher.VerifyAccess();
        if (_disposed || _target is null) return;
        var foreground = NativeMethods.GetForegroundWindow();
        var targetIsForeground = foreground == _target.Handle;
        var expectedInitialForeground = _targetWasForegroundAtBegin && !SnapshotMinimized;
        if (WindowPrankHoldPolicy.ShouldRelinquishOwnership(SameLiveIdentity(_target),
                targetIsForeground && !expectedInitialForeground, PrankNativeMethods.IsZoomed(_target.Handle)))
        { Relinquish("user_takeover_or_window_gone"); return; }
        var expectedMinimizeTransition = _targetWasForegroundAtBegin && _minimizeRequested && !SnapshotMinimized;
        var foregroundChanged = !expectedMinimizeTransition && foreground != IntPtr.Zero && foreground != _foregroundAtBegin && foreground != _petHandle;
        var foregroundIsOwnUi = foregroundChanged &&
            NativeMethods.GetWindowThreadProcessId(foreground, out var foregroundProcessId) != 0 &&
            foregroundProcessId == (uint)Environment.ProcessId;
        if (WindowPrankHoldPolicy.ShouldAbortForForegroundChange(IsHeld,
                foregroundChanged, foregroundIsOwnUi))
        { Abort("foreground_changed"); return; }
        if (SnapshotMinimized)
        {
            if (WindowPrankGeometry.ShouldRelinquishSnapshot(_restoring, NativeMethods.IsIconic(_target.Handle), PlacementUnchanged(_target)))
                Relinquish("user_restored_or_repositioned_window");
        }
    }

    public void Update(double deltaSeconds)
    {
        CheckSafety();
        if (_disposed || _target is null || SnapshotMinimized || !_motion) return;
        var dt = Math.Clamp(double.IsFinite(deltaSeconds) ? deltaSeconds : 0, 0, 0.25);
        if (NativeMethods.IsIconic(_target.Handle) || !NativeMethods.IsWindowVisible(_target.Handle) ||
            !NativeMethods.GetWindowRect(_target.Handle, out var actualNative))
        { Relinquish("motion_window_unavailable"); return; }
        var actual = ToRectangle(actualNative);
        if (_pendingOuter is { } pending)
        {
            if (WindowPrankGeometry.SameBounds(actual, pending))
            { _expectedOuter = actual; _pendingOuter = null; _pendingTime = 0; }
            else if (WindowPrankGeometry.SameBounds(actual, _expectedOuter) && (_pendingTime += dt) < 0.35)
                return;
            else { Relinquish("motion_refused_or_user_moved"); return; }
        }
        else if (!WindowPrankGeometry.SameBounds(actual, _expectedOuter))
        { Relinquish("user_moved_window"); return; }
        _motionTime += dt;
        var visible = WindowPrankGeometry.AtMotionProgress(_target.VisibleBounds, _motionDestination, MotionProgress);
        var outer = WindowPrankGeometry.ToOuterBounds(visible, _target.VisibleBounds, _target.OuterBounds);
        if (_motionTime >= _motionDuration && WindowPrankGeometry.SameBounds(outer, actual))
        {
            if (!WindowPrankSelectionPolicy.IsConfirmedMotion(_target.OuterBounds, outer, actual,
                    NativeMethods.IsIconic(_target.Handle)))
            { Relinquish("motion_completion_unconfirmed"); return; }
            RecordCompletion(_target, actual, minimizeConfirmed: false, restoreConfirmed: false);
            _motion = false;
            _target = null;
            LastResult = "motion_completed";
            DiagnosticsLog.WriteEvent("prank.motion.complete");
            return;
        }
        if (!WindowPrankGeometry.SameBounds(outer, actual, 0))
        {
            if (!NativeMethods.SetWindowPos(_target.Handle, IntPtr.Zero, outer.X, outer.Y, 0, 0,
                NativeMethods.SwpNoSize | NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate |
                NativeMethods.SwpNoOwnerZOrder | PrankNativeMethods.SwpAsyncWindowPos))
            { Relinquish("motion_request_failed"); return; }
            _pendingOuter = outer;
        }
    }

    public async Task<bool> RestoreAsync(CancellationToken cancellationToken = default)
    {
        _dispatcher.VerifyAccess();
        if (_target is null) return true;
        var target = _target;
        if (!SnapshotMinimized || !CanRestore(target))
        { Relinquish("restore_no_longer_owned"); return !NativeMethods.IsIconic(target.Handle); }
        var generation = ++_generation;
        _restoring = true;
        _overlay?.ResetCollection();
        _overlay?.SetProgress(0, new Point(target.VisibleBounds.Right, target.VisibleBounds.Bottom));
        if (!PrankNativeMethods.ShowWindowAsync(target.Handle, PrankNativeMethods.SwShowNoActivate))
        { Abort("restore_request_failed"); return false; }
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalMilliseconds < 900)
        {
            if (generation != _generation) return false;
            if (!SameLiveIdentity(target)) { Relinquish("restore_window_gone"); return false; }
            if (!NativeMethods.IsIconic(target.Handle))
            {
                await _dispatcher.InvokeAsync(() => { }, DispatcherPriority.Render);
                if (generation != _generation) return false;
                if (!SameLiveIdentity(target) || !PlacementUnchanged(target))
                { Relinquish("restore_user_takeover"); return false; }
                if (!NativeMethods.GetWindowRect(target.Handle, out var restoredOuter) ||
                    !WindowPrankSelectionPolicy.IsConfirmedRestore(SnapshotMinimized,
                        NativeMethods.IsIconic(target.Handle), PlacementUnchanged(target), target.OuterBounds, ToRectangle(restoredOuter)))
                { Relinquish("restore_bounds_unconfirmed"); return false; }
                RecordCompletion(target, ToRectangle(restoredOuter), minimizeConfirmed: true, restoreConfirmed: true);
                Relinquish(null);
                LastResult = "restored";
                DiagnosticsLog.WriteEvent("prank.snapshot.restore", ("kind", _kind));
                return true;
            }
            if (cancellationToken.IsCancellationRequested) { Abort("restore_cancelled"); return false; }
            await Task.Delay(15);
        }
        Abort("restore_not_confirmed");
        return false;
    }

    public void Abort(string reason = "aborted")
    {
        _dispatcher.VerifyAccess();
        // A paired show request also cancels an already-posted minimize when it has not reached the target thread yet.
        if (_target is { } target && _minimizeRequested && SameLiveIdentity(target) &&
            (NativeMethods.GetForegroundWindow() != target.Handle || !SnapshotMinimized) && PlacementUnchanged(target) &&
            (NativeMethods.IsIconic(target.Handle) || HasExpectedOuter(target.OuterBounds)))
            PrankNativeMethods.ShowWindowAsync(target.Handle, PrankNativeMethods.SwShowNoActivate);
        Relinquish(reason);
    }

    public void Dispose()
    {
        if (_disposed) return;
        Abort("disposed");
        _disposed = true;
    }

    private void Relinquish(string? reason)
    {
        ++_generation;
        if (_overlay is { } overlay)
        {
            _overlay = null;
            try { overlay.CloseSafely(); }
            catch (InvalidOperationException exception)
            {
                DiagnosticsLog.Write("Prank overlay was already closing.", exception);
            }
        }
        _snapshot?.Dispose();
        _snapshot = null;
        _target = null;
        _pendingOuter = null;
        _motion = false;
        SnapshotMinimized = false;
        IsHeld = false;
        _minimizeRequested = false;
        _restoring = false;
        _targetWasForegroundAtBegin = false;
        if (reason is not null)
        {
            CancelReason = reason;
            LastResult = reason;
            DiagnosticsLog.WriteEvent("prank.cancel", ("reason", reason));
        }
    }

    private bool Reject(string reason)
    {
        CancelReason = reason;
        LastResult = reason;
        DiagnosticsLog.WriteEventThrottled("prank." + reason, TimeSpan.FromSeconds(30), "prank.skipped", ("reason", reason));
        return false;
    }

    private bool HasExpectedOuter(Rectangle expected) => _target is not null &&
        NativeMethods.GetWindowRect(_target.Handle, out var actual) && WindowPrankGeometry.SameBounds(expected, ToRectangle(actual));

    private static bool CanRestore(WindowPrankTarget target) => SameLiveIdentity(target) &&
        NativeMethods.IsIconic(target.Handle) && NativeMethods.GetForegroundWindow() != target.Handle && PlacementUnchanged(target);

    private static bool PlacementUnchanged(WindowPrankTarget target)
    {
        var placement = new PrankNativeMethods.WindowPlacement { Length = Marshal.SizeOf<PrankNativeMethods.WindowPlacement>() };
        return PrankNativeMethods.GetWindowPlacement(target.Handle, ref placement) &&
            WindowPrankGeometry.SameBounds(ToRectangle(placement.NormalPosition), ToRectangle(target.Placement.NormalPosition));
    }

    private static bool SameLiveIdentity(WindowPrankTarget target) => PrankNativeMethods.IsWindow(target.Handle) &&
        NativeMethods.GetWindowThreadProcessId(target.Handle, out var processId) == target.ThreadId && processId == target.ProcessId;

    private bool StillEligible(WindowPrankTarget target, bool includePetOcclusion)
    {
        // Once a target has been selected, preserve the explicit foreground
        // choice across the menu/approach transition; its identity, bounds,
        // visibility and safety gates are still revalidated below.
        var inspection = ReadCandidate(target.Handle, includePetOcclusion, target.ProcessId,
            allowNonOrdinary: true);
        if (inspection.Target is { } current)
        {
            var reason = current.ThreadId != target.ThreadId ? "window_identity_changed"
                : !WindowPrankGeometry.SameBounds(current.OuterBounds, target.OuterBounds) ? "outer_bounds_changed"
                : !WindowPrankGeometry.SameBounds(current.VisibleBounds, target.VisibleBounds) ? "visible_bounds_changed"
                : null;
            if (reason is not null) inspection = inspection with { Target = null, Reason = reason };
        }
        LastInspection = inspection;
        if (!inspection.Eligible) LogInspection(inspection);
        return inspection.Eligible;
    }

    private WindowPrankInspection ReadCandidate(IntPtr handle, bool includePetOcclusion,
        uint? expectedProcessId = null, bool allowNonOrdinary = false)
    {
        uint processId = 0;
        WindowPrankInspection Denied(string reason, int error = 0, IntPtr blocker = default) =>
            new(handle, processId, null, reason, blocker, error);
        if (handle == IntPtr.Zero || !PrankNativeMethods.IsWindow(handle)) return Denied("window_gone");
        var threadId = NativeMethods.GetWindowThreadProcessId(handle, out processId);
        if (threadId == 0) return Denied("window_identity_unavailable");
        if (expectedProcessId.HasValue && processId != expectedProcessId.Value) return Denied("process_identity_changed");
        if (processId == Environment.ProcessId) return Denied("own_process");
        if (!NativeMethods.IsWindowVisible(handle)) return Denied("not_visible");
        if (NativeMethods.IsIconic(handle)) return Denied("minimized");
        if (PrankNativeMethods.IsZoomed(handle)) return Denied("maximized");
        // The user may be actively working in the target. Foreground status alone is not a safety failure.
        var cloakStatus = NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DwmwaCloaked, out int cloaked, sizeof(int));
        if (cloakStatus != 0) return Denied("cloaking_query_failed", cloakStatus);
        if (cloaked != 0) return Denied("cloaked");
        if (!NativeMethods.GetWindowRect(handle, out var outer)) return Denied("outer_bounds_unavailable");
        var visibleStatus = NativeMethods.DwmGetWindowAttribute(handle, NativeMethods.DwmwaExtendedFrameBounds,
            out NativeMethods.NativeRect visible, Marshal.SizeOf<NativeMethods.NativeRect>());
        if (visibleStatus != 0) return Denied("visible_bounds_unavailable", visibleStatus);
        if (visible.Right <= visible.Left || visible.Bottom <= visible.Top) return Denied("empty_visible_bounds");
        var rectangle = ToRectangle(visible);
        var monitor = DisplayGeometry.FromWindow(handle);
        if (monitor is null) return Denied("monitor_unavailable");
        var isForeground = NativeMethods.GetForegroundWindow() == handle || allowNonOrdinary;
        if (WindowPrankSelectionPolicy.IsFullScreen(rectangle, monitor.MonitorArea, monitor.WorkArea))
            return Denied("fullscreen");
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlStyle).ToInt64();
        var extended = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle).ToInt64();
        var className = new StringBuilder(128);
        if (NativeMethods.GetClassName(handle, className, className.Capacity) == 0) return Denied("window_class_unavailable");
        // A non-maximized foreground window is the user's explicit target.
        // Keep the hard safety gates below, but do not reject it merely for
        // being a dialog/tool/layered application. Background/autonomous
        // selection continues to require an ordinary application surface.
        if (!isForeground)
        {
            var ordinaryRejection = WindowPrankSelectionPolicy.OrdinaryWindowRejection(style, extended,
                PrankNativeMethods.GetWindow(handle, PrankNativeMethods.GwOwner) != IntPtr.Zero,
                className.ToString() == "#32770", PrankNativeMethods.IsWindowEnabled(handle));
            if (ordinaryRejection is not null) return Denied(ordinaryRejection);
        }
        if (PrankNativeMethods.IsHungAppWindow(handle)) return Denied("unresponsive_window");
        var protectedContent = PrankNativeMethods.GetWindowDisplayAffinity(handle, out var affinity) && affinity != 0;
        if (protectedContent) return Denied("protected_content");
        if (rectangle.Width < 180 || rectangle.Height < 100) return Denied("window_too_small");
        if ((long)rectangle.Width * rectangle.Height > 16_000_000) return Denied("window_too_large");
        if (!monitor.WorkArea.Contains(rectangle)) return Denied("outside_work_area");
        var processRejection = NonElevatedProcessRejection(processId, out var processError);
        if (processRejection is not null) return Denied(processRejection, processError);
        var occlusion = OcclusionRejection(handle, rectangle, includePetOcclusion);
        if (occlusion.Reason is not null) return Denied(occlusion.Reason, blocker: occlusion.Blocker);
        var placement = new PrankNativeMethods.WindowPlacement { Length = Marshal.SizeOf<PrankNativeMethods.WindowPlacement>() };
        if (!PrankNativeMethods.GetWindowPlacement(handle, ref placement))
            return Denied("placement_query_failed", Marshal.GetLastWin32Error());
        var target = new WindowPrankTarget(handle, processId, threadId, rectangle, ToRectangle(outer), monitor.WorkArea, monitor.MonitorArea)
            { Placement = placement };
        return new(handle, processId, target, "eligible");
    }

    private (string? Reason, IntPtr Blocker) OcclusionRejection(IntPtr target, Rectangle bounds, bool includePet)
    {
        var checkedCount = 0;
        for (var window = PrankNativeMethods.GetWindow(target, PrankNativeMethods.GwHwndPrev);
             window != IntPtr.Zero && checkedCount++ < 2048;
             window = PrankNativeMethods.GetWindow(window, PrankNativeMethods.GwHwndPrev))
        {
            if (!NativeMethods.IsWindowVisible(window) || NativeMethods.IsIconic(window)) continue;
            if (NativeMethods.DwmGetWindowAttribute(window, NativeMethods.DwmwaCloaked, out int cloaked, sizeof(int)) == 0 && cloaked != 0)
                continue;
            var blockerClass = new StringBuilder(128);
            if (NativeMethods.GetClassName(window, blockerClass, blockerClass.Capacity) == 0)
                continue;
            var blockerStyle = NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlStyle).ToInt64();
            var blockerExtended = NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlExStyle).ToInt64();
            var blockerTitle = new StringBuilder(256);
            NativeMethods.GetWindowText(window, blockerTitle, blockerTitle.Capacity);
            if (WindowTerrainEligibilityPolicy.IsLikelyCaptureOverlay(
                    blockerClass.ToString(), blockerTitle.ToString(), blockerExtended, blockerStyle))
            {
                DiagnosticsLog.WriteEventThrottled("prank.capture-overlay", TimeSpan.FromSeconds(15),
                    "prank.occlusion.capture_overlay_ignored",
                    ("Class", blockerClass.ToString()), ("Layered", (blockerExtended & PrankNativeMethods.WsExLayered) != 0),
                    ("Topmost", (blockerExtended & PrankNativeMethods.WsExTopmost) != 0));
                continue;
            }
            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            if (!includePet && processId == Environment.ProcessId) continue;
            if (!NativeMethods.GetWindowRect(window, out var outer)) return ("occluder_bounds_unavailable", window);
            var visibleBounds = TryVisibleBounds(window, out var visible) ? ToRectangle(visible) : ToRectangle(outer);
            var intersection = Rectangle.Intersect(bounds, visibleBounds);
            if (intersection.Width <= 0 || intersection.Height <= 0) continue;
            var outerBounds = ToRectangle(outer);
            var evidence = ReadOccluderEvidence(window, intersection, outerBounds);
            var className = new StringBuilder(128);
            var classRead = NativeMethods.GetClassName(window, className, className.Capacity) > 0;
            var windowTitle = new StringBuilder(256);
            var titleRead = NativeMethods.GetWindowText(window, windowTitle, windowTitle.Capacity) > 0;
            var extendedStyle = NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlExStyle).ToInt64();
            var style = NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlStyle).ToInt64();
            var diagnostic = WindowOcclusionDiagnostic.Classify(
                classRead ? className.ToString() : null,
                titleRead ? windowTitle.ToString() : null,
                extendedStyle,
                style,
                evidence.LayeredAttributesRead,
                evidence.LayeredAttributeFlags,
                evidence.GlobalAlpha,
                evidence.RegionQueried,
                evidence.RegionType,
                evidence.RegionIntersects);
            DiagnosticsLog.WriteEventThrottled(
                "prank.occluder." + diagnostic.Classification,
                TimeSpan.FromSeconds(2),
                "prank.occluder.diagnostic",
                ("Classification", diagnostic.Classification),
                ("ClassCapture", diagnostic.ClassCaptureIdentity),
                ("TitleCapture", diagnostic.TitleCaptureIdentity),
                ("ExtendedStyle", $"0x{diagnostic.ExtendedStyle:X}"),
                ("Style", $"0x{diagnostic.Style:X}"),
                ("Layered", diagnostic.Layered),
                ("LayeredAttributesRead", diagnostic.LayeredAttributesRead),
                ("LayeredAttributeFlags", diagnostic.LayeredAttributeFlags),
                ("GlobalAlpha", diagnostic.GlobalAlpha),
                ("RegionQueried", diagnostic.RegionQueried),
                ("RegionType", diagnostic.RegionType),
                ("RegionIntersects", diagnostic.RegionIntersects),
                ("ProvablyNonCovering", diagnostic.ProvablyNonCovering),
                ("TerrainOverlayLike", diagnostic.TerrainOverlayLike));
            if (diagnostic.ProvablyNonCovering) continue;
            return ("occluded", window);
        }
        return checkedCount < 2048 ? (null, IntPtr.Zero) : ("occlusion_enumeration_limit", IntPtr.Zero);
    }

    private readonly record struct OccluderEvidence(
        bool LayeredAttributesRead,
        uint LayeredAttributeFlags,
        byte GlobalAlpha,
        bool RegionQueried,
        int RegionType,
        bool RegionIntersects);

    private static OccluderEvidence ReadOccluderEvidence(IntPtr window, Rectangle intersection, Rectangle outer)
    {
        var layered = (NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlExStyle).ToInt64() &
            PrankNativeMethods.WsExLayered) != 0;
        byte alpha = 255;
        uint flags = 0;
        var attributesRead = layered &&
            PrankNativeMethods.GetLayeredWindowAttributes(window, out _, out alpha, out flags);
        var region = PrankNativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (region == IntPtr.Zero)
        {
            return new OccluderEvidence(attributesRead, flags, alpha, false, 0, true);
        }

        try
        {
            var regionType = PrankNativeMethods.GetWindowRgn(window, region);
            var local = WindowPrankSelectionPolicy.ToWindowRegionCoordinates(intersection, outer);
            var rectangle = new NativeMethods.NativeRect
            {
                Left = local.Left,
                Top = local.Top,
                Right = local.Right,
                Bottom = local.Bottom,
            };
            var intersects = regionType is not (2 or 3) ||
                PrankNativeMethods.RectInRegion(region, ref rectangle);
            return new OccluderEvidence(attributesRead, flags, alpha, true, regionType, intersects);
        }
        finally
        {
            PrankNativeMethods.DeleteObject(region);
        }
    }

    private static bool IsProvablyNonCovering(IntPtr window, Rectangle intersection, Rectangle outer)
    {
        var evidence = ReadOccluderEvidence(window, intersection, outer);
        var diagnostic = WindowOcclusionDiagnostic.Classify(
            null,
            null,
            NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlExStyle).ToInt64(),
            NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlStyle).ToInt64(),
            evidence.LayeredAttributesRead,
            evidence.LayeredAttributeFlags,
            evidence.GlobalAlpha,
            evidence.RegionQueried,
            evidence.RegionType,
            evidence.RegionIntersects);
        return diagnostic.ProvablyNonCovering;
    }

    private static string? NonElevatedProcessRejection(uint processId, out int nativeError)
    {
        nativeError = 0;
        var process = PrankNativeMethods.OpenProcess(0x1000, false, processId);
        if (process == IntPtr.Zero) { nativeError = Marshal.GetLastWin32Error(); return "process_query_failed"; }
        IntPtr token = IntPtr.Zero;
        try
        {
            if (!PrankNativeMethods.OpenProcessToken(process, 8, out token))
            { nativeError = Marshal.GetLastWin32Error(); return "token_open_failed"; }
            if (!PrankNativeMethods.GetTokenInformation(token, 20, out var elevated, sizeof(int), out _))
            { nativeError = Marshal.GetLastWin32Error(); return "token_elevation_query_failed"; }
            return elevated == 0 ? null : "elevated_process";
        }
        finally
        {
            if (token != IntPtr.Zero) PrankNativeMethods.CloseHandle(token);
            PrankNativeMethods.CloseHandle(process);
        }
    }

    private static string FormatHandle(IntPtr handle) => $"0x{handle.ToInt64():X}";

    private static void LogInspection(WindowPrankInspection inspection) => DiagnosticsLog.WriteEvent(
        "prank.target.inspected", ("Window", FormatHandle(inspection.Handle)), ("Process", inspection.ProcessId),
        ("Reason", inspection.Reason), ("BlockingWindow", FormatHandle(inspection.BlockingWindow)),
        ("NativeError", inspection.NativeError));

    private void RecordCompletion(WindowPrankTarget target, Rectangle actualOuter, bool minimizeConfirmed, bool restoreConfirmed)
    {
        LastCompletion = new(++CompletionSequence, _operation, target.Handle, target.ProcessId,
            target.OuterBounds, actualOuter, minimizeConfirmed, restoreConfirmed);
        DiagnosticsLog.WriteEvent("prank.operation.confirmed", ("Sequence", CompletionSequence),
            ("Operation", _operation), ("Window", FormatHandle(target.Handle)), ("Process", target.ProcessId),
            ("InitialBounds", target.OuterBounds), ("ActualBounds", actualOuter),
            ("MinimizeConfirmed", minimizeConfirmed), ("RestoreConfirmed", restoreConfirmed));
    }

    private static bool TryVisibleBounds(IntPtr window, out NativeMethods.NativeRect bounds) =>
        NativeMethods.DwmGetWindowAttribute(window, NativeMethods.DwmwaExtendedFrameBounds,
            out bounds, Marshal.SizeOf<NativeMethods.NativeRect>()) == 0 && bounds.Right > bounds.Left && bounds.Bottom > bounds.Top;

    private static Rectangle ToRectangle(NativeMethods.NativeRect rect) => Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);

    private static BitmapSource? CaptureVisiblePixels(Rectangle bounds, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
        cancellationToken.ThrowIfCancellationRequested();
        var locked = bitmap.LockBits(new Rectangle(Point.Empty, bounds.Size), ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        byte[] pixels;
        var stride = bounds.Width * 4;
        try
        {
            pixels = new byte[checked(stride * bounds.Height)];
            for (var y = 0; y < bounds.Height; y++)
                Marshal.Copy(IntPtr.Add(locked.Scan0, y * locked.Stride), pixels, y * stride, stride);
        }
        finally { bitmap.UnlockBits(locked); }
        if (!WindowPrankGeometry.HasUsefulPixels(pixels, stride, bounds.Width, bounds.Height)) return null;
        for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        var source = BitmapSource.Create(bounds.Width, bounds.Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        source.Freeze();
        Array.Clear(pixels);
        return source;
    }
}
