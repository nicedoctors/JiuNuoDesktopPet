namespace SoftMochiPet.Core;

public readonly record struct WindowEnclosureStep(
    double OffsetX,
    double OffsetY,
    double VelocityX,
    double VelocityY,
    bool HitLeft,
    bool HitTop,
    bool HitRight,
    bool HitBottom,
    bool IsResting,
    double ImpactSpeed)
{
    public bool HitHorizontal => HitLeft || HitRight;
    public bool HitVertical => HitTop || HitBottom;
    public bool HitAny => HitHorizontal || HitVertical;
}

/// <summary>
/// Simulates the pet as a free body inside a moving window rectangle. The pet
/// remains inertial until a wall reaches it, then bounces relative to that wall.
/// </summary>
public static class WindowEnclosurePhysics
{
    public static WindowEnclosureStep Resolve(
        PetCollisionBounds pet,
        WindowBodySnapshot previousBox,
        WindowBodySnapshot currentBox,
        double velocityX,
        double velocityY,
        double elapsedSeconds,
        double scale)
    {
        if (elapsedSeconds <= 0 || currentBox.Width <= 0 || currentBox.Height <= 0)
        {
            return new WindowEnclosureStep(0, 0, velocityX, velocityY,
                false, false, false, false, false, 0);
        }

        var delta = Math.Min(elapsedSeconds, 0.08);
        scale = Math.Max(0.5, scale);
        var inset = 6 * scale;
        var leftBoundary = currentBox.Left + inset;
        var topBoundary = currentBox.Top + inset;
        var rightBoundary = currentBox.Right - inset;
        var bottomBoundary = currentBox.Bottom - inset;
        var petWidth = pet.Right - pet.Left;
        var petHeight = pet.Bottom - pet.Top;

        velocityX *= Math.Exp(-0.32 * delta);
        velocityY = Math.Min(1400 * scale, velocityY + 1650 * scale * delta);
        var offsetX = velocityX * delta;
        var offsetY = velocityY * delta;

        var maximumWallVelocityX = 900 * scale;
        var maximumWallVelocityY = 850 * scale;
        var leftWallVelocity = Math.Clamp(
            (currentBox.Left - previousBox.Left) / delta,
            -maximumWallVelocityX,
            maximumWallVelocityX);
        var rightWallVelocity = Math.Clamp(
            (currentBox.Right - previousBox.Right) / delta,
            -maximumWallVelocityX,
            maximumWallVelocityX);
        var topWallVelocity = Math.Clamp(
            (currentBox.Top - previousBox.Top) / delta,
            -maximumWallVelocityY,
            maximumWallVelocityY);
        var bottomWallVelocity = Math.Clamp(
            (currentBox.Bottom - previousBox.Bottom) / delta,
            -maximumWallVelocityY,
            maximumWallVelocityY);
        var hitLeft = false;
        var hitTop = false;
        var hitRight = false;
        var hitBottom = false;
        var isResting = false;
        var impactSpeed = 0.0;

        if (rightBoundary - leftBoundary < petWidth)
        {
            offsetX = (leftBoundary + rightBoundary - pet.Left - pet.Right) / 2;
            velocityX = (leftWallVelocity + rightWallVelocity) / 2;
            hitLeft = true;
            hitRight = true;
        }
        else
        {
            var nextLeft = pet.Left + offsetX;
            var nextRight = pet.Right + offsetX;
            if (nextLeft < leftBoundary)
            {
                offsetX += leftBoundary - nextLeft;
                var relativeVelocity = velocityX - leftWallVelocity;
                impactSpeed = Math.Max(impactSpeed, Math.Abs(relativeVelocity));
                if (relativeVelocity < 0)
                {
                    velocityX = leftWallVelocity - relativeVelocity * 0.68;
                }
                hitLeft = true;
            }
            else if (nextRight > rightBoundary)
            {
                offsetX -= nextRight - rightBoundary;
                var relativeVelocity = velocityX - rightWallVelocity;
                impactSpeed = Math.Max(impactSpeed, Math.Abs(relativeVelocity));
                if (relativeVelocity > 0)
                {
                    velocityX = rightWallVelocity - relativeVelocity * 0.68;
                }
                hitRight = true;
            }
        }

        if (bottomBoundary - topBoundary < petHeight)
        {
            offsetY = (topBoundary + bottomBoundary - pet.Top - pet.Bottom) / 2;
            velocityY = (topWallVelocity + bottomWallVelocity) / 2;
            hitTop = true;
            hitBottom = true;
        }
        else
        {
            var nextTop = pet.Top + offsetY;
            var nextBottom = pet.Bottom + offsetY;
            if (nextTop < topBoundary)
            {
                offsetY += topBoundary - nextTop;
                var relativeVelocity = velocityY - topWallVelocity;
                impactSpeed = Math.Max(impactSpeed, Math.Abs(relativeVelocity));
                if (relativeVelocity < 0)
                {
                    velocityY = topWallVelocity - relativeVelocity * 0.58;
                }
                hitTop = true;
            }
            else if (nextBottom > bottomBoundary)
            {
                offsetY -= nextBottom - bottomBoundary;
                var relativeVelocity = velocityY - bottomWallVelocity;
                impactSpeed = Math.Max(impactSpeed, Math.Abs(relativeVelocity));
                if (relativeVelocity > 0 &&
                    relativeVelocity < 115 * scale &&
                    Math.Abs(bottomWallVelocity) < 90 * scale)
                {
                    velocityY = bottomWallVelocity;
                    isResting = true;
                }
                else if (relativeVelocity > 0)
                {
                    velocityY = bottomWallVelocity - relativeVelocity * 0.48;
                }
                hitBottom = true;
            }
        }

        if (isResting)
        {
            velocityX *= Math.Exp(-2.2 * delta);
        }
        velocityX = Math.Clamp(velocityX, -1350 * scale, 1350 * scale);
        velocityY = Math.Clamp(velocityY, -1200 * scale, 1200 * scale);
        return new WindowEnclosureStep(
            offsetX,
            offsetY,
            velocityX,
            velocityY,
            hitLeft,
            hitTop,
            hitRight,
            hitBottom,
            isResting,
            impactSpeed);
    }
}
