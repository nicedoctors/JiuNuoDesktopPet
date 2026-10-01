using System.Drawing;

namespace SoftMochiPet.Core;

public sealed record PairIconKickLayout(
    Point Stage,
    Point NuonuoTopLeft,
    Point FeibiTopLeft,
    Point Mouth,
    bool Rebound,
    int ReboundX);

public static class PairIconKickPlanner
{
    public static PairIconKickLayout? Plan(
        Rectangle workArea,
        Point icon,
        Size nuonuoSize,
        Size feibiSize,
        Point nuonuoMouthOffset,
        Point feibiKickOffset,
        int gap)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0 || gap <= 0 ||
            !workArea.Contains(icon)) return null;

        var minX = workArea.Left + 4 + feibiKickOffset.X;
        var maxX = workArea.Right - 4 - (feibiSize.Width - feibiKickOffset.X);
        var minY = workArea.Top + 8 + Math.Max(nuonuoMouthOffset.Y, feibiKickOffset.Y);
        var maxY = workArea.Bottom - 4 - Math.Max(
            nuonuoSize.Height - nuonuoMouthOffset.Y, feibiSize.Height - feibiKickOffset.Y);
        if (minX > maxX || minY > maxY) return null;
        // Edge icons remain at their original position until anticipation, then
        // slide a bounded distance to an on-screen foot/mouth contact height.
        var stage = new Point(Math.Clamp(icon.X, minX, maxX), Math.Clamp(icon.Y, minY, maxY));
        var feibi = new Point(stage.X - feibiKickOffset.X, stage.Y - feibiKickOffset.Y);
        var mouth = new Point(stage.X - gap, stage.Y);
        var nuonuo = new Point(mouth.X - nuonuoMouthOffset.X,
            mouth.Y - nuonuoMouthOffset.Y);
        var rebound = !Fits(workArea, nuonuo, nuonuoSize);
        if (rebound)
        {
            mouth.X = stage.X + gap;
            nuonuo.X = mouth.X - nuonuoMouthOffset.X;
        }

        if (!Fits(workArea, feibi, feibiSize) ||
            !Fits(workArea, nuonuo, nuonuoSize) ||
            Math.Abs(stage.Y - icon.Y) > Math.Max(160, feibiSize.Height) ||
            Math.Abs(stage.X - icon.X) > Math.Max(160, feibiSize.Width)) return null;

        return new PairIconKickLayout(stage, nuonuo, feibi, mouth, rebound,
            workArea.Left + 12);
    }

    private static bool Fits(Rectangle area, Point topLeft, Size size) =>
        topLeft.X >= area.Left + 4 && topLeft.Y >= area.Top + 4 &&
        topLeft.X + size.Width <= area.Right - 4 &&
        topLeft.Y + size.Height <= area.Bottom - 4;
}
