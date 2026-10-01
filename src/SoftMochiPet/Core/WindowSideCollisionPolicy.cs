namespace SoftMochiPet.Core;

public readonly record struct WindowBodySnapshot(
    IntPtr SourceHandle,
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public int Width => Math.Max(0, Right - Left);
    public int Height => Math.Max(0, Bottom - Top);
}

public readonly record struct PetCollisionBounds(
    double Left,
    double Top,
    double Right,
    double Bottom);

public readonly record struct WindowSideImpact(
    IntPtr SourceHandle,
    int Direction,
    double EdgeVelocityX,
    double CorrectionX,
    double PetVelocityX,
    double PetVelocityY);

/// <summary>
/// Resolves a horizontal window-edge sweep against the pet's visible body.
/// Coordinates and velocities use physical pixels so mixed-DPI monitors behave
/// consistently with the rest of the terrain system.
/// </summary>
public static class WindowSideCollisionPolicy
{
    public static WindowSideImpact? Resolve(
        WindowBodySnapshot previous,
        WindowBodySnapshot current,
        PetCollisionBounds pet,
        double elapsedSeconds,
        double scale)
    {
        if (previous.SourceHandle == IntPtr.Zero ||
            previous.SourceHandle != current.SourceHandle ||
            previous.Width <= 0 || previous.Height <= 0 ||
            current.Width <= 0 || current.Height <= 0 ||
            elapsedSeconds <= 0 || elapsedSeconds > 0.5)
        {
            return null;
        }

        scale = Math.Max(0.5, scale);
        // A shallow corner contact is still a side hit as long as it reaches
        // the visible body. Feet are excluded by the caller's external hitbox,
        // so standing on a top edge cannot be misclassified here.
        var requiredVerticalOverlap = 4 * scale;
        var verticalOverlap = Math.Max(
            Overlap(previous.Top, previous.Bottom, pet.Top, pet.Bottom),
            Overlap(current.Top, current.Bottom, pet.Top, pet.Bottom));
        if (verticalOverlap < requiredVerticalOverlap)
        {
            return null;
        }

        var contactTolerance = 3 * scale;
        var minimumEdgeSpeed = 22 * scale;
        var rightEdgeDelta = current.Right - previous.Right;
        var leftEdgeDelta = current.Left - previous.Left;

        WindowSideImpact? rightImpact = null;
        var rightEdgeVelocity = rightEdgeDelta / elapsedSeconds;
        if (rightEdgeDelta > 0 && rightEdgeVelocity >= minimumEdgeSpeed &&
            previous.Right <= pet.Left + contactTolerance &&
            current.Right >= pet.Left - contactTolerance)
        {
            var maximumCorrection = (pet.Right - pet.Left) * 0.45;
            rightImpact = CreateImpact(
                current.SourceHandle,
                1,
                rightEdgeVelocity,
                Math.Min(
                    maximumCorrection,
                    Math.Max(0, current.Right - pet.Left + 2 * scale)),
                scale);
        }

        WindowSideImpact? leftImpact = null;
        var leftEdgeVelocity = leftEdgeDelta / elapsedSeconds;
        if (leftEdgeDelta < 0 && leftEdgeVelocity <= -minimumEdgeSpeed &&
            previous.Left >= pet.Right - contactTolerance &&
            current.Left <= pet.Right + contactTolerance)
        {
            var maximumCorrection = (pet.Right - pet.Left) * 0.45;
            leftImpact = CreateImpact(
                current.SourceHandle,
                -1,
                leftEdgeVelocity,
                Math.Max(
                    -maximumCorrection,
                    Math.Min(0, current.Left - pet.Right - 2 * scale)),
                scale);
        }

        if (rightImpact is null)
        {
            return leftImpact;
        }

        if (leftImpact is null)
        {
            return rightImpact;
        }

        return Math.Abs(rightImpact.Value.EdgeVelocityX) >= Math.Abs(leftImpact.Value.EdgeVelocityX)
            ? rightImpact
            : leftImpact;
    }

    private static WindowSideImpact CreateImpact(
        IntPtr sourceHandle,
        int direction,
        double edgeVelocityX,
        double correctionX,
        double scale)
    {
        var speed = Math.Min(Math.Abs(edgeVelocityX), 1100 * scale);
        var horizontalSpeed = Math.Clamp(
            speed * 0.92 + 100 * scale,
            230 * scale,
            1450 * scale);
        var upwardSpeed = Math.Clamp(
            105 * scale + speed * 0.035,
            125 * scale,
            220 * scale);
        return new WindowSideImpact(
            sourceHandle,
            direction,
            direction * speed,
            correctionX,
            direction * horizontalSpeed,
            -upwardSpeed);
    }

    private static double Overlap(double firstStart, double firstEnd, double secondStart, double secondEnd)
    {
        return Math.Max(0, Math.Min(firstEnd, secondEnd) - Math.Max(firstStart, secondStart));
    }
}
