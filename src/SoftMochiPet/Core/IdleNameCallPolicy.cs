namespace SoftMochiPet.Core;

public static class IdleNameCallPolicy
{
    public const double MinimumSeconds = 20;
    public const double MaximumSeconds = 50;

    public static double NextDelay(double randomUnit)
    {
        var normalized = double.IsFinite(randomUnit) ? Math.Clamp(randomUnit, 0, 1) : 0.5;
        return MinimumSeconds + normalized * (MaximumSeconds - MinimumSeconds);
    }
}
