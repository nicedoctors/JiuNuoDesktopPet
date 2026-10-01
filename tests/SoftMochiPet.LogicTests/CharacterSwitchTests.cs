using System.Reflection;
using SoftMochiPet.Core;

internal static class CharacterSwitchTests
{
    public static void SwitchWaitsForDragAndMealCompletion()
    {
        var policy = typeof(SoftMochiPet.MainWindow).GetMethod(
            "CanApplyCharacterChange", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Character switch safety policy was not found.");
        foreach (var state in Enum.GetValues<PetState>())
        {
            var actual = (bool)policy.Invoke(null, [state, false])!;
            var expected = state is not (PetState.Dragging or PetState.Pinching or PetState.Chomping or PetState.Satisfied);
            Assert(actual == expected,
                $"Switching must preserve active drag capture and atomic meal completion: {state}.");
            Assert(!(bool)policy.Invoke(null, [state, true])!,
                "An active mouse capture must defer switching even before the visible state catches up.");
        }
    }

    public static void SuctionAnchorsPreserveLegacyPathAndFollowCharacterGeometry()
    {
        var resolver = typeof(SoftMochiPet.MainWindow).GetMethod(
            "GetFoodTrajectoryAnchors", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Character food trajectory resolver was not found.");
        var legacy = Resolve(PetCharacterProfile.Nuonuo.Geometry);
        Assert(legacy.StartX == 18 && legacy.StartY == 240 && Math.Abs(legacy.MiddleX - 123) < 0.000001 &&
            legacy.EndX == 164 && legacy.EndY == 277,
            "The original Nuonuo inline icon and ghost must retain their exact calibrated suction path.");
        var geometry = PetCharacterProfile.Nuonuo.Geometry with
        {
            MouthCanvasX = 100,
            MouthCanvasY = 200,
            SuctionMouthCanvasX = 330,
            SuctionMouthCanvasY = 320,
        };
        var custom = Resolve(geometry);
        Assert(custom.StartX == 73 && custom.StartY == 173 && custom.EndX == 303 && custom.EndY == 293 &&
            Math.Abs(custom.MiddleX - (73 + 230 * 105.0 / 146)) < 0.000001,
            "Every character's food trajectory must start at its waiting icon and finish inside its own mouth.");

        (double StartX, double StartY, double MiddleX, double EndX, double EndY) Resolve(PetCharacterGeometry value) =>
            ((double, double, double, double, double))resolver.Invoke(null, [value])!;
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
