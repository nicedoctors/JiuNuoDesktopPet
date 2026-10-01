using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SoftMochiPet;
using SoftMochiPet.Core;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using DrawingRectangle = System.Drawing.Rectangle;

internal static class WindowPrankRenderTests
{
    private const int Width = 1200, Height = 900;
    private static readonly DrawingRectangle SourceBounds = new(160, 160, 640, 400);
    private static readonly Type CanvasType = typeof(PrankEffectWindow).GetNestedType("PrankCanvas", BindingFlags.NonPublic)!;

    public static void CollectionGeometryPreservesEndpointsAndFitsTheHat()
    {
        var bounds = new System.Drawing.RectangleF(-560, 90, 480, 290);
        var mouth = new System.Drawing.PointF(-60, 420);
        for (var piece = 0; piece < 16; piece++)
        {
            var start = WindowPrankRenderGeometry.CollectionPose(bounds, mouth, 80, 0, piece, 16);
            Assert(start.Center == new System.Drawing.PointF(-320, 235) && start.Scale == 1 &&
                start.Rotation == 0 && start.Visible && !start.ClipAtMouth,
                "Collection begins exactly at each real fragment's transformed bounds, including negative monitor coordinates.");
            var end = WindowPrankRenderGeometry.CollectionPose(bounds, mouth, 80, 1, piece, 16);
            Assert(!end.Visible && end.Center.X == mouth.X && end.Center.Y > mouth.Y &&
                Math.Max(bounds.Width, bounds.Height) * end.Scale <= 80 * 0.64 + 0.001,
                "Every fragment fits the mouth and is fully consumed at progress one, including the last delayed piece.");
            var previousScale = 1d;
            for (var step = 0; step <= 100; step++)
            {
                var pose = WindowPrankRenderGeometry.CollectionPose(bounds, mouth, 80, step / 100d, piece, 16);
                Assert(double.IsFinite(pose.Scale) && float.IsFinite(pose.Center.X) && float.IsFinite(pose.Center.Y) &&
                    pose.Scale > 0 && pose.Scale <= previousScale + 0.000001,
                    "Collection stays finite and shrinks monotonically without a size jump or inversion.");
                previousScale = pose.Scale;
            }
        }
    }

    public static void TearPullsCompleteHalvesWithoutHorizontalWipe() => OnSta(() =>
    {
        var canvas = CreateCanvas(WindowPrankVisualKind.Tear);
        Set(canvas, "ImpactPoint", new Point(800, 360));
        Set(canvas, "Progress", 0.33d);
        Hands(canvas, new Point(800, 345), new Point(800, 375));
        Hands(canvas, new Point(800, 220), new Point(800, 460));
        var pixels = Pixels(Render(canvas));
        var pose = WindowPrankRenderGeometry.TearHalfPose(new System.Drawing.PointF(800, 360),
            new System.Drawing.PointF(800, 220), new WindowPrankTearStretch(1, 0), 0.33, false, true);
        var matrix = new Matrix();
        matrix.RotateAt(pose.Rotation, 800, 360);
        matrix.Translate(pose.Grip.X - 800, pose.Grip.Y - 360);
        var moved = matrix.Transform(new Point(270, 230));
        var offset = ((int)Math.Round(moved.Y) * Width + (int)Math.Round(moved.X)) * 4;
        var source = PatternPixel(110, 70);
        Assert(pixels[offset + 3] >= 250 && Math.Abs(pixels[offset] - source.B) <= 2 &&
            Math.Abs(pixels[offset + 1] - source.G) <= 2 && Math.Abs(pixels[offset + 2] - source.R) <= 2,
            "The far-left source marker must move with the complete upper half immediately; no horizontal wipe may leave it pinned or erase it.");
        Assert(VisiblePixels(pixels) > SourceBounds.Width * SourceBounds.Height * 0.99,
            "Breaking the window must retain all source-image area in the two complete halves, not narrow strips.");
    });

