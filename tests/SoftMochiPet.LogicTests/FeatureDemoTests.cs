using SoftMochiPet.Core;
using SoftMochiPet.Models;
using SoftMochiPet.Services;

internal static class FeatureDemoTests
{
    public static void PlanIsCompleteBoundedAndAvoidsSystemChanges()
    {
        var steps = FeatureDemoPlan.Steps;
        Assert(FeatureDemoPlan.Revision == 6 && steps.Count == 8 &&
               steps.Select(step => step.Action).SequenceEqual(Enum.GetValues<FeatureDemoAction>()) &&
               steps.Select(step => step.Id).SequenceEqual(new[] { "Kick", "Punch", "Charge", "HatStoreReturn",
                   "Tease", "Shatter", "Tear", "HatlessInterruption" }),
            "The focused demonstration must contain eight character-specific checks, with exactly one hat-storage step.");
        Assert(steps.All(step => !string.IsNullOrWhiteSpace(step.Title) &&
                               double.IsFinite(step.TimeoutSeconds) && step.TimeoutSeconds is >= 1 and <= 60) &&
               steps.Select(step => step.Id).Distinct(StringComparer.Ordinal).Count() == steps.Count,
            "Each step needs a stable unique report ID and a bounded animation timeout.");
        Assert(steps[^1].Action == FeatureDemoAction.HatlessInterruption &&
               FeatureDemoPlan.ManualChecks.Select(check => check.Id).SequenceEqual(
                   new[] { "PrankAppearance", "PrankTakeover", "PrankVoice" }),
            "Reused behavior, size and system-setting checks must stay outside this focused demonstration.");
    }

    public static void CompletionEvidenceRejectsStaleWrongAndUnmovedWindows()
    {
        var bounds = new System.Drawing.Rectangle(100, 200, 640, 320);
        var handle = new IntPtr(42);
        var moved = new WindowPrankCompletionEvidence(3, "Kick", handle, 50, bounds,
            new System.Drawing.Rectangle(50, 200, 640, 320), false, false);
        Assert(FeatureDemoEvidencePolicy.Matches(FeatureDemoAction.Kick, 2, handle, 50, moved),
            "Confirmed movement of the intended window must pass.");
        foreach (var invalid in new[] { moved with { Sequence = 2 }, moved with { Handle = new IntPtr(43) },
            moved with { ProcessId = 51 }, moved with { Operation = "Punch" }, moved with { FinalOuterBounds = bounds } })
            Assert(!FeatureDemoEvidencePolicy.Matches(FeatureDemoAction.Kick, 2, handle, 50, invalid),
                "An old event, other target, wrong action or unmoved window must not count as success.");
        Assert(!FeatureDemoEvidencePolicy.Matches(FeatureDemoAction.Kick, 2, handle, 50, null),
            "Returning to Idle without a native completion event is not success.");
    }

    public static void MeterPreparationDoesNotRequireAWindow()
    {
        Assert(!FeatureDemoPlan.Steps.Single(step => step.Action == FeatureDemoAction.Tease).RequiresWindowTarget &&
            FeatureDemoPlan.Steps.Where(step => step.Action != FeatureDemoAction.Tease).All(step => step.RequiresWindowTarget),
            "Charging the pet's meter must not inspect or require a window; every actual window prank still must.");
        var meter = new MischiefController();
        for (var index = 0; index < 9; index++)
        {
            meter.Tick(1.6, false);
            Assert(meter.Tease(), "The demo's isolated tease sequence must charge without a target or autonomous pranks.");
        }
        Assert(meter.Value == 100 && !meter.AutonomousActionDue,
            "The standalone meter step must finish full without dispatching an autonomous attack.");
    }

