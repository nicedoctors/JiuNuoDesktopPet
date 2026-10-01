using System.Runtime.InteropServices;
using SoftMochiPet.Core;
using SoftMochiPet.Interop;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;

namespace SoftMochiPet.Services;

public sealed record MonitorGeometry(
    IntPtr Handle,
    DrawingRectangle MonitorArea,
    DrawingRectangle WorkArea,
    int ScalePercent)
{
    public double Scale => Math.Max(1, ScalePercent) / 100d;
}

public static class DisplayGeometry
{
    private const int EffectiveMonitorDpi = 0;
    private static readonly object ScaleCacheLock = new();
    private static readonly Dictionary<IntPtr, int> WindowScaleByMonitor = [];

    public static IReadOnlyList<MonitorGeometry> GetAllMonitors()
    {
        var monitors = new List<MonitorGeometry>();
        NativeMethods.EnumDisplayMonitors(
            IntPtr.Zero,
            IntPtr.Zero,
            (IntPtr handle, IntPtr deviceContext, ref NativeMethods.NativeRect monitorRectangle, IntPtr parameter) =>
            {
                var geometry = FromHandle(handle);
                if (geometry is not null)
                {
                    monitors.Add(geometry);
                }

                return true;
            },
            IntPtr.Zero);
        return monitors;
    }

    public static MonitorGeometry? FromPoint(DrawingPoint point, bool nearest = true)
    {
        var handle = NativeMethods.MonitorFromPoint(
            new NativeMethods.NativePoint { X = point.X, Y = point.Y },
            nearest ? NativeMethods.MonitorDefaultToNearest : NativeMethods.MonitorDefaultToNull);
        return FromHandle(handle);
    }

    public static MonitorGeometry? FromWindow(IntPtr window)
    {
        var handle = MonitorHandleFromWindow(window);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var windowDpi = GetWindowDpi(window);
        var windowScale = DpiScalePolicy.FromDpi(windowDpi);
        if (windowScale is not null)
        {
            lock (ScaleCacheLock)
            {
                WindowScaleByMonitor[handle] = windowScale.Value;
            }
        }

        return FromHandle(handle, windowScale);
    }

    public static IntPtr MonitorHandleFromWindow(IntPtr window) =>
        window == IntPtr.Zero
            ? IntPtr.Zero
            : NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest);

    public static uint GetWindowDpi(IntPtr window)
    {
        if (window == IntPtr.Zero)
        {
            return 0;
        }

        try
        {
            return NativeMethods.GetDpiForWindow(window);
        }
        catch (EntryPointNotFoundException)
        {
            return 0;
        }
    }

    public static void InvalidateScaleCache()
    {
        lock (ScaleCacheLock)
        {
            WindowScaleByMonitor.Clear();
        }
    }

    public static DrawingSize ProjectDipSize(double width, double height, MonitorGeometry monitor)
    {
        return new DrawingSize(
            Math.Max(1, (int)Math.Round(width * monitor.Scale)),
            Math.Max(1, (int)Math.Round(height * monitor.Scale)));
    }

    public static DrawingPoint ClampPointToNearestWorkArea(DrawingPoint point, int marginPixels = 0)
    {
        var monitor = FromPoint(point);
        if (monitor is null)
        {
            return point;
        }

        var area = monitor.WorkArea;
        var minimumX = Math.Min(area.Right - 1, area.Left + marginPixels);
        var maximumX = Math.Max(minimumX, area.Right - 1 - marginPixels);
        var minimumY = Math.Min(area.Bottom - 1, area.Top + marginPixels);
        var maximumY = Math.Max(minimumY, area.Bottom - 1 - marginPixels);
        return new DrawingPoint(
            Math.Clamp(point.X, minimumX, maximumX),
            Math.Clamp(point.Y, minimumY, maximumY));
    }

    public static bool MoveWindowPhysical(IntPtr window, int left, int top)
    {
        return window != IntPtr.Zero && NativeMethods.SetWindowPos(
            window,
            IntPtr.Zero,
            left,
            top,
            0,
            0,
            NativeMethods.SwpNoSize |
            NativeMethods.SwpNoZOrder |
            NativeMethods.SwpNoActivate |
            NativeMethods.SwpNoOwnerZOrder);
    }

    public static bool OffsetWindowPhysical(IntPtr window, int deltaX, int deltaY)
    {
        return window != IntPtr.Zero &&
            NativeMethods.GetWindowRect(window, out var rectangle) &&
            MoveWindowPhysical(window, rectangle.Left + deltaX, rectangle.Top + deltaY);
    }

    public static bool ClampWindowToNearestWorkArea(IntPtr window)
    {
        if (window == IntPtr.Zero || !NativeMethods.GetWindowRect(window, out var rectangle))
        {
            return false;
        }

        var width = Math.Max(1, rectangle.Right - rectangle.Left);
        var height = Math.Max(1, rectangle.Bottom - rectangle.Top);
        var center = new DrawingPoint(rectangle.Left + width / 2, rectangle.Top + height / 2);
        var monitor = FromPoint(center);
        if (monitor is null)
        {
            return false;
        }

        var area = monitor.WorkArea;
        var left = width <= area.Width
            ? Math.Clamp(rectangle.Left, area.Left, area.Right - width)
            : area.Left + (area.Width - width) / 2;
        var top = height <= area.Height
            ? Math.Clamp(rectangle.Top, area.Top, area.Bottom - height)
            : area.Top + (area.Height - height) / 2;
        return (left == rectangle.Left && top == rectangle.Top) ||
            MoveWindowPhysical(window, left, top);
    }

    internal static MonitorGeometry? FromHandle(IntPtr handle, int? preferredScalePercent = null)
    {
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var info = new NativeMethods.MonitorInfo
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfo>(),
        };
        if (!NativeMethods.GetMonitorInfo(handle, ref info))
        {
            return null;
        }

        int? cachedScale;
        lock (ScaleCacheLock)
        {
            cachedScale = WindowScaleByMonitor.TryGetValue(handle, out var detectedScale)
                ? detectedScale
                : null;
        }

        uint monitorDpi = 0;
        var shellScalePercent = 0;
        try
        {
            if (NativeMethods.GetDpiForMonitor(
                    handle,
                    EffectiveMonitorDpi,
                    out var detectedDpiX,
                    out _) == 0)
            {
                monitorDpi = detectedDpiX;
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        try
        {
            if (NativeMethods.GetScaleFactorForMonitor(handle, out var detectedScale) == 0)
            {
                shellScalePercent = detectedScale;
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        var scalePercent = preferredScalePercent ?? cachedScale ??
            DpiScalePolicy.Resolve(0, monitorDpi, shellScalePercent);

        return new MonitorGeometry(
            handle,
            ToRectangle(info.MonitorArea),
            ToRectangle(info.WorkArea),
            scalePercent);
    }

    private static DrawingRectangle ToRectangle(NativeMethods.NativeRect rectangle)
    {
        return new DrawingRectangle(
            rectangle.Left,
            rectangle.Top,
            Math.Max(0, rectangle.Right - rectangle.Left),
            Math.Max(0, rectangle.Bottom - rectangle.Top));
    }
}
