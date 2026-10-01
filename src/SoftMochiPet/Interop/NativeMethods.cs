using System.Runtime.InteropServices;
using System.Text;

namespace SoftMochiPet.Interop;

internal static class NativeMethods
{
    internal const int WhMouseLl = 14;
    internal const int WmLeftButtonDown = 0x0201;
    internal const int WmRightButtonDown = 0x0204;
    internal const int WmNcHitTest = 0x0084;
    internal const int WmSettingChange = 0x001A;
    internal const int WmWindowPosChanged = 0x0047;
    internal const int WmDisplayChange = 0x007E;
    internal const int WmDpiChanged = 0x02E0;
    internal const int HtTransparent = -1;
    internal const int GwlExStyle = -20;
    internal const int GwlStyle = -16;
    internal const long WsExToolWindow = 0x00000080L;
    internal const long WsExTransparent = 0x00000020L;
    internal const long WsExNoActivate = 0x08000000L;
    internal const uint ShcneAssocChanged = 0x08000000;
    internal const uint ShcnfIdList = 0x0000;
    internal const int LvmFirst = 0x1000;
    internal const int LvmGetItemCount = LvmFirst + 4;
    internal const int LvmGetNextItem = LvmFirst + 12;
    internal const int LvmGetItemRect = LvmFirst + 14;
    internal const int LvmHitTest = LvmFirst + 18;
    internal const int LvmGetItemTextW = LvmFirst + 115;
    internal const int LvniSelected = 0x0002;
    internal const uint LvifText = 0x0001;
    internal const int WhKeyboardLl = 13;
    internal const int WmKeyDown = 0x0100;
    internal const int WmSysKeyDown = 0x0104;
    internal const int VkDelete = 0x2E;
    internal const int LvirIcon = 1;
    internal const int LvirLabel = 2;
    internal const uint ProcessVmOperation = 0x0008;
    internal const uint ProcessVmRead = 0x0010;
    internal const uint ProcessVmWrite = 0x0020;
    internal const uint ProcessQueryLimitedInformation = 0x1000;
    internal const uint MemCommit = 0x1000;
    internal const uint MemReserve = 0x2000;
    internal const uint MemRelease = 0x8000;
    internal const uint PageReadWrite = 0x04;
    internal const uint SwpNoSize = 0x0001;
    internal const uint SwpNoZOrder = 0x0004;
    internal const uint SwpNoActivate = 0x0010;
    internal const uint SwpNoOwnerZOrder = 0x0200;
    internal const uint MonitorDefaultToNull = 0x00000000;
    internal const uint MonitorDefaultToNearest = 0x00000002;
    internal const int SpiSetWorkArea = 0x002F;
    internal const int DwmwaExtendedFrameBounds = 9;
    internal const int DwmwaCloaked = 14;

    internal delegate IntPtr LowLevelMouseProc(int code, IntPtr message, IntPtr data);
    internal delegate IntPtr LowLevelKeyboardProc(int code, IntPtr message, IntPtr data);
    internal delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);
    internal delegate bool EnumDisplayMonitorsProc(
        IntPtr monitor,
        IntPtr deviceContext,
        ref NativeRect monitorRectangle,
        IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativePoint
    {
        internal int X;
        internal int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MouseHookData
    {
        internal NativePoint Point;
        internal uint MouseData;
        internal uint Flags;
        internal uint Time;
        internal UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KeyboardHookData
    {
        internal uint VirtualKeyCode;
        internal uint ScanCode;
        internal uint Flags;
        internal uint Time;
        internal UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeRect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        internal int Size;
        internal NativeRect MonitorArea;
        internal NativeRect WorkArea;
        internal uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ListViewHitTestInfo
    {
        internal NativePoint Point;
        internal uint Flags;
        internal int ItemIndex;
        internal int SubItemIndex;
        internal int GroupIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct ListViewItem
    {
        internal uint Mask;
        internal int ItemIndex;
        internal int SubItemIndex;
        internal uint State;
        internal uint StateMask;
        internal IntPtr Text;
        internal int TextMax;
        internal int Image;
        internal IntPtr Param;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct ShellFileInfo
    {
        internal IntPtr IconHandle;
        internal int IconIndex;
        internal uint Attributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        internal string DisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        internal string TypeName;
    }

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(
        int hookType,
        LowLevelMouseProc callback,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr SetWindowsHookEx(
        int hookType,
        LowLevelKeyboardProc callback,
        IntPtr module,
        uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr GetModuleHandle(string? moduleName);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    internal static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(NativePoint point);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetParent(IntPtr window);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);

    [DllImport("user32.dll")]
    internal static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(
        IntPtr deviceContext,
        IntPtr clipRectangle,
        EnumDisplayMonitorsProc callback,
        IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsZoomed(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr FindWindowEx(
        IntPtr parent,
        IntPtr childAfter,
        string? className,
        string? windowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetClassName(IntPtr window, StringBuilder className, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr window, StringBuilder windowText, int maximumCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ScreenToClient(IntPtr window, ref NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr window, out NativeRect rectangle);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(
        IntPtr window,
        int attribute,
        out NativeRect value,
        int valueSize);

    [DllImport("dwmapi.dll")]
    internal static extern int DwmGetWindowAttribute(
        IntPtr window,
        int attribute,
        out int value,
        int valueSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    internal static extern uint GetDpiForWindow(IntPtr window);

    [DllImport("shcore.dll")]
    internal static extern int GetDpiForMonitor(
        IntPtr monitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);

    [DllImport("shcore.dll")]
    internal static extern int GetScaleFactorForMonitor(IntPtr monitor, out int scalePercent);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    internal static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    internal static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr VirtualAllocEx(
        IntPtr process,
        IntPtr address,
        UIntPtr size,
        uint allocationType,
        uint protection);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool VirtualFreeEx(
        IntPtr process,
        IntPtr address,
        UIntPtr size,
        uint freeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool WriteProcessMemory(
        IntPtr process,
        IntPtr baseAddress,
        IntPtr buffer,
        UIntPtr size,
        out UIntPtr bytesWritten);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReadProcessMemory(
        IntPtr process,
        IntPtr baseAddress,
        IntPtr buffer,
        UIntPtr size,
        out UIntPtr bytesRead);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    internal static extern IntPtr SHGetFileInfo(
        string path,
        uint fileAttributes,
        ref ShellFileInfo fileInfo,
        uint fileInfoSize,
        uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyIcon(IntPtr icon);

    [DllImport("shell32.dll")]
    internal static extern void SHChangeNotify(
        uint eventId,
        uint flags,
        IntPtr item1,
        IntPtr item2);
}
