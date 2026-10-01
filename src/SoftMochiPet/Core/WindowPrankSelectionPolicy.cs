using System.Drawing;

namespace SoftMochiPet.Core;

public static class WindowPrankSelectionPolicy
{
    public static bool CoversScreenArea(Rectangle bounds, Rectangle area, int edgeTolerance = 4)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || area.Width <= 0 || area.Height <= 0)
            return false;
        var intersection = Rectangle.Intersect(bounds, area);
        var coverage = (long)intersection.Width * intersection.Height;
        var areaPixels = (long)area.Width * area.Height;
        return coverage * 100 >= areaPixels * 99 &&
            Math.Abs(bounds.Left - area.Left) <= edgeTolerance &&
            Math.Abs(bounds.Top - area.Top) <= edgeTolerance;
    }

    public static bool IsFullScreen(Rectangle bounds, Rectangle monitorArea, Rectangle workArea) =>
        CoversScreenArea(bounds, monitorArea) || CoversScreenArea(bounds, workArea);

    public static string? OrdinaryWindowRejection(long style, long extendedStyle,
        bool hasOwner, bool isDialog, bool enabled)
    {
        if ((style & 0x40000000) != 0) return "child_window";
        if ((style & 0x00C00000) != 0x00C00000) return "missing_caption";
        if ((style & 0x00080000) == 0) return "missing_system_menu";
        if ((style & 0x00020000) == 0) return "not_minimizable";
        if ((extendedStyle & 0x00000080) != 0) return "tool_window";
        if ((extendedStyle & 0x08000000) != 0) return "noactivate_window";
        if ((extendedStyle & 0x00000008) != 0) return "topmost_window";
        if ((extendedStyle & 0x00080000) != 0) return "layered_target";
        if (hasOwner) return "owned_window";
        if (isDialog) return "dialog_window";
        return enabled ? null : "disabled_window";
    }

    public static bool IsProvablyNonCovering(bool layered, bool attributesRead,
        uint attributeFlags, byte alpha, int regionType, bool regionIntersects)
    {
        // WS_EX_TRANSPARENT affects paint order, not visual opacity. A layered
        // window without readable global alpha may still contain opaque pixels.
        var globallyTransparent = layered && attributesRead && (attributeFlags & 2) != 0 && alpha == 0;
        var emptyRegion = regionType == 1;
        var outsideKnownRegion = regionType is 2 or 3 && !regionIntersects;
        return globallyTransparent || emptyRegion || outsideKnownRegion;
    }

    public static Rectangle ToWindowRegionCoordinates(Rectangle screenIntersection, Rectangle outerBounds) =>
        new(screenIntersection.X - outerBounds.X, screenIntersection.Y - outerBounds.Y,
            screenIntersection.Width, screenIntersection.Height);

    public static bool IsConfirmedMotion(Rectangle initialOuter, Rectangle expectedOuter,
        Rectangle actualOuter, bool minimized) =>
        !minimized && actualOuter.Size == initialOuter.Size &&
        WindowPrankGeometry.SameBounds(expectedOuter, actualOuter) &&
        !WindowPrankGeometry.SameBounds(initialOuter, actualOuter);

    public static bool IsConfirmedRestore(bool minimizeConfirmed, bool minimized,
        bool placementMatches, Rectangle initialOuter, Rectangle actualOuter) =>
        minimizeConfirmed && !minimized && placementMatches &&
        WindowPrankGeometry.SameBounds(initialOuter, actualOuter);
}
