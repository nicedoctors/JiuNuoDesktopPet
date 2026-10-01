using SoftMochiPet.Core;

internal static class TearPrankPresentationTests
{
    public static void TemporarySizeRespectsDpiAndWorkArea()
    {
        foreach (var dpi in new[] { 0.75, 1, 1.25, 1.5, 2, 3 })
        {
            foreach (var baseSize in new[] { 132d, 220, 440, 1000 })
            {
                var actual = TearPrankPresentation.ExpectedGrowSize(baseSize, 700 * dpi, 1920 * dpi, 1080 * dpi, dpi);
                var desired = Math.Max(baseSize * 2.25, 700 * 0.95);
                var expected = Math.Max(baseSize, Math.Min(desired, Math.Min(baseSize * 3, 1080 * 0.70)));
                Close(actual, expected, "Equivalent logical desktops must produce the same temporary size across DPI scales.");
                Assert(actual >= baseSize && actual <= baseSize * 3,
                    "The temporary performance must neither shrink the pet nor exceed three times its starting size.");
                if (baseSize <= 1080 * 0.70)
                    Assert(actual * dpi <= 1080 * dpi * 0.70 + 1e-9,
                        "Growth must leave the requested physical work-area margin.");
            }
        }
        Close(TearPrankPresentation.ExpectedGrowSize(132, 400, 1920, 1080, 1), 380,
            "A small pet should grow in proportion to the target window when that exceeds the base multiplier.");
        Close(TearPrankPresentation.ExpectedGrowSize(220, 100, 1920, 1080, 1), 495,
            "A small target must still show the minimum 2.25-times performance size where space permits.");
        Close(TearPrankPresentation.ExpectedGrowSize(220, 700, 600, 1920, 1), 420,
            "Portrait and narrow work areas must limit growth by their shorter dimension.");
    }

    public static void TemporarySizePreservesUserSize()
    {
        var preferredSize = PetSizePolicy.FromPercent(PetSizePolicy.MaximumPercent);
        var grown = TearPrankPresentation.ExpectedGrowSize(preferredSize, 900, 1920, 1080, 1);
        Assert(grown > preferredSize && grown > PetSizePolicy.ClampPreferredSize(grown),
            "Temporary tear growth must not pass through the saved-size slider's 200-percent clamp.");
        Close(preferredSize, 440, "Presentation calculations must leave the requested user size intact.");
        Close(TearPrankPresentation.ExpectedGrowSize(900, 900, 1920, 1080, 1), 900,
            "A pet already larger than the stage limit must retain its size rather than shrink.");
        Close(TearPrankPresentation.ExpectedGrowSize(220, 700, 384, 300, 2), 220,
            "A tiny or high-DPI desktop must preserve the starting size when there is no room for growth.");
        Close(TearPrankPresentation.ExpectedGrowSize(220, 700, 0, 1080, 1), 220,
            "Unavailable work-area dimensions must skip temporary growth.");
        Close(TearPrankPresentation.ExpectedGrowSize(220, 700, 1920, 1080, double.NaN), 660,
            "An unavailable DPI sample must retain a finite presentation target.");
    }

    public static void BodyScaleHasContinuousTearKeyPoses()
    {
        foreach (var frame in new[] { -1d, 0, 1, 3, 16, 17 })
            Pose(frame, 1, 1);
        Pose(6, 1.06, 0.92);
        foreach (var frame in new[] { 8d, 9, 10 })
            Pose(frame, 0.96, 1.12);
        Pose(4.5, 1.03, 0.96);
        Pose(7, 1.01, 1.02);
        Pose(13, 0.98, 1.06);

        var peak = TearPrankPresentation.At(9 / 16d);
        var previous = TearPrankPresentation.At(0);
        for (var sample = 1; sample <= 4096; sample++)
        {
            var current = TearPrankPresentation.At(sample / 4096d);
            Assert(double.IsFinite(current.BodyScaleX) && double.IsFinite(current.BodyScaleY) &&
                current.BodyScaleX is >= 0.96 and <= 1.06 && current.BodyScaleY is >= 0.92 and <= 1.12,
                "Squash and stretch must stay finite and bounded throughout the entire source clip.");
            Assert(current.BodyScaleY <= peak.BodyScaleY &&
                Math.Abs(current.BodyScaleX - previous.BodyScaleX) < 0.002 &&
                Math.Abs(current.BodyScaleY - previous.BodyScaleY) < 0.002,
                "Frame nine must carry the maximum vertical tension without a scale jump.");
            previous = current;
        }
        foreach (var frame in new[] { 3d, 6, 8, 9, 10, 16 })
        {
            var left = TearPrankPresentation.At((frame - 1e-5) / 16);
            var right = TearPrankPresentation.At((frame + 1e-5) / 16);
            Assert(Math.Abs(left.BodyScaleX - right.BodyScaleX) < 1e-8 &&
                Math.Abs(left.BodyScaleY - right.BodyScaleY) < 1e-8,
                "Each held key pose must join the neighboring SmoothStep segment continuously.");
        }
        var invalid = TearPrankPresentation.At(double.NaN);
        Assert(invalid == new TearPrankPresentation(1, 1), "Invalid source progress must leave the body undeformed.");
    }

    private static void Pose(double frame, double x, double y)
    {
        var pose = TearPrankPresentation.At(frame / 16);
        Close(pose.BodyScaleX, x, $"Unexpected horizontal pose at source frame {frame}.");
        Close(pose.BodyScaleY, y, $"Unexpected vertical pose at source frame {frame}.");
    }

    private static void Close(double actual, double expected, string message) =>
        Assert(double.IsFinite(actual) && Math.Abs(actual - expected) <= 1e-9,
            $"{message} Expected {expected:R}, actual {actual:R}.");

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
