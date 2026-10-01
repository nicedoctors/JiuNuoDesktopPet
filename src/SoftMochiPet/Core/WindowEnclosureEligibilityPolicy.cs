using System.Drawing;
using SoftMochiPet.Services;

namespace SoftMochiPet.Core;

/// <summary>Only bounded application windows may act as physical enclosures.</summary>
public static class WindowEnclosureEligibilityPolicy
{
    public static bool IsBoundedBody(Rectangle bounds, IReadOnlyList<MonitorGeometry> monitors)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return false;
        foreach (var monitor in monitors)
        {
            // A maximized or borderless full-screen surface is a background,
            // not a box. A real window behind the target does not reach here;
            // this rule only filters enclosure candidates.
            var monitorArea = monitor.MonitorArea;
            var workArea = monitor.WorkArea;
            if (Covers(bounds, monitorArea) || Covers(bounds, workArea)) return false;
        }
        return true;
    }

    private static bool Covers(Rectangle bounds, Rectangle area)
    {
        if (area.Width <= 0 || area.Height <= 0) return false;
        var intersection = Rectangle.Intersect(bounds, area);
        var coverage = (long)intersection.Width * intersection.Height;
        var areaPixels = (long)area.Width * area.Height;
        return coverage * 100 >= areaPixels * 99 &&
            Math.Abs(bounds.Top - area.Top) <= 4 &&
            Math.Abs(bounds.Left - area.Left) <= 4;
    }
}
