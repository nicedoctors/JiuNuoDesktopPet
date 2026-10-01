using System.Drawing;

namespace SoftMochiPet.Core;

public static class GhostAnchorLayout
{
    private static readonly Point[] Offsets =
    [
        new(0, 0), new(84, 0), new(-84, 0), new(0, 94), new(0, -94),
        new(84, 94), new(-84, 94), new(84, -94), new(-84, -94),
        new(168, 0), new(-168, 0), new(0, 188), new(0, -188),
    ];

    public static Point ChooseDistinct(Point preferred, IReadOnlyCollection<Point> occupied, int minimumDistance = 56)
    {
        var minimumDistanceSquared = minimumDistance * minimumDistance;
        foreach (var offset in Offsets)
        {
            var candidate = new Point(preferred.X + offset.X, preferred.Y + offset.Y);
            if (occupied.All(point => DistanceSquared(point, candidate) >= minimumDistanceSquared))
            {
                return candidate;
            }
        }

        var index = occupied.Count + 1;
        return new Point(preferred.X + index * 24, preferred.Y + index * 18);
    }

    private static long DistanceSquared(Point first, Point second)
    {
        var deltaX = (long)first.X - second.X;
        var deltaY = (long)first.Y - second.Y;
        return deltaX * deltaX + deltaY * deltaY;
    }
}
