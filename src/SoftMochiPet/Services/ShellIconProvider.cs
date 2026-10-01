using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using SystemIcons = System.Drawing.SystemIcons;
using SoftMochiPet.Interop;

namespace SoftMochiPet.Services;

public static class ShellIconProvider
{
    private const uint FileAttributeNormal = 0x00000080;
    private const uint FileAttributeDirectory = 0x00000010;
    private const uint ShgfiIcon = 0x000000100;
    private const uint ShgfiDisplayName = 0x000000200;
    private const uint ShgfiUseFileAttributes = 0x000000010;
    private const uint ShgfiLargeIcon = 0x000000000;
    private const uint ShgfiShellIconSize = 0x000000004;
    private const uint ShgfiAddOverlays = 0x000000020;
    private const uint ShgfiLinkOverlay = 0x000008000;

    public static BitmapSource? GetIcon(string originalPath)
    {
        var info = new NativeMethods.ShellFileInfo();
        var exists = File.Exists(originalPath) || Directory.Exists(originalPath);
        var attributes = Directory.Exists(originalPath) || string.IsNullOrWhiteSpace(Path.GetExtension(originalPath))
            ? FileAttributeDirectory
            : FileAttributeNormal;
        var flags = ShgfiIcon | ShgfiLargeIcon | ShgfiShellIconSize | ShgfiAddOverlays;
        if (Path.GetExtension(originalPath).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            flags |= ShgfiLinkOverlay;
        }
        if (!exists)
        {
            flags |= ShgfiUseFileAttributes;
        }
        var result = NativeMethods.SHGetFileInfo(
            originalPath,
            attributes,
            ref info,
            (uint)Marshal.SizeOf<NativeMethods.ShellFileInfo>(),
            flags);

        if (result == IntPtr.Zero || info.IconHandle == IntPtr.Zero)
        {
            var fallback = Imaging.CreateBitmapSourceFromHIcon(
                SystemIcons.Application.Handle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(64, 64));
            fallback.Freeze();
            return fallback;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                info.IconHandle,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.DestroyIcon(info.IconHandle);
        }
    }

    public static string? GetDisplayName(string originalPath)
    {
        if (!File.Exists(originalPath) && !Directory.Exists(originalPath))
        {
            return null;
        }

        var info = new NativeMethods.ShellFileInfo();
        var result = NativeMethods.SHGetFileInfo(
            originalPath,
            0,
            ref info,
            (uint)Marshal.SizeOf<NativeMethods.ShellFileInfo>(),
            ShgfiDisplayName);
        return result != IntPtr.Zero && !string.IsNullOrWhiteSpace(info.DisplayName)
            ? info.DisplayName
            : null;
    }
}
