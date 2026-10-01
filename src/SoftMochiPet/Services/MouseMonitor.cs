using System.Runtime.InteropServices;
using SoftMochiPet.Core;
using SoftMochiPet.Interop;
using DrawingPoint = System.Drawing.Point;

namespace SoftMochiPet.Services;

public sealed record PointerSnapshot(
    DrawingPoint Point,
    DateTimeOffset At,
    DesktopIconSnapshot? DesktopIcon = null,
    string DesktopCaptureStatus = "Pending")
{
    public DrawingPoint EffectivePoint => DesktopIcon?.Center ?? Point;
}

public sealed class MouseMonitor : IDisposable
{
    private readonly object _gate = new();
    private readonly NativeMethods.LowLevelMouseProc _mouseCallback;
    private readonly NativeMethods.LowLevelKeyboardProc _keyboardCallback;
    private readonly DeletionAnchorBuffer<PointerSnapshot> _deletionAnchors = new();
    private IntPtr _mouseHook;
    private IntPtr _keyboardHook;
    private PointerSnapshot? _lastLeftClick;
    private PointerSnapshot? _lastRightClick;
    private DateTimeOffset? _lastFallbackAnchorAt;

    public MouseMonitor()
    {
        _mouseCallback = MouseHookCallback;
        _keyboardCallback = KeyboardHookCallback;
    }

