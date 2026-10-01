using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SoftMochiPet.Core;
using SoftMochiPet.Interop;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;

namespace SoftMochiPet;

/// <summary>A memory-only, non-activating overlay; held results pass input through to the desktop.</summary>
public sealed class PrankEffectWindow : Window
{
    private readonly PrankCanvas _canvas;
    private readonly DrawingRectangle _monitor;
    private HwndSource? _source;
    private IntPtr _handle;
    private bool _clickThrough;
    private bool _closeRequested;
    public bool IsCollected => _canvas.IsCollected;

    public PrankEffectWindow(BitmapSource image, DrawingRectangle window, DrawingRectangle monitor,
        WindowPrankVisualKind kind, IntPtr petHandle)
    {
        _monitor = monitor;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        _canvas = new PrankCanvas(image, window, monitor, kind);
        Content = _canvas;
        Closing += (_, _) => _closeRequested = true;
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => { _closeRequested = true; _source?.RemoveHook(Hook); _canvas.Release(); };
    }

    public void CloseSafely()
    {
        if (_closeRequested) return;
        _closeRequested = true;
        Close();
    }

    public void SetProgress(double progress, DrawingPoint hatMouth, double hatWidth = 60)
    {
        _canvas.Progress = Math.Clamp(progress, 0, 1);
        _canvas.HatMouth = new Point(hatMouth.X - _monitor.X, hatMouth.Y - _monitor.Y);
        _canvas.ImpactPoint = _canvas.HatMouth;
        _canvas.HatWidth = Math.Clamp(hatWidth, 20, 240);
        _canvas.InvalidateVisual();
    }

    public void SetCollectionProgress(double progress, DrawingPoint hatMouth, double hatWidth = 60)
    {
        _canvas.SetCollectionProgress(progress,
            new Point(hatMouth.X - _monitor.X, hatMouth.Y - _monitor.Y), hatWidth);
        _canvas.InvalidateVisual();
    }

    public void ResetCollection()
    {
        _canvas.ResetCollection();
        _canvas.InvalidateVisual();
    }

