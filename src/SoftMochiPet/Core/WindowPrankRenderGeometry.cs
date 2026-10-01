using System.Drawing;

namespace SoftMochiPet.Core;

public readonly record struct WindowPrankCollectionPose(PointF Center, double Scale, double Rotation,
    bool Visible, bool ClipAtMouth);
public readonly record struct WindowPrankTearStretch(double ScaleY, double OffsetY);
public readonly record struct WindowPrankTearPose(PointF Grip, double ScaleY, double Rotation);

public static class WindowPrankRenderGeometry
{
    public static double NormalizeProgress(double progress) =>
        Math.Clamp(double.IsFinite(progress) ? progress : 0, 0, 1);

    public static bool PassesPointerThrough(bool held, bool hasForeground, RectangleF foregroundBounds, PointF point) =>
        held || hasForeground && foregroundBounds.Width > 0 && foregroundBounds.Height > 0 && foregroundBounds.Contains(point);

    public static WindowPrankTearStretch TearStretch(RectangleF source, float seamY, double tension,
        PointF? firstUpper, PointF? firstLower, PointF? upperHand, PointF? lowerHand)
    {
        var t = NormalizeProgress(tension);
        var additionalStretch = 0d;
        var translation = 0d;
        if (firstUpper is { } firstTop && firstLower is { } firstBottom &&
            upperHand is { } top && lowerHand is { } bottom)
        {
            var separation = (bottom.Y - top.Y) - (firstBottom.Y - firstTop.Y);
            additionalStretch = Math.Clamp(separation / Math.Max(1, source.Height), 0, 0.12);
            translation = Math.Clamp((top.Y + bottom.Y - firstTop.Y - firstBottom.Y) / 2,
                -source.Height * 0.08, source.Height * 0.08) * t;
        }
        var scaleY = 1 + (0.12 + additionalStretch) * t;
        return new(scaleY, seamY * (1 - scaleY) + translation);
    }

    public static WindowPrankTearPose TearHalfPose(PointF sourceGrip, PointF hand,
        WindowPrankTearStretch stretch, double progress, bool reassembling, bool upperHalf)
    {
        var p = NormalizeProgress(progress);
        var opened = reassembling ? p * p * (3 - 2 * p) : 1 - Math.Pow(1 - p, 3);
        var residualStretch = reassembling ? 0 : 1 - opened;
        var scaleY = 1 + (stretch.ScaleY - 1) * residualStretch;
        var initial = new PointF(sourceGrip.X,
            (float)(sourceGrip.Y * scaleY + stretch.OffsetY * residualStretch));
        var grip = new PointF((float)(initial.X + (hand.X - initial.X) * opened),
            (float)(initial.Y + (hand.Y - initial.Y) * opened));
        return new(grip, scaleY, (upperHalf ? 1 : -1) * 2.5 * opened);
    }

    public static WindowPrankCollectionPose CollectionPose(RectangleF sourceBounds, PointF mouth,
        double mouthWidth, double progress, int pieceIndex, int pieceCount)
    {
        var start = new PointF(sourceBounds.X + sourceBounds.Width / 2, sourceBounds.Y + sourceBounds.Height / 2);
        if (sourceBounds.Width <= 0 || sourceBounds.Height <= 0)
            return new(start, 1, 0, false, false);
        var delay = Math.Clamp(pieceIndex / (double)Math.Max(1, pieceCount - 1), 0, 1) * 0.16;
        var t = Math.Clamp((NormalizeProgress(progress) - delay) / (1 - delay), 0, 1);
        var eased = t * t * (3 - 2 * t);
        var width = Math.Clamp(double.IsFinite(mouthWidth) ? mouthWidth : 60, 20, 240);
        var finalScale = Math.Min(1, width * 0.64 / Math.Max(sourceBounds.Width, sourceBounds.Height));
        var scale = Math.Exp(Math.Log(finalScale) * eased);
        var end = new PointF(mouth.X, (float)(mouth.Y + sourceBounds.Height * finalScale * 0.7));
        var lift = Math.Clamp(Math.Abs(start.X - end.X) * 0.18 + Math.Abs(start.Y - end.Y) * 0.08, 28, 160);
        var controlY = Math.Min(start.Y, end.Y) - lift;
        var center = new PointF((float)(start.X + (end.X - start.X) * eased),
            (float)((1 - eased) * (1 - eased) * start.Y + 2 * (1 - eased) * eased * controlY + eased * eased * end.Y));
        return new(center, scale, Math.Sin(t * Math.PI) * (pieceIndex % 2 == 0 ? -12 : 12),
            t < 1, t > 0.72);
    }
}
