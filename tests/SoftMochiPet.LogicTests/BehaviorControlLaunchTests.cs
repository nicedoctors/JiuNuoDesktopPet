using System.Xml.Linq;

internal static class BehaviorControlLaunchTests
{
    public static void AppSeparatesModeAndDoesNotActivateControlledPet()
    {
        var root = SourceRoot();
        var app = File.ReadAllText(Path.Combine(root, "src", "SoftMochiPet", "App.xaml.cs"));
        Assert(app.Contains("e.Args.Contains(\"--behavior-control\", StringComparer.Ordinal)", StringComparison.Ordinal) &&
            app.Contains("new MainWindow(behaviorControl: true)", StringComparison.Ordinal),
            "The explicit behavior-control flag must select the manual-mode constructor.");
        Assert(app.Contains("if (behaviorControl || behaviorIsRunning)", StringComparison.Ordinal) &&
            app.Contains("!window.IsFeatureTestMode && !window.IsBehaviorControlMode", StringComparison.Ordinal),
            "Both startup routing and delivered activation callbacks must protect a running control session.");
        Assert(app.Contains("SingleInstance.v2", StringComparison.Ordinal) &&
            app.Contains("BehaviorControl.v1", StringComparison.Ordinal) &&
            app.IndexOf("if (behaviorControl || behaviorIsRunning)", StringComparison.Ordinal) < app.IndexOf("_activationEvent.Set();", StringComparison.Ordinal),
            "Control launches must keep the shared single-instance lock and block before normal activation forwarding.");
    }

    public static void BehaviorPackageUsesIsolatedLauncherAndOutput()
    {
        var root = SourceRoot();
        var c = File.ReadAllText(Path.Combine(root, "launcher", "NuonuoLauncher.c"));
        var rc = File.ReadAllText(Path.Combine(root, "launcher", "NuonuoLauncher.rc"));
        var project = XDocument.Load(Path.Combine(root, "launcher", "NuonuoLauncher.vcxproj"));
        XNamespace ns = project.Root!.Name.Namespace;
        var properties = project.Root.Elements(ns + "PropertyGroup").Single(element =>
            (string?)element.Attribute("Condition") == "'$(BehaviorControlLauncher)'=='true'");
        Assert((string?)properties.Element(ns + "TargetName") == "啾糯桌宠-行为控制版" &&
            ((string?)properties.Element(ns + "OutDir"))?.Contains("\\BehaviorControl\\", StringComparison.Ordinal) == true &&
            ((string?)properties.Element(ns + "IntDir"))?.Contains("\\BehaviorControl\\", StringComparison.Ordinal) == true,
            "The control launcher must have independent output and intermediate compilation directories.");
        Assert(c.Contains("#if defined(BEHAVIOR_CONTROL_LAUNCHER)", StringComparison.Ordinal) &&
            c.Contains("--behavior-control", StringComparison.Ordinal) && c.Contains("UNREFERENCED_PARAMETER(commandLine)", StringComparison.Ordinal) &&
            rc.Contains("啾糯桌宠-行为控制版.exe", StringComparison.Ordinal),
            "The native control launcher must carry only its fixed mode flag and its own filename metadata.");
        var package = File.ReadAllText(Path.Combine(root, "package.ps1"));
        Assert(package.Contains("param([switch]$BehaviorControl)", StringComparison.Ordinal) &&
            package.Contains("$packageName = if ($BehaviorControl) { '啾糯桌宠-行为控制版' } else { '啾糯桌宠-免安装版' }", StringComparison.Ordinal) &&
            !package.Contains("$testToolsDirectory", StringComparison.Ordinal) &&
            !package.Contains("FeatureTestLauncher=true", StringComparison.Ordinal) &&
            package.Contains("BehaviorControl\\啾糯桌宠-行为控制版.exe", StringComparison.Ordinal),
            "Packaging must select only its own folder/ZIP and keep the formal package free of the test directory.");
        Assert(package.Contains("/p:BehaviorControlLauncher=$($BehaviorControl.IsPresent.ToString().ToLowerInvariant())", StringComparison.Ordinal) &&
            package.Contains("$packageDirectory.StartsWith($distPrefix", StringComparison.Ordinal) &&
            package.Contains("$archivePath.StartsWith($distPrefix", StringComparison.Ordinal),
            "The default package must explicitly disable control mode and retain destructive-path containment checks.");
    }

    private static string SourceRoot()
    {
        foreach (var origin in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(origin); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "launcher", "NuonuoLauncher.vcxproj"))) return directory.FullName;
        throw new InvalidOperationException("Mode and package source-contract checks require the project source tree.");
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
