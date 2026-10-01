namespace SoftMochiPet.Core;

public readonly record struct CompanionPose(string Clip, double Seconds);
public readonly record struct CompanionBallSample(CompanionPose Nuonuo, CompanionPose Feibi,
    double X, double Y, double Opacity, bool Complete);

/// <summary>Shared by the live prop and silent offscreen animation verification.</summary>
public static class CompanionBallTimeline
{
    public static CompanionBallSample Sample(double t, double nx, double ny, double fx, double fy,
        double hatX, double hatY, double ground, double radius, double unit)
    {
        var n = new CompanionPose("pair_notice", Math.Min(t, 2.88));
        var f = new CompanionPose("pair_ball", 0);
        double x = fx, y = ground - radius, opacity = 1;
        if (t < 5.2)
        {
            f = t < 2.8 ? new("hat_open", Math.Min(t, 1.76))
                : t < 4.6 ? new("hat_wear", t - 2.8) : f;
            if (t < 1.65) { opacity = 0; x = hatX; y = hatY; }
            else if (t < 2.8)
            {
                var u = CompanionLifePolicy.Ease((t - 1.65) / 1.15);
                x = hatX + (fx - hatX) * u;
                y = hatY + (ground - radius - hatY) * u - unit * .15 * Math.Sin(Math.PI * u);
            }
            else y = FirstBounce(t, fy, ground, radius, unit);
        }
        else if (t < CompanionLifePolicy.BallStart)
        {
            n = new("pair_ball", 0);
            f = new("pair_ball", t - 5.2);
            y = FirstBounce(t, fy, ground, radius, unit);
        }
        else if (t < CompanionLifePolicy.BallEnd)
        {
            var rally = (int)((t - CompanionLifePolicy.BallStart) / CompanionLifePolicy.RallySeconds);
            var local = t - CompanionLifePolicy.BallStart - rally * CompanionLifePolicy.RallySeconds;
            var fromFeibi = rally % 2 == 0;
            var source = new CompanionPose("pair_ball", Math.Min(2.07, CompanionLifePolicy.KickContact + local));
            var target = new CompanionPose("pair_ball", Math.Max(0,
                local - (CompanionLifePolicy.RallySeconds - CompanionLifePolicy.KickContact)));
            n = fromFeibi ? target : source;
            f = fromFeibi ? source : target;
            var u = local / CompanionLifePolicy.RallySeconds;
            var arc = CompanionLifePolicy.BallArc(fromFeibi ? fx : nx, fromFeibi ? nx : fx,
                fromFeibi ? fy : ny, 0, u, unit * .3);
            x = arc.X;
            y = arc.Y + ((fromFeibi ? ny : fy) - (fromFeibi ? fy : ny)) * u;
        }
        else
        {
            var end = t - CompanionLifePolicy.BallEnd;
            n = new("pair_notice", Math.Min(end, 2.88));
            f = end < .35 ? new("pair_ball", CompanionLifePolicy.KickContact - .13)
                : end < 2.5 ? new("hat_open", Math.Min(end - .35, 1.76))
                : new("hat_wear", end - 2.5);
            var u = CompanionLifePolicy.Ease((end - 1.1) / 1.1);
            x = fx + (hatX - fx) * u;
            y = ground - radius + (hatY - ground + radius) * u - unit * .12 * Math.Sin(Math.PI * u);
            if (end < .35) y = fy + (ground - radius - fy) * CompanionLifePolicy.Ease(end / .35);
            opacity = 1 - CompanionLifePolicy.Ease((end - 1.9) / .3);
        }
        return new(n, f, x, y, opacity, t >= CompanionLifePolicy.BallEnd + 4.35);
    }

    private static double FirstBounce(double t, double fy, double ground, double radius, double unit)
    {
        var u = (t - 2.8) / (CompanionLifePolicy.BallStart - 2.8);
        return ground - radius + (fy - ground + radius) * u - unit * .22 * 4 * u * (1 - u);
    }
}
