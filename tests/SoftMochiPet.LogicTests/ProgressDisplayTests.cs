using SoftMochiPet.Core;
using SoftMochiPet.Models;

internal static class ProgressDisplayTests
{
    public static void ModeCombinationsKeepBothBarsInSync()
    {
        foreach (var coexistence in new[] { false, true })
        foreach (var fasting in new[] { false, true })
        foreach (var infinite in new[] { false, true })
        {
            var settings = new PetSettings
            {
                CoexistenceMode = coexistence, FastingMode = fasting, InfiniteMode = infinite
            };
            var nuonuo = Visible(PetCharacterProfile.Nuonuo, settings);
            var feibi = Visible(PetCharacterProfile.FeibiJiubi, settings);
            Assert(nuonuo == (!fasting && !infinite), "Nuonuo's existing hunger visibility must be preserved.");
            Assert(feibi == (!infinite && (!coexistence || !fasting)),
                "Solo Feibi ignores fasting; in coexistence she uses the same visibility as Nuonuo.");
            if (coexistence) Assert(nuonuo == feibi, "Both bars must always open and close together.");
        }
    }

    public static void ModeTransitionsRespectRemainingModes()
    {
        var settings = new PetSettings { CoexistenceMode = true };
        ExpectBoth(settings, true);
        settings.FastingMode = true;
        ExpectBoth(settings, false);
        settings.InfiniteMode = true;
        settings.FastingMode = false;
        ExpectBoth(settings, false);
        settings.FastingMode = true;
        settings.InfiniteMode = false;
        ExpectBoth(settings, false);
        settings.CoexistenceMode = false;
        Assert(Visible(PetCharacterProfile.FeibiJiubi, settings) &&
            !Visible(PetCharacterProfile.Nuonuo, settings), "Leaving coexistence must restore solo visibility.");
        settings.CoexistenceMode = true;
        ExpectBoth(settings, false);
        settings.FastingMode = false;
        ExpectBoth(settings, true);
    }

    public static void PreferencesSurviveRestartForEitherPrimary()
    {
        var directory = Directory.CreateTempSubdirectory("SoftMochiPet-progress-tests-").FullName;
        try
        {
            var store = new SettingsStore(directory);
            foreach (var primary in PetCharacterProfile.All)
            foreach (var fasting in new[] { false, true })
            foreach (var infinite in new[] { false, true })
            {
                store.Save(new PetSettings
                {
                    CharacterId = primary.Id, CoexistenceMode = true,
                    FastingMode = fasting, InfiniteMode = infinite, QuietMode = true, AllowMischief = false
                });
                var restored = store.Load();
                ExpectBoth(restored, !fasting && !infinite);
                Assert(restored.CharacterId == primary.Id && restored.QuietMode && !restored.AllowMischief,
                    "Shared visibility must not change character selection or independent activity preferences.");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static bool Visible(PetCharacterProfile character, PetSettings settings) =>
        PetModePolicy.ShouldShowProgressDisplay(character,
            settings.FastingMode, settings.InfiniteMode, settings.CoexistenceMode);

    private static void ExpectBoth(PetSettings settings, bool visible)
    {
        foreach (var character in PetCharacterProfile.All)
            Assert(Visible(character, settings) == visible, "Both characters must reflect shared mode changes immediately.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
