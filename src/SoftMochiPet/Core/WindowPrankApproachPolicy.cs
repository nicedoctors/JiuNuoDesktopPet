using System.Drawing;

namespace SoftMochiPet.Core;

public readonly record struct WindowPrankApproach(int FootX, int Direction);

/// <summary>Chooses the nearer usable side of a target window.</summary>
public static class WindowPrankApproachPolicy
{
    public static WindowPrankApproach? Choose(Rectangle target, Rectangle workArea, int petWidth, int petX)
    {
        var width = Math.Max(1, petWidth);
        var offset = (int)Math.Round(width * 0.62) + 8;
        var right = target.Right + offset;
        var left = target.Left - offset;
        var rightFits = workArea.Right - right >= width / 2 + 1;
        var leftFits = left - workArea.Left >= width / 2 + 1;
        if (!leftFits && !rightFits) return null;
        if (leftFits && (!rightFits || Math.Abs(petX - left) <= Math.Abs(right - petX)))
            return new WindowPrankApproach(left, -1);
        return new WindowPrankApproach(right, 1);
    }
}
