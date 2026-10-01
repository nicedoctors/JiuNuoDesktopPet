namespace SoftMochiPet.Core;

public enum FeatureDemoAction
{
    Kick, Punch, Charge, HatStoreReturn, Tease,
    Shatter, Tear, HatlessInterruption,
}

public sealed record FeatureDemoStep(FeatureDemoAction Action, string Title, double TimeoutSeconds)
{
    public string Id => Action.ToString();
    public bool RequiresWindowTarget => Action != FeatureDemoAction.Tease;
    public bool RequiresManualRestore => Action is FeatureDemoAction.HatStoreReturn or FeatureDemoAction.Shatter
        or FeatureDemoAction.Tear or FeatureDemoAction.HatlessInterruption;
    public FeatureDemoAction CompletionAction => Action == FeatureDemoAction.HatlessInterruption
        ? FeatureDemoAction.Shatter : Action;
}

public sealed record FeatureDemoManualCheck(string Id, string Title);

public static class FeatureDemoPlan
{
    public const int Revision = 6;
    public const double RestoreTimeoutSeconds = 20;
    public static IReadOnlyList<FeatureDemoStep> Steps { get; } = Array.AsReadOnly(new[]
    {
        new FeatureDemoStep(FeatureDemoAction.Kick, "踢开测试窗口", 20),
        new FeatureDemoStep(FeatureDemoAction.Punch, "一拳打飞测试窗口", 20),
        new FeatureDemoStep(FeatureDemoAction.Charge, "冲过去撞一撞", 20),
        new FeatureDemoStep(FeatureDemoAction.HatStoreReturn, "藏好窗口，等待右键归还", 40),
        new FeatureDemoStep(FeatureDemoAction.Tease, "逗逗她，捣蛋条涨起来", 10),
        new FeatureDemoStep(FeatureDemoAction.Shatter, "踢碎并收进帽子，等待右键恢复", 60),
        new FeatureDemoStep(FeatureDemoAction.Tear, "撕开并收进帽子，等待右键恢复", 60),
        new FeatureDemoStep(FeatureDemoAction.HatlessInterruption, "无帽中断并捡帽，等待右键恢复", 60),
    });

    public static IReadOnlyList<FeatureDemoManualCheck> ManualChecks { get; } = Array.AsReadOnly(new[]
    {
        new FeatureDemoManualCheck("PrankAppearance", "观察撕片边缘完整、碎片收进帽口、右键出帽后聚合归位"),
        new FeatureDemoManualCheck("PrankTakeover", "收纳后桌面无残留碎片，窗口持续收起，右键恢复；用户接管时停止控制"),
        new FeatureDemoManualCheck("PrankVoice", "专属语音、禁音即停与连续动作不叠音"),
    });

    public static string FailureHint(string reason) => reason switch
    {
        "foreground_window" => "测试窗口正在前台：点桌面空白，再右键重试本项",
        "occluded" or "window_occluded" => "测试窗口被遮挡：移开遮挡后右键重试本项",
        "elevated_process" => "请退出以管理员身份运行的桌宠，使用普通启动",
        "test_window_closed" => "测试窗口已关闭，右键重试本项会重新创建",
        "step_timeout" => "此项未按时完成，请查看报告后重试本项",
        "unexpected_restore_before_manual_request" => "未收到右键请求就结束了窗口持有，请查看报告后重试",
        "manual_restore_identity_mismatch" => "恢复请求与当前测试窗口不符，请查看报告后重试",
        _ => $"此项已暂停（{reason}），可右键重试本项",
    };

    public static string ManualRestoreHint(double elapsedSeconds)
    {
        var elapsed = double.IsFinite(elapsedSeconds) ? Math.Max(0, Math.Floor(elapsedSeconds)) : 0;
        return $"等待右键恢复（已等待 {elapsed:0} 秒）";
    }

    public static string ConfirmedStepResult(FeatureDemoAction action, bool hatlessInterruptionObserved,
        bool? remainsStoredBeforeReturn = null) =>
        (action == FeatureDemoAction.HatlessInterruption && !hatlessInterruptionObserved) ||
        (action is FeatureDemoAction.Shatter or FeatureDemoAction.Tear or FeatureDemoAction.HatlessInterruption &&
            remainsStoredBeforeReturn == false) ? "NeedsReview" : "Completed";
}
