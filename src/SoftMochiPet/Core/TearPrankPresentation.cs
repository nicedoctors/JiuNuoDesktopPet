namespace SoftMochiPet.Core;

/// <summary>Temporary tear-only presentation; it never clamps or writes the user's preferred pet size.</summary>
public sealed record TearPrankPresentation(double BodyScaleX, double BodyScaleY)
{
    public static double ExpectedGrowSize(double baseDip, double windowPhysicalHeight,
        double workWidth, double workHeight, double dpi)
    {
        var baseSize = double.IsFinite(baseDip) && baseDip > 0 ? baseDip : PetSizePolicy.DefaultSize;
        var scale = double.IsFinite(dpi) && dpi > 0 ? dpi : 1;
        if (!double.IsFinite(workWidth) || !double.IsFinite(workHeight) || workWidth <= 0 || workHeight <= 0)
            return baseSize;
        var windowHeight = double.IsFinite(windowPhysicalHeight) ? Math.Max(0, windowPhysicalHeight) : 0;
        var desired = Math.Max(baseSize * 2.25, windowHeight * 0.95 / scale);
        var maximum = Math.Min(baseSize * 3, Math.Min(workWidth, workHeight) * 0.70 / scale);
        // A pet already larger than the stage limit must not shrink to perform the attack.
        return Math.Max(baseSize, Math.Min(desired, maximum));
    }

    public static TearPrankPresentation At(double sourceProgress)
    {
        var frame = (double.IsFinite(sourceProgress) ? Math.Clamp(sourceProgress, 0, 1) : 0) * 16;
        if (frame <= 3) return new(1, 1);
        if (frame < 6) return Between(new(1, 1), new(1.06, 0.92), (frame - 3) / 3);
        if (frame < 8) return Between(new(1.06, 0.92), new(0.96, 1.12), (frame - 6) / 2);
        if (frame <= 10) return new(0.96, 1.12);
        return Between(new(0.96, 1.12), new(1, 1), (frame - 10) / 6);
    }

    private static TearPrankPresentation Between(TearPrankPresentation from, TearPrankPresentation to, double progress)
    {
        var weight = progress * progress * (3 - 2 * progress);
        return new(from.BodyScaleX + (to.BodyScaleX - from.BodyScaleX) * weight,
            from.BodyScaleY + (to.BodyScaleY - from.BodyScaleY) * weight);
    }
}
