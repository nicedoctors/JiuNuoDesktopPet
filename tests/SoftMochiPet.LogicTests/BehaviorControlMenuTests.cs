using System.Reflection;
using System.Runtime.ExceptionServices;
using SoftMochiPet.Core;
using SoftMochiPet.Services;
using Forms = System.Windows.Forms;

internal static class BehaviorControlMenuTests
{
    private static readonly Type MenuType = typeof(TrayIconService)
        .GetNestedType("BehaviorControlMenu", BindingFlags.NonPublic)!;

    public static void CatalogActionsAreGroupedOnceAndFilteredByCharacter() => OnSta(() =>
    {
        var menu = CreateMenu(PetCharacterProfile.Nuonuo);
        try
        {
            var items = ActionItems(menu);
            Assert(items.Count == BehaviorControlCatalog.All.Count(item => item.Action != BehaviorControlAction.Stop) &&
                items.Select(item => (BehaviorControlAction)item.Tag!).Distinct().Count() == items.Count,
                "Every catalog action except the separate stop command must appear exactly once.");
            foreach (var definition in BehaviorControlCatalog.All.Where(item => item.Action != BehaviorControlAction.Stop))
            {
                var item = items.Single(item => Equals(item.Tag, definition.Action));
                Assert(item.Text == definition.Title && item.ToolTipText == definition.Description,
                    "Action titles and condition hints must come from the shared catalog.");
                Assert(Groups(menu).Single(group => group.Text == definition.Group).DropDownItems.Contains(item),
                    "Each action must remain in its catalog-defined category.");
            }
            foreach (var character in PetCharacterProfile.All)
            {
                Invoke(menu, "SetCharacter", character);
                var expected = BehaviorControlCatalog.ForCharacter(character)
                    .Where(item => item.Action != BehaviorControlAction.Stop).Select(item => item.Action).ToHashSet();
                var available = ActionItems(menu).Where(item => item.Available)
                    .Select(item => (BehaviorControlAction)item.Tag!).ToHashSet();
                Assert(available.SetEquals(expected), "Character switching must hide every unsupported action.");
                Assert(Groups(menu).All(group => group.Available ==
                    group.DropDownItems.Cast<Forms.ToolStripItem>().Any(item => item.Available)),
                    "A category without actions for the selected character must be hidden.");
            }
            AssertUnshown(menu);
        }
        finally { DisposeMenu(menu); }
    });

    public static void AvailabilityUpdatesReuseItemsAndKeepUnsupportedActionsDisabled() => OnSta(() =>
    {
        var menu = CreateMenu(PetCharacterProfile.Nuonuo);
        try
        {
            var originals = ActionItems(menu).ToArray();
            var everyAction = BehaviorControlCatalog.All.ToDictionary(item => item.Action, _ => true);
            Invoke(menu, "SetAvailability", everyAction);
            foreach (var item in originals)
                Assert(item.Enabled == BehaviorControlCatalog.Get((BehaviorControlAction)item.Tag!).Supports(PetCharacterProfile.Nuonuo),
                    "An availability refresh must never enable actions belonging to the other character.");
            var allowed = BehaviorControlCatalog.ForCharacter(PetCharacterProfile.Nuonuo)
                .First(item => item.Action != BehaviorControlAction.Stop).Action;
            Invoke(menu, "SetAvailability", new Dictionary<BehaviorControlAction, bool> { [allowed] = true });
            Assert(ActionItems(menu).Count(item => item.Enabled) == 1 &&
                ActionItems(menu).Single(item => item.Enabled).Tag!.Equals(allowed),
                "Actions omitted from a complete availability refresh must stay disabled.");
            Invoke(menu, "SetCharacter", PetCharacterProfile.FeibiJiubi);
            Assert(ActionItems(menu).All(item => !item.Enabled),
                "A character change must wait for fresh conditions instead of keeping stale enabled actions.");
            Invoke(menu, "SetAvailability", everyAction);
            Assert(ActionItems(menu).SequenceEqual(originals),
                "Opening refreshes and role changes must update existing menu items, not recreate the menu.");
            AssertUnshown(menu);
        }
        finally { DisposeMenu(menu); }
    });

