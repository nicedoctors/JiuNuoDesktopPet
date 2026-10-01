namespace SoftMochiPet.Core;

public static class CompanionLifePolicy
{
    public const double ContextGap = 18;
    public const double FollowCooldown = 65;
    public const double NuzzleCooldown = 80;
    public const double BallCooldown = 100;
    public const double WatchCooldown = 45;
    public const double EventLifetime = 8;
    public const double KickFrameSeconds = .13;
    public const double KickContact = 7 * KickFrameSeconds;
    public const double BallStart = 5.2 + KickContact;
    public const double RallySeconds = 1.6;
    public const int RallyCount = 6;
    public const double BallEnd = BallStart + RallySeconds * RallyCount;

    public static double Ease(double t)
    {
        t = Math.Clamp(t, 0, 1);
        return t * t * (3 - 2 * t);
    }

    public static (double Leader, double Follower) FollowProgress(double seconds)
    {
        var leader = seconds < 2.4 ? .55 * Ease(seconds / 2.4)
            : seconds < 3.4 ? .55 : .55 + .45 * Ease((seconds - 3.4) / 2.6);
        return (leader, Ease((seconds - .8) / 5.7));
    }

    public static bool IsPassing(double distance, double petWidth, bool moving) =>
        moving && distance >= petWidth * .3 && distance <= petWidth * 1.05;

    public static bool ShouldFollow(double distance, double petWidth, bool leaderWalkingAway) =>
        leaderWalkingAway && distance >= petWidth * 1.3 && distance <= petWidth * 4;

    public static bool EventFresh(double now, double eventAt) =>
        eventAt > 0 && now >= eventAt && now - eventAt <= EventLifetime;

    public static (double X, double Y) BallArc(double startX, double endX, double groundY,
        double radius, double progress, double height)
    {
        var t = Math.Clamp(progress, 0, 1);
        return (startX + (endX - startX) * t, groundY - radius - 4 * height * t * (1 - t));
    }
}
