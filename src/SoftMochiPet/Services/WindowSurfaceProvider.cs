using System.Runtime.InteropServices;
using System.Text;
using SoftMochiPet.Core;
using SoftMochiPet.Interop;
using DrawingRectangle = System.Drawing.Rectangle;

namespace SoftMochiPet.Services;

public sealed record WindowSurface(
    IntPtr SourceHandle,
    int Left,
    int Right,
    int Top,
    bool IsDesktopFloor,
    int Bottom = 0)
{
    public int Width => Math.Max(0, Right - Left);
    public int EffectiveBottom => Bottom > Top ? Bottom : Top;

    public bool ContainsX(int x, int inset = 0)
    {
        var safeInset = Math.Min(Math.Max(0, inset), Math.Max(0, Width / 2 - 1));
        return x >= Left + safeInset && x <= Right - safeInset;
    }
}

/// <summary>
/// Produces a lightweight, physical-pixel map of surfaces the pet can stand on.
/// Only stable top-level app windows and each monitor's work-area floor are used.
/// </summary>
public sealed class WindowSurfaceProvider
{
    private static readonly HashSet<string> IgnoredClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "DV2ControlHost",
        "tooltips_class32",
        "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow",
    };

    private readonly uint _currentProcessId = (uint)Environment.ProcessId;
    private WindowSurface[] _surfaces = [];
    private WindowBodySnapshot[] _windowBodies = [];

    public IReadOnlyList<WindowSurface> Surfaces => _surfaces;
    public IReadOnlyList<WindowBodySnapshot> WindowBodies => _windowBodies;
    public long RefreshVersion { get; private set; }

    public void Refresh(IntPtr petWindow, IntPtr? onlyWindow = null)
    {
        var monitors = DisplayGeometry.GetAllMonitors();
        var surfaces = new List<WindowSurface>();
        var windowBodies = new List<WindowBodySnapshot>();
        foreach (var monitor in monitors)
        {
            surfaces.Add(new WindowSurface(
                monitor.Handle,
                monitor.WorkArea.Left,
                monitor.WorkArea.Right - 1,
                monitor.WorkArea.Bottom - 1,
                IsDesktopFloor: true,
                Bottom: monitor.WorkArea.Bottom - 1));
        }

        NativeMethods.EnumWindows((window, _) =>
        {
            if (onlyWindow.HasValue && window != onlyWindow.Value) return true;
            if (!TryGetEligibleBounds(window, petWindow, out var rectangle))
            {
                return true;
            }

            var width = rectangle.Right - rectangle.Left;
            var height = rectangle.Bottom - rectangle.Top;
            if (width < 140 || height < 60)
            {
                return true;
            }

            var body = new DrawingRectangle(rectangle.Left, rectangle.Top, width, height);
            if (WindowEnclosureEligibilityPolicy.IsBoundedBody(body, monitors))
            {
                windowBodies.Add(new WindowBodySnapshot(
                    window,
                    rectangle.Left,
                    rectangle.Top,
                    rectangle.Right,
                    rectangle.Bottom));
            }

            surfaces.AddRange(CreateWindowSegments(
                window,
                new DrawingRectangle(rectangle.Left, rectangle.Top, width, height),
                monitors));

            return true;
        }, IntPtr.Zero);

        _surfaces = surfaces
            .OrderBy(surface => surface.Top)
            .ThenByDescending(surface => surface.IsDesktopFloor)
            .ToArray();
        _windowBodies = windowBodies.ToArray();
        RefreshVersion++;
    }

    /// <summary>
    /// Splits a spanning window into one platform per monitor on which its top
    /// edge is actually visible. This prevents a platform from bridging a gap
    /// between monitors and keeps the usable half on every intersected screen.
    /// </summary>
    public static IReadOnlyList<WindowSurface> CreateWindowSegments(
        IntPtr sourceHandle,
        DrawingRectangle windowBounds,
        IReadOnlyList<MonitorGeometry> monitors)
    {
        var segments = new List<WindowSurface>();
        foreach (var monitor in monitors)
        {
            var workArea = monitor.WorkArea;
            if (windowBounds.Bottom <= workArea.Top || windowBounds.Top >= workArea.Bottom ||
                windowBounds.Top <= workArea.Top + 36 || windowBounds.Top >= workArea.Bottom - 30)
            {
                continue;
            }

            var left = Math.Max(windowBounds.Left, workArea.Left);
            var rightExclusive = Math.Min(windowBounds.Right, workArea.Right);
            if (rightExclusive - left >= 140)
            {
                segments.Add(new WindowSurface(
                    sourceHandle,
                    left,
                    rightExclusive - 1,
                    windowBounds.Top,
                    IsDesktopFloor: false,
                    Bottom: windowBounds.Bottom));
            }
        }

        return segments;
    }

    public WindowSurface? FindLandingSurface(int footX, double previousFootY, double nextFootY, int inset = 8)
    {
        return FindLandingSurface(_surfaces, footX, previousFootY, nextFootY, inset);
    }

    public static WindowSurface? FindLandingSurface(
        IReadOnlyList<WindowSurface> surfaces,
        int footX,
        double previousFootY,
        double nextFootY,
        int inset = 8)
    {
        var start = Math.Min(previousFootY, nextFootY) - 1;
        var end = Math.Max(previousFootY, nextFootY) + 2;
        return surfaces.FirstOrDefault(surface =>
            surface.ContainsX(footX, inset) && surface.Top >= start && surface.Top <= end);
    }

    public static WindowSurface? FindDesktopRecoverySurface(
        IReadOnlyList<WindowSurface> surfaces,
        IntPtr monitorHandle,
        int footX,
        double footY,
        int inset = 8)
    {
        return surfaces
            .Where(surface =>
                surface.IsDesktopFloor &&
                surface.SourceHandle == monitorHandle &&
                surface.ContainsX(footX, inset) &&
                footY > surface.Top + 2)
            .OrderBy(surface => Math.Abs(surface.Top - footY))
            .FirstOrDefault();
    }

    public WindowSurface? FindSupport(int footX, double footY, int tolerance = 6, int inset = 8)
    {
        return FindSupport(_surfaces, footX, footY, tolerance, inset);
    }

    public static WindowSurface? FindSupport(
        IReadOnlyList<WindowSurface> surfaces,
        int footX,
        double footY,
        int tolerance = 6,
        int inset = 8)
    {
        return surfaces
            .Where(surface => surface.ContainsX(footX, inset) && Math.Abs(surface.Top - footY) <= tolerance)
            .OrderBy(surface => Math.Abs(surface.Top - footY))
            .FirstOrDefault();
    }

    public WindowSurface? FindTrackedSurface(
        IntPtr sourceHandle,
        bool isDesktopFloor,
        int footX,
        double footY)
    {
        return FindTrackedSurface(_surfaces, sourceHandle, isDesktopFloor, footX, footY);
    }

    public WindowSurface? FindLiveTrackedSurface(
        IntPtr sourceHandle,
        bool isDesktopFloor,
        int footX,
        double footY)
    {
        if (sourceHandle == IntPtr.Zero)
        {
            return null;
        }

        if (isDesktopFloor)
        {
            return FindTrackedSurface(sourceHandle, true, footX, footY);
        }

        if (!TryGetEligibleBounds(sourceHandle, IntPtr.Zero, out var rectangle))
        {
            return null;
        }

        var width = rectangle.Right - rectangle.Left;
        var height = rectangle.Bottom - rectangle.Top;
        if (width < 140 || height < 60)
        {
            return null;
        }

        return CreateWindowSegments(
                sourceHandle,
                new DrawingRectangle(rectangle.Left, rectangle.Top, width, height),
                DisplayGeometry.GetAllMonitors())
            .Where(surface => surface.ContainsX(footX, 8))
            .OrderBy(surface => Math.Abs(surface.Top - footY))
            .FirstOrDefault();
    }

    public WindowBodySnapshot? FindLiveWindowBody(IntPtr sourceHandle)
    {
        if (!TryGetEligibleBounds(sourceHandle, IntPtr.Zero, out var rectangle))
        {
            return null;
        }

        var width = rectangle.Right - rectangle.Left;
        var height = rectangle.Bottom - rectangle.Top;
        var body = new DrawingRectangle(rectangle.Left, rectangle.Top, width, height);
        return width < 140 || height < 60 ||
            !WindowEnclosureEligibilityPolicy.IsBoundedBody(body, DisplayGeometry.GetAllMonitors())
            ? null
            : new WindowBodySnapshot(
                sourceHandle,
                rectangle.Left,
                rectangle.Top,
                rectangle.Right,
                rectangle.Bottom);
    }

    public static WindowSurface? FindTrackedSurface(
        IReadOnlyList<WindowSurface> surfaces,
        IntPtr sourceHandle,
        bool isDesktopFloor,
        int footX,
        double footY)
    {
        return surfaces
            .Where(surface =>
                surface.SourceHandle == sourceHandle &&
                surface.IsDesktopFloor == isDesktopFloor &&
                surface.ContainsX(footX, 8))
            .OrderBy(surface => Math.Abs(surface.Top - footY))
            .FirstOrDefault();
    }

    private static bool TryGetVisibleBounds(IntPtr window, out NativeMethods.NativeRect rectangle)
    {
        try
        {
            if (NativeMethods.DwmGetWindowAttribute(
                    window,
                    NativeMethods.DwmwaExtendedFrameBounds,
                    out rectangle,
                    Marshal.SizeOf<NativeMethods.NativeRect>()) == 0)
            {
                return rectangle.Right > rectangle.Left && rectangle.Bottom > rectangle.Top;
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        return NativeMethods.GetWindowRect(window, out rectangle) &&
            rectangle.Right > rectangle.Left && rectangle.Bottom > rectangle.Top;
    }

    private bool TryGetEligibleBounds(
        IntPtr window,
        IntPtr petWindow,
        out NativeMethods.NativeRect rectangle)
    {
        rectangle = default;
        if (window == IntPtr.Zero || window == petWindow ||
            !NativeMethods.IsWindowVisible(window) || NativeMethods.IsIconic(window))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == _currentProcessId)
        {
            return false;
        }

        var extendedStyle = NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlExStyle).ToInt64();
        if ((extendedStyle & (NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate)) != 0 ||
            IsCloaked(window))
        {
            return false;
        }

        var className = GetClassName(window);
        if (IgnoredClasses.Contains(className))
        {
            return false;
        }

        var style = NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlStyle).ToInt64();
        if (WindowTerrainEligibilityPolicy.ShouldIgnoreOverlay(
                className,
                GetWindowTitle(window),
                extendedStyle,
                style))
        {
            return false;
        }

        return TryGetVisibleBounds(window, out rectangle);
    }

    private static bool IsCloaked(IntPtr window)
    {
        try
        {
            return NativeMethods.DwmGetWindowAttribute(
                window,
                NativeMethods.DwmwaCloaked,
                out int cloaked,
                sizeof(int)) == 0 && cloaked != 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static string GetClassName(IntPtr window)
    {
        var className = new StringBuilder(128);
        return NativeMethods.GetClassName(window, className, className.Capacity) > 0
            ? className.ToString()
            : string.Empty;
    }

    private static string GetWindowTitle(IntPtr window)
    {
        var windowTitle = new StringBuilder(256);
        return NativeMethods.GetWindowText(window, windowTitle, windowTitle.Capacity) > 0
            ? windowTitle.ToString()
            : string.Empty;
    }
}