    public static void ClicksOnlyEmitRequestsAndAutonomyStartsDisabled() => OnSta(() =>
    {
        var requests = new List<BehaviorControlAction>();
        var stopCount = 0;
        var autonomy = new List<bool>();
        var menu = CreateMenu(PetCharacterProfile.FeibiJiubi, requests.Add, () => stopCount++, autonomy.Add);
        try
        {
            var toggle = Item(menu, "Autonomy");
            Assert(!toggle.Checked && autonomy.Count == 0,
                "Behavior-control sessions must start without autonomous daily activity or a synthetic toggle event.");
            var action = BehaviorControlAction.Kick;
            var entry = ActionItems(menu).Single(item => Equals(item.Tag, action));
            entry.PerformClick();
            Assert(requests.Count == 0, "Unavailable actions must not send execution requests.");
            Invoke(menu, "SetAvailability", new Dictionary<BehaviorControlAction, bool> { [action] = true });
            entry.PerformClick();
            Item(menu, "Stop").PerformClick();
            toggle.PerformClick();
            toggle.PerformClick();
            Assert(requests.SequenceEqual(new[] { action }) && stopCount == 1 &&
                autonomy.SequenceEqual(new[] { true, false }),
                "Menu commands must emit only the selected request; stopping and daily autonomy remain separate.");
            AssertUnshown(menu);
        }
        finally { DisposeMenu(menu); }
    });

    public static void StatusUpdatesDoNotEnableOrRecreateControls() => OnSta(() =>
    {
        var menu = CreateMenu(PetCharacterProfile.Nuonuo);
        try
        {
            var status = Item(menu, "Status");
            var actions = Item(menu, "Actions");
            foreach (var message in new[] { "正在执行：散步", "没有合适的窗口", "需要站在窗口上" })
            {
                Invoke(menu, "SetStatus", message);
                Assert(ReferenceEquals(status, Item(menu, "Status")) && status.Text == $"行为控制版：{message}" &&
                    !status.Enabled && ReferenceEquals(actions, Item(menu, "Actions")),
                    "Status feedback must update in place and cannot become an executable action.");
            }
            AssertUnshown(menu);
        }
        finally { DisposeMenu(menu); }
    });

    private static object CreateMenu(PetCharacterProfile profile, Action<BehaviorControlAction>? request = null,
        Action? stop = null, Action<bool>? autonomy = null) => Activator.CreateInstance(MenuType,
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
        new object[] { profile, request ?? (_ => { }), stop ?? (() => { }), autonomy ?? (_ => { }) }, null)!;

    private static Forms.ToolStripMenuItem Item(object menu, string name) =>
        (Forms.ToolStripMenuItem)MenuType.GetProperty(name)!.GetValue(menu)!;
    private static List<Forms.ToolStripMenuItem> Groups(object menu) =>
        Item(menu, "Actions").DropDownItems.Cast<Forms.ToolStripMenuItem>().ToList();
    private static List<Forms.ToolStripMenuItem> ActionItems(object menu) =>
        Groups(menu).SelectMany(group => group.DropDownItems.Cast<Forms.ToolStripMenuItem>()).ToList();

    private static void AssertUnshown(object menu)
    {
        Assert(!Item(menu, "Actions").DropDown.IsHandleCreated &&
            Groups(menu).All(group => !group.DropDown.IsHandleCreated),
            "Menu tests must not display dropdowns or create native handles; no NotifyIcon or Window is constructed.");
    }

    private static void DisposeMenu(object menu)
    {
        foreach (var name in new[] { "Status", "Actions", "Stop", "Autonomy" }) Item(menu, name).Dispose();
    }

    private static void Invoke(object menu, string method, object argument)
    {
        try { MenuType.GetMethod(method)!.Invoke(menu, new[] { argument }); }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        { ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); }
    }

    private static void OnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception exception) { failure = exception; } }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
