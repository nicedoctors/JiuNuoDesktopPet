namespace SoftMochiPet.Core;

public readonly record struct PairDialogue(bool FeibiFirst, string First, string Reply);

public static class PairDialogueCatalog
{
    public static IReadOnlyList<PairDialogue> Exchanges { get; } =
    [
        new(true, "糯糯，猜我帽里有啥？", "蛋糕……？"),
        new(true, "去那边看看嘛！", "等我一下呀。"),
        new(true, "我跳得高不高？", "好高呀。"),
        new(true, "帽子里有惊喜！", "能吃吗？"),
        new(true, "糯糯，醒醒嘛。", "再睡一小会儿……"),
        new(false, "你又在笑什么？", "嘿嘿，秘密！"),
        new(false, "帽子借我躺躺。", "好呀，钻进来！"),
        new(false, "你跑慢一点呀。", "那你牵着我！"),
        new(false, "今天有蛋糕吗？", "我找找看！"),
        new(false, "不要捣蛋啦。", "就一下下嘛！"),
    ];
}