    public static void TearTensionKeepsTheWholeWindowSealedUntilRupture() => OnSta(() =>
    {
        var canvas = CreateCanvas(WindowPrankVisualKind.Tear);
        Set(canvas, "ImpactPoint", new Point(800, 360));
        Hands(canvas, new Point(800, 330), new Point(800, 390));
        var original = Pixels(Render(canvas));
        var previousVisible = VisiblePixels(original);
        foreach (var tension in new[] { 0.25, 0.6, 0.8, 1d })
        {
            Hands(canvas, new Point(800, 330 - tension * 40), new Point(800, 390 + tension * 40));
            Tension(canvas, tension);
            var stretched = Pixels(Render(canvas));
            for (var y = SourceBounds.Top + 1; y < SourceBounds.Bottom - 1; y++)
            for (var x = SourceBounds.Left + 1; x < SourceBounds.Right - 1; x++)
                Assert(stretched[(y * Width + x) * 4 + 3] == 255,
                    "An unbroken window must remain one opaque continuous image, including the future fracture line.");
            Assert(VisiblePixels(stretched) > VisiblePixels(original) && !stretched.SequenceEqual(original),
                "Anticipation must visibly stretch the whole window without introducing an early crack.");
            Assert(VisiblePixels(stretched) >= previousVisible,
                "The last anticipation frame must inherit and increase the previous stretch, never shrink back before rupture.");
            previousVisible = VisiblePixels(stretched);
        }
        var beforeBreak = Pixels(Render(canvas));
        Set(canvas, "Progress", 0.000001d);
        var afterBreak = Pixels(Render(canvas));
        Assert(Math.Abs(VisiblePixels(beforeBreak) - VisiblePixels(afterBreak)) < 1600,
            "Rupture begins from the stretched silhouette rather than snapping back to the unstretched original.");
    });

    public static void EffectPointerPassesOnlyThroughHeldOrCharacterArea()
    {
        var character = new System.Drawing.RectangleF(680, 100, 600, 600);
        var overlappingFace = new System.Drawing.PointF(790, 250);
        var otherContent = new System.Drawing.PointF(300, 250);
        Assert(WindowPrankRenderGeometry.PassesPointerThrough(false, true, character, overlappingFace),
            "The enlarged character's visible overlap must receive dragging and right-click instead of being swallowed by its effect overlay.");
        Assert(!WindowPrankRenderGeometry.PassesPointerThrough(false, true, character, otherContent) &&
            !WindowPrankRenderGeometry.PassesPointerThrough(false, false, character, overlappingFace) &&
            !WindowPrankRenderGeometry.PassesPointerThrough(false, true, System.Drawing.RectangleF.Empty, overlappingFace),
            "Other fake content remains protected, and a missing or empty foreground cannot make unrelated content click-through.");
        Assert(WindowPrankRenderGeometry.PassesPointerThrough(true, false, System.Drawing.RectangleF.Empty, otherContent),
            "A completed held result keeps its existing desktop-wide click-through behavior.");
    }

    public static void TearGripTracksBothHandsAndRestoreReturnsToTheSource()
    {
        var grip = new System.Drawing.PointF(800, 360);
        var upper = new System.Drawing.PointF(800, 210);
        var lower = new System.Drawing.PointF(804, 510);
        var stretch = new WindowPrankTearStretch(1.24, -86.4);
        foreach (var side in new[] { true, false })
        {
            var hand = side ? upper : lower;
            var full = WindowPrankRenderGeometry.TearHalfPose(grip, hand, stretch, 1, false, side);
            Assert(full.Grip == hand && full.ScaleY == 1 && Math.Abs(full.Rotation) <= 2.5,
                "The broken seam grip is exactly at its real hand, with only mild rotation and no synthetic sideways throw.");
            var restored = WindowPrankRenderGeometry.TearHalfPose(grip, hand, stretch, 0, true, side);
            Assert(restored.Grip == grip && restored.ScaleY == 1 && restored.Rotation == 0,
                "Manual reassembly ends at the original source geometry without replaying stretched anticipation.");
        }
        var early = WindowPrankRenderGeometry.TearHalfPose(grip, upper, stretch, 0.25, false, true);
        Assert(grip.Y - early.Grip.Y > (grip.Y - upper.Y) * 0.5 && early.Grip.X == grip.X,
            "The break must supply immediate vertical separation, not ease into a sideways glide.");
    }

