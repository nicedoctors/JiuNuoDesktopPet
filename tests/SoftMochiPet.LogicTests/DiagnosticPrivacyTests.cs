using SoftMochiPet.Services;

internal static class DiagnosticPrivacyTests
{
    public static void PrivateFieldsAreOpaqueAndCorrelatable()
    {
        var path = Path.Combine(Path.GetTempPath(), "privacy-fixture", "sample.txt");
        var first = DiagnosticPrivacy.Field("Path", path);
        Assert(first.StartsWith("[private:", StringComparison.Ordinal), "Paths must be opaque.");
        Assert(first == DiagnosticPrivacy.Field("OriginalPath", path), "One session must correlate the same target.");
        Assert(first != DiagnosticPrivacy.Field("Path", path + "x"), "Different targets need different references.");
        foreach (var key in new[] { "Name", "DisplayName", "WindowTitle", "Directory", "Folder", "ReportDirectory" })
            Assert(!DiagnosticPrivacy.Field(key, "private-fixture").Contains("private-fixture"), "User content must be redacted.");
        Assert(DiagnosticPrivacy.Field("Phase", "PickHat") == "PickHat", "Diagnostic states must remain readable.");
    }

    public static void FreeTextDoesNotLeakPathsOrInjectLines()
    {
        var path = Path.Combine(Path.GetTempPath(), "privacy-fixture", "sample.txt");
        var output = DiagnosticPrivacy.Message("Failure at " + path + "\r\nforged event");
        Assert(!output.Contains(path) && !output.Contains("sample.txt"), "Full paths and file names must not be logged.");
        Assert(!output.Contains('\n') && !output.Contains('\r'), "Fields cannot inject a new log line.");
        var email = "fixture" + "@" + "example.invalid";
        Assert(!DiagnosticPrivacy.Message(email).Contains(email), "Email text must be redacted.");
        var networkPath = new string('\\', 2) + "fixture-host\\share\\sample.txt";
        Assert(!DiagnosticPrivacy.Message(networkPath).Contains("fixture-host"), "UNC host names must not leak.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
