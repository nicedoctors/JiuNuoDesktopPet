namespace SoftMochiPet.Core;

public static class DpiScalePolicy
{
    private const uint DefaultDpi = 96;
    private const int MinimumScalePercent = 50;
    private const int MaximumScalePercent = 500;

    public static int Resolve(uint windowDpi, uint monitorDpi, int shellScalePercent)
    {
        var windowScale = FromDpi(windowDpi);
        if (windowScale is not null)
        {
            return windowScale.Value;
        }

        var monitorScale = FromDpi(monitorDpi);
        if (monitorScale is not null)
        {
            return monitorScale.Value;
        }

        return IsValidScale(shellScalePercent) ? shellScalePercent : 100;
    }

    public static int? FromDpi(uint dpi)
    {
        if (dpi == 0)
        {
            return null;
        }

        var scale = (int)Math.Round(dpi * 100d / DefaultDpi);
        return IsValidScale(scale) ? scale : null;
    }

    private static bool IsValidScale(int scalePercent) =>
        scalePercent is >= MinimumScalePercent and <= MaximumScalePercent;
}
