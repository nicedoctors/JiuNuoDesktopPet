using DrawingPoint = System.Drawing.Point;

namespace SoftMochiPet.Core;

public sealed record DragReleaseMotion(
    bool HasInertia,
    double VelocityX,
    double VelocityY,
    double Speed,
    double SampleSeconds);

/// <summary>
/// Keeps only the tail of a drag gesture so release velocity follows the user's
/// final flick instead of being diluted by the whole drag path.
/// </summary>
public sealed class DragMotionTracker
{
    private sealed record Sample(DrawingPoint Point, double AtSeconds);

    private readonly Queue<Sample> _samples = new();

    public void Reset(DrawingPoint point, double atSeconds)
    {
        _samples.Clear();
        _samples.Enqueue(new Sample(point, atSeconds));
    }

    public void Add(DrawingPoint point, double atSeconds)
    {
        if (_samples.TryPeek(out var first) && atSeconds < first.AtSeconds)
        {
            Reset(point, atSeconds);
            return;
        }

        if (_samples.LastOrDefault() is { } last &&
            last.Point == point && atSeconds - last.AtSeconds < 0.004)
        {
            return;
        }

        _samples.Enqueue(new Sample(point, atSeconds));
        while (_samples.Count > 2 &&
               (atSeconds - _samples.Peek().AtSeconds > 0.14 || _samples.Count > 12))
        {
            _samples.Dequeue();
        }
    }

    public DragReleaseMotion Release(DrawingPoint point, double atSeconds, double dpiScale)
    {
        Add(point, atSeconds);
        if (_samples.Count < 2)
        {
            return new DragReleaseMotion(false, 0, 0, 0, 0);
        }

        var samples = _samples.ToArray();
        var newest = samples[^1];
        if (atSeconds - newest.AtSeconds > 0.10)
        {
            return new DragReleaseMotion(false, 0, 0, 0, 0);
        }

        var oldest = samples[0];
        var sampleSeconds = newest.AtSeconds - oldest.AtSeconds;
        if (sampleSeconds < 0.012)
        {
            return new DragReleaseMotion(false, 0, 0, 0, sampleSeconds);
        }

        var rawX = (newest.Point.X - oldest.Point.X) / sampleSeconds;
        var rawY = (newest.Point.Y - oldest.Point.Y) / sampleSeconds;
        var rawSpeed = Math.Sqrt(rawX * rawX + rawY * rawY);
        var scale = Math.Max(0.75, dpiScale);
        var threshold = 430 * scale;
        if (rawSpeed < threshold)
        {
            return new DragReleaseMotion(false, 0, 0, rawSpeed, sampleSeconds);
        }

        var transfer = Math.Clamp((rawSpeed - threshold) / (850 * scale), 0.38, 0.82);
        var velocityX = Math.Clamp(rawX * transfer, -1750 * scale, 1750 * scale);
        var velocityY = Math.Clamp(rawY * transfer, -1350 * scale, 1100 * scale);
        var speed = Math.Sqrt(velocityX * velocityX + velocityY * velocityY);
        return new DragReleaseMotion(true, velocityX, velocityY, speed, sampleSeconds);
    }
}