    public static void HeldWindowsWaitForManualRestoreWithoutCountdown()
    {
        Assert(FeatureDemoPlan.Steps.Where(step => step.RequiresManualRestore).Select(step => step.Action)
            .SequenceEqual(new[] { FeatureDemoAction.HatStoreReturn, FeatureDemoAction.Shatter,
                FeatureDemoAction.Tear, FeatureDemoAction.HatlessInterruption }),
            "Every held snapshot, including an interrupted burst, must require a real manual restoration request.");
        Assert(new[] { 0d, 3d, 12d, 60d, 3600d }.Select(FeatureDemoPlan.ManualRestoreHint).SequenceEqual(
            new[] { "等待右键恢复（已等待 0 秒）", "等待右键恢复（已等待 3 秒）", "等待右键恢复（已等待 12 秒）",
                "等待右键恢复（已等待 60 秒）", "等待右键恢复（已等待 3600 秒）" }),
            "Human waiting must count elapsed time without announcing or scheduling automatic restoration.");
        Assert(new[] { -1d, double.NaN, double.PositiveInfinity }.All(value =>
                FeatureDemoPlan.ManualRestoreHint(value) == "等待右键恢复（已等待 0 秒）") &&
            FeatureDemoPlan.RestoreTimeoutSeconds > 0 && FeatureDemoPlan.RestoreTimeoutSeconds <= 60,
            "The wait label must handle invalid clocks; native restoration keeps its own bounded deadline.");
        Assert(FeatureDemoPlan.Steps.All(step => step.CompletionAction ==
            (step.Action == FeatureDemoAction.HatlessInterruption ? FeatureDemoAction.Shatter : step.Action)),
            "The interrupted burst must verify the same real Shatter completion as its held snapshot.");
        Assert(FeatureDemoPlan.ConfirmedStepResult(FeatureDemoAction.HatlessInterruption, false) == "NeedsReview" &&
            FeatureDemoPlan.ConfirmedStepResult(FeatureDemoAction.HatlessInterruption, true) == "Completed" &&
            FeatureDemoPlan.Steps.Where(step => step.Action != FeatureDemoAction.HatlessInterruption).All(step =>
                FeatureDemoPlan.ConfirmedStepResult(step.Action, false) == "Completed"),
            "An early user restoration is valid but cannot claim the unfinished hatless demonstration was observed.");
        foreach (var action in new[] { FeatureDemoAction.Shatter, FeatureDemoAction.Tear, FeatureDemoAction.HatlessInterruption })
            Assert(FeatureDemoPlan.ConfirmedStepResult(action, true, false) == "NeedsReview" &&
                FeatureDemoPlan.ConfirmedStepResult(action, true, true) == "Completed",
                "Restoring before the destroyed pieces enter the hat cannot pass the collection demonstration.");
    }

    public static void SnapshotEvidenceRequiresMinimizingAndRestoringTheSameWindow()
    {
        var bounds = new System.Drawing.Rectangle(100, 200, 640, 320);
        var handle = new IntPtr(42);
        var restored = new WindowPrankCompletionEvidence(3, "Hat", handle, 50, bounds, bounds, true, true);
        Assert(FeatureDemoEvidencePolicy.Matches(FeatureDemoAction.HatStoreReturn, 2, handle, 50, restored),
            "Confirmed hat return must pass.");
        Assert(!FeatureDemoEvidencePolicy.Matches(FeatureDemoAction.HatStoreReturn, 2, handle, 50,
                restored with { MinimizeConfirmed = false }) &&
            !FeatureDemoEvidencePolicy.Matches(FeatureDemoAction.HatStoreReturn, 2, handle, 50,
                restored with { RestoreConfirmed = false }) &&
            !FeatureDemoEvidencePolicy.Matches(FeatureDemoAction.HatStoreReturn, 2, handle, 50,
                restored with { FinalOuterBounds = new System.Drawing.Rectangle(200, 200, 640, 320) }),
            "Snapshot completion requires actual minimize, actual restore and original bounds.");
        var interrupted = FeatureDemoPlan.Steps.Single(step => step.Action == FeatureDemoAction.HatlessInterruption);
        var shatter = restored with { Operation = "Shatter" };
        Assert(FeatureDemoEvidencePolicy.Matches(interrupted.CompletionAction, 2, handle, 50, shatter),
            "An interrupted burst must use actual restore evidence for its own Shatter snapshot.");
        foreach (var invalid in new[] { shatter with { Sequence = 2 }, shatter with { Handle = new IntPtr(43) },
            shatter with { ProcessId = 51 }, shatter with { MinimizeConfirmed = false },
            shatter with { RestoreConfirmed = false }, restored })
            Assert(!FeatureDemoEvidencePolicy.Matches(interrupted.CompletionAction, 2, handle, 50, invalid),
                "Hat recovery alone or a stale, different, unminimized or unrestored target cannot pass interruption.");
    }

