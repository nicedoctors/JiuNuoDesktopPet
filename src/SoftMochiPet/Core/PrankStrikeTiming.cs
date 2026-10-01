namespace SoftMochiPet.Core;

/// <summary>Retimes only the five existing attacks; progress remains in the original sixteen-frame coordinates.</summary>
public sealed record PrankStrikeTiming(string Clip, int WindupFrame, double WindupSeconds,
    double StrikeSeconds, double ImpactPauseSeconds, double FollowThroughSeconds, double RecoverySeconds)
{
    public static IReadOnlyList<PrankStrikeTiming> All { get; } = Array.AsReadOnly(new[]
    {
        new PrankStrikeTiming("kick", 5, 0.580, 0.140, 0.120, 0.300, 0.460),
        new PrankStrikeTiming("punch", 4, 0.500, 0.130, 0.100, 0.280, 0.440),
        new PrankStrikeTiming("charge", 5, 0.620, 0.140, 0.140, 0.320, 0.480),
        new PrankStrikeTiming("bare_kick", 7, 1.000, 0.120, 0.180, 0.580, 0.700),
        new PrankStrikeTiming("bare_tear", 6, 1.750, 0.300, 0.180, 0.800, 0.700),
    });

    public static PrankStrikeTiming? Find(string clip) =>
        All.FirstOrDefault(item => string.Equals(item.Clip, clip, StringComparison.OrdinalIgnoreCase));
    public double ContactSeconds => WindupSeconds + StrikeSeconds;
    public double DurationSeconds => ContactSeconds + ImpactPauseSeconds + FollowThroughSeconds + RecoverySeconds;

    public double SourceProgress(double seconds)
    {
        var time = double.IsFinite(seconds) ? Math.Clamp(seconds, 0, DurationSeconds) : 0;
        if (time >= DurationSeconds) return 1;
        if (time < WindupSeconds) return WindupSourceFrame(time / WindupSeconds) / 16;
        if (time < ContactSeconds)
            return (WindupFrame + (8 - WindupFrame) * (time - WindupSeconds) / StrikeSeconds) / 16;
        var afterPause = time - ContactSeconds - ImpactPauseSeconds;
        if (afterPause <= 0) return 0.5;
        if (afterPause < FollowThroughSeconds)
        {
            var followProgress = afterPause / FollowThroughSeconds;
            if (Clip == "bare_tear")
            {
                // Reach the fully separated frame nine promptly, then keep that
                // pose readable until the source starts releasing at frame ten.
                return (followProgress < 0.25
                    ? 8 + followProgress / 0.25
                    : 9 + (followProgress - 0.25) / 0.75) / 16;
            }
            return (8 + 3 * followProgress) / 16;
        }
        var releaseFrame = Clip == "bare_tear" ? 10 : 11;
        return Math.Min(1, (releaseFrame + (16 - releaseFrame) *
            (afterPause - FollowThroughSeconds) / RecoverySeconds) / 16);
    }

    private double WindupSourceFrame(double progress)
    {
        if (Clip == "bare_kick")
        {
            // Frame five is a light feint; the visibly coiled leg in frame six
            // carries the heavy windup before frame seven launches into contact.
            if (progress < 0.28) return 3 * progress / 0.28;
            if (progress < 0.54) return 3 + 2 * (progress - 0.28) / 0.26;
            if (progress < 0.605) return 5 + (progress - 0.54) / 0.065;
            return 6 + (progress - 0.605) / 0.395;
        }
        if (Clip == "bare_tear")
        {
            // Grab at frame three first, then leave the strained two-hand grip
            // readable before the fast pull into the measured frame-eight tear.
            const double grabFraction = 0.450 / 1.750;
            return progress < grabFraction
                ? 3 * progress / grabFraction
                : 3 + (WindupFrame - 3) * (progress - grabFraction) / (1 - grabFraction);
        }
        return WindupFrame * progress;
    }

    public double MotionDelta(double before, double after)
    {
        var overlap = Math.Max(0, Math.Min(after, ContactSeconds + ImpactPauseSeconds) - Math.Max(before, ContactSeconds));
        return Math.Max(0, after - before - overlap);
    }

    public double ConsumeImpactPause(double elapsed, double systemWaitSeconds) =>
        Math.Max(elapsed, Math.Min(ContactSeconds + ImpactPauseSeconds, elapsed + Math.Max(0, systemWaitSeconds)));

    public double PoseWeight(double sourceProgress)
    {
        var anticipation = WindupFrame / 16d;
        if (sourceProgress < anticipation)
            return -0.12 * Math.Pow(Math.Clamp(sourceProgress / anticipation, 0, 1), 2);
        if (sourceProgress <= 0.5)
            return -0.12 + 1.12 * Math.Pow((sourceProgress - anticipation) / (0.5 - anticipation), 2);
        var recovery = Math.Clamp((sourceProgress - 0.5) * 2, 0, 1);
        // One reaction overshoot, returning exactly to the staging position without oscillation.
        return Math.Pow(1 - recovery, 3) - 0.16 * Math.Sin(Math.PI * recovery);
    }
}
