namespace SoftMochiPet.Core;

/// <summary>
/// Separates solid application windows from transient capture and input overlays.
/// Those overlays often cover a whole monitor and animate their bounds while a
/// recording starts, so treating them as terrain makes the pet bounce or fall.
/// </summary>
public static class WindowTerrainEligibilityPolicy
{
    public const long WsExTopmost = 0x00000008L;
    public const long WsExTransparent = 0x00000020L;
    public const long WsExLayered = 0x00080000L;
    public const long WsCaption = 0x00C00000L;
    public const long WsPopup = unchecked((long)0x80000000);

    private static readonly string[] CaptureIdentityFragments =
    [
        "录屏",
        "屏幕录制",
        "屏幕录像",
        "截屏",
        "截图",
        "屏幕截图",
        "屏幕截取",
        "ScreenCapture",
        "Screen Capture",
        "ScreenRecord",
        "Screen Record",
        "Screenshot",
        "NVIDIA GeForce Overlay",
        "Windows Input Experience",
        "Windows 输入体验",
    ];

    private static readonly string[] KnownDesktopOverlayClasses =
    [
        "CEF-OSC-WIDGET",
        "Windows.UI.Core.CoreWindow",
    ];

    /// <summary>
    /// Returns whether a native class name resembles a known screen-capture
    /// surface. This is identity evidence only; it never proves that a
    /// window is visually transparent.
    /// </summary>
    public static bool ClassIdentifiesCapture(string? className)
    {
        var value = className ?? string.Empty;
        return CaptureIdentityFragments.Any(fragment =>
            value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns whether a native title resembles a known screen-capture
    /// surface. This is identity evidence only; it never permits an
    /// occlusion bypass by itself.
    /// </summary>
    public static bool TitleIdentifiesCapture(string? windowTitle)
    {
        var value = windowTitle ?? string.Empty;
        return CaptureIdentityFragments.Any(fragment =>
            value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsKnownDesktopOverlay(string? className, string? windowTitle)
    {
        className ??= string.Empty;
        windowTitle ??= string.Empty;
        return KnownDesktopOverlayClasses.Any(value => className.Equals(value, StringComparison.OrdinalIgnoreCase)) ||
            windowTitle.Equals("NVIDIA GeForce Overlay", StringComparison.OrdinalIgnoreCase) ||
            windowTitle.Equals("Windows 输入体验", StringComparison.OrdinalIgnoreCase) ||
            windowTitle.Equals("Windows Input Experience", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldIgnoreOverlay(
        string? className,
        string? windowTitle,
        long extendedStyle,
        long style)
    {
        className ??= string.Empty;
        windowTitle ??= string.Empty;

        if ((extendedStyle & WsExTransparent) != 0)
        {
            return true;
        }

        var classIdentifiesCapture = ClassIdentifiesCapture(className);
        if (classIdentifiesCapture)
        {
            return true;
        }

        var layered = (extendedStyle & WsExLayered) != 0;
        var topmost = (extendedStyle & WsExTopmost) != 0;
        var hasCaption = (style & WsCaption) != 0;
        var titleIdentifiesCapture = TitleIdentifiesCapture(windowTitle);
        if (titleIdentifiesCapture && (layered || topmost || !hasCaption))
        {
            return true;
        }

        if (layered && topmost && !hasCaption)
        {
            return true;
        }

        // These are known system/capture surfaces seen on the desktop as
        // full-screen borderless layers. Their native windows are not user
        // content and must not block an otherwise visible foreground target.
        // Keep the check identity-specific and require popup/layered styling;
        // an ordinary titled application is never covered by this rule.
        if (IsKnownDesktopOverlay(className, windowTitle) && !hasCaption &&
            (layered || (style & WsPopup) != 0))
        {
            return true;
        }

        return string.IsNullOrWhiteSpace(windowTitle) && topmost &&
            className.StartsWith("Chrome_WidgetWin_", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// A narrow allow-list for click-through/capture layers when checking a
    /// visible prank target. Ordinary windows that merely have a recording word
    /// in their title are never included.
    /// </summary>
    public static bool IsLikelyCaptureOverlay(
        string? className, string? windowTitle, long extendedStyle, long style)
    {
        if (!ShouldIgnoreOverlay(className, windowTitle, extendedStyle, style)) return false;
        var clickThrough = (extendedStyle & WsExTransparent) != 0;
        var borderlessLayer = (extendedStyle & WsExLayered) != 0 &&
            (extendedStyle & WsExTopmost) != 0 && (style & WsCaption) == 0;
        var knownDesktopLayer = IsKnownDesktopOverlay(className, windowTitle) &&
            (style & WsCaption) == 0 &&
            ((extendedStyle & WsExLayered) != 0 || (style & WsPopup) != 0);
        return clickThrough || borderlessLayer || knownDesktopLayer;
    }
}
