using System.Runtime.InteropServices;

namespace SoftMochiPet.Interop;

internal static class PrankNativeMethods
{
    internal const uint SwpAsyncWindowPos = 0x4000;
    internal const int SwMinimizeNoActivate = 7;
    internal const int SwShowNoActivate = 4;
    internal const long WsCaption = 0x00C00000;
    internal const long WsSysMenu = 0x00080000;
    internal const long WsMinimizeBox = 0x00020000;
    internal const long WsChild = 0x40000000;
    internal const long WsExTopmost = 0x00000008;
    internal const long WsExLayered = 0x00080000;
    internal const uint GwOwner = 4;
    internal const uint GwHwndPrev = 3;

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowPlacement
    {
        internal int Length;
        internal int Flags;
        internal int ShowCommand;
        internal NativeMethods.NativePoint MinimumPosition;
        internal NativeMethods.NativePoint MaximumPosition;
        internal NativeMethods.NativeRect NormalPosition;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindow(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsHungAppWindow(IntPtr window);
    [DllImport("user32.dll")]
    internal static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowPlacement(IntPtr window, ref WindowPlacement placement);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetLayeredWindowAttributes(IntPtr window, out uint colorKey, out byte alpha, out uint flags);
    [DllImport("user32.dll", SetLastError = true)]
    internal static extern int GetWindowRgn(IntPtr window, IntPtr region);
    [DllImport("gdi32.dll")]
    internal static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RectInRegion(IntPtr region, ref NativeMethods.NativeRect rectangle);
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DeleteObject(IntPtr value);
    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint access, bool inherit, uint processId);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetTokenInformation(IntPtr token, int tokenClass, out int information, int length, out int returnLength);
    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);
}
