using Point = System.Windows.Point;

namespace SoftMochiPet.Core;

public readonly record struct PairFeedFlight(Point Start, Point End, double Duration, double Lift)
{
    public const double ThrowAt = 3.86;
    public double Arrival => ThrowAt + Duration;
    public double CatchStart => Arrival - .525;
    public double ReadyStart => CatchStart - .75;
    public double Finished => Math.Max(6.4, CatchStart + 1.75);

    public static PairFeedFlight Create(Point start, Point end, double scale, double ceiling)
    {
        var distance = (end - start).Length;
        var duration = Math.Clamp(.55 + distance / (950 * Math.Max(.5, scale)), .7, 4);
        var lift = Math.Min(Math.Clamp(distance * .16, 38 * scale, 200 * scale),
            Math.Max(0, Math.Min(start.Y, end.Y) - ceiling));
        return new(start, end, duration, lift);
    }

    public Point At(double progress)
    {
        var t = Math.Clamp(progress, 0, 1);
        return new(Start.X + (End.X - Start.X) * t,
            Start.Y + (End.Y - Start.Y) * t - 4 * Lift * t * (1 - t));
    }
}
