using System.Text.Json;
using SoftMochiPet.Core;
using SoftMochiPet.Models;

internal static class MischiefSettingsTests
{
    public static void NewSettingsEnableAutonomousMischief()
    {
        var defaults = new PetSettings();
        Assert(defaults.Version == 5 && defaults.AllowMischief,
            "New installations must use version-five settings with autonomous mischief enabled.");
        InTestDirectory(directory =>
        {
            var store = new SettingsStore(directory);
            var loaded = store.Load();
            Assert(loaded.Version == 5 && loaded.AllowMischief,
                "A missing settings file must receive the same enabled default.");
        });
    }

    public static void LegacySettingsEnableMischiefOnce() => InTestDirectory(directory =>
    {
        var store = new SettingsStore(directory);
        for (var version = 1; version < 5; version++)
        {
            File.WriteAllText(store.SettingsPath, JsonSerializer.Serialize(new PetSettings
            {
                Version = version, AllowMischief = false, CharacterId = PetCharacterProfile.FeibiJiubi.Id,
                ReactToDeletes = false, SoundEnabled = false, VoiceVolume = 0.31,
                FastingMode = true, QuietMode = true, PetSize = 286, WatchedFolders = ["fixture-folder"],
            }));
            var upgraded = store.Load();
            Assert(upgraded.Version == 5 && upgraded.AllowMischief &&
                JsonSerializer.Deserialize<PetSettings>(File.ReadAllText(store.SettingsPath)) is { Version: 5, AllowMischief: true },
                "Every older settings version must persist the one-time enabled upgrade.");
            Assert(upgraded.CharacterId == PetCharacterProfile.FeibiJiubi.Id && !upgraded.ReactToDeletes &&
                upgraded.WatchedFolders.SequenceEqual(new[] { "fixture-folder" }),
                "Mischief migration must preserve character selection, file reactions and watched folders.");
            if (version == 4)
                Assert(!upgraded.SoundEnabled && upgraded.VoiceVolume == 0.31 && upgraded.FastingMode &&
                    upgraded.QuietMode && upgraded.PetSize == 286,
                    "The version-five migration must change only version and mischief, retaining all other version-four preferences.");
            upgraded.AllowMischief = false;
            store.Save(upgraded);
            var disabledJson = File.ReadAllText(store.SettingsPath);
            Assert(!store.Load().AllowMischief && File.ReadAllText(store.SettingsPath) == disabledJson,
                "After upgrading, a user's saved opt-out must survive without a second migration rewrite.");
        }
    });

    public static void VersionFiveOptOutSurvivesReload() => InTestDirectory(directory =>
    {
        var store = new SettingsStore(directory);
        foreach (var version in new[] { 5, 6 })
        {
            File.WriteAllText(store.SettingsPath, JsonSerializer.Serialize(new PetSettings
            {
                Version = version, AllowMischief = false, SoundEnabled = false,
                CharacterId = PetCharacterProfile.FeibiJiubi.Id, PetSize = 330,
            }));
            var before = File.ReadAllText(store.SettingsPath);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var loaded = store.Load();
                Assert(loaded.Version == version && !loaded.AllowMischief && !loaded.SoundEnabled &&
                    loaded.PetSize == 330 && File.ReadAllText(store.SettingsPath) == before,
                    "Current and future settings versions must retain explicit opt-outs across repeated loads.");
            }
        }
    });

    public static void FeatureSessionsDoNotMigrateRealMischiefSettings() => InTestDirectory(directory =>
    {
        var realPath = Path.Combine(directory, "real-settings.json");
        foreach (var version in new[] { 4, 5 })
        {
            var source = JsonSerializer.Serialize(new PetSettings
            {
                Version = version, AllowMischief = false, SoundEnabled = false,
                ReactToDeletes = true, WatchedFolders = ["fixture-folder"],
            });
            File.WriteAllText(realPath, source);
            var session = FeatureDemoSession.Create(realPath, directory);
            var demo = session.SettingsStore.Load();
            Assert(demo.Version == 5 && !demo.AllowMischief && !demo.SoundEnabled &&
                !demo.ReactToDeletes && demo.WatchedFolders.Count == 0,
                "Feature tests must retain their isolated disabled-autonomy settings despite the new application default.");
            Assert(File.ReadAllText(realPath) == source && session.SettingsStore.SettingsPath != realPath,
                "Creating and loading a feature session must never migrate the user's real settings file.");
            demo.AllowMischief = true;
            session.SettingsStore.Save(demo);
            Assert(File.ReadAllText(realPath) == source,
                "Even an explicitly changed demo preference must remain confined to its temporary session.");
        }
    });

    private static void InTestDirectory(Action<string> action)
    {
        var directory = Path.GetFullPath(Path.Combine(Path.GetTempPath(), $"SoftMochiPet.MischiefSettingsTests.{Guid.NewGuid():N}"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
