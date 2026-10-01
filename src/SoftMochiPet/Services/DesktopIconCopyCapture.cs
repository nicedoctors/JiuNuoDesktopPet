using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SoftMochiPet.Services;

/// <summary>Captures only a visible desktop icon for an explicitly requested, non-destructive meal.</summary>
public static class DesktopIconCopyCapture
{
    public static bool HasSafeBounds(Rectangle bounds) => bounds.Width is >= 8 and <= 256 &&
        bounds.Height is >= 8 and <= 256 &&
        (long)bounds.X + bounds.Width <= int.MaxValue && (long)bounds.Y + bounds.Height <= int.MaxValue;

    public static BitmapSource? Capture(DesktopIconSnapshot icon)
    {
        var bounds = icon.Bounds;
        if (!HasSafeBounds(bounds)) return null;
        var samplePoints = new[] { new Point(bounds.Left + 1, bounds.Top + 1),
            new Point(bounds.Right - 2, bounds.Top + 1), new Point(bounds.Left + 1, bounds.Bottom - 2),
            new Point(bounds.Right - 2, bounds.Bottom - 2), icon.Center };
        var desktop = DesktopIconLocator.FindDesktopListView();
        if (desktop == IntPtr.Zero || samplePoints.Any(point => DesktopIconLocator.FindListViewAt(point) != desktop))
            return null;
        try
        {
            using var bitmap = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            var locked = bitmap.LockBits(new Rectangle(Point.Empty, bounds.Size), ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var stride = bounds.Width * 4;
            var pixels = new byte[stride * bounds.Height];
            try
            {
                for (var y = 0; y < bounds.Height; y++)
                    Marshal.Copy(IntPtr.Add(locked.Scan0, y * locked.Stride), pixels, y * stride, stride);
            }
            finally { bitmap.UnlockBits(locked); }
            for (var i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
            var result = BitmapSource.Create(bounds.Width, bounds.Height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
            result.Freeze();
            Array.Clear(pixels);
            return result;
        }
        catch (Exception exception) when (exception is ExternalException or ArgumentException or InvalidOperationException)
        {
            DiagnosticsLog.WriteEvent("BehaviorIconCopyUnavailable", ("Reason", exception.GetType().Name));
            return null;
        }
    }
}
