namespace SoftMochiPet.Core;

internal static class FeibiJiubiDialogue
{
    private static readonly IReadOnlyDictionary<DialogueCue, IReadOnlyList<string>> Lines =
        new Dictionary<DialogueCue, IReadOnlyList<string>>
        {
            [DialogueCue.Startup] = ["啾，你来啦！", "嗨，陪我玩！", "今天去哪呀？", "嘿嘿，见面啦。"],
            [DialogueCue.IconsHidden] = ["咦，都躲啦？", "在玩捉迷藏？", "出来玩嘛！", "我明明看见啦。"],
            [DialogueCue.PlatformLaunch] = ["芜湖——！", "帽子抓紧我！", "哇，脚轻轻！", "还能更高吗？"],
            [DialogueCue.HardLanding] = ["哎哟，弹一下！", "鞋鞋先到啦！", "嘿，还能蹦！", "啾，没摔扁！"],
            [DialogueCue.ConfidentClimb] = ["嘿，看我爬！", "上面有啥呀？", "小脚很有劲！", "冲呀冲呀！"],
            [DialogueCue.CarefulClimb] = ["鞋鞋别滑呀。", "再够高一点！", "我抓得住嘛？", "帽子别挡眼呀。"],
            [DialogueCue.ClimbSlip] = ["哎呀，手滑滑！", "差一小点嘛！", "墙墙好滑呀。", "不服，再爬！"],
            [DialogueCue.ClimbSuccess] = ["看，我在这儿！", "嘿嘿，够高吧！", "风吹帽子啦！", "下面变小啦！"],
            [DialogueCue.Exploration] = ["那边亮亮的！", "嘿，去瞅瞅！", "还有哪儿没玩？", "跟小脚走嘛！"],
            [DialogueCue.Sleep] = ["眼皮好重呀。", "再玩……一会。", "啾，借我靠靠。", "梦里也有你？"],
            [DialogueCue.WakeNormally] = ["嘿，又见面啦！", "梦里跑好远呀。", "睡翘一撮毛！", "啾，还想出去！"],
            [DialogueCue.PickedUp] = ["举高高耶！", "啾，带我去哪？", "小脚够不到啦。", "晃晃还挺好玩！"],
            [DialogueCue.Released] = ["哇，地板来啦！", "小脚找地板！", "等等我的帽子！", "啾，轻轻一点！"],
            [DialogueCue.DragThrow] = ["咻——好快呀！", "帽子追不上啦！", "哇，风呼呼的！", "再飞一小会！"],
            [DialogueCue.TerrainHop] = ["蹦高一点嘛！", "嘿，小脚弹弹！", "啾，够得着！", "一下就过去呀！"],
            [DialogueCue.TerrainJumpDown] = ["下面也想逛！", "嘿，接住帽子！", "往下蹦蹦嘛！", "啾，那儿好玩！"],
            [DialogueCue.EdgeSlide] = ["哧溜，好滑呀！", "小手抓牢嘛！", "鞋鞋蹭痒痒。", "再溜一小段！"],
            [DialogueCue.WindowMaximized] = ["哇，挤到我啦！", "窗窗长好快！", "帽子吓歪啦！", "怎么突然飞啦！"],
            [DialogueCue.Home] = ["窝窝真舒服！", "嘿，还是这里！", "回来看你啦！", "帽子也歇歇。"],
            [DialogueCue.VoiceEnabled] = ["啾，在这里！", "嘿，看看我！", "哈喽小伙伴！", "想我了没呀？"],
            [DialogueCue.VoiceDisabled] = ["嘘，悄悄的。", "蹑手蹑脚走。", "啾……小小声。", "偷偷陪着你。"],
        };

    private static readonly IReadOnlyList<string> PlayLines = ["嘿，玩什么呀？", "陪我蹦一下！", "我有小主意！", "帽子藏好啦。"];

    public static IReadOnlyList<string> GetLines(DialogueCue cue) =>
        Lines.TryGetValue(cue, out var lines) ? lines : PlayLines;
}
