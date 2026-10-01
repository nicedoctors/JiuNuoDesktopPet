namespace SoftMochiPet.Core;

public static class LifeBehaviorPolicy
{
    public const double MaximumIdleSeconds = 5;
    public const double MinimumNapSeconds = 7.5;
    public const double PatrolNeedCheckSeconds = 5;
    public const double HungerReactionCooldownSeconds = 45;
    public const double SleepInterruptionThreshold = 62;
    public const double LickInterruptionThreshold = 62;
    public const double RollInterruptionThreshold = 65;
    public const double FoodAppealInterruptionThreshold = 70;

    public static bool IsHungerReaction(AutonomousAction action) =>
        action is AutonomousAction.AskForFood or AutonomousAction.LickIcon or AutonomousAction.Roll;

    public static bool IsHungerReactionReady(
        AutonomousAction action,
        double now,
        double nextHungerReactionAt) =>
        !IsHungerReaction(action) || now >= nextHungerReactionAt;
}
