using System.Diagnostics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SoftMochiPet.Core;

internal static class CheekPinchTests
{
    public static void CheeksAreTargetedWithoutStealingBodyDrag()
    {
        foreach (var profile in PetCharacterProfile.All)
        {
            var g = CheekPinchGeometry.For(profile);
            Assert(g.HitTest(g.LeftX, g.CheekY) == CheekSide.Left, "Left cheek must be pinchable.");
            Assert(g.HitTest(g.RightX, g.CheekY) == CheekSide.Right, "Right cheek must be pinchable.");
            Assert(g.HitTest(192, 320) is null && g.HitTest(192, 100) is null,
                "Body and hat must remain ordinary draggable areas.");
        }
    }

    public static void LongPullNibbleAndReleaseSettleAtAnyFrameRate()
    {
        foreach (var side in Enum.GetValues<CheekSide>())
        foreach (var delta in new[] { 1d/30, 1d/60, 1d/120 })
        {
            var pinch = new CheekPinchDynamics();
            pinch.Begin(side);
            pinch.Drag(side == CheekSide.Left ? -105 : 105, 12);
            for (var time = 0d; time < 1; time += delta) pinch.Tick(delta);
            Assert(pinch.Held && pinch.OutwardTension > .9 && pinch.Nibble > .1,
                "A sustained pull must animate the nibbling mouth.");
            var before = (pinch.X, pinch.Y, pinch.Nibble);
            pinch.Release();
            Assert(before == (pinch.X, pinch.Y, pinch.Nibble), "Release must not change pose immediately.");
            for (var time = 0d; time < 2 && pinch.Active; time += delta) pinch.Tick(delta);
            Assert(!pinch.Active && pinch.X == 0 && pinch.Y == 0 && pinch.Nibble == 0, "Release must settle.");
        }
    }

    public static void SmallPullPreservesPoseAndTracksBothAxes()
    {
        var pinch = new CheekPinchDynamics();
        pinch.Begin(CheekSide.Left);
        pinch.Drag(-50, 15);
        for (var i = 0; i < 45; i++) pinch.Tick(1d/60);
        Assert(pinch.X is < -49 and > -51 && pinch.Y is > 14 and < 16 && pinch.Nibble == 0,
            "Small pulls must follow both axes continuously without triggering nibbling.");
        var before = (pinch.X, pinch.Y);
        pinch.Release();
        Assert(before == (pinch.X, pinch.Y), "Release must keep the current geometry.");
        pinch.Tick(double.NaN);
        Assert(double.IsFinite(pinch.X), "Invalid elapsed time must not poison the spring.");
    }

