namespace SoftMochiPet.Core;

public static class SubpixelMotion
{
    public static int TakeWholePixels(double movement, ref double remainder)
    {
        var accumulated = movement + remainder;
        var wholePixels = (int)Math.Truncate(accumulated);
        remainder = accumulated - wholePixels;
        return wholePixels;
    }
}
