namespace SoftMochiPet.Core;

public enum TerrainEdgeAction
{
    TurnAround,
    Hop,
    SlideDown,
    JumpDown,
}

public static class TerrainBehaviorPlanner
{
    public static TerrainEdgeAction ChooseAtEdge(bool isDesktopFloor, double randomUnit)
    {
        var random = Math.Clamp(randomUnit, 0, 0.999999);
        if (isDesktopFloor)
        {
            return random < 0.22 ? TerrainEdgeAction.Hop : TerrainEdgeAction.TurnAround;
        }

        return random switch
        {
            < 0.20 => TerrainEdgeAction.Hop,
            < 0.34 => TerrainEdgeAction.SlideDown,
            < 0.48 => TerrainEdgeAction.JumpDown,
            _ => TerrainEdgeAction.TurnAround,
        };
    }
}
