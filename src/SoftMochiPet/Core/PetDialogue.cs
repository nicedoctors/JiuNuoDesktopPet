namespace SoftMochiPet.Core;

public enum DialogueCue
{
    Startup,
    Buffet,
    MealSpotted,
    MealQueueContinues,
    IconsHidden,
    ManualIconLick,
    CuriousIconLick,
    PlatformLaunch,
    HardLanding,
    VeryHungry,
    ABitHungry,
    ConfidentClimb,
    CarefulClimb,
    ClimbSlip,
    ClimbSuccess,
    HungryRoll,
    Suction,
    Exploration,
    Sleep,
    WakeForFood,
    WakeNormally,
    ComfortablyFull,
    IconTaste,
    FeastContinues,
    AnotherBite,
    Stuffed,
    TastedItem,
    PickedUp,
    Released,
    DragThrow,
    TerrainHop,
    TerrainJumpDown,
    EdgeSlide,
    WindowMaximized,
    HungryRequestQueued,
    Home,
    DeleteReactionEnabled,
    DeleteReactionDisabled,
    VoiceEnabled,
    VoiceDisabled,
}

/// <summary>
/// Short reactions selected from each character's own dialogue catalog.
/// </summary>
public static class PetDialogueCatalog
{
    private static readonly IReadOnlyDictionary<DialogueCue, IReadOnlyList<string>> Lines =
        new Dictionary<DialogueCue, IReadOnlyList<string>>
        {
            [DialogueCue.Startup] =
            [
                "早呀。",
                "我醒啦。",
                "今天吃什么？",
                "先发会儿呆。",
            ],
            [DialogueCue.Buffet] =
            [
                "好多！",
                "都是我的？",
                "吃不完啦。",
                "嘿嘿，开饭。",
            ],
            [DialogueCue.MealSpotted] =
            [
                "有吃的。",
                "香香的。",
                "我来啦。",
                "那个能吃吗？",
            ],
            [DialogueCue.MealQueueContinues] =
            [
                "还有呀。",
                "下一个。",
                "都要吃。",
                "别跑。",
            ],
            [DialogueCue.IconsHidden] =
            [
                "没有图标……",
                "藏哪啦？",
                "舔不到。",
                "空空的。",
            ],
            [DialogueCue.ManualIconLick] =
            [
                "好饿……",
                "先舔一口。",
                "就一小口。",
                "这个能吃吗？",
            ],
            [DialogueCue.CuriousIconLick] =
            [
                "什么味呀？",
                "舔一下。",
                "看着好香。",
                "就试试。",
            ],
            [DialogueCue.PlatformLaunch] =
            [
                "飞起来啦！",
                "呜哇！",
                "脚呢？",
                "好高！",
            ],
            [DialogueCue.HardLanding] =
            [
                "噗叽。",
                "扁掉啦。",
                "呼，到了。",
                "我又圆啦。",
            ],
            [DialogueCue.VeryHungry] =
            [
                "好饿……",
                "饿扁了。",
                "想吃东西。",
                "肚子空啦。",
            ],
            [DialogueCue.ABitHungry] =
            [
                "有点饿。",
                "想吃一口。",
                "有吃的吗？",
                "嘴馋了。",
            ],
            [DialogueCue.ConfidentClimb] =
            [
                "我能上去。",
                "看我的。",
                "嘿咻。",
                "上面有吃的？",
            ],
            [DialogueCue.CarefulClimb] =
            [
                "好高……",
                "慢一点。",
                "别掉呀。",
                "我试试。",
            ],
            [DialogueCue.ClimbSlip] =
            [
                "滑下来啦……",
                "没抓住。",
                "呜……差一点。",
                "再来。",
            ],
            [DialogueCue.ClimbSuccess] =
            [
                "上来啦。",
                "嘿嘿。",
                "我厉害吧。",
                "这里好高。",
            ],
            [DialogueCue.HungryRoll] =
            [
                "好饿好饿。",
                "给我吃的。",
                "我滚一下。",
                "饿扁啦。",
            ],
            [DialogueCue.Suction] =
            [
                "吸过来！",
                "啊——",
                "到嘴里来。",
                "我要吃啦。",
            ],
            [DialogueCue.Exploration] =
            [
                "这里是哪？",
                "没来过。",
                "看看这边。",
                "好奇。",
            ],
            [DialogueCue.Sleep] =
            [
                "我要睡觉啦。",
                "困困。",
                "先睡一会儿。",
                "晚安。",
            ],
            [DialogueCue.WakeForFood] =
            [
                "有吃的？",
                "我醒啦！",
                "香香的！",
                "开饭吗？",
            ],
            [DialogueCue.WakeNormally] =
            [
                "醒啦。",
                "还想睡……",
                "睡饱啦。",
                "再躺一下？",
            ],
            [DialogueCue.ComfortablyFull] =
            [
                "吃饱啦。",
                "肚子圆圆。",
                "好撑。",
                "不动了。",
            ],
            [DialogueCue.IconTaste] =
            [
                "不好吃。",
                "只有图标味。",
                "空的。",
                "再舔一下？",
            ],
            [DialogueCue.FeastContinues] =
            [
                "还能吃！",
                "再来一个。",
                "都给我。",
                "还没饱。",
            ],
            [DialogueCue.AnotherBite] =
            [
                "下一个。",
                "还有吗？",
                "继续吃。",
                "我还要。",
            ],
            [DialogueCue.Stuffed] =
            [
                "吃撑啦。",
                "肚子好圆。",
                "真的饱了。",
                "动不了啦。",
            ],
            [DialogueCue.TastedItem] =
            [
                "{item} 好吃。",
                "{item} 香香的。",
                "喜欢 {item}。",
                "这个叫 {item}？",
            ],
            [DialogueCue.PickedUp] =
            [
                "被抓住啦。",
                "轻一点。",
                "要拉长啦。",
                "呜……",
            ],
            [DialogueCue.Released] =
            [
                "放开啦。",
                "我掉啦。",
                "脚在哪里？",
                "转圈圈……",
            ],
            [DialogueCue.DragThrow] =
            [
                "飞走啦！",
                "呜哇——",
                "停不下来！",
                "我被甩啦！",
            ],
            [DialogueCue.TerrainHop] =
            [
                "跳一下。",
                "嘿咻！",
                "我会飞。",
                "蹦起来啦。",
            ],
            [DialogueCue.TerrainJumpDown] =
            [
                "我跳啦。",
                "下面接住我。",
                "走你！",
                "飞下去。",
            ],
            [DialogueCue.EdgeSlide] =
            [
                "滑下去啦。",
                "慢慢滑。",
                "手要抓住。",
                "这是滑梯？",
            ],
            [DialogueCue.WindowMaximized] =
            [
                "变大啦！",
                "被弹飞啦！",
                "呜哇，好大！",
                "窗口长大了！",
            ],
            [DialogueCue.HungryRequestQueued] =
            [
                "好饿……等一下。",
                "站稳就吃。",
                "马上去舔。",
                "肚子等不及啦。",
            ],
            [DialogueCue.Home] =
            [
                "到家啦。",
                "回窝。",
                "这里好。",
                "不走了。",
            ],
            [DialogueCue.DeleteReactionEnabled] =
            [
                "删掉的给我吃。",
                "我会接住。",
                "记得叫我。",
                "有吃的啦。",
            ],
            [DialogueCue.DeleteReactionDisabled] =
            [
                "今天不吃。",
                "先忍一下。",
                "不追啦。",
                "图标安全啦。",
            ],
            [DialogueCue.VoiceEnabled] =
            [
                "我有声音啦。",
                "听得到吗？",
                "嘿嘿，是我。",
                "我说话啦。",
            ],
            [DialogueCue.VoiceDisabled] =
            [
                "我安静啦。",
                "嘘。",
                "不说话啦。",
                "小声一点。",
            ],
        };

    public static IReadOnlyList<string> GetLines(DialogueCue cue, PetCharacterProfile? profile = null) =>
        profile?.Id == PetCharacterProfile.FeibiJiubi.Id ? FeibiJiubiDialogue.GetLines(cue) : Lines[cue];
}

public sealed class PetDialogueSelector
{
    private readonly Random _random;
    private readonly PetCharacterProfile _profile;
    private readonly Dictionary<DialogueCue, int> _lastLineByCue = [];

    public PetDialogueSelector(Random? random = null, PetCharacterProfile? profile = null)
    {
        _random = random ?? Random.Shared;
        _profile = profile ?? PetCharacterProfile.Nuonuo;
    }

    public string Choose(DialogueCue cue, string? itemName = null)
    {
        var lines = PetDialogueCatalog.GetLines(cue, _profile);
        var index = _random.Next(lines.Count);
        if (lines.Count > 1 && _lastLineByCue.TryGetValue(cue, out var previous) && index == previous)
        {
            index = (index + 1) % lines.Count;
        }

        _lastLineByCue[cue] = index;
        return lines[index].Replace("{item}", itemName ?? "这份文件", StringComparison.Ordinal);
    }
}
