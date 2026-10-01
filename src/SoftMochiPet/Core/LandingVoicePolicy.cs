namespace SoftMochiPet.Core;

public static class LandingVoicePolicy
{
    private static readonly HashSet<string> UserOrEnvironmentLaunchReasons =
    [
        "DragThrow",
        "PlatformLaunch",
        "PlatformDownwardRelease",
        "WindowMaximized",
        "WindowSideImpact",
        "EnclosureWindowLost",
    ];

    public static bool ShouldReact(
        double fallDistancePixels,
        double downwardImpactSpeed,
        string reason,
        double monitorScale)
    {
        var scale = Math.Max(0.5, monitorScale);
        if (fallDistancePixels >= 90 * scale)
        {
            return true;
        }

        return UserOrEnvironmentLaunchReasons.Contains(reason) &&
            downwardImpactSpeed >= 360 * scale;
    }
}
