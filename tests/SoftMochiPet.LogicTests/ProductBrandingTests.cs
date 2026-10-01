using System.Reflection;
using System.Resources;
using SoftMochiPet.Core;
using SoftMochiPet.Services;

internal static class ProductBrandingTests
{
    public static void ProductionAssemblyAndHelperCommandsUseOfficialName()
    {
        const string productName = "啾糯桌宠";
        var assembly = typeof(PetCharacterProfile).Assembly;
        Assert(assembly.GetName().Name == productName &&
            Path.GetFileName(assembly.Location) == $"{productName}.dll" &&
            assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product == productName &&
            assembly.GetCustomAttribute<AssemblyTitleAttribute>()?.Title == productName,
            "The production assembly, file, product and title must use the same official name.");

        using var resources = assembly.GetManifestResourceStream($"{productName}.g.resources")
            ?? throw new InvalidOperationException("The renamed WPF assembly must retain its compiled resource bundle.");
        using var reader = new ResourceReader(resources);
        var entries = reader.GetEnumerator();
        var mainWindowFound = false;
        while (entries.MoveNext())
            mainWindowFound |= string.Equals(entries.Key as string, "mainwindow.baml", StringComparison.Ordinal);
        Assert(mainWindowFound, "The renamed resource bundle must include MainWindow BAML without creating a window.");

        var appHost = Path.ChangeExtension(assembly.Location, ".exe");
        var native = FeatureTestTargetLaunchPolicy.CreateStartInfo(appHost, assembly.Location, 2345);
        Assert(native.FileName == appHost && !native.UseShellExecute && native.CreateNoWindow &&
            native.ArgumentList.SequenceEqual(new[] { "--feibi-feature-target", "--parent-pid", "2345" }),
            "The native helper command must keep the renamed app host and only its fixed helper arguments.");
        var dotnetHost = Path.Combine(Path.GetDirectoryName(assembly.Location)!, "dotnet.exe");
        var managed = FeatureTestTargetLaunchPolicy.CreateStartInfo(dotnetHost, assembly.Location, 2345);
        Assert(managed.FileName == dotnetHost && managed.ArgumentList.SequenceEqual(new[]
            { assembly.Location, "--feibi-feature-target", "--parent-pid", "2345" }),
            "Development helper commands must reference the actual renamed entry assembly, not the old file name.");
        Assert(StartupRegistrationService.BuildRunCommand(appHost) == $"\"{appHost}\"",
            "The Windows startup command must preserve and quote the renamed executable path.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
