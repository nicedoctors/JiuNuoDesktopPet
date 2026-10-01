using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using SoftMochiPet.Interop;
using DrawingRectangle = System.Drawing.Rectangle;

namespace SoftMochiPet;

public partial class SpeechBubbleWindow : Window
{
    private static readonly IntPtr HwndTopmost = new(-1);
    private IntPtr _handle;
    private int _lastLeft = int.MinValue;
    private int _lastTop = int.MinValue;
    private int _lastWidth;
    private int _lastHeight;

    public SpeechBubbleWindow(bool feibi)
    {
        InitializeComponent();
        if (feibi)
        {
            var accent = new SolidColorBrush(System.Windows.Media.Color.FromRgb(143, 177, 225));
            BubblePanel.BorderBrush = accent;
            BubbleTail.Stroke = accent;
        }
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            var style = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle).ToInt64();
            NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GwlExStyle,
                new IntPtr(style | NativeMethods.WsExToolWindow |
                    NativeMethods.WsExTransparent | NativeMethods.WsExNoActivate));
        };
    }

    internal void ShowLine(string line, NativeMethods.NativeRect petBounds, DrawingRectangle workArea)
    {
        LineText.Text = line;
        if (!IsVisible)
        {
            Opacity = 0;
            Show();
        }
        Position(petBounds, workArea);
        Opacity = 1;
    }

    internal void Position(NativeMethods.NativeRect petBounds, DrawingRectangle workArea)
    {
        if (!IsVisible || _handle == IntPtr.Zero) return;
        var dpi = NativeMethods.GetDpiForWindow(_handle);
        var scale = (dpi == 0 ? 96d : dpi) / 96d;
        var width = (int)Math.Round(Width * scale);
        var height = (int)Math.Round(Height * scale);
        var petWidth = petBounds.Right - petBounds.Left;
        var petHeight = petBounds.Bottom - petBounds.Top;
        var left = Math.Clamp(petBounds.Left + petWidth / 2 - width / 2,
            workArea.Left, Math.Max(workArea.Left, workArea.Right - width));
        var top = petBounds.Top + (int)Math.Round(petHeight * .16) - height;
        if (top < workArea.Top)
        {
            top = Math.Clamp(petBounds.Top + petHeight / 5,
                workArea.Top, Math.Max(workArea.Top, workArea.Bottom - height));
            if (petBounds.Right + width + 6 <= workArea.Right) left = petBounds.Right + 6;
            else if (petBounds.Left - width - 6 >= workArea.Left) left = petBounds.Left - width - 6;
        }
        if (left == _lastLeft && top == _lastTop && width == _lastWidth && height == _lastHeight) return;
        NativeMethods.SetWindowPos(_handle, HwndTopmost, left, top, width, height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
        _lastLeft = left;
        _lastTop = top;
        _lastWidth = width;
        _lastHeight = height;
    }

    public void HideLine()
    {
        if (IsVisible) Hide();
        LineText.Text = string.Empty;
        _lastLeft = int.MinValue;
    }
}
