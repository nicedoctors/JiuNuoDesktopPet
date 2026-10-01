using System.Text.Json;
using SoftMochiPet.Core;
using SoftMochiPet.Models;

internal static class BehaviorControlSessionTests
{
    public static void FirstUseCopiesOnlyPresentationWithoutNormalMigration() => InDirectory(directory =>
    {
        var real = Path.Combine(directory, "settings.json");
        const string source = """
            {"Version":1,"CharacterId":"FEIBIJIUBI","PetSize":286,"VoiceVolume":0.31,"SoundEnabled":false,
             "AllowMischief":true,"ReactToDeletes":true,"QuietMode":true,"FastingMode":true,
             "WatchedFolders":{"must-not-be-deserialized":true}}
            """;
        File.WriteAllText(real, source);
        var session = BehaviorControlSession.Create(real, directory);
        var settings = session.Settings;
        Assert(File.ReadAllText(real) == source, "First control launch must not migrate or rewrite ordinary settings.");
        Assert(settings.CharacterId == PetCharacterProfile.FeibiJiubi.Id && settings.PetSize == 286 &&
            settings.VoiceVolume == 0.31 && !settings.SoundEnabled,
            "Only the four requested ordinary presentation preferences must transfer.");
        Assert(settings.Version >= 5 && !settings.AllowMischief && !settings.ReactToDeletes &&
            !settings.QuietMode && !settings.FastingMode && settings.WatchedFolders.Count == 0,
            "Normal autonomy, life modes and watched folders must not be imported into manual control.");
        Assert(session.DirectoryPath == Path.GetFullPath(Path.Combine(directory, "SoftMochiPet", "behavior-control")) &&
            !File.ReadAllText(session.SettingsStore.SettingsPath).Contains("must-not-be-deserialized", StringComparison.Ordinal),
            "Manual control needs its fixed isolated directory without any copied watched-folder data.");
    });

    public static void PersistentControlSettingsIgnoreLaterNormalChanges() => InDirectory(directory =>
    {
        var real = Path.Combine(directory, "normal.json");
        File.WriteAllText(real, "{\"CharacterId\":\"nuonuo\",\"PetSize\":220,\"SoundEnabled\":true}");
        var first = BehaviorControlSession.Create(real, directory);
        first.Settings.CharacterId = PetCharacterProfile.FeibiJiubi.Id;
        first.Settings.PetSize = 330;
        first.Settings.SoundEnabled = false;
        first.Settings.VoiceVolume = 0.2;
        first.Settings.FastingMode = true;
        first.Settings.QuietMode = true;
        first.Settings.AllowMischief = false;
        first.SettingsStore.Save(first.Settings);
        const string later = "{\"Version\":4,\"CharacterId\":\"nuonuo\",\"PetSize\":132,\"SoundEnabled\":true,\"AllowMischief\":true}";
        File.WriteAllText(real, later);
        var second = BehaviorControlSession.Create(real, directory);
        Assert(second.DirectoryPath == first.DirectoryPath && second.Settings.CharacterId == PetCharacterProfile.FeibiJiubi.Id &&
            second.Settings.PetSize == 330 && !second.Settings.SoundEnabled && second.Settings.VoiceVolume == 0.2 &&
            second.Settings.QuietMode && second.Settings.FastingMode && !second.Settings.AllowMischief,
            "Control preferences must persist instead of re-importing the normal profile each run.");
        Assert(File.ReadAllText(real) == later && !second.SettingsStore.Load().AllowMischief,
            "Persistent control settings must not run old-version normal migration or overwrite the normal file.");
    });

    public static void CharacterStoresRemainInsideControlDirectory() => InDirectory(directory =>
    {
        var normalRoot = Path.Combine(directory, "SoftMochiPet");
        var normalLife = new PetLifeStateStore(normalRoot);
        normalLife.Save(new PetLifeState { TotalMeals = 91 });
        var before = File.ReadAllText(normalLife.StatePath);
        var session = BehaviorControlSession.Create(Path.Combine(directory, "missing.json"), directory);
        foreach (var profile in PetCharacterProfile.All)
        {
            var own = session.LifeStateDirectory(profile);
            Assert(Path.GetRelativePath(session.DirectoryPath, own) == Path.Combine("characters", profile.Id),
                "Each control character must keep life and mischief beneath its own isolated subdirectory.");
            var life = new PetLifeStateStore(own);
            Assert(life.Load().TotalMeals == 0, "Manual control must not import ordinary character life state.");
            life.Save(new PetLifeState { TotalMeals = 2 });
            var mischief = new MischiefStateStore(own);
            mischief.Save(new MischiefState { Value = 42 });
            Assert(mischief.Load().Value == 42 && life.Load().TotalMeals == 2,
                "Control life and mischief stores must persist normally within their own character directory.");
        }
        Assert(File.ReadAllText(normalLife.StatePath) == before && normalLife.Load().TotalMeals == 91,
            "Manual state writes must leave the real life-state file byte-for-byte intact.");
    });

    public static void InvalidPreferencesKeepControlDefaults() => InDirectory(directory =>
    {
        foreach (var source in new[] { "[]", "null", "{broken", "{\"PetSize\":\"invalid\",\"AllowMischief\":true}" })
        {
            var branch = Path.Combine(directory, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(branch);
            var normal = Path.Combine(branch, "normal.json");
            File.WriteAllText(normal, source);
            var session = BehaviorControlSession.Create(normal, branch);
            Assert(!session.Settings.AllowMischief && !session.Settings.ReactToDeletes &&
                session.Settings.PetSize == PetSizePolicy.DefaultSize && File.ReadAllText(normal) == source,
                "Malformed ordinary preferences must keep safe manual defaults and never invoke migration.");
            File.WriteAllText(session.SettingsStore.SettingsPath, "{\"PetSize\":-20,\"VoiceVolume\":2,\"WatchedFolders\":{}}");
            var reloaded = BehaviorControlSession.Create(normal, branch);
            Assert(!reloaded.Settings.AllowMischief && !reloaded.Settings.ReactToDeletes &&
                reloaded.Settings.PetSize == PetSizePolicy.FromPercent(PetSizePolicy.MinimumPercent) &&
                reloaded.Settings.VoiceVolume == 1 && reloaded.Settings.WatchedFolders.Count == 0,
                "Incomplete control preferences must stay disabled and normalize only their own allowed fields.");
            File.WriteAllText(reloaded.SettingsStore.SettingsPath, "{broken");
            var damaged = BehaviorControlSession.Create(normal, branch);
            Assert(!damaged.Settings.AllowMischief && !damaged.Settings.ReactToDeletes,
                "A damaged control profile must not fall back to ordinary autonomous-enabled defaults.");
        }
    });

    private static void InDirectory(Action<string> action)
    {
        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"SoftMochiPet.BehaviorControlTests.{Guid.NewGuid():N}"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
