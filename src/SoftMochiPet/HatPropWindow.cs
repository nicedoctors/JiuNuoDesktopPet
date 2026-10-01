using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SoftMochiPet.Interop;
using SoftMochiPet.Services;
using Point = System.Windows.Point;
using Image = System.Windows.Controls.Image;
using Brushes = System.Windows.Media.Brushes;

namespace SoftMochiPet;

internal sealed class HatPropWindow : Window
{
    private readonly Image _image;
    private readonly RotateTransform _rotation = new();
    private readonly double _aspectRatio;
    private IntPtr _handle;
    private bool _closeRequested;

    public HatPropWindow(BitmapSource image)
    {
        var visible = CropTransparentMargin(image);
        _aspectRatio = (double)visible.PixelHeight / visible.PixelWidth;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowActivated = false;
        ShowInTaskbar = false;
        Focusable = false;
        Topmost = true;
        _image = new Image
        {
            Source = visible, Stretch = Stretch.Uniform, RenderTransform = _rotation,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5), IsHitTestVisible = false,
        };
        RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
        Content = _image;
        Closing += (_, _) => _closeRequested = true;
        Closed += (_, _) => _closeRequested = true;
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle).ToInt64();
            NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GwlExStyle,
                new IntPtr(style | 0x00000020L | 0x08000000L | NativeMethods.WsExToolWindow));
        };
    }

    public void Place(Point center, double physicalWidth, double angle, double opacity = 1)
    {
        if (_closeRequested) return;
        var monitor = DisplayGeometry.FromPoint(new System.Drawing.Point((int)center.X, (int)center.Y));
        var scale = monitor?.Scale ?? 1;
        var canvasSize = Math.Max(16, physicalWidth * 1.5);
        Width = Height = canvasSize / scale;
        _image.Width = physicalWidth / scale;
        _image.Height = physicalWidth * _aspectRatio / scale;
        _rotation.Angle = angle;
        Opacity = Math.Clamp(opacity, 0, 1);
        if (!IsVisible) Show();
        if (_closeRequested || _handle == IntPtr.Zero) return;
        DisplayGeometry.MoveWindowPhysical(_handle,
            (int)Math.Round(center.X - canvasSize / 2), (int)Math.Round(center.Y - canvasSize / 2));
    }

    public void CloseSafely()
    {
        if (_closeRequested) return;
        _closeRequested = true;
        Close();
    }

    private static BitmapSource CropTransparentMargin(BitmapSource source)
    {
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var left = bitmap.PixelWidth;
        var top = bitmap.PixelHeight;
        var right = 0;
        var bottom = 0;
        for (var y = 0; y < bitmap.PixelHeight; y++)
            for (var x = 0; x < bitmap.PixelWidth; x++)
                if (pixels[y * stride + x * 4 + 3] > 0)
                {
                    left = Math.Min(left, x); top = Math.Min(top, y);
                    right = Math.Max(right, x + 1); bottom = Math.Max(bottom, y + 1);
                }
        if (left >= right || top >= bottom) throw new System.IO.InvalidDataException("The hat prop is empty.");
        var cropped = new CroppedBitmap(bitmap, new Int32Rect(left, top, right - left, bottom - top));
        cropped.Freeze();
        return cropped;
    }
}
