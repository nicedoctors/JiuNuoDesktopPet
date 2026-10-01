using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using SoftMochiPet.Interop;

namespace SoftMochiPet;

public partial class GhostIconWindow : Window
{
    private const int OuterPaddingPixels = 6;
    private static readonly IntPtr HwndTopmost = new(-1);
    private HwndSource? _windowSource;
    private IntPtr _handle;
    private int _iconPixelWidth = 48;
    private int _iconPixelHeight = 48;
    private int _labelPixelWidth = 90;
    private int _labelPixelHeight = 34;
    private int _labelOffsetX;
    private int _labelOffsetY = 43;
    private double _anchorPixelX = 30;
    private double _anchorPixelY = 30;
    private double _centerX;
    private double _centerY;

    public GhostIconWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            _windowSource = HwndSource.FromHwnd(_handle);
            _windowSource?.AddHook(WindowMessageHook);
            MakeMouseTransparent();
        };
        Closed += (_, _) => _windowSource?.RemoveHook(WindowMessageHook);
    }

    public bool IsGhostVisible => IsVisible;

    public void SetLabelOpacity(double opacity) =>
        GhostLabel.Opacity = Math.Clamp(opacity, 0, 1);

    public void ShowAt(
        BitmapSource? icon,
        string displayName,
        double centerX,
        double centerY,
        int? iconPixelWidth = null,
        int? iconPixelHeight = null,
        int? labelPixelWidth = null,
        int? labelPixelHeight = null,
        int? labelOffsetX = null,
        int? labelOffsetY = null)
    {
        GhostImage.Source = icon;
        GhostLabel.Text = displayName;
        GhostLabel.Opacity = 1;
        _iconPixelWidth = Math.Clamp(iconPixelWidth ?? icon?.PixelWidth ?? 48, 8, 256);
        _iconPixelHeight = Math.Clamp(iconPixelHeight ?? icon?.PixelHeight ?? 48, 8, 256);
        _labelPixelWidth = Math.Clamp(labelPixelWidth ?? 90, 24, 256);
        _labelPixelHeight = Math.Clamp(labelPixelHeight ?? 34, 14, 96);
        _labelOffsetX = Math.Clamp(labelOffsetX ?? 0, -256, 256);
        _labelOffsetY = Math.Clamp(
            labelOffsetY ?? (_iconPixelHeight / 2 + 2 + _labelPixelHeight / 2),
            -256,
            256);
        if (!IsVisible)
        {
            Show();
        }

        // Move onto the target monitor first so GetDpiForWindow reflects that
        // monitor before converting the measured physical-pixel rectangles.
        PositionPhysical(centerX, centerY);
        ConfigurePhysicalVisualSize();
        SetVisual(centerX, centerY, 1, 1, 0, 1);
    }

    public void SetVisual(
        double centerX,
        double centerY,
        double scaleX,
        double scaleY,
        double angle,
        double opacity)
    {
        _centerX = centerX;
        _centerY = centerY;
        GhostScale.ScaleX = scaleX;
        GhostScale.ScaleY = scaleY;
        GhostRotate.Angle = angle;
        Opacity = Math.Clamp(opacity, 0, 1);
        PositionPhysical(centerX, centerY);
    }

    public void RefreshDisplayGeometry()
    {
        if (!IsVisible)
        {
            return;
        }

        ConfigurePhysicalVisualSize();
        PositionPhysical(_centerX, _centerY);
    }

    private IntPtr WindowMessageHook(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmDpiChanged)
        {
            Dispatcher.BeginInvoke(
                RefreshDisplayGeometry,
                System.Windows.Threading.DispatcherPriority.ContextIdle);
        }

        return IntPtr.Zero;
    }

    public void HideGhost()
    {
        Hide();
        GhostImage.Source = null;
        GhostLabel.Text = string.Empty;
        GhostLabel.Opacity = 1;
        Opacity = 1;
        GhostScale.ScaleX = 1;
        GhostScale.ScaleY = 1;
        GhostRotate.Angle = 0;
    }

    private void MakeMouseTransparent()
    {
        var style = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle).ToInt64();
        NativeMethods.SetWindowLongPtr(
            _handle,
            NativeMethods.GwlExStyle,
            new IntPtr(style | NativeMethods.WsExToolWindow | NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate));
    }

    private void ConfigurePhysicalVisualSize()
    {
        var dpi = _handle == IntPtr.Zero ? 96u : NativeMethods.GetDpiForWindow(_handle);
        var dpiScale = (dpi == 0 ? 96d : dpi) / 96d;

        var iconLeft = -_iconPixelWidth / 2d;
        var iconTop = -_iconPixelHeight / 2d;
        var iconRight = iconLeft + _iconPixelWidth;
        var iconBottom = iconTop + _iconPixelHeight;
        var labelLeft = _labelOffsetX - _labelPixelWidth / 2d;
        var labelTop = _labelOffsetY - _labelPixelHeight / 2d;
        var labelRight = labelLeft + _labelPixelWidth;
        var labelBottom = labelTop + _labelPixelHeight;
        var contentLeft = Math.Min(iconLeft, labelLeft) - OuterPaddingPixels;
        var contentTop = Math.Min(iconTop, labelTop) - OuterPaddingPixels;
        var contentRight = Math.Max(iconRight, labelRight) + OuterPaddingPixels;
        var contentBottom = Math.Max(iconBottom, labelBottom) + OuterPaddingPixels;
        var windowPixelWidth = Math.Max(1, contentRight - contentLeft);
        var windowPixelHeight = Math.Max(1, contentBottom - contentTop);

        _anchorPixelX = -contentLeft;
        _anchorPixelY = -contentTop;
        GhostImage.Width = _iconPixelWidth / dpiScale;
        GhostImage.Height = _iconPixelHeight / dpiScale;
        Canvas.SetLeft(GhostImage, (_anchorPixelX + iconLeft) / dpiScale);
        Canvas.SetTop(GhostImage, (_anchorPixelY + iconTop) / dpiScale);

        GhostLabel.Width = _labelPixelWidth / dpiScale;
        GhostLabel.Height = _labelPixelHeight / dpiScale;
        Canvas.SetLeft(GhostLabel, (_anchorPixelX + labelLeft) / dpiScale);
        Canvas.SetTop(GhostLabel, (_anchorPixelY + labelTop) / dpiScale);

        Width = windowPixelWidth / dpiScale;
        Height = windowPixelHeight / dpiScale;
        GhostRoot.RenderTransformOrigin = new System.Windows.Point(
            _anchorPixelX / windowPixelWidth,
            _anchorPixelY / windowPixelHeight);
        UpdateLayout();
    }

    private void PositionPhysical(double centerX, double centerY)
    {
        if (_handle == IntPtr.Zero)
        {
            return;
        }

        NativeMethods.SetWindowPos(
            _handle,
            HwndTopmost,
            (int)Math.Round(centerX - _anchorPixelX),
            (int)Math.Round(centerY - _anchorPixelY),
            0,
            0,
            NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
    }
}
