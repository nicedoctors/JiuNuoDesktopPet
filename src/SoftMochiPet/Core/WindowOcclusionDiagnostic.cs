namespace SoftMochiPet.Core;

/// <summary>
/// Restricted, non-bypass evidence recorded when a window intersects a prank
/// target. The record intentionally contains no native handle, process id,
/// class text, or title text; those values are not needed to diagnose the
/// capture-overlay question and may contain user data.
/// </summary>
public sealed record WindowOcclusionDiagnostic(
    bool ClassCaptureIdentity,
    bool TitleCaptureIdentity,
    long ExtendedStyle,
    long Style,
    bool Layered,
    bool LayeredAttributesRead,
    uint LayeredAttributeFlags,
    byte GlobalAlpha,
    bool RegionQueried,
    int RegionType,
    bool RegionIntersects,
    bool ProvablyNonCovering,
    bool TerrainOverlayLike,
    string Classification)
{
    public static WindowOcclusionDiagnostic Classify(
        string? className,
        string? windowTitle,
        long extendedStyle,
        long style,
        bool layeredAttributesRead,
        uint layeredAttributeFlags,
        byte globalAlpha,
        bool regionQueried,
        int regionType,
        bool regionIntersects)
    {
        var layered = (extendedStyle & WindowTerrainEligibilityPolicy.WsExLayered) != 0;
        var classCapture = WindowTerrainEligibilityPolicy.ClassIdentifiesCapture(className);
        var titleCapture = WindowTerrainEligibilityPolicy.TitleIdentifiesCapture(windowTitle);
        var provablyNonCovering = WindowPrankSelectionPolicy.IsProvablyNonCovering(
            layered,
            layeredAttributesRead,
            layeredAttributeFlags,
            globalAlpha,
            regionQueried ? regionType : 0,
            regionQueried ? regionIntersects : true);
        var terrainOverlayLike = WindowTerrainEligibilityPolicy.ShouldIgnoreOverlay(
            className, windowTitle, extendedStyle, style);
        var classification = provablyNonCovering
            ? "proven_noncovering"
            : classCapture || titleCapture
                ? "capture_identity_blocker"
                : terrainOverlayLike
                    ? "overlay_identity_blocker"
                    : "ordinary_blocker";

        return new WindowOcclusionDiagnostic(
            classCapture,
            titleCapture,
            extendedStyle,
            style,
            layered,
            layeredAttributesRead,
            layeredAttributeFlags,
            globalAlpha,
            regionQueried,
            regionType,
            regionIntersects,
            provablyNonCovering,
            terrainOverlayLike,
            classification);
    }
}
