using System.Runtime.InteropServices;
using System.Text;
using SoftMochiPet.Interop;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;

namespace SoftMochiPet.Services;

public sealed record DesktopIconSnapshot(
    DrawingPoint Center,
    DrawingSize PixelSize,
    DrawingRectangle Bounds,
    DrawingRectangle? LabelBounds,
    string? DisplayText = null);

internal static class DesktopIconLocator
{
    private const int RemoteBufferSize = 1024;
    private const int RemoteTextOffset = 256;
    private const int MaximumTextCharacters = 260;

    public static IntPtr FindListViewAt(DrawingPoint screenPoint)
    {
        var window = NativeMethods.WindowFromPoint(new NativeMethods.NativePoint
        {
            X = screenPoint.X,
            Y = screenPoint.Y,
        });
        var className = new StringBuilder(128);
        var isDesktopHost = false;

        for (var depth = 0; depth < 8 && window != IntPtr.Zero; depth++)
        {
            className.Clear();
            if (NativeMethods.GetClassName(window, className, className.Capacity) > 0)
            {
                var currentClass = className.ToString();
                if (currentClass.Equals("SysListView32", StringComparison.Ordinal))
                {
                    return window;
                }

                isDesktopHost |= currentClass.Equals("WorkerW", StringComparison.Ordinal) ||
                    currentClass.Equals("Progman", StringComparison.Ordinal) ||
                    currentClass.Equals("SHELLDLL_DefView", StringComparison.Ordinal);
            }

            window = NativeMethods.GetParent(window);
        }

        // On current Windows desktop implementations WindowFromPoint can return
        // WorkerW. SysListView32 is a descendant, not an ancestor, so locate the
        // desktop FolderView explicitly as a fallback. Do not use this fallback
        // for popup menus: a menu click can geometrically overlap an unrelated
        // desktop icon behind it.
        return isDesktopHost ? FindDesktopListView() : IntPtr.Zero;
    }

    internal static IntPtr FindDesktopListView()
    {
        var result = IntPtr.Zero;
        NativeMethods.EnumWindows((topLevelWindow, _) =>
        {
            var shellView = NativeMethods.FindWindowEx(
                topLevelWindow,
                IntPtr.Zero,
                "SHELLDLL_DefView",
                null);
            if (shellView == IntPtr.Zero)
            {
                return true;
            }

            result = NativeMethods.FindWindowEx(
                shellView,
                IntPtr.Zero,
                "SysListView32",
                "FolderView");
            if (result == IntPtr.Zero)
            {
                result = NativeMethods.FindWindowEx(
                    shellView,
                    IntPtr.Zero,
                    "SysListView32",
                    null);
            }

            return result == IntPtr.Zero;
        }, IntPtr.Zero);
        return result;
    }

