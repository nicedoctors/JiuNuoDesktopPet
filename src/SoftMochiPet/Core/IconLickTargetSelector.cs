using DrawingPoint = System.Drawing.Point;

namespace SoftMochiPet.Core;

public static class IconLickTargetSelector
{
    public static DrawingPoint? Choose(
        IReadOnlyList<DrawingPoint> candidates,
        DrawingPoint origin,
        double randomUnit,
        int preferredMinimumDistance = 72,
        int nearestPoolSize = 12)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var preferredMinimumDistanceSquared = preferredMinimumDistance * preferredMinimumDistance;
        var ordered = candidates
            .Distinct()
            .Select(point => new
            {
                Point = point,
                DistanceSquared = SquaredDistance(point, origin),
            })
            .OrderBy(candidate => candidate.DistanceSquared)
            .ToArray();
        var preferred = ordered
            .Where(candidate => candidate.DistanceSquared >= preferredMinimumDistanceSquared)
            .Take(Math.Max(1, nearestPoolSize))
            .ToArray();
        var pool = preferred.Length > 0
            ? preferred
            : ordered.Take(Math.Max(1, nearestPoolSize)).ToArray();
        var index = Math.Min(
            pool.Length - 1,
            (int)Math.Floor(Math.Clamp(randomUnit, 0, 0.999999) * pool.Length));
        return pool[index].Point;
    }

    private static long SquaredDistance(DrawingPoint left, DrawingPoint right)
    {
        var deltaX = (long)left.X - right.X;
        var deltaY = (long)left.Y - right.Y;
        return deltaX * deltaX + deltaY * deltaY;
    }
}
