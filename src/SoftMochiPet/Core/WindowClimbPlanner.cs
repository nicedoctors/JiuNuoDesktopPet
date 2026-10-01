using SoftMochiPet.Services;

namespace SoftMochiPet.Core;

public enum WindowClimbSide
{
    Left,
    Right,
}

public sealed record WindowClimbPlan(
    IntPtr TargetHandle,
    WindowClimbSide Side,
    int ApproachX,
    int LandingX,
    int TargetTop,
    int TargetBottom,
    int Height);

public static class WindowClimbPlanner
{
    public static WindowClimbPlan? Choose(
        IReadOnlyList<WindowSurface> surfaces,
        WindowSurface currentSupport,
        int footX,
        double scale,
        double randomUnit)
    {
        scale = Math.Clamp(scale, 0.5, 4);
        var minimumHeight = (int)Math.Round(64 * scale);
        var maximumHeight = (int)Math.Round(1320 * scale);
        var sideGap = Math.Max(8, (int)Math.Round(14 * scale));
        var landingInset = Math.Max(20, (int)Math.Round(34 * scale));
        // She can make a small exaggerated hop to catch a floating window's
        // lower corner before climbing the visible side.
        var floorReachTolerance = (int)Math.Round(420 * scale);
        var candidates = new List<(WindowClimbPlan Plan, double Score)>();

        foreach (var target in surfaces)
        {
            if (target.IsDesktopFloor || target.SourceHandle == IntPtr.Zero ||
                target.SourceHandle == currentSupport.SourceHandle)
            {
                continue;
            }

            var height = currentSupport.Top - target.Top;
            if (height < minimumHeight || height > maximumHeight ||
                target.EffectiveBottom < currentSupport.Top - floorReachTolerance)
            {
                continue;
            }

            AddSide(WindowClimbSide.Left, target.Left - sideGap, target.Left + landingInset);
            AddSide(WindowClimbSide.Right, target.Right + sideGap, target.Right - landingInset);

            void AddSide(WindowClimbSide side, int approachX, int landingX)
            {
                if (!currentSupport.ContainsX(approachX, inset: 18) ||
                    !target.ContainsX(landingX, inset: 8))
                {
                    return;
                }

                var horizontalDistance = Math.Abs(approachX - footX);
                var score = horizontalDistance + height * 0.16;
                candidates.Add((
                    new WindowClimbPlan(
                        target.SourceHandle,
                        side,
                        approachX,
                        landingX,
                        target.Top,
                        target.EffectiveBottom,
                        height),
                    score));
            }
        }

        var pool = candidates
            .OrderBy(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Plan.TargetTop)
            .Take(4)
            .ToArray();
        if (pool.Length == 0)
        {
            return null;
        }

        var index = Math.Min(
            pool.Length - 1,
            (int)Math.Floor(Math.Clamp(randomUnit, 0, 0.999999) * pool.Length));
        return pool[index].Plan;
    }
}