    public void Start()
    {
        if (_mouseHook != IntPtr.Zero || _keyboardHook != IntPtr.Zero)
        {
            return;
        }

        _mouseHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhMouseLl,
            _mouseCallback,
            NativeMethods.GetModuleHandle(null),
            0);
        _keyboardHook = NativeMethods.SetWindowsHookEx(
            NativeMethods.WhKeyboardLl,
            _keyboardCallback,
            NativeMethods.GetModuleHandle(null),
            0);
        DiagnosticsLog.WriteEvent(
            "InputHooksStarted",
            ("MouseHook", _mouseHook != IntPtr.Zero),
            ("KeyboardHook", _keyboardHook != IntPtr.Zero));
    }

    public PointerSnapshot? TakeRecentDeletionAnchor(string? displayName = null)
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var desktopForeground = IsDesktopForeground();
            if (desktopForeground && !string.IsNullOrWhiteSpace(displayName) &&
                _deletionAnchors.TryTakeMatching(
                    snapshot => string.Equals(
                        snapshot.DesktopIcon?.DisplayText,
                        displayName,
                        StringComparison.OrdinalIgnoreCase),
                    now,
                    TimeSpan.FromSeconds(15),
                    out var namedReservation))
            {
                return namedReservation;
            }
            if (desktopForeground &&
                _deletionAnchors.TryTake(now, TimeSpan.FromSeconds(15), out var reserved))
            {
                return reserved;
            }
            if (!desktopForeground)
            {
                _deletionAnchors.Clear();
            }

            var right = _lastRightClick is not null && now - _lastRightClick.At <= TimeSpan.FromSeconds(10)
                ? _lastRightClick
                : null;
            var left = _lastLeftClick is not null && now - _lastLeftClick.At <= TimeSpan.FromSeconds(30)
                ? _lastLeftClick
                : null;

            // A context-menu delete produces a newer left click on the Delete
            // command. Never let that menu click replace the icon that was
            // captured by the preceding right click.
            var exactRight = right?.DesktopIcon is not null ? right : null;
            var exactLeft = left?.DesktopIcon is not null ? left : null;
            if (desktopForeground && (exactRight is not null || exactLeft is not null))
            {
                var exact = exactRight is null || exactLeft?.At > exactRight.At
                    ? exactLeft
                    : exactRight;
                if (exact is not null && exact.At != _lastFallbackAnchorAt)
                {
                    _lastFallbackAnchorAt = exact.At;
                    return exact;
                }
            }

            var fallback = right ?? left;
            return fallback is null
                ? null
                : fallback with
                {
                    DesktopIcon = null,
                    DesktopCaptureStatus = "CursorFallback",
                };
        }
    }

    public static DrawingPoint GetCursorPosition()
    {
        return NativeMethods.GetCursorPos(out var point)
            ? new DrawingPoint(point.X, point.Y)
            : DrawingPoint.Empty;
    }

    private IntPtr MouseHookCallback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message.ToInt32() is NativeMethods.WmLeftButtonDown or NativeMethods.WmRightButtonDown)
        {
            var hookData = Marshal.PtrToStructure<NativeMethods.MouseHookData>(data);
            var clickPoint = new DrawingPoint(hookData.Point.X, hookData.Point.Y);
            // Capture the desktop ListView before Explorer has a chance to open a
            // context menu over the clicked icon. Remote rectangle reading stays
            // asynchronous so the low-level hook returns immediately.
            var listView = DesktopIconLocator.FindListViewAt(clickPoint);
            lock (_gate)
            {
                var snapshot = new PointerSnapshot(
                    clickPoint,
                    DateTimeOffset.UtcNow,
                    DesktopCaptureStatus: listView == IntPtr.Zero
                        ? "DesktopListViewNotFound"
                        : "PendingHitTest");
                var isRightClick = message.ToInt32() == NativeMethods.WmRightButtonDown;
                if (isRightClick)
                {
                    _lastRightClick = snapshot;
                }
                else
                {
                    _lastLeftClick = snapshot;
                }

                _ = ResolveDesktopIconAsync(snapshot, isRightClick, listView);
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHook, code, message, data);
    }

    private IntPtr KeyboardHookCallback(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0 && message.ToInt32() is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown)
        {
            var hookData = Marshal.PtrToStructure<NativeMethods.KeyboardHookData>(data);
            if (hookData.VirtualKeyCode == NativeMethods.VkDelete)
            {
                if (IsDesktopForeground())
                {
                    CaptureSelectedDesktopIcons("DeleteKey");
                }
                else
                {
                    lock (_gate)
                    {
                        _deletionAnchors.Clear();
                    }
                }
            }
        }

        return NativeMethods.CallNextHookEx(_keyboardHook, code, message, data);
    }

    private async Task ResolveDesktopIconAsync(
        PointerSnapshot snapshot,
        bool isRightClick,
        IntPtr listView)
    {
        await Task.Yield();
        var desktopIcon = DesktopIconLocator.TryGetIconAt(listView, snapshot.Point);
        var captureStatus = listView == IntPtr.Zero
            ? "DesktopListViewNotFound"
            : desktopIcon is null
                ? "DesktopIconHitTestMissed"
                : "ExactDesktopIcon";

        lock (_gate)
        {
            if (isRightClick && _lastRightClick?.At == snapshot.At)
            {
                _lastRightClick = snapshot with
                {
                    DesktopIcon = desktopIcon,
                    DesktopCaptureStatus = captureStatus,
                };
            }
            else if (!isRightClick && _lastLeftClick?.At == snapshot.At)
            {
                _lastLeftClick = snapshot with
                {
                    DesktopIcon = desktopIcon,
                    DesktopCaptureStatus = captureStatus,
                };
            }

            if (desktopIcon is not null)
            {
                ReplaceSelectedDesktopAnchors(
                    DesktopIconLocator.GetSelectedIcons(),
                    snapshot.At,
                    isRightClick ? "DesktopRightClickSelection" : "DesktopLeftClickSelection");
            }
        }
    }

    private void CaptureSelectedDesktopIcons(string source)
    {
        try
        {
            var capturedAt = DateTimeOffset.UtcNow;
            var selected = DesktopIconLocator.GetSelectedIcons();
            lock (_gate)
            {
                ReplaceSelectedDesktopAnchors(selected, capturedAt, source);
            }
        }
        catch (Exception exception)
        {
            DiagnosticsLog.Write("Selected desktop icon capture failed.", exception);
        }
    }

    private void ReplaceSelectedDesktopAnchors(
        IReadOnlyList<DesktopIconSnapshot> selected,
        DateTimeOffset capturedAt,
        string source)
    {
        if (selected.Count == 0)
        {
            return;
        }

        _deletionAnchors.Replace(
            selected.Select(icon => new PointerSnapshot(
                icon.Center,
                capturedAt,
                icon,
                source)),
            capturedAt);
        DiagnosticsLog.WriteEvent(
            "DesktopSelectionCaptured",
            ("Source", source),
            ("Count", selected.Count),
            ("Icons", string.Join(';', selected.Select(icon =>
                $"{icon.DisplayText ?? "?"}@{icon.Center.X},{icon.Center.Y}"))));
    }

    private static bool IsDesktopForeground()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        if (foreground == NativeMethods.GetShellWindow())
        {
            return true;
        }

        var className = new System.Text.StringBuilder(128);
        return NativeMethods.GetClassName(foreground, className, className.Capacity) > 0 &&
            className.ToString() is "Progman" or "WorkerW" or "SHELLDLL_DefView" or "SysListView32";
    }

    public void Dispose()
    {
        if (_mouseHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        if (_keyboardHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }
    }
}