    public static void SessionPreservesMuteAndNeverMigratesRealSettings()
    {
        InTestDirectory(directory =>
        {
            var original = Path.Combine(directory, "settings.json");
            const string source = """
                {"Version":1,"SoundEnabled":false,"VoiceVolume":0.31,"PetSize":286,
                 "QuietMode":true,"AllowMischief":true,"ReactToDeletes":true,
                 "CharacterId":"nuonuo","WatchedFolders":["fixture-folder"]}
                """;
            File.WriteAllText(original, source);
            var session = FeatureDemoSession.Create(original, directory);
            var preferences = session.SettingsStore.Load();
            Assert(File.ReadAllText(original) == source,
                "Creating a demo must not migrate or rewrite the real settings file.");
            Assert(!preferences.SoundEnabled && preferences.VoiceVolume == 0.31 && preferences.PetSize == 286,
                "Demo appearance and audio must retain the user's explicit preferences.");
            Assert(preferences.CharacterId == PetCharacterProfile.FeibiJiubi.Id && !preferences.QuietMode &&
                   !preferences.AllowMischief && !preferences.ReactToDeletes && preferences.WatchedFolders.Count == 0,
                "Demo playback must not enable autonomous pranks or watch real files.");
            session.Settings.SoundEnabled = true;
            session.SettingsStore.Save(session.Settings);
            Assert(File.ReadAllText(original) == source &&
                   !File.ReadAllText(session.SettingsStore.SettingsPath).Contains("fixture-folder", StringComparison.Ordinal),
                "Saving demo preferences must remain isolated and exclude watched-folder data.");
        });
    }

    public static void SessionsIsolateBothCharacterSavesAndRetainReports()
    {
        InTestDirectory(directory =>
        {
            var missing = Path.Combine(directory, "missing-settings.json");
            var first = FeatureDemoSession.Create(missing, directory);
            var second = FeatureDemoSession.Create(missing, directory);
            var expectedParent = Path.Combine(directory, "SoftMochiPet.FeatureTest");
            Assert(first.DirectoryPath != second.DirectoryPath &&
                   Path.GetDirectoryName(first.DirectoryPath) == expectedParent &&
                   Guid.TryParseExact(Path.GetFileName(first.DirectoryPath), "N", out _),
                "Every demonstration must have a fresh uniquely named session directory.");
            var nuonuo = new PetLifeStateStore(first.LifeStateDirectory(PetCharacterProfile.Nuonuo));
            var feibi = new PetLifeStateStore(first.LifeStateDirectory(PetCharacterProfile.FeibiJiubi));
            Assert(nuonuo.StateDirectory != feibi.StateDirectory &&
                   Path.GetRelativePath(first.DirectoryPath, nuonuo.StateDirectory) == Path.Combine("characters", "nuonuo") &&
                   Path.GetRelativePath(first.DirectoryPath, feibi.StateDirectory) == Path.Combine("characters", "feibijiubi"),
                "Both characters' demo saves must remain under the current session, not real AppData.");
            nuonuo.Save(new PetLifeState { TotalMeals = 7 });
            feibi.Save(new PetLifeState { TotalMeals = 2 });
            Assert(nuonuo.Load().TotalMeals == 7 && feibi.Load().TotalMeals == 2 &&
                   new PetLifeStateStore(second.LifeStateDirectory(PetCharacterProfile.FeibiJiubi)).Load().TotalMeals == 0,
                "Demo save data must not leak between characters or sessions.");
            var report = Path.Combine(first.DirectoryPath, "report.json");
            File.WriteAllText(report, "{}");
            _ = FeatureDemoSession.Create(missing, directory);
            Assert(File.Exists(report), "Starting another demo must retain earlier reports and session data.");
        });
    }

    public static void SessionHandlesInvalidPreferencesWithoutEnablingSound()
    {
        InTestDirectory(directory =>
        {
            var settingsPath = Path.Combine(directory, "settings.json");
            foreach (var json in new[] { "{broken", "null", "[]", "{\"SoundEnabled\":\"false\"}" })
            {
                File.WriteAllText(settingsPath, json);
                var session = FeatureDemoSession.Create(settingsPath, directory);
                Assert(!session.Settings.SoundEnabled && File.ReadAllText(settingsPath) == json,
                    "Unreadable or malformed preferences must keep the demo silent and leave the original intact.");
            }
            File.WriteAllText(settingsPath, "{\"SoundEnabled\":true,\"VoiceVolume\":2,\"PetSize\":-1}");
            var enabled = FeatureDemoSession.Create(settingsPath, directory);
            Assert(enabled.Settings.SoundEnabled && enabled.Settings.VoiceVolume == 1 &&
                   enabled.Settings.PetSize == PetSizePolicy.FromPercent(PetSizePolicy.MinimumPercent),
                "Valid sound opt-in is retained and numeric presentation preferences are clamped.");
        });
    }

    private static void InTestDirectory(Action<string> action)
    {
        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"SoftMochiPet.FeatureDemoTests.{Guid.NewGuid():N}"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
