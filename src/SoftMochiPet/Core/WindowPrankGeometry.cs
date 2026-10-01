using System.Drawing;

namespace SoftMochiPet.Core;

public enum WindowPrankMotionKind { Kick, Punch, Charge }
public enum WindowPrankVisualKind { Hat, Shatter, Tear }

/// <summary>All coordinates are physical desktop pixels, including negative monitor origins.</summary>
public static class WindowPrankGeometry
{
    public static bool IsEligible(
        bool visible, bool minimized, bool maximized, bool foreground, bool ownProcess,
        bool ordinaryApp, bool responsive, bool protectedContent, bool fullyOnMonitor,
        bool unoccluded) =>
        visible && !minimized && !maximized && !ownProcess && ordinaryApp &&
        responsive && !protectedContent && fullyOnMonitor && unoccluded;

    public static double MotionDuration(WindowPrankMotionKind kind) => kind switch
    {
        WindowPrankMotionKind.Punch => 0.24,
        WindowPrankMotionKind.Charge => 0.55,
        _ => 0.42,
    };

    public static Rectangle MotionDestination(Rectangle visible, Rectangle workArea, WindowPrankMotionKind kind, int direction)
    {
        var fraction = kind switch { WindowPrankMotionKind.Punch => 0.085, WindowPrankMotionKind.Charge => 0.22, _ => 0.15 };
        var mass = Math.Clamp(Math.Sqrt((double)visible.Width * visible.Height / (900 * 600)), 0.7, 1.6);
        var displacement = (int)Math.Round(workArea.Width * fraction / mass) * (direction < 0 ? -1 : 1);
        var left = Math.Clamp(visible.Left + displacement, workArea.Left, Math.Max(workArea.Left, workArea.Right - visible.Width));
        var top = Math.Clamp(visible.Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - visible.Height));
        return new Rectangle(left, top, visible.Width, visible.Height);
    }

    public static Rectangle AtMotionProgress(Rectangle start, Rectangle end, double progress)
    {
        var t = Math.Clamp(progress, 0, 1);
        // Contact supplies the impulse immediately; the remaining motion only
        // loses speed. No overshoot can push a real window beyond its safe end.
        var eased = 1 - Math.Pow(1 - t, 5);
        return new Rectangle((int)Math.Round(start.X + (end.X - start.X) * eased),
            (int)Math.Round(start.Y + (end.Y - start.Y) * eased), start.Width, start.Height);
    }

    public static Rectangle ToOuterBounds(Rectangle nextVisible, Rectangle originalVisible, Rectangle originalOuter) =>
        new(originalOuter.X + nextVisible.X - originalVisible.X, originalOuter.Y + nextVisible.Y - originalVisible.Y,
            originalOuter.Width, originalOuter.Height);

    public static bool SameBounds(Rectangle first, Rectangle second, int tolerance = 2) =>
        Math.Abs(first.Left - second.Left) <= tolerance && Math.Abs(first.Top - second.Top) <= tolerance &&
        Math.Abs(first.Right - second.Right) <= tolerance && Math.Abs(first.Bottom - second.Bottom) <= tolerance;

    public static bool ShouldRelinquishSnapshot(bool restoring, bool iconic, bool placementUnchanged) =>
        !placementUnchanged || (!restoring && !iconic);

    public static bool HasUsefulPixels(byte[] pixels, int stride, int width, int height)
    {
        if (width < 1 || height < 1 || stride < width * 4 || pixels.Length < (long)stride * height)
            return false;
        var min = 255;
        var max = 0;
        var samples = 0;
        var dark = 0;
        for (var y = height / 10; y < height * 9 / 10; y += Math.Max(1, height / 32))
        for (var x = width / 10; x < width * 9 / 10; x += Math.Max(1, width / 32))
        {
            var i = y * stride + x * 4;
            var light = (pixels[i] + pixels[i + 1] + pixels[i + 2]) / 3;
            min = Math.Min(min, light);
            max = Math.Max(max, light);
            if (light < 9) dark++;
            samples++;
        }
        return samples > 0 && dark < samples * 0.98 && max - min >= 5;
    }

    public static PointF[][] CreateShards(float width, float height)
    {
        const int columns = 4, rows = 4;
        var grid = new PointF[columns + 1, rows + 1];
        for (var y = 0; y <= rows; y++)
        for (var x = 0; x <= columns; x++)
        {
            var jx = x == 0 || x == columns ? 0 : MathF.Sin(x * 7.1f + y * 3.7f) * 0.18f;
            var jy = y == 0 || y == rows ? 0 : MathF.Sin(x * 3.1f + y * 5.3f) * 0.18f;
            grid[x, y] = new PointF((x + jx) * width / columns, (y + jy) * height / rows);
        }
        var result = new List<PointF[]>();
        for (var y = 0; y < rows; y++)
        for (var x = 0; x < columns; x++)
            result.Add([grid[x, y], grid[x + 1, y], grid[x + 1, y + 1], grid[x, y + 1]]);
        return result.ToArray();
    }

    public static PointF[] CreateTearSeam(float width, float height, float? contactY = null)
    {
        var result = new PointF[13];
        var center = Math.Clamp(contactY ?? height / 2, height * 0.08f, height * 0.92f);
        var amplitude = Math.Min(height * 0.045f, Math.Min(center, height - center) * 0.4f);
        for (var i = 0; i < result.Length; i++)
            result[i] = new PointF(width * (1 - i / 12f), center + (i == 0 || i == 12 ? 0 : MathF.Sin(i * 2.4f) * amplitude));
        return result;
    }

    public static PointF ClampImpact(PointF contact, RectangleF source) => new(
        Math.Clamp(contact.X, source.Left, source.Right), Math.Clamp(contact.Y, source.Top, source.Bottom));

    public static PointF ShatterOffset(PointF center, PointF impact, double progress)
    {
        var burst = Math.Clamp((progress - 0.10) / 0.9, 0, 1);
        var distance = 1 - Math.Pow(1 - burst, 2);
        return new PointF((float)((center.X - impact.X) * 0.58 * distance),
            (float)((center.Y - impact.Y) * 0.44 * distance + 55 * burst * burst));
    }

}
