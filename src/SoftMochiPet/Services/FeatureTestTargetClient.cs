using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using SoftMochiPet.Core;
using SoftMochiPet.Interop;

namespace SoftMochiPet.Services;

/// <summary>Controls only the child process created for the user's feature test.</summary>
public sealed class FeatureTestTargetClient : IDisposable
{
    private const long TopmostStyle = 0x00000008;
    private const uint AsyncWindowPosition = 0x4000;
    private const uint ShowWindowFlag = 0x0040;
    private const uint NoMove = 0x0002;
    private readonly Process _process;
    private readonly uint _windowThreadId;
    private bool _disposed;

    private FeatureTestTargetClient(Process process, IntPtr handle, uint windowThreadId)
    {
        _process = process;
        Handle = handle;
        ProcessId = (uint)process.Id;
        _windowThreadId = windowThreadId;
    }

    public IntPtr Handle { get; }
    public uint ProcessId { get; }
    public bool IsAlive => !_disposed && IsOwnedLiveWindow();
    public Rectangle Bounds => IsAlive && NativeMethods.GetWindowRect(Handle, out var bounds)
        ? Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom) : Rectangle.Empty;

    public static async Task<FeatureTestTargetClient> StartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("找不到当前程序入口。");
        var startInfo = FeatureTestTargetLaunchPolicy.CreateStartInfo(processPath,
            Assembly.GetEntryAssembly()?.Location, Environment.ProcessId);
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException("专用测试窗口没有启动。");
        process.EnableRaisingEvents = true;
        try
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(10))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (process.HasExited) throw new InvalidOperationException("专用测试窗口已退出。");
                process.Refresh();
                var handle = process.MainWindowHandle;
                if (handle != IntPtr.Zero)
                {
                    var thread = NativeMethods.GetWindowThreadProcessId(handle, out var ownerPid);
                    if (thread != 0 && ownerPid == (uint)process.Id)
                        return new FeatureTestTargetClient(process, handle, thread);
                }
                await Task.Delay(25, cancellationToken);
            }
            throw new TimeoutException("等待专用测试窗口超时。");
        }
        catch
        {
            // A delayed helper is closed when its own window appears. Never kill
            // a process or send a close message based only on a recycled HWND.
            _ = ClosePendingChildAsync(process);
            throw;
        }
    }

    public bool Move(Rectangle physicalBounds)
    {
        if (!IsAlive || !FeatureTestTargetLaunchPolicy.IsValidBounds(physicalBounds)) return false;
        return NativeMethods.SetWindowPos(Handle, IntPtr.Zero,
            physicalBounds.X, physicalBounds.Y, physicalBounds.Width, physicalBounds.Height,
            NativeMethods.SwpNoZOrder | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder | 0x4000);
    }

    public void Show()
    {
        if (IsAlive) ShowWindowAsync(Handle, 4);
    }

    public async Task PrepareAsync(Rectangle physicalBounds, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!IsAlive) throw new InvalidOperationException("test_window_closed");
        if (!FeatureTestTargetLaunchPolicy.IsValidBounds(physicalBounds))
            throw new InvalidOperationException("test_window_invalid_bounds");
        var phase = "promote";
        try
        {
            if (!NativeMethods.IsWindowVisible(Handle) || NativeMethods.IsIconic(Handle))
                ShowWindowAsync(Handle, 4);
            // A brief owned-helper-only promotion bypasses no capture rule: the
            // caller still inspects the ordinary, demoted window before acting.
            RequestPreparedPosition(physicalBounds, topmost: true, phase);
            await WaitForPreparedPositionAsync(physicalBounds, topmost: true, phase, token);
            phase = "demote";
            RequestPreparedPosition(physicalBounds, topmost: false, phase);
            await WaitForPreparedPositionAsync(physicalBounds, topmost: false, phase, token);
        }
        catch (OperationCanceledException)
        {
            LogPreparation(phase, "canceled");
            throw;
        }
        catch (InvalidOperationException exception)
        {
            LogPreparation(phase, exception.Message);
            throw;
        }
        finally
        {
            // Cancellation cannot strand the helper in the topmost band. This
            // cleanup deliberately does not reuse the canceled operation token.
            await EnsureOrdinaryLayerAsync();
        }
    }

    private void RequestPreparedPosition(Rectangle bounds, bool topmost, string phase)
    {
        if (!IsAlive) throw new InvalidOperationException("test_window_closed");
        if (!NativeMethods.SetWindowPos(Handle, new IntPtr(topmost ? -1 : -2),
            bounds.X, bounds.Y, bounds.Width, bounds.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder | AsyncWindowPosition | ShowWindowFlag))
            throw new InvalidOperationException($"test_window_{phase}_request_failed");
        LogPreparation(phase, "requested", bounds);
    }

    private async Task WaitForPreparedPositionAsync(Rectangle bounds, bool topmost, string phase, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalSeconds < 3)
        {
            token.ThrowIfCancellationRequested();
            if (!IsAlive) throw new InvalidOperationException("test_window_closed");
            if (FeatureTestTargetLaunchPolicy.IsPreparationConfirmed(
                NativeMethods.IsWindowVisible(Handle), NativeMethods.IsIconic(Handle), IsTopmost(), topmost, Bounds, bounds))
            {
                LogPreparation(phase, "confirmed", bounds, watch.Elapsed.TotalMilliseconds);
                return;
            }
            await Task.Delay(20, token);
        }
        throw new InvalidOperationException($"test_window_{phase}_not_confirmed");
    }

    private async Task EnsureOrdinaryLayerAsync()
    {
        if (!IsAlive) return;
        var requested = NativeMethods.SetWindowPos(Handle, new IntPtr(-2), 0, 0, 0, 0,
            NoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder | AsyncWindowPosition);
        if (!requested)
        {
            LogPreparation("cleanup", "normal_layer_request_failed");
            return;
        }
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalMilliseconds < 750 && IsAlive)
        {
            if (!IsTopmost())
            {
                LogPreparation("cleanup", "normal_layer_confirmed", elapsedMs: watch.Elapsed.TotalMilliseconds);
                return;
            }
            await Task.Delay(20);
        }
        LogPreparation("cleanup", IsAlive ? "normal_layer_not_confirmed" : "window_closed",
            elapsedMs: watch.Elapsed.TotalMilliseconds);
    }

    private bool IsTopmost() => (NativeMethods.GetWindowLongPtr(Handle, NativeMethods.GwlExStyle).ToInt64() & TopmostStyle) != 0;

    private void LogPreparation(string phase, string status, Rectangle? expected = null, double? elapsedMs = null)
    {
        var alive = IsAlive;
        DiagnosticsLog.WriteEvent("FeatureTestTargetPreparation", ("Window", $"0x{Handle.ToInt64():X}"),
            ("Process", ProcessId), ("Thread", _windowThreadId), ("Phase", phase), ("Status", status),
            ("Alive", alive), ("Topmost", alive && IsTopmost()), ("Bounds", alive ? Bounds : Rectangle.Empty),
            ("ExpectedBounds", expected), ("ElapsedMs", elapsedMs));
    }

    public void SetStep(string text)
    {
        if (IsAlive) SetWindowText(Handle, FeatureTestTargetLaunchPolicy.StepTitle(text));
    }

    public void Dispose()
    {
        if (_disposed) return;
        if (IsOwnedLiveWindow()) PostMessage(Handle, 0x0010, IntPtr.Zero, IntPtr.Zero);
        _disposed = true;
        _process.Dispose();
    }

    private bool IsOwnedLiveWindow()
    {
        try
        {
            if (_process.HasExited || Handle == IntPtr.Zero) return false;
            var thread = NativeMethods.GetWindowThreadProcessId(Handle, out var owner);
            return thread == _windowThreadId && owner == ProcessId && IsWindow(Handle);
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static async Task ClosePendingChildAsync(Process process)
    {
        try
        {
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(30) && !process.HasExited)
            {
                process.Refresh();
                var handle = process.MainWindowHandle;
                if (handle != IntPtr.Zero && NativeMethods.GetWindowThreadProcessId(handle, out var owner) != 0 &&
                    owner == (uint)process.Id)
                {
                    PostMessage(handle, 0x0010, IntPtr.Zero, IntPtr.Zero);
                    return;
                }
                await Task.Delay(100).ConfigureAwait(false);
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }
        finally { process.Dispose(); }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr handle, int command);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowText(IntPtr handle, string text);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);
}

