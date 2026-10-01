namespace SoftMochiPet.Core;

public static class PetSizePolicy
{
    public const double DefaultSize = 220;
    public const int MinimumPercent = 60;
    public const int MaximumPercent = 200;
    public const double MinimumFittedSize = 96;

    public static double ClampPreferredSize(double size)
    {
        if (!double.IsFinite(size))
        {
            return DefaultSize;
        }

        return Math.Clamp(
            size,
            FromPercent(MinimumPercent),
            FromPercent(MaximumPercent));
    }

    public static double FromPercent(int percent) =>
        DefaultSize * Math.Clamp(percent, MinimumPercent, MaximumPercent) / 100d;

    public static int ToPercent(double size) =>
        Math.Clamp(
            (int)Math.Round(ClampPreferredSize(size) / DefaultSize * 100),
            MinimumPercent,
            MaximumPercent);

    public static double FitToWorkArea(
        double preferredSize,
        int workAreaWidth,
        int workAreaHeight,
        double dpiScale)
    {
        var limitingPhysicalDimension = Math.Max(1, Math.Min(workAreaWidth, workAreaHeight));
        var safeScale = double.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1;
        var maximumDipSize = limitingPhysicalDimension * 0.78 / safeScale;
        return Math.Max(MinimumFittedSize, Math.Min(ClampPreferredSize(preferredSize), maximumDipSize));
    }
}
