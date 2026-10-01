namespace SoftMochiPet.Core;

public enum BehaviorControlAction
{
    Stop,
    Stand,
    Walk,
    Run,
    Curious,
    Explore,
    Patrol,
    Hop,
    Climb,
    SlideDown,
    JumpDown,
    Roll,
    Sleep,
    Wake,
    FreeFall,
    Toss,
    Hungry,
    LickIcon,
    EatIcon,
    Satisfied,
    Kick,
    Punch,
    Charge,
    HatStore,
    Shatter,
    Tear,
    Tease,
    FillMischief,
    ReturnWindows,
    ThrowAndRecoverHat,
}

[Flags]
public enum BehaviorControlCharacters
{
    None = 0,
    Nuonuo = 1,
    FeibiJiubi = 2,
    Both = Nuonuo | FeibiJiubi,
}

[Flags]
public enum BehaviorControlRequirements
{
    None = 0,
    SessionAvailable = 1 << 0,
    NotDragging = 1 << 1,
    Awake = 1 << 2,
    GroundSupport = 1 << 3,
    OutsideEnclosure = 1 << 4,
    ReachableClimbWindow = 1 << 5,
    WindowPlatform = 1 << 6,
    FreeAirSpace = 1 << 7,
    Sleeping = 1 << 8,
    FoodEnabled = 1 << 9,
    DesktopIcon = 1 << 10,
    EligibleWindow = 1 << 11,
    EmptyHat = 1 << 12,
    HeldWindow = 1 << 13,
    MischiefReady = 1 << 14,
}

public enum BehaviorControlKind
{
    Behavior,
    StateChange,
    AnimationPreview,
}

public sealed record BehaviorControlItem(
    BehaviorControlAction Action,
    string Group,
    string Title,
    BehaviorControlCharacters Characters,
    BehaviorControlRequirements Requirements,
    BehaviorControlKind Kind,
    string Description)
{
    public bool Supports(PetCharacterProfile character)
    {
        ArgumentNullException.ThrowIfNull(character);
        var flag = character.SupportsFood ? BehaviorControlCharacters.Nuonuo
            : character.UsesMischief ? BehaviorControlCharacters.FeibiJiubi : BehaviorControlCharacters.None;
        return (Characters & flag) != 0;
    }
}

/// <summary>
/// Menu metadata for the behavior-control build. Conditions describe the real
/// action route; the dispatcher must recheck live targets before execution.
/// </summary>
public static class BehaviorControlCatalog
{
    private const BehaviorControlCharacters Both = BehaviorControlCharacters.Both;
    private const BehaviorControlCharacters Nuonuo = BehaviorControlCharacters.Nuonuo;
    private const BehaviorControlCharacters Feibi = BehaviorControlCharacters.FeibiJiubi;
    private const BehaviorControlKind Behavior = BehaviorControlKind.Behavior;
    private const BehaviorControlKind StateChange = BehaviorControlKind.StateChange;
    private const BehaviorControlRequirements Ready = BehaviorControlRequirements.SessionAvailable |
        BehaviorControlRequirements.NotDragging;
    private const BehaviorControlRequirements Awake = Ready | BehaviorControlRequirements.Awake;
    private const BehaviorControlRequirements Ground = Awake | BehaviorControlRequirements.GroundSupport;
    private const BehaviorControlRequirements Travel = Ground | BehaviorControlRequirements.OutsideEnclosure;
    private const BehaviorControlRequirements WindowAction = Awake | BehaviorControlRequirements.OutsideEnclosure |
        BehaviorControlRequirements.EligibleWindow;
    private const BehaviorControlRequirements IconAction = Awake | BehaviorControlRequirements.OutsideEnclosure |
        BehaviorControlRequirements.FoodEnabled | BehaviorControlRequirements.DesktopIcon;

