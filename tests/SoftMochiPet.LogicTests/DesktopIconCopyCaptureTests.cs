using System.Drawing;
using SoftMochiPet.Services;

internal static class DesktopIconCopyCaptureTests
{
    public static void BoundsAreSmallFiniteAndAllowNegativeMonitorCoordinates()
    {
        Assert(new[]
        {
            new Rectangle(0, 0, 8, 8),
            new Rectangle(100, 100, 256, 256),
            new Rectangle(-1920, -1080, 64, 48),
            new Rectangle(int.MinValue, int.MinValue, 8, 8),
            new Rectangle(int.MaxValue - 256, int.MaxValue - 256, 256, 256),
        }.All(DesktopIconCopyCapture.HasSafeBounds),
            "Normal icons and negative-position monitors must be accepted when capture dimensions and edges are bounded.");
        Assert(new[]
        {
            Rectangle.Empty,
            new Rectangle(0, 0, 7, 64),
            new Rectangle(0, 0, 64, 7),
            new Rectangle(0, 0, 257, 64),
            new Rectangle(0, 0, 64, 257),
            new Rectangle(0, 0, -32, 32),
            new Rectangle(0, 0, 32, -32),
            new Rectangle(int.MaxValue - 7, 0, 8, 8),
            new Rectangle(0, int.MaxValue - 7, 8, 8),
            new Rectangle(0, 0, int.MaxValue, int.MaxValue),
        }.All(bounds => !DesktopIconCopyCapture.HasSafeBounds(bounds)),
            "Empty, negative, oversized or overflowing rectangles must be rejected before screen capture.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