    public void SetClickThrough(bool enabled)
    {
        _clickThrough = enabled;
        if (_handle == IntPtr.Zero) return;
        var style = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle).ToInt64();
        NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GwlExStyle, new IntPtr(enabled
            ? style | NativeMethods.WsExTransparent : style & ~NativeMethods.WsExTransparent));
    }

    public void SetTearHands(DrawingPoint? upperHand, DrawingPoint? lowerHand)
    {
        _canvas.SetTearHands(
            upperHand is { } upper ? new Point(upper.X - _monitor.X, upper.Y - _monitor.Y) : null,
            lowerHand is { } lower ? new Point(lower.X - _monitor.X, lower.Y - _monitor.Y) : null);
        _canvas.InvalidateVisual();
    }

    public void SetTearTension(double tension)
    {
        _canvas.SetTearTension(tension);
        _canvas.InvalidateVisual();
    }

    public void SetForeground(BitmapSource? foreground, DrawingRectangle spriteBounds, bool mirror = false)
    {
        _canvas.ForegroundImage = foreground;
        _canvas.ForegroundBounds = new Rect(spriteBounds.X - _monitor.X, spriteBounds.Y - _monitor.Y,
            Math.Max(0, spriteBounds.Width), Math.Max(0, spriteBounds.Height));
        _canvas.ForegroundMirror = mirror;
        _canvas.InvalidateVisual();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _handle = new WindowInteropHelper(this).Handle;
        _source = HwndSource.FromHwnd(_handle);
        _source?.AddHook(Hook);
        var style = NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle).ToInt64();
        NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GwlExStyle,
            new IntPtr((style | NativeMethods.WsExNoActivate | NativeMethods.WsExToolWindow) & ~NativeMethods.WsExTransparent));
        PositionPhysical();
    }

    private void PositionPhysical()
    {
        if (_handle == IntPtr.Zero) return;
        NativeMethods.SetWindowPos(_handle, new IntPtr(-1),
            _monitor.Left, _monitor.Top, _monitor.Width, _monitor.Height, NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
        var scale = Math.Max(96u, NativeMethods.GetDpiForWindow(_handle)) / 96d;
        Width = _monitor.Width / scale;
        Height = _monitor.Height / scale;
        _canvas.PhysicalScale = scale;
        NativeMethods.SetWindowPos(_handle, IntPtr.Zero, _monitor.Left, _monitor.Top, _monitor.Width, _monitor.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder | NativeMethods.SwpNoZOrder);
        _canvas.InvalidateVisual();
    }

    private IntPtr Hook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmDpiChanged)
            Dispatcher.BeginInvoke(PositionPhysical);
        if (message == NativeMethods.WmNcHitTest)
        {
            var packed = lParam.ToInt64();
            var point = new Point((short)(packed & 0xffff) - _monitor.Left, (short)((packed >> 16) & 0xffff) - _monitor.Top);
            handled = true;
            return new IntPtr(!_canvas.PassesPointerThrough(_clickThrough, point) && _canvas.ContainsFakeContent(point)
                ? 1 : NativeMethods.HtTransparent);
        }
        if (message == 0x0021)
        {
            handled = true;
            return new IntPtr(_clickThrough ? 3 : 4);
        }
        if (message is 0x0201 or 0x0204 or 0x0207)
        {
            handled = true;
        }
        return IntPtr.Zero;
    }

    private sealed class PrankCanvas : FrameworkElement
    {
        private BitmapSource? _image;
        private readonly Rect _original;
        private readonly WindowPrankVisualKind _kind;
        private readonly System.Drawing.PointF[][] _shards;
        private readonly List<Rect> _hitBounds = [];
        private double _progress;
        private bool _reassembling;
        private Point? _upperGripReference;
        private Point? _lowerGripReference;
        private Point? _upperHand;
        private Point? _lowerHand;
        private double _tearTension;
        private WindowPrankTearStretch? _breakStretch;
        private Drawing[]? _collectionParts;
        private double _collectionProgress;
        private Point _collectionMouth;
        private double _collectionMouthWidth;
        public bool IsCollected => _collectionParts is not null && _collectionProgress >= 1;
        public double PhysicalScale { get; set; } = 1;
        public double Progress
        {
            get => _progress;
            set
            {
                if (_kind == WindowPrankVisualKind.Tear && value < _progress - 0.001) _reassembling = true;
                _progress = Math.Clamp(value, 0, 1);
            }
        }
        public Point HatMouth { get; set; }
        public Point? ImpactPoint { get; set; }
        public double HatWidth { get; set; } = 60;
        public BitmapSource? ForegroundImage { get; set; }
        public Rect ForegroundBounds { get; set; }
        public bool ForegroundMirror { get; set; }

        public PrankCanvas(BitmapSource image, DrawingRectangle window, DrawingRectangle monitor, WindowPrankVisualKind kind)
        {
            _image = image;
            _original = new Rect(window.X - monitor.X, window.Y - monitor.Y, window.Width, window.Height);
            _kind = kind;
            _shards = WindowPrankGeometry.CreateShards(window.Width, window.Height);
            RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
        }

        public void Release() { _image = null; ForegroundImage = null; ResetCollection(); }
        public bool ContainsFakeContent(Point point) => _hitBounds.Any(rect => rect.Contains(point));
        public bool PassesPointerThrough(bool held, Point point) => WindowPrankRenderGeometry.PassesPointerThrough(
            held, ForegroundImage is not null, ForegroundBounds.IsEmpty ? System.Drawing.RectangleF.Empty : ToRectangleF(ForegroundBounds), ToPointF(point));

        public void ResetCollection()
        {
            _collectionParts = null;
            _collectionProgress = 0;
        }

        public void SetCollectionProgress(double progress, Point mouth, double mouthWidth)
        {
            progress = WindowPrankRenderGeometry.NormalizeProgress(progress);
            if (progress == 0) { ResetCollection(); return; }
            if (_image is null) return;
            if (_collectionParts is null)
            {
                var drawing = new DrawingGroup();
                using (var dc = drawing.Open()) DrawWindowContent(dc);
                drawing.Freeze();
                _collectionParts = drawing.Children.Where(part => !part.Bounds.IsEmpty).ToArray();
            }
            _collectionProgress = progress;
            _collectionMouth = mouth;
            _collectionMouthWidth = mouthWidth;
        }

        public void SetTearHands(Point? upperHand, Point? lowerHand)
        {
            if (_reassembling || upperHand is not { } upper || lowerHand is not { } lower ||
                !double.IsFinite(upper.X) || !double.IsFinite(upper.Y) || !double.IsFinite(lower.X) || !double.IsFinite(lower.Y)) return;
            _upperGripReference ??= upper;
            _lowerGripReference ??= lower;
            _upperHand = upper;
            _lowerHand = lower;
        }

        public void SetTearTension(double tension)
        {
            if (_kind == WindowPrankVisualKind.Tear && !_reassembling && _progress <= 0)
                _tearTension = WindowPrankRenderGeometry.NormalizeProgress(tension);
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            _hitBounds.Clear();
            if (_image is null || IsCollected) return;
            dc.PushTransform(new ScaleTransform(1 / PhysicalScale, 1 / PhysicalScale));
            if (_collectionParts is not null) DrawCollection(dc);
            else DrawWindowContent(dc);
            if (ForegroundImage is not null && !ForegroundBounds.IsEmpty)
            {
                if (ForegroundMirror)
                    dc.PushTransform(new ScaleTransform(-1, 1, ForegroundBounds.X + ForegroundBounds.Width / 2, 0));
                dc.DrawImage(ForegroundImage, ForegroundBounds);
                if (ForegroundMirror) dc.Pop();
            }
            dc.Pop();
        }

        private void DrawWindowContent(DrawingContext dc)
        {
            if (_kind == WindowPrankVisualKind.Hat) DrawHat(dc);
            else if (_kind == WindowPrankVisualKind.Tear) DrawTear(dc);
            else if (Progress < 0.001) DrawWhole(dc);
            else if (_kind == WindowPrankVisualKind.Shatter) DrawShards(dc);
        }

        private void DrawCollection(DrawingContext dc)
        {
            for (var index = 0; index < _collectionParts!.Length; index++)
            {
                var part = _collectionParts[index];
                var bounds = part.Bounds;
                var pose = WindowPrankRenderGeometry.CollectionPose(ToRectangleF(bounds), ToPointF(_collectionMouth),
                    _collectionMouthWidth, _collectionProgress, index, _collectionParts.Length);
                if (!pose.Visible) continue;
                var center = new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
                if (pose.ClipAtMouth)
                    dc.PushClip(new RectangleGeometry(new Rect(0, 0, Math.Max(1, ActualWidth * PhysicalScale),
                        Math.Max(0, _collectionMouth.Y))));
                dc.PushTransform(new TranslateTransform(pose.Center.X - center.X, pose.Center.Y - center.Y));
                dc.PushTransform(new RotateTransform(pose.Rotation, center.X, center.Y));
                dc.PushTransform(new ScaleTransform(pose.Scale, pose.Scale, center.X, center.Y));
                dc.DrawDrawing(part);
                dc.Pop(); dc.Pop(); dc.Pop();
                if (pose.ClipAtMouth) dc.Pop();
                var hit = new Rect(pose.Center.X - bounds.Width * pose.Scale / 2,
                    pose.Center.Y - bounds.Height * pose.Scale / 2, bounds.Width * pose.Scale, bounds.Height * pose.Scale);
                if (pose.ClipAtMouth)
                    hit.Intersect(new Rect(0, 0, Math.Max(1, ActualWidth * PhysicalScale), Math.Max(0, _collectionMouth.Y)));
                if (!hit.IsEmpty) _hitBounds.Add(hit);
            }
        }

        private void DrawWhole(DrawingContext dc)
        {
            dc.DrawImage(_image, _original);
            _hitBounds.Add(_original);
        }

        private void DrawHat(DrawingContext dc)
        {
            if (Progress < 0.001) { DrawWhole(dc); return; }
            var t = Progress * Progress * (3 - 2 * Progress);
            var initial = new Point(_original.X + _original.Width / 2, _original.Y + _original.Height / 2);
            var scale = Math.Exp(Math.Log(Math.Min(1, HatWidth * 0.8 / _original.Width)) * t);
            var width = _original.Width * scale;
            var height = _original.Height * scale;
            var cx = initial.X + (HatMouth.X - initial.X) * t;
            var cy = initial.Y + (HatMouth.Y + height * 0.65 - initial.Y) * t - Math.Sin(t * Math.PI) * 90;
            var rect = new Rect(cx - width / 2, cy - height / 2, width, height);
            var clipped = t > 0.68;
            if (clipped) dc.PushClip(new RectangleGeometry(new Rect(0, 0, Math.Max(1, ActualWidth * PhysicalScale), Math.Max(0, HatMouth.Y))));
            var angle = Math.Sin(t * Math.PI) * -12;
            dc.PushTransform(new RotateTransform(angle, cx, cy));
            dc.DrawImage(_image, rect);
            dc.Pop();
            if (clipped) dc.Pop();
            if (Progress < 0.98)
            {
                var hit = rect;
                if (clipped) hit.Intersect(new Rect(0, 0, Math.Max(1, ActualWidth * PhysicalScale), Math.Max(0, HatMouth.Y)));
                if (!hit.IsEmpty) _hitBounds.Add(hit);
            }
        }

        private void DrawShards(DrawingContext dc)
        {
            var t = Progress;
            var burst = Math.Clamp((t - 0.10) / 0.9, 0, 1);
            var distance = 1 - Math.Pow(1 - burst, 2);
            var contact = ImpactPoint ?? new Point(_original.X + _original.Width * 0.72, _original.Y + _original.Height * 0.48);
            var impact = WindowPrankGeometry.ClampImpact(ToPointF(contact),
                new System.Drawing.RectangleF((float)_original.X, (float)_original.Y, (float)_original.Width, (float)_original.Height));
            for (var i = 0; i < _shards.Length; i++)
            {
                var points = _shards[i].Select(p => new Point(_original.X + p.X, _original.Y + p.Y)).ToArray();
                var center = new Point(points.Average(p => p.X), points.Average(p => p.Y));
                var offset = WindowPrankGeometry.ShatterOffset(ToPointF(center), impact, t);
                var dx = offset.X;
                var dy = offset.Y;
                dc.PushTransform(new TranslateTransform(dx, dy));
                dc.PushTransform(new RotateTransform((i % 2 == 0 ? 1 : -1) * (10 + i % 4 * 6) * distance, center.X, center.Y));
                var geometry = Polygon(points);
                dc.PushClip(geometry);
                dc.DrawImage(_image, _original);
                dc.Pop();
                dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb((byte)(160 * Math.Min(1, t * 8)), 230, 224, 255)), 1.4), geometry);
                dc.Pop();
                dc.Pop();
                var hit = geometry.Bounds;
                hit.Inflate(20, 20);
                hit.Offset(dx, dy);
                _hitBounds.Add(hit);
            }
        }

        private void DrawTear(DrawingContext dc)
        {
            var contactY = ImpactPoint is { } contact ? (float)(contact.Y - _original.Y) : (float?)null;
            var seam = WindowPrankGeometry.CreateTearSeam((float)_original.Width, (float)_original.Height, contactY)
                .Select(p => new Point(_original.X + p.X, _original.Y + p.Y)).ToArray();
            if (Progress <= 0)
            {
                var stretch = _reassembling ? new WindowPrankTearStretch(1, 0) : CurrentTearStretch(seam[0].Y);
                var bounds = new Rect(_original.X, _original.Y * stretch.ScaleY + stretch.OffsetY,
                    _original.Width, _original.Height * stretch.ScaleY);
                dc.DrawImage(_image, bounds);
                _hitBounds.Add(bounds);
                return;
            }
            _breakStretch ??= CurrentTearStretch(seam[0].Y);
            for (var side = 0; side < 2; side++)
            {
                var points = side == 0
                    ? new[] { _original.TopRight, _original.TopLeft }.Concat(seam.Reverse()).ToArray()
                    : new[] { _original.BottomRight }.Concat(seam).Concat(new[] { _original.BottomLeft }).ToArray();
                var geometry = Polygon(points);
                var reference = side == 0 ? _upperGripReference : _lowerGripReference;
                var hand = side == 0 ? _upperHand : _lowerHand;
                var pivot = PointOnSeam(seam, reference?.X ?? _original.Right);
                var destination = hand ?? new Point(pivot.X, pivot.Y + (side == 0 ? -1 : 1) * Math.Min(110, _original.Height * 0.24));
                var pose = WindowPrankRenderGeometry.TearHalfPose(ToPointF(pivot), ToPointF(destination),
                    _breakStretch.Value, Progress, _reassembling, side == 0);
                var matrix = new Matrix();
                matrix.Translate(-pivot.X, -pivot.Y);
                matrix.Scale(1, pose.ScaleY);
                matrix.Rotate(pose.Rotation);
                matrix.Translate(pose.Grip.X, pose.Grip.Y);
                // Each full half is one draw group, so collection preserves exactly two pieces.
                dc.PushTransform(new MatrixTransform(matrix));
                dc.PushClip(geometry);
                dc.DrawImage(_image, _original);
                dc.Pop();
                var edge = new StreamGeometry();
                using (var context = edge.Open())
                {
                    context.BeginFigure(seam[0], false, false);
                    context.PolyLineTo(seam.Skip(1).ToArray(), true, false);
                }
                edge.Freeze();
                dc.DrawGeometry(null, new Pen(Brushes.Lavender, 1.5 * Math.Min(1, Progress * 4)), edge);
                dc.Pop();
                _hitBounds.Add(new MatrixTransform(matrix).TransformBounds(geometry.Bounds));
            }
        }

        private WindowPrankTearStretch CurrentTearStretch(double seamY) => WindowPrankRenderGeometry.TearStretch(
            ToRectangleF(_original), (float)seamY, _tearTension,
            _upperGripReference is { } firstUpper ? ToPointF(firstUpper) : null,
            _lowerGripReference is { } firstLower ? ToPointF(firstLower) : null,
            _upperHand is { } upper ? ToPointF(upper) : null,
            _lowerHand is { } lower ? ToPointF(lower) : null);

        private static Point PointOnSeam(IReadOnlyList<Point> seam, double x)
        {
            x = Math.Clamp(x, seam[^1].X, seam[0].X);
            for (var i = 1; i < seam.Count; i++)
            {
                if (x < seam[i].X) continue;
                var fraction = (seam[i - 1].X - x) / Math.Max(1, seam[i - 1].X - seam[i].X);
                return new Point(x, seam[i - 1].Y + (seam[i].Y - seam[i - 1].Y) * fraction);
            }
            return seam[^1];
        }

        private static System.Drawing.PointF ToPointF(Point point) => new((float)point.X, (float)point.Y);
        private static System.Drawing.RectangleF ToRectangleF(Rect rectangle) =>
            new((float)rectangle.X, (float)rectangle.Y, (float)rectangle.Width, (float)rectangle.Height);

        private static StreamGeometry Polygon(IReadOnlyList<Point> points)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(points[0], true, true);
                context.PolyLineTo(points.Skip(1).ToArray(), true, false);
            }
            geometry.Freeze();
            return geometry;
        }
    }
}