    public static IReadOnlyList<BehaviorControlItem> All { get; } = Array.AsReadOnly(new[]
    {
        new BehaviorControlItem(BehaviorControlAction.Stop, "行为控制", "停止当前动作", Both,
            BehaviorControlRequirements.None, StateChange, "停止当前行为并安全收场；已收进帽子的窗口仍可主动归还。"),
        new BehaviorControlItem(BehaviorControlAction.Stand, "站姿与情绪", "站立", Both,
            Ground, Behavior, "回到有支撑的位置，保持站立待机。"),
        new BehaviorControlItem(BehaviorControlAction.Curious, "站姿与情绪", "好奇张望", Both,
            Ground, Behavior, "在原地好奇地张望。"),
        new BehaviorControlItem(BehaviorControlAction.Walk, "行走与探索", "走一走", Both,
            Travel, Behavior, "沿当前支撑面走到一个附近位置。"),
        new BehaviorControlItem(BehaviorControlAction.Run, "行走与探索", "跑一跑", Both,
            Travel, Behavior, "沿支撑面真实跑动，不只是原地播放跑步帧。"),
        new BehaviorControlItem(BehaviorControlAction.Explore, "行走与探索", "探索一下", Both,
            Travel, Behavior, "走到附近探索，抵达后好奇张望。"),
        new BehaviorControlItem(BehaviorControlAction.Patrol, "行走与探索", "四处巡游", Both,
            Travel, Behavior, "持续沿支撑面巡游，到边缘时按地形决定后续动作。"),
        new BehaviorControlItem(BehaviorControlAction.Hop, "跳跃与地形", "蹦一下", Both,
            Travel | BehaviorControlRequirements.FreeAirSpace, Behavior, "从支撑面蹦起，再按重力落下。"),
        new BehaviorControlItem(BehaviorControlAction.Climb, "跳跃与地形", "爬上窗口", Both,
            Travel | BehaviorControlRequirements.ReachableClimbWindow, Behavior, "走向可攀爬的真实窗口边缘，再尝试爬上去；有可能失手。"),
        new BehaviorControlItem(BehaviorControlAction.SlideDown, "跳跃与地形", "沿窗口滑下", Both,
            Travel | BehaviorControlRequirements.WindowPlatform, Behavior, "在真实窗口顶边走到边缘，沿侧面滑下后落地。"),
        new BehaviorControlItem(BehaviorControlAction.JumpDown, "跳跃与地形", "从窗口边跳下", Both,
            Travel | BehaviorControlRequirements.WindowPlatform | BehaviorControlRequirements.FreeAirSpace,
            Behavior, "从当前窗口顶边跳下，按真实落地规则结束。"),
        new BehaviorControlItem(BehaviorControlAction.Roll, "跳跃与地形", "翻滚一下", Both,
            Ground, Behavior, "在支撑面上翻滚；菲比也可手动体验，不附加饥饿状态。"),
        new BehaviorControlItem(BehaviorControlAction.FreeFall, "跳跃与地形", "自由落下", Both,
            Awake | BehaviorControlRequirements.FreeAirSpace, Behavior, "从有下落空间的位置松开角色，按重力落地，不操作系统鼠标。"),
        new BehaviorControlItem(BehaviorControlAction.Toss, "跳跃与地形", "抛起来", Both,
            Awake | BehaviorControlRequirements.FreeAirSpace, Behavior, "给角色一个向上的初速度，体验被抛起与落地的物理效果。"),
        new BehaviorControlItem(BehaviorControlAction.Sleep, "睡觉与醒来", "我要睡觉啦", Both,
            Ready | BehaviorControlRequirements.GroundSupport, Behavior, "在支撑面上入睡，播放入睡与呼吸循环。"),
        new BehaviorControlItem(BehaviorControlAction.Wake, "睡觉与醒来", "醒来啦", Both,
            Ready | BehaviorControlRequirements.Sleeping | BehaviorControlRequirements.GroundSupport,
            Behavior, "从睡眠状态醒来，再回到日常站姿。"),
        new BehaviorControlItem(BehaviorControlAction.Hungry, "糯糯互动", "好饿", Nuonuo,
            Ground | BehaviorControlRequirements.FoodEnabled, StateChange, "进入饥饿状态并表达好饿；辟谷模式下不可用。"),
        new BehaviorControlItem(BehaviorControlAction.LickIcon, "糯糯互动", "舔舔图标", Nuonuo,
            IconAction, Behavior, "跑到可见桌面图标旁舔一舔，不修改图标或文件。"),
        new BehaviorControlItem(BehaviorControlAction.EatIcon, "糯糯互动", "吸入图标副本", Nuonuo,
            IconAction, Behavior, "使用真实图标外观的副本，跑过去吸入并满足收场；不删除真实文件。"),
        new BehaviorControlItem(BehaviorControlAction.Satisfied, "动作欣赏", "满足（动作欣赏）", Nuonuo,
            Ground, BehaviorControlKind.AnimationPreview, "单独欣赏吃饱后的满足动作，不假装已经吃掉文件，也不增加进食记录。"),
        new BehaviorControlItem(BehaviorControlAction.Kick, "菲比捣蛋", "踢飞窗口", Feibi,
            WindowAction, Behavior, "踢动符合安全条件的真实窗口；帽中已有存货时仍可执行。"),
        new BehaviorControlItem(BehaviorControlAction.Punch, "菲比捣蛋", "打飞窗口", Feibi,
            WindowAction, Behavior, "打动符合安全条件的真实窗口；帽中已有存货时仍可执行。"),
        new BehaviorControlItem(BehaviorControlAction.Charge, "菲比捣蛋", "撞击窗口", Feibi,
            WindowAction, Behavior, "冲向符合安全条件的真实窗口，命中后让窗口滑动减速。"),
        new BehaviorControlItem(BehaviorControlAction.HatStore, "菲比捣蛋", "把窗口藏进帽子", Feibi,
            WindowAction | BehaviorControlRequirements.EmptyHat, Behavior, "把窗口快照收进帽子，真实窗口最小化，等待主动归还。"),
        new BehaviorControlItem(BehaviorControlAction.Shatter, "菲比捣蛋", "踢碎窗口", Feibi,
            WindowAction | BehaviorControlRequirements.EmptyHat, Behavior, "踢碎窗口快照，再把碎片收进帽子；不破坏应用或文件。"),
        new BehaviorControlItem(BehaviorControlAction.Tear, "菲比捣蛋", "上下撕开窗口", Feibi,
            WindowAction | BehaviorControlRequirements.EmptyHat, Behavior, "变大后上下扯开窗口快照，再收进帽子；原窗口可完整归还。"),
        new BehaviorControlItem(BehaviorControlAction.Tease, "菲比互动", "逗逗她", Feibi,
            Awake | BehaviorControlRequirements.MischiefReady, StateChange, "按现有逗弄规则增加捣蛋值；连续逗弄和安全冷却仍有间隔。"),
        new BehaviorControlItem(BehaviorControlAction.FillMischief, "菲比互动", "捣蛋槽加满", Feibi,
            Awake, StateChange, "把捣蛋值设为满槽；不绕过真实窗口安全条件，也不自动归还已有存货。"),
        new BehaviorControlItem(BehaviorControlAction.ReturnWindows, "帽子互动", "把窗口还回来", Feibi,
            Ready | BehaviorControlRequirements.HeldWindow, Behavior, "播放取回与拼合过程，再确认原窗口实际恢复。"),
        new BehaviorControlItem(BehaviorControlAction.ThrowAndRecoverHat, "帽子互动", "抛帽子再捡回来", Feibi,
            Travel | BehaviorControlRequirements.EmptyHat | BehaviorControlRequirements.FreeAirSpace,
            Behavior, "完整抛帽、跑过去捡回并重新戴好，不孤立播放无帽中间帧。"),
    });

    private static readonly IReadOnlyList<BehaviorControlItem> NuonuoItems =
        Array.AsReadOnly(All.Where(item => item.Supports(PetCharacterProfile.Nuonuo)).ToArray());
    private static readonly IReadOnlyList<BehaviorControlItem> FeibiItems =
        Array.AsReadOnly(All.Where(item => item.Supports(PetCharacterProfile.FeibiJiubi)).ToArray());

    public static IReadOnlyList<BehaviorControlItem> ForCharacter(PetCharacterProfile character)
    {
        ArgumentNullException.ThrowIfNull(character);
        return character.UsesMischief ? FeibiItems : NuonuoItems;
    }

    public static BehaviorControlItem Get(BehaviorControlAction action) =>
        All.FirstOrDefault(item => item.Action == action) ?? throw new ArgumentOutOfRangeException(nameof(action), action, null);
}