public static class FeatureTestTargetLaunchPolicy
{
    public static bool IsPreparationConfirmed(bool visible, bool minimized, bool topmost, bool expectedTopmost,
        Rectangle actualBounds, Rectangle expectedBounds) =>
        visible && !minimized && topmost == expectedTopmost && IsValidBounds(actualBounds) &&
        IsValidBounds(expectedBounds) && WindowPrankGeometry.SameBounds(actualBounds, expectedBounds);

    public static ProcessStartInfo CreateStartInfo(string processPath, string? entryAssembly, int parentPid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processPath);
        if (parentPid <= 0) throw new ArgumentOutOfRangeException(nameof(parentPid));
        var info = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(processPath))!,
        };
        if (string.Equals(Path.GetFileNameWithoutExtension(processPath), "dotnet", StringComparison.OrdinalIgnoreCase))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(entryAssembly);
            info.ArgumentList.Add(entryAssembly);
        }
        info.ArgumentList.Add("--feibi-feature-target");
        info.ArgumentList.Add("--parent-pid");
        info.ArgumentList.Add(parentPid.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return info;
    }

    public static bool IsValidBounds(Rectangle bounds) => bounds.Width is >= 180 and <= 16384 &&
        bounds.Height is >= 100 and <= 16384 && (long)bounds.X + bounds.Width is >= int.MinValue and <= int.MaxValue &&
        (long)bounds.Y + bounds.Height is >= int.MinValue and <= int.MaxValue;

    public static string StepTitle(string? text)
    {
        var step = string.IsNullOrWhiteSpace(text) ? string.Empty
            : new string(text.Where(character => !char.IsControl(character)).Take(80).ToArray()).Trim();
        return step.Length == 0 ? FeatureTestTargetWindow.WindowTitle : $"{FeatureTestTargetWindow.WindowTitle} · {step}";
    }
}
