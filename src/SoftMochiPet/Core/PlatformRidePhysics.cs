namespace SoftMochiPet.Core;

public enum PlatformMotionResponse
{
    Follow,
    LaunchUpward,
    ReleaseDownward,
}

public sealed record PlatformMotionDecision(PlatformMotionResponse Response, double InitialPetVelocityY);

public sealed record AirbornePlatformContactDecision(
    bool PushesPet,
    double CorrectedFootY,
    double PetVelocityY,
    double PlatformVelocityY);

public static class PlatformRidePhysics
{
    public const double SupportRecoveryGraceSeconds = 0.35;

    public static PlatformMotionDecision Evaluate(double displacementY, double velocityY, double dpiScale)
    {
        var scale = Math.Max(0.75, dpiScale);
        if (displacementY <= -3 * scale && velocityY <= -360 * scale)
        {
            return new PlatformMotionDecision(
                PlatformMotionResponse.LaunchUpward,
                Math.Clamp(velocityY * 0.88, -1150 * scale, -300 * scale));
        }

        if (displacementY >= 2 * scale && velocityY >= 90 * scale)
        {
            return new PlatformMotionDecision(PlatformMotionResponse.ReleaseDownward, 0);
        }

        return new PlatformMotionDecision(PlatformMotionResponse.Follow, 0);
    }

    public static bool ShouldPreserveTransientSupport(double unavailableSeconds) =>
        double.IsFinite(unavailableSeconds) &&
        unavailableSeconds >= 0 &&
        unavailableSeconds <= SupportRecoveryGraceSeconds;

    public static AirbornePlatformContactDecision ResolveAirborneContact(
        double footY,
        double petVelocityY,
        int previousPlatformTop,
        int currentPlatformTop,
        double deltaSeconds,
        double dpiScale)
    {
        var scale = Math.Max(0.75, dpiScale);
        var displacementY = currentPlatformTop - previousPlatformTop;
        var platformVelocityY = displacementY / Math.Max(deltaSeconds, 1d / 240);
        var risesThroughPet = currentPlatformTop < footY - 0.5;
        var risesFast = displacementY <= -3 * scale && platformVelocityY <= -360 * scale;
        if (!risesThroughPet || !risesFast)
        {
            return new AirbornePlatformContactDecision(
                false,
                footY,
                petVelocityY,
                platformVelocityY);
        }

        var inheritedVelocity = Math.Clamp(
            platformVelocityY * 0.88,
            -1150 * scale,
            -300 * scale);
        return new AirbornePlatformContactDecision(
            true,
            currentPlatformTop,
            Math.Min(petVelocityY, inheritedVelocity),
            platformVelocityY);
    }
}
