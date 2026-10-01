namespace SoftMochiPet.Core;

public readonly record struct FrameTiming(double SimulationSeconds, double AnimationSeconds)
{
    public static FrameTiming FromElapsed(double elapsedSeconds, double maximumAnimationStep = 0.05)
    {
        var simulationSeconds = Math.Max(0, elapsedSeconds);
        var animationSeconds = Math.Clamp(simulationSeconds, 0, maximumAnimationStep);
        return new FrameTiming(simulationSeconds, animationSeconds);
    }
}
