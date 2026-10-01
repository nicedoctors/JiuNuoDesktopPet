using SoftMochiPet.Core;

internal static class WindowOcclusionDiagnosticTests
{
    public static void CaptureIdentityIsRecordedWithoutAnOcclusionBypass()
    {
        var diagnostic = WindowOcclusionDiagnostic.Classify(
            "QQScreenCaptureWnd",
            "",
            WindowTerrainEligibilityPolicy.WsExLayered | WindowTerrainEligibilityPolicy.WsExTopmost,
            0,
            layeredAttributesRead: true,
            layeredAttributeFlags: 2,
            globalAlpha: 128,
            regionQueried: true,
            regionType: 2,
            regionIntersects: true);

        Assert(diagnostic.ClassCaptureIdentity && !diagnostic.TitleCaptureIdentity,
            "Capture identity should be reduced to booleans without retaining class text.");
        Assert(diagnostic.Layered && diagnostic.LayeredAttributesRead && diagnostic.GlobalAlpha == 128,
            "Layered alpha evidence must be retained for diagnosis.");
        Assert(!diagnostic.ProvablyNonCovering && diagnostic.Classification == "capture_identity_blocker",
            "A visible capture overlay remains a blocker when its alpha is nonzero.");
    }

    public static void TitleIdentityIsRecordedWithoutExposingWindowText()
    {
        var diagnostic = WindowOcclusionDiagnostic.Classify(
            "OverlayHost",
            "QQ Screen Recording",
            WindowTerrainEligibilityPolicy.WsExTopmost,
            WindowTerrainEligibilityPolicy.WsCaption,
            layeredAttributesRead: false,
            layeredAttributeFlags: 0,
            globalAlpha: 255,
            regionQueried: false,
            regionType: 0,
            regionIntersects: true);

        Assert(!diagnostic.ClassCaptureIdentity && diagnostic.TitleCaptureIdentity,
            "Capture title identity should be recorded separately from class identity.");
        Assert(!diagnostic.ProvablyNonCovering && diagnostic.Classification == "capture_identity_blocker",
            "Capture title text alone must not bypass visible occlusion.");
    }

    public static void OnlyProvenTransparencyChangesOcclusionClassification()
    {
        var globalAlphaZero = WindowOcclusionDiagnostic.Classify(
            "LayeredOverlay",
            "",
            WindowTerrainEligibilityPolicy.WsExLayered,
            0,
            layeredAttributesRead: true,
            layeredAttributeFlags: 2,
            globalAlpha: 0,
            regionQueried: false,
            regionType: 0,
            regionIntersects: true);
        var emptyRegion = WindowOcclusionDiagnostic.Classify(
            "Overlay",
            "",
            0,
            0,
            layeredAttributesRead: false,
            layeredAttributeFlags: 0,
            globalAlpha: 255,
            regionQueried: true,
            regionType: 1,
            regionIntersects: false);

        Assert(globalAlphaZero.ProvablyNonCovering && globalAlphaZero.Classification == "proven_noncovering",
            "A successfully read global alpha of zero is safe non-coverage evidence.");
        Assert(emptyRegion.ProvablyNonCovering && emptyRegion.Classification == "proven_noncovering",
            "An empty native region is safe non-coverage evidence.");
    }

    public static void TransparentStyleAndTerrainIdentityNeverProveCoverageIsAbsent()
    {
        var transparentStyle = WindowOcclusionDiagnostic.Classify(
            "RecorderToolbar",
            "",
            WindowTerrainEligibilityPolicy.WsExTransparent,
            0,
            layeredAttributesRead: false,
            layeredAttributeFlags: 0,
            globalAlpha: 255,
            regionQueried: false,
            regionType: 0,
            regionIntersects: true);

        Assert(transparentStyle.TerrainOverlayLike && !transparentStyle.ProvablyNonCovering,
            "WS_EX_TRANSPARENT changes paint order, not visual coverage; it must not bypass occlusion.");
        Assert(transparentStyle.Classification == "overlay_identity_blocker",
            "Unproven terrain identity remains a diagnosable blocker.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
