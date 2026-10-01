namespace SoftMochiPet.Core;

public static class SpriteGeometry
{
    public const double CanvasSize = 512;
    public const double RuntimeFrameSize = 384;
    public const double RuntimeFootBaseline = 374;
    public const double FootCanvasX = CanvasSize / 2;
    public const double FootCanvasY = RuntimeFootBaseline / RuntimeFrameSize * CanvasSize;
}
