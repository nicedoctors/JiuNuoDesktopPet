namespace SoftMochiPet.Core;

public sealed record WindowExpansionBounce(double VelocityX, double VelocityY);

public static class WindowExpansionPhysics
{
    public static WindowExpansionBounce? Evaluate(
        bool supportUnavailable,
        bool windowMaximized,
        double footX,
        double monitorCenterX,
        double dpiScale)
    {
        if (!supportUnavailable || !windowMaximized)
        {
            return null;
        }

        var scale = Math.Max(0.75, dpiScale);
        var direction = footX < monitorCenterX ? -1 : 1;
        return new WindowExpansionBounce(direction * 260 * scale, -820 * scale);
    }
}
