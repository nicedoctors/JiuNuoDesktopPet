namespace SoftMochiPet.Core;

public static class PetModePolicy
{
    public static double ResolveHunger(double hunger, bool fastingMode)
    {
        if (fastingMode)
        {
            return 0;
        }

        return double.IsFinite(hunger) ? Math.Clamp(hunger, 0, 100) : 36;
    }

    public static bool AllowsAutonomousAction(
        AutonomousAction action,
        bool fastingMode,
        bool quietMode)
    {
        if (quietMode)
        {
            return action is AutonomousAction.Rest or AutonomousAction.Sleep;
        }

        return !fastingMode || action is not (
            AutonomousAction.AskForFood or
            AutonomousAction.LickIcon or
            AutonomousAction.Roll);
    }

    public static bool AllowsDeletionMeal(bool quietMode) => !quietMode;

    public static bool ShouldShowProgressDisplay(
        PetCharacterProfile character, bool fastingMode, bool infiniteMode, bool coexistenceMode) =>
        !infiniteMode && !(fastingMode && (coexistenceMode || character.SupportsFood));
}
