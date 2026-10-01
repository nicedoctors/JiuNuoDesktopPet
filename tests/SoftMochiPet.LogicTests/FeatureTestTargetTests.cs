using System.Diagnostics;
using System.Drawing;
using SoftMochiPet;
using SoftMochiPet.Services;

internal static class FeatureTestTargetTests
{
    public static void HelperArgumentsRequireAnExplicitParentProcess()
    {
        Assert(FeatureTestTargetArguments.TryReadParentPid(
                ["--feibi-feature-target", "--parent-pid", "2345"], out var parentPid) && parentPid == 2345,
            "The helper must accept only the dedicated feature-target switch and a positive parent PID.");
        string[][] invalid =
        [
            [], ["--feibi-feature-target"], ["--parent-pid", "2345"],
            ["--feibi-feature-target", "--parent-pid", "0"],
            ["--feibi-feature-target", "--parent-pid", "-1"],
            ["--feibi-feature-target", "--parent-pid", "other.exe"],
            ["--feibi-feature-target", "--parent-pid", "2147483648"],
            ["--feibi-feature-target", "--parent-pid", "2345", "--anything"],
        ];
        Assert(invalid.All(args => !FeatureTestTargetArguments.TryReadParentPid(args, out _)) &&
            !FeatureTestTargetArguments.TryReadParentPid(null, out _),
            "Missing, malformed and extra arguments cannot accidentally initialize a helper window.");
    }

    public static void HelperLaunchUsesArgumentListAndNoShellOrConsole()
    {
        var native = FeatureTestTargetLaunchPolicy.CreateStartInfo(
            @"C:\Feature Test\啾糯桌宠.exe", null, 2345);
        Assert(native.FileName == @"C:\Feature Test\啾糯桌宠.exe" &&
            native.ArgumentList.SequenceEqual(new[] { "--feibi-feature-target", "--parent-pid", "2345" }) &&
            !native.UseShellExecute && native.CreateNoWindow && native.WindowStyle == ProcessWindowStyle.Hidden &&
            string.IsNullOrEmpty(native.Arguments),
            "The launcher must start its own executable with escaped ArgumentList fields and no shell window.");
        var hosted = FeatureTestTargetLaunchPolicy.CreateStartInfo(
            @"C:\Runtime\dotnet.exe", @"C:\Feature Test\啾糯桌宠.dll", 2345);
        Assert(hosted.ArgumentList.SequenceEqual(new[]
            { @"C:\Feature Test\啾糯桌宠.dll", "--feibi-feature-target", "--parent-pid", "2345" }),
            "A dotnet-hosted run must prepend the actual entry assembly without string-built shell commands.");
        var rejected = false;
        try { FeatureTestTargetLaunchPolicy.CreateStartInfo(@"C:\Runtime\dotnet.exe", null, 2345); }
        catch (ArgumentException) { rejected = true; }
        Assert(rejected, "The helper must not start a bare dotnet process when its entry assembly is unknown.");
    }

    public static void HelperBoundsAndStepTextAreBounded()
    {
        Assert(FeatureTestTargetLaunchPolicy.IsValidBounds(new Rectangle(-1200, -200, 580, 390)),
            "A dedicated target must support negative-coordinate monitors.");
        foreach (var bounds in new[]
        {
            Rectangle.Empty, new Rectangle(0, 0, -1, 200), new Rectangle(0, 0, 200, 99),
            new Rectangle(0, 0, 17000, 200), new Rectangle(int.MaxValue - 10, 0, 580, 390),
        })
            Assert(!FeatureTestTargetLaunchPolicy.IsValidBounds(bounds),
                "Invalid sizes and overflowing native bounds must be rejected before any window mutation.");
        Assert(FeatureTestTargetLaunchPolicy.StepTitle(null) == FeatureTestTargetWindow.WindowTitle &&
            FeatureTestTargetLaunchPolicy.StepTitle("\r\n踢一下\0").EndsWith(" · 踢一下", StringComparison.Ordinal) &&
            FeatureTestTargetLaunchPolicy.StepTitle(new string('测', 200)).Length <=
                FeatureTestTargetWindow.WindowTitle.Length + 3 + 80,
            "Step labels must keep the dedicated-window identity and omit unbounded or control-character text.");
    }

    public static void HelperPreparationRequiresAcknowledgedLayerAndGeometry()
    {
        var expected = new Rectangle(-1200, 200, 650, 320);
        bool Ready(bool visible = true, bool minimized = false, bool topmost = false, bool expectedTopmost = false,
            Rectangle? actual = null) => FeatureTestTargetLaunchPolicy.IsPreparationConfirmed(
                visible, minimized, topmost, expectedTopmost, actual ?? expected, expected);
        Assert(Ready(topmost: true, expectedTopmost: true) && Ready(),
            "Promotion and demotion each complete only when the actual layer matches their own requested phase.");
        Assert(!Ready(expectedTopmost: true) && !Ready(topmost: true),
            "Matching coordinates cannot acknowledge a queued or unperformed layer transition.");
        Assert(!Ready(visible: false) && !Ready(minimized: true) &&
            !Ready(actual: new Rectangle(-1100, 200, 650, 320)) &&
            !Ready(actual: new Rectangle(-1200, 200, 700, 320)) && !Ready(actual: Rectangle.Empty),
            "A hidden, minimized, wrongly positioned, resized, or missing helper cannot satisfy preparation.");
        Assert(Ready(actual: new Rectangle(-1199, 201, 650, 320)),
            "Native pixel rounding remains acceptable after the correct layer is observed.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