    public static void CollectionRendersRealPiecesAndReturnsTheOriginalPixels() => OnSta(() =>
    {
        foreach (var kind in new[] { WindowPrankVisualKind.Shatter, WindowPrankVisualKind.Tear })
        {
            var canvas = CreateCanvas(kind);
            Set(canvas, "ImpactPoint", new Point(800, 360));
            Set(canvas, "Progress", 1d);
            Hands(canvas, new Point(800, 345), new Point(800, 375));
            Hands(canvas, new Point(820, 270), new Point(815, 460));
            var broken = Pixels(Render(canvas));
            Collect(canvas, 0);
            Assert(broken.SequenceEqual(Pixels(Render(canvas))), "Collection zero must not replace the final broken shape with an intact rectangle.");
            Collect(canvas, 0.4);
            var parts = (Array)CanvasType.GetField("_collectionParts", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(canvas)!;
            Assert(parts.Length == (kind == WindowPrankVisualKind.Tear ? 2 : 16),
                "Collection preserves exactly two complete torn halves or sixteen shattered fragments.");
            var intermediate = Pixels(Render(canvas));
            Assert(VisiblePixels(intermediate) > 1500 && !broken.SequenceEqual(intermediate),
                "The intermediate collection frame must visibly move and shrink the actual pieces.");
            Collect(canvas, 1);
            Assert((bool)CanvasType.GetProperty("IsCollected")!.GetValue(canvas)! &&
                VisiblePixels(Pixels(Render(canvas))) == 0,
                "A collected result draws no source content at all while its screenshot is retained in memory.");
            Collect(canvas, 0.4);
            Assert(intermediate.SequenceEqual(Pixels(Render(canvas))),
                "Returning from the hat must retrace the same frozen pieces, not regenerate or shift their starting pose.");
            Collect(canvas, 0);
            Assert(broken.SequenceEqual(Pixels(Render(canvas))), "All pieces must return continuously to the same broken pose.");
            Set(canvas, "Progress", 0d);
            var whole = Pixels(Render(canvas));
            var untouched = Pixels(Render(CreateCanvas(kind)));
            Assert(whole.SequenceEqual(untouched),
                "Reassembly after collection restores the original unmodified screenshot, not a cached broken or hidden frame.");
        }
    });

    public static void ActualTearCharacterRemainsVisibleAboveWindowPieces() => OnSta(() =>
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "assets", "characters", "feibijiubi");
        var artwork = new PrankArtwork(directory);
        var clip = artwork.Clip("bare_tear");
        var firstUpper = clip.TearUpperHandAt(3);
        var firstLower = clip.TearLowerHandAt(3);
        const double scale = 0.75;
        var characterBounds = new Rect(Math.Round(800 - (firstUpper.X + firstLower.X) * 0.5 * scale),
            Math.Round(360 - (firstUpper.Y + firstLower.Y) * 0.5 * scale), 384, 384);
        Point Screen(Point local) => new(characterBounds.Left + local.X * scale, characterBounds.Top + local.Y * scale);
        foreach (var frame in new[] { 8, 9, 10, 11 })
        {
            var canvas = CreateCanvas(WindowPrankVisualKind.Tear);
            Set(canvas, "ImpactPoint", new Point(800, 360));
            Hands(canvas, Screen(firstUpper), Screen(firstLower));
            Hands(canvas, Screen(clip.TearUpperHandAt(Math.Min(frame, 9))), Screen(clip.TearLowerHandAt(Math.Min(frame, 9))));
            Tension(canvas, 1);
            Set(canvas, "Progress", Math.Clamp(frame - 8d, 0, 1));
            var withoutCharacter = Pixels(Render(canvas));
            var image = Decode(Path.Combine(directory, "runtime", "bare_tear", $"frame_{frame:00}.png"));
            Set(canvas, "ForegroundImage", image);
            Set(canvas, "ForegroundBounds", characterBounds);
            var composed = Pixels(Render(canvas));
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen()) dc.DrawImage(image, characterBounds);
            var onlyCharacter = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
            onlyCharacter.Render(visual);
            var expected = Pixels(onlyCharacter);
            var overlapping = 0;
            var opaqueCharacter = 0;
            for (var offset = 0; offset < expected.Length; offset += 4)
            {
                if (expected[offset + 3] != 255) continue;
                opaqueCharacter++;
                if (withoutCharacter[offset + 3] >= 250) overlapping++;
                Assert(composed[offset + 3] == 255 && Math.Abs(composed[offset] - expected[offset]) <= 1 &&
                    Math.Abs(composed[offset + 1] - expected[offset + 1]) <= 1 &&
                    Math.Abs(composed[offset + 2] - expected[offset + 2]) <= 1,
                    $"The actual opaque character pixels must stay above the window in bare_tear frame {frame}.");
            }
            Assert(opaqueCharacter > 1000 && (frame >= 10 || overlapping > 100),
                $"Frame {frame} must contain the real character; engaged frames must overlap the window, while released hands may no longer touch it.");
        }
    });

    private static FrameworkElement CreateCanvas(WindowPrankVisualKind kind)
    {
        var bytes = new byte[640 * 400 * 4];
        for (var y = 0; y < 400; y++)
        for (var x = 0; x < 640; x++)
        {
            var color = PatternPixel(x, y);
            var offset = (y * 640 + x) * 4;
            bytes[offset] = color.B; bytes[offset + 1] = color.G; bytes[offset + 2] = color.R; bytes[offset + 3] = 255;
        }
        var image = BitmapSource.Create(640, 400, 96, 96, PixelFormats.Pbgra32, null, bytes, 640 * 4);
        image.Freeze();
        return (FrameworkElement)Activator.CreateInstance(CanvasType,
            [image, SourceBounds, new DrawingRectangle(0, 0, Width, Height), kind])!;
    }

    private static (byte B, byte G, byte R) PatternPixel(int x, int y) =>
        ((byte)(40 + x / 20 % 5 * 30), (byte)(60 + y / 20 % 5 * 30), (byte)(130 + (x / 20 + y / 20) % 3 * 30));

    private static void Set(FrameworkElement canvas, string property, object value)
    {
        CanvasType.GetProperty(property)!.SetValue(canvas, value);
        canvas.InvalidateVisual();
    }

    private static void Hands(FrameworkElement canvas, Point upper, Point lower) =>
        CanvasType.GetMethod("SetTearHands")!.Invoke(canvas, [upper, lower]);

    private static void Tension(FrameworkElement canvas, double tension)
    {
        CanvasType.GetMethod("SetTearTension")!.Invoke(canvas, [tension]);
        canvas.InvalidateVisual();
    }

    private static void Collect(FrameworkElement canvas, double progress)
    {
        CanvasType.GetMethod("SetCollectionProgress")!.Invoke(canvas, [progress, new Point(970, 520), 84d]);
        canvas.InvalidateVisual();
    }

    private static RenderTargetBitmap Render(FrameworkElement canvas)
    {
        canvas.Measure(new Size(Width, Height));
        canvas.Arrange(new Rect(0, 0, Width, Height));
        canvas.UpdateLayout();
        var image = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        image.Render(canvas);
        return image;
    }

    private static byte[] Pixels(BitmapSource image)
    {
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0);
        return pixels;
    }

    private static int VisiblePixels(byte[] pixels)
    {
        var count = 0;
        for (var offset = 3; offset < pixels.Length; offset += 4) if (pixels[offset] > 0) count++;
        return count;
    }

    private static BitmapSource Decode(string path)
    {
        using var stream = File.OpenRead(path);
        var image = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        image.Freeze();
        return image;
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } })
            { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