    public static DesktopIconSnapshot? TryGetIconAt(IntPtr listView, DrawingPoint screenPoint)
    {
        try
        {
            if (listView == IntPtr.Zero)
            {
                return null;
            }

            NativeMethods.GetWindowThreadProcessId(listView, out var processId);
            if (processId == 0)
            {
                return null;
            }

            var process = NativeMethods.OpenProcess(
                NativeMethods.ProcessVmOperation |
                NativeMethods.ProcessVmRead |
                NativeMethods.ProcessVmWrite |
                NativeMethods.ProcessQueryLimitedInformation,
                inheritHandle: false,
                processId);
            if (process == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var remoteBuffer = NativeMethods.VirtualAllocEx(
                    process,
                    IntPtr.Zero,
                    new UIntPtr(RemoteBufferSize),
                    NativeMethods.MemCommit | NativeMethods.MemReserve,
                    NativeMethods.PageReadWrite);
                if (remoteBuffer == IntPtr.Zero)
                {
                    return null;
                }

                try
                {
                    var clientPoint = new NativeMethods.NativePoint
                    {
                        X = screenPoint.X,
                        Y = screenPoint.Y,
                    };
                    if (!NativeMethods.ScreenToClient(listView, ref clientPoint))
                    {
                        return null;
                    }

                    var hitTest = new NativeMethods.ListViewHitTestInfo
                    {
                        Point = clientPoint,
                        ItemIndex = -1,
                        SubItemIndex = -1,
                        GroupIndex = -1,
                    };
                    if (!WriteStructure(process, remoteBuffer, hitTest))
                    {
                        return null;
                    }

                    var itemIndex = NativeMethods.SendMessage(
                        listView,
                        NativeMethods.LvmHitTest,
                        IntPtr.Zero,
                        remoteBuffer).ToInt32();
                    if (itemIndex < 0)
                    {
                        return null;
                    }

                    var iconBounds = TryReadItemRectangle(
                        process,
                        remoteBuffer,
                        listView,
                        itemIndex,
                        NativeMethods.LvirIcon);
                    if (iconBounds is null ||
                        iconBounds.Value.Width is < 8 or > 256 ||
                        iconBounds.Value.Height is < 8 or > 256)
                    {
                        return null;
                    }

                    var bounds = iconBounds.Value;
                    var labelBounds = TryReadItemRectangle(
                        process,
                        remoteBuffer,
                        listView,
                        itemIndex,
                        NativeMethods.LvirLabel);
                    return new DesktopIconSnapshot(
                        new DrawingPoint(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2),
                        bounds.Size,
                        bounds,
                        labelBounds,
                        TryReadItemText(process, remoteBuffer, listView, itemIndex));
                }
                finally
                {
                    NativeMethods.VirtualFreeEx(
                        process,
                        remoteBuffer,
                        UIntPtr.Zero,
                        NativeMethods.MemRelease);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(process);
            }
        }
        catch
        {
            return null;
        }
    }

    public static IReadOnlyList<DesktopIconSnapshot> GetAllIcons()
    {
        return GetIcons(selectedOnly: false);
    }

    public static IReadOnlyList<DesktopIconSnapshot> GetSelectedIcons()
    {
        return GetIcons(selectedOnly: true);
    }

    private static IReadOnlyList<DesktopIconSnapshot> GetIcons(bool selectedOnly)
    {
        var listView = FindDesktopListView();
        if (listView == IntPtr.Zero)
        {
            return [];
        }

        try
        {
            NativeMethods.GetWindowThreadProcessId(listView, out var processId);
            if (processId == 0)
            {
                return [];
            }

            var process = NativeMethods.OpenProcess(
                NativeMethods.ProcessVmOperation |
                NativeMethods.ProcessVmRead |
                NativeMethods.ProcessVmWrite |
                NativeMethods.ProcessQueryLimitedInformation,
                inheritHandle: false,
                processId);
            if (process == IntPtr.Zero)
            {
                return [];
            }

            try
            {
                var remoteBuffer = NativeMethods.VirtualAllocEx(
                    process,
                    IntPtr.Zero,
                    new UIntPtr(RemoteBufferSize),
                    NativeMethods.MemCommit | NativeMethods.MemReserve,
                    NativeMethods.PageReadWrite);
                if (remoteBuffer == IntPtr.Zero)
                {
                    return [];
                }

                try
                {
                    var itemCount = Math.Clamp(
                        NativeMethods.SendMessage(
                            listView,
                            NativeMethods.LvmGetItemCount,
                            IntPtr.Zero,
                            IntPtr.Zero).ToInt32(),
                        0,
                        512);
                    var icons = new List<DesktopIconSnapshot>(selectedOnly ? Math.Min(itemCount, 32) : itemCount);
                    var itemIndices = selectedOnly
                        ? EnumerateSelectedIndices(listView, itemCount)
                        : Enumerable.Range(0, itemCount);
                    foreach (var itemIndex in itemIndices)
                    {
                        var iconBounds = TryReadItemRectangle(
                            process,
                            remoteBuffer,
                            listView,
                            itemIndex,
                            NativeMethods.LvirIcon);
                        if (iconBounds is null ||
                            iconBounds.Value.Width is < 8 or > 256 ||
                            iconBounds.Value.Height is < 8 or > 256)
                        {
                            continue;
                        }

                        var bounds = iconBounds.Value;
                        var labelBounds = TryReadItemRectangle(
                            process,
                            remoteBuffer,
                            listView,
                            itemIndex,
                            NativeMethods.LvirLabel);
                        icons.Add(new DesktopIconSnapshot(
                            new DrawingPoint(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2),
                            bounds.Size,
                            bounds,
                            labelBounds,
                            TryReadItemText(process, remoteBuffer, listView, itemIndex)));
                    }

                    return icons;
                }
                finally
                {
                    NativeMethods.VirtualFreeEx(
                        process,
                        remoteBuffer,
                        UIntPtr.Zero,
                        NativeMethods.MemRelease);
                }
            }
            finally
            {
                NativeMethods.CloseHandle(process);
            }
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<int> EnumerateSelectedIndices(IntPtr listView, int itemCount)
    {
        var current = -1;
        for (var count = 0; count < itemCount; count++)
        {
            current = NativeMethods.SendMessage(
                listView,
                NativeMethods.LvmGetNextItem,
                new IntPtr(current),
                new IntPtr(NativeMethods.LvniSelected)).ToInt32();
            if (current < 0)
            {
                yield break;
            }

            yield return current;
        }
    }

    private static DrawingRectangle? TryReadItemRectangle(
        IntPtr process,
        IntPtr remoteBuffer,
        IntPtr listView,
        int itemIndex,
        int rectangleKind)
    {
        var rectangle = new NativeMethods.NativeRect
        {
            Left = rectangleKind,
        };
        if (!WriteStructure(process, remoteBuffer, rectangle) ||
            NativeMethods.SendMessage(
                listView,
                NativeMethods.LvmGetItemRect,
                new IntPtr(itemIndex),
                remoteBuffer) == IntPtr.Zero ||
            !ReadStructure(process, remoteBuffer, out rectangle))
        {
            return null;
        }

        var topLeft = new NativeMethods.NativePoint
        {
            X = rectangle.Left,
            Y = rectangle.Top,
        };
        var bottomRight = new NativeMethods.NativePoint
        {
            X = rectangle.Right,
            Y = rectangle.Bottom,
        };
        if (!NativeMethods.ClientToScreen(listView, ref topLeft) ||
            !NativeMethods.ClientToScreen(listView, ref bottomRight))
        {
            return null;
        }

        var width = bottomRight.X - topLeft.X;
        var height = bottomRight.Y - topLeft.Y;
        return width is < 1 or > 512 || height is < 1 or > 256
            ? null
            : new DrawingRectangle(topLeft.X, topLeft.Y, width, height);
    }

    private static string? TryReadItemText(
        IntPtr process,
        IntPtr remoteBuffer,
        IntPtr listView,
        int itemIndex)
    {
        var remoteText = IntPtr.Add(remoteBuffer, RemoteTextOffset);
        var item = new NativeMethods.ListViewItem
        {
            Mask = NativeMethods.LvifText,
            ItemIndex = itemIndex,
            SubItemIndex = 0,
            Text = remoteText,
            TextMax = MaximumTextCharacters,
        };
        if (!WriteStructure(process, remoteBuffer, item))
        {
            return null;
        }

        _ = NativeMethods.SendMessage(
            listView,
            NativeMethods.LvmGetItemTextW,
            new IntPtr(itemIndex),
            remoteBuffer);
        var byteCount = MaximumTextCharacters * sizeof(char);
        var localBuffer = Marshal.AllocHGlobal(byteCount);
        try
        {
            if (!NativeMethods.ReadProcessMemory(
                    process,
                    remoteText,
                    localBuffer,
                    new UIntPtr((uint)byteCount),
                    out var read) || read.ToUInt64() == 0)
            {
                return null;
            }

            var text = Marshal.PtrToStringUni(localBuffer, MaximumTextCharacters)?.TrimEnd('\0');
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        finally
        {
            Marshal.FreeHGlobal(localBuffer);
        }
    }

    private static bool WriteStructure<T>(IntPtr process, IntPtr remoteBuffer, T value)
        where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var localBuffer = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(value, localBuffer, fDeleteOld: false);
            return NativeMethods.WriteProcessMemory(
                process,
                remoteBuffer,
                localBuffer,
                new UIntPtr((uint)size),
                out var written) && written.ToUInt64() == (ulong)size;
        }
        finally
        {
            Marshal.FreeHGlobal(localBuffer);
        }
    }

    private static bool ReadStructure<T>(IntPtr process, IntPtr remoteBuffer, out T value)
        where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var localBuffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!NativeMethods.ReadProcessMemory(
                    process,
                    remoteBuffer,
                    localBuffer,
                    new UIntPtr((uint)size),
                    out var read) || read.ToUInt64() != (ulong)size)
            {
                value = default;
                return false;
            }

            value = Marshal.PtrToStructure<T>(localBuffer);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(localBuffer);
        }
    }
}
