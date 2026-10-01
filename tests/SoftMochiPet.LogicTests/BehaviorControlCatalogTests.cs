using SoftMochiPet.Core;

internal static class BehaviorControlCatalogTests
{
    public static void EveryDirectActionHasOneNamedCatalogEntry()
    {
        var entries = BehaviorControlCatalog.All;
        Assert(entries.Count == 30 && entries.Select(item => item.Action).Distinct().Count() == entries.Count &&
            Enum.GetValues<BehaviorControlAction>().All(action => entries.Count(item => item.Action == action) == 1),
            "Every routeable direct action must have exactly one stable menu entry.");
        Assert(entries.All(item => !string.IsNullOrWhiteSpace(item.Group) && !string.IsNullOrWhiteSpace(item.Title) &&
            !string.IsNullOrWhiteSpace(item.Description) && item.Group.Any(IsChinese) && item.Title.Any(IsChinese)),
            "Groups and titles must be directly readable Chinese, with a specific behavior description.");
        Assert(entries.All(item => ReferenceEquals(BehaviorControlCatalog.Get(item.Action), item)),
            "Lookup must return the same immutable metadata shown by the menu.");
        Assert(entries[0].Action == BehaviorControlAction.Stop && entries[0].Requirements == BehaviorControlRequirements.None,
            "Stopping must remain available without requiring a window, support surface, meter or hat state.");
    }

    public static void CharacterMenusSeparateFoodAndWindowMischief()
    {
        var nuonuo = BehaviorControlCatalog.ForCharacter(PetCharacterProfile.Nuonuo);
        var feibi = BehaviorControlCatalog.ForCharacter(PetCharacterProfile.FeibiJiubi);
        Assert(nuonuo.Count == 20 && feibi.Count == 26,
            "Both characters expose sixteen common actions, with four food and ten mischief entries kept separate.");
        Assert(nuonuo.All(item => item.Supports(PetCharacterProfile.Nuonuo)) &&
            feibi.All(item => item.Supports(PetCharacterProfile.FeibiJiubi)),
            "Menu filtering and the action's character guard must agree.");
        BehaviorControlAction[] food = [BehaviorControlAction.Hungry, BehaviorControlAction.LickIcon,
            BehaviorControlAction.EatIcon, BehaviorControlAction.Satisfied];
        Assert(food.All(action => nuonuo.Any(item => item.Action == action) && feibi.All(item => item.Action != action)),
            "Feibi must never inherit hunger, tongue, eating or satisfied-food controls.");
        BehaviorControlAction[] pranks = [BehaviorControlAction.Kick, BehaviorControlAction.Punch, BehaviorControlAction.Charge,
            BehaviorControlAction.HatStore, BehaviorControlAction.Shatter, BehaviorControlAction.Tear, BehaviorControlAction.Tease,
            BehaviorControlAction.FillMischief, BehaviorControlAction.ReturnWindows, BehaviorControlAction.ThrowAndRecoverHat];
        Assert(pranks.All(action => feibi.Any(item => item.Action == action) && nuonuo.All(item => item.Action != action)),
            "Nuonuo must never inherit the window manipulation or mischief meter controls.");
        Assert(nuonuo.Any(item => item.Action == BehaviorControlAction.Roll) && feibi.Any(item => item.Action == BehaviorControlAction.Roll),
            "Explicitly requested non-food rolling remains available to both characters despite differing autonomous weights.");
    }

