using SoftMochiPet.Models;

namespace SoftMochiPet.Core;

public enum AutonomousAction
{
    Rest,
    Wander,
    Explore,
    Sleep,
    AskForFood,
    LickIcon,
    Roll,
    Hop,
}

public static class AutonomousBehaviorPlanner
{
    public static AutonomousAction Choose(
        PetLifeState life,
        bool hasGroundSupport,
        IReadOnlySet<AutonomousAction> availableActions,
        double randomUnit,
        PetCharacterProfile? profile = null)
    {
        var isFeibiJiubi = profile?.Id == PetCharacterProfile.FeibiJiubi.Id;
        var supportsFood = profile?.SupportsFood ?? true;
        var energy = Math.Clamp((75 - life.Sleepiness) / 35, 0, 1);
        var tiredMovementScale = isFeibiJiubi
            ? 1 - Math.Clamp((life.Sleepiness - 75) / 25, 0, 1) * 0.45
            : 1;
        var choices = new List<(AutonomousAction Action, double Weight)>();
        Add(AutonomousAction.Rest, 2.2 + (supportsFood ? Math.Max(0, life.Fullness - 68) / 24 : 0));

        if (hasGroundSupport)
        {
            Add(AutonomousAction.Wander, (1.1 + life.Curiosity / 85) *
                (isFeibiJiubi ? 1 + 0.12 * energy : 1) * tiredMovementScale);
            Add(AutonomousAction.Explore, (0.25 + Math.Max(0, life.Curiosity - 30) / 13) *
                (isFeibiJiubi ? 1 + 0.45 * energy : 1) * tiredMovementScale);
        }

        // A short nap is also an ordinary idle variation. Sleepiness raises its
        // probability, but even a rested pet can occasionally choose it.
        Add(AutonomousAction.Sleep, 0.75 + Math.Max(0, life.Sleepiness - 43) / 9);
        if (supportsFood)
        {
            Add(AutonomousAction.AskForFood, Math.Max(0, life.Hunger - 48) / 10);
            Add(AutonomousAction.LickIcon, 0.16 + Math.Max(0, life.Hunger - 35) / 24);
            Add(AutonomousAction.Roll, Math.Max(0, life.Hunger - 58) / 10);
        }
        if (hasGroundSupport)
        {
            Add(AutonomousAction.Hop, (0.24 + life.Curiosity / 180) *
                (isFeibiJiubi ? 1 + 0.75 * energy : 1) * tiredMovementScale);
        }

        if (choices.Count == 0)
        {
            return AutonomousAction.Rest;
        }

        var total = choices.Sum(choice => choice.Weight);
        var target = Math.Clamp(randomUnit, 0, 0.999999) * total;
        foreach (var choice in choices)
        {
            target -= choice.Weight;
            if (target <= 0)
            {
                return choice.Action;
            }
        }

        return choices[^1].Action;

        void Add(AutonomousAction action, double weight)
        {
            if (weight > 0 && availableActions.Contains(action))
            {
                choices.Add((action, weight));
            }
        }
    }
}
