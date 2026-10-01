namespace SoftMochiPet.Core;

public sealed record AirborneHorizontalCorrection(
    bool Corrected,
    double NextFootX,
    double VelocityX);

public static class AirborneBoundaryPolicy
{
    private const double BounceRetention = 0.46;

    public static AirborneHorizontalCorrection ResolveHorizontal(
        double proposedFootX,
        double velocityX,
        int monitorLeft,
        int monitorRight,
        double margin)
    {
        if (monitorRight <= monitorLeft)
        {
            return new AirborneHorizontalCorrection(false, proposedFootX, velocityX);
        }

        var minimum = Math.Min(monitorRight - 1d, monitorLeft + Math.Max(0, margin));
        var maximum = Math.Max(minimum, monitorRight - 1d - Math.Max(0, margin));
        var correctedX = Math.Clamp(proposedFootX, minimum, maximum);
        if (Math.Abs(correctedX - proposedFootX) < 0.01)
        {
            return new AirborneHorizontalCorrection(false, proposedFootX, velocityX);
        }

        var hitLeft = proposedFootX < minimum;
        var correctedVelocity = hitLeft
            ? velocityX > 0 ? velocityX : Math.Abs(velocityX) * BounceRetention
            : velocityX < 0 ? velocityX : -Math.Abs(velocityX) * BounceRetention;
        return new AirborneHorizontalCorrection(true, correctedX, correctedVelocity);
    }
}