    private static BitmapSource Load(PetCharacterProfile profile)
    {
        var frame = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory,
            profile.RuntimeRelativeDirectory, "idle", "frame_00.png"), UriKind.Absolute));
        frame.Freeze();
        return frame;
    }

    private static byte[] Pixels(BitmapSource source)
    {
        var bitmap = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        var bytes = new byte[bitmap.PixelWidth * bitmap.PixelHeight * 4];
        bitmap.CopyPixels(bytes, bitmap.PixelWidth * 4, 0);
        return bytes;
    }

    public static void ProductionCheeksPreserveBodyAndCanvas()
    {
        foreach (var profile in PetCharacterProfile.All)
        foreach (var side in Enum.GetValues<CheekSide>())
        {
            var source = Load(profile);
            var original = Pixels(source);
            var renderer = new CheekPinchFrameRenderer();
            var g = CheekPinchGeometry.For(profile);
            Assert(ReferenceEquals(source, renderer.Render(source, g, side, 0, 0, 0)),
                "A zero pull must preserve the exact displayed frame.");
            var frame = renderer.Render(source, g, side, side == CheekSide.Left ? -105 : 105, 42, .8);
            Assert(frame.PixelWidth == 384 && frame.PixelHeight == 384, "Canvas must remain fixed.");
            var pixels = Pixels(frame);
            var changed = 0;
            for (var y = 0; y < 384; y++)
            for (var x = 0; x < 384; x++)
            for (var c = 0; c < 4; c++)
            {
                var i = (y*384+x)*4+c;
                if (original[i] != pixels[i]) changed++;
                if (y < 189 || y > 283)
                    Assert(original[i] == pixels[i], "Outside the facial rig, the stable cel must remain pixel-identical.");
                if (c < 3) Assert(pixels[i] <= pixels[(y*384+x)*4+3], "Premultiplied color must remain valid.");
            }
            Assert(changed > 500, "The cheek pull must remain visible.");
            Assert(ReferenceEquals(source, renderer.Render(source, g, side, 0, 0, 0)), "Release restores the original.");
        }
    }

    public static void ContinuousPullReusesItsBitmapAndLargeBuffer()
    {
        var source = Load(PetCharacterProfile.FeibiJiubi);
        var g = CheekPinchGeometry.For(PetCharacterProfile.FeibiJiubi);
        var renderer = new CheekPinchFrameRenderer();
        var output = renderer.Render(source, g, CheekSide.Left, -20, 10, 0);
        var before = GC.GetAllocatedBytesForCurrentThread();
        var timer = Stopwatch.StartNew();
        for (var i = 0; i < 120; i++)
            Assert(ReferenceEquals(output, renderer.Render(source, g, CheekSide.Left, -20-i*.5, 10, 0)),
                "Dragging must reuse the same bitmap and buffers.");
        timer.Stop();
        Assert(GC.GetAllocatedBytesForCurrentThread() - before < 1_000_000, "Hot rendering must not allocate full images.");
        Console.WriteLine($"Cheek renderer mean: {timer.Elapsed.TotalMilliseconds/120:F2} ms/frame (offline).");
    }

    public static void WriteOfflinePreview(string directory)
    {
        Directory.CreateDirectory(directory);
        foreach (var profile in PetCharacterProfile.All)
        {
            var source = Load(profile);
            var renderer = new CheekPinchFrameRenderer();
            foreach (var (label, x, y, nibble) in new[] {
                ("start",0d,0d,0d), ("gentle",-35d,0d,0d), ("full",-105d,0d,0d),
                ("up",-80d,-42d,0d), ("down",-80d,42d,0d), ("nibble",-100d,0d,1d),
                ("right",105d,0d,0d), ("settle",0d,0d,0d) })
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderer.Render(source, CheekPinchGeometry.For(profile),
                    x > 0 ? CheekSide.Right : CheekSide.Left, x, y, nibble)));
                using var stream = File.Create(Path.Combine(directory, $"{profile.Id}-{label}.png"));
                encoder.Save(stream);
            }
            var sequence = Path.Combine(directory, profile.Id);
            Directory.CreateDirectory(sequence);
            var dynamics = new CheekPinchDynamics();
            dynamics.Begin(CheekSide.Left);
            for (var i=0; i<105; i++)
            {
                if (i < 50) dynamics.Drag(-105*Math.Min(1,i/25d), 12*Math.Sin(i*.08));
                if (i == 65) dynamics.Release();
                dynamics.Tick(1d/30);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(renderer.Render(source, CheekPinchGeometry.For(profile),
                    CheekSide.Left,dynamics.X,dynamics.Y,dynamics.Nibble)));
                using var stream = File.Create(Path.Combine(sequence,$"{i:000}.png"));
                encoder.Save(stream);
            }
        }
    }

    public static void PairCalibrationsKeepContactsInBounds()
    {
        foreach (var profile in PetCharacterProfile.All)
        foreach (var folder in new[] { "pair_cheek", "pair_feed", "pair_sleep" })
        for (var frame=0; frame<16; frame++)
        {
            var mouth = PairSpriteGeometry.Map(profile,folder,frame,290,240);
            Assert(double.IsFinite(mouth.X) && mouth.X is > 0 and < 512 && mouth.Y is > 0 and < 512,
                "Calibrated attachment points must remain finite and inside the sprite.");
        }
        var calibrated = PairSpriteGeometry.Map(PetCharacterProfile.Nuonuo,"pair_feed",9,290,240);
        Assert(Math.Abs(calibrated.Y-240) > 5, "The mouth contact must follow the resized art, not stale coordinates.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