    public static void ActionConditionsDescribeRealTargetsAndStorage()
    {
        bool Requires(BehaviorControlAction action, BehaviorControlRequirements flags) =>
            (BehaviorControlCatalog.Get(action).Requirements & flags) == flags;
        foreach (var action in new[] { BehaviorControlAction.Kick, BehaviorControlAction.Punch, BehaviorControlAction.Charge,
                     BehaviorControlAction.HatStore, BehaviorControlAction.Shatter, BehaviorControlAction.Tear })
            Assert(Requires(action, BehaviorControlRequirements.EligibleWindow | BehaviorControlRequirements.SessionAvailable),
                "Every real window action retains the live-target and session safety requirements.");
        foreach (var action in new[] { BehaviorControlAction.HatStore, BehaviorControlAction.Shatter, BehaviorControlAction.Tear })
            Assert(Requires(action, BehaviorControlRequirements.EmptyHat), "A second stored snapshot requires a free hat.");
        foreach (var action in new[] { BehaviorControlAction.Kick, BehaviorControlAction.Punch, BehaviorControlAction.Charge })
            Assert(!Requires(action, BehaviorControlRequirements.EmptyHat), "Non-storing strikes must remain available with a held result.");
        Assert(Requires(BehaviorControlAction.ReturnWindows, BehaviorControlRequirements.HeldWindow) &&
            !Requires(BehaviorControlAction.ReturnWindows, BehaviorControlRequirements.EligibleWindow),
            "A return uses the already-owned minimized target, never a new candidate selected from user windows.");
        Assert(Requires(BehaviorControlAction.Climb, BehaviorControlRequirements.GroundSupport | BehaviorControlRequirements.ReachableClimbWindow) &&
            Requires(BehaviorControlAction.SlideDown, BehaviorControlRequirements.WindowPlatform) &&
            Requires(BehaviorControlAction.JumpDown, BehaviorControlRequirements.WindowPlatform) &&
            Requires(BehaviorControlAction.Wake, BehaviorControlRequirements.Sleeping),
            "Terrain and wake commands must declare the actual state needed by their production routes.");
        Assert(Requires(BehaviorControlAction.EatIcon, BehaviorControlRequirements.DesktopIcon | BehaviorControlRequirements.FoodEnabled) &&
            Requires(BehaviorControlAction.LickIcon, BehaviorControlRequirements.DesktopIcon | BehaviorControlRequirements.FoodEnabled),
            "Icon interactions must use a real eligible desktop icon and retain the food-mode boundary.");
    }

    public static void AnimationAppreciationNeverSubstitutesForARealBehavior()
    {
        var previews = BehaviorControlCatalog.All.Where(item => item.Kind == BehaviorControlKind.AnimationPreview).ToArray();
        Assert(previews.Length == 1 && previews[0].Action == BehaviorControlAction.Satisfied &&
            previews[0].Title.Contains("动作欣赏", StringComparison.Ordinal) && previews[0].Description.Contains("不增加进食记录", StringComparison.Ordinal),
            "The isolated satisfaction clip must be explicitly labeled as appreciation with no claimed meal completion.");
        Assert(BehaviorControlCatalog.Get(BehaviorControlAction.EatIcon).Description.Contains("不删除真实文件", StringComparison.Ordinal) &&
            BehaviorControlCatalog.Get(BehaviorControlAction.ThrowAndRecoverHat).Kind == BehaviorControlKind.Behavior &&
            BehaviorControlCatalog.Get(BehaviorControlAction.Run).Kind == BehaviorControlKind.Behavior,
            "Safe icon consumption, complete hat recovery and running must use the true route rather than a clip-only shortcut.");
        Assert(Enum.GetNames<BehaviorControlAction>().All(name => !name.Contains("Drag", StringComparison.Ordinal)),
            "Dragging is sustained user input, not a command that synthesizes mouse events.");
        Assert(BehaviorControlCatalog.Get(BehaviorControlAction.FillMischief).Kind == BehaviorControlKind.StateChange &&
            !BehaviorControlCatalog.Get(BehaviorControlAction.FillMischief).Requirements.HasFlag(BehaviorControlRequirements.EligibleWindow),
            "Filling the meter is a state edit, not evidence that a real target exists or a prank completed.");
    }

    private static bool IsChinese(char value) => value is >= '\u4e00' and <= '\u9fff';

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
