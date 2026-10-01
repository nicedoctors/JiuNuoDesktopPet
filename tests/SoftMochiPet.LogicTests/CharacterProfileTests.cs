using System.Text.Json;
using SoftMochiPet.Core;
using SoftMochiPet.Models;

internal static class CharacterProfileTests
{
    public static void ProfilesPreserveLegacyAssetsAndIsolateCharacters()
    {
        var nuonuo = PetCharacterProfile.Nuonuo;
        var feibi = PetCharacterProfile.FeibiJiubi;
        Assert(PetCharacterProfile.All.Count == 2 && PetCharacterProfile.All.Distinct().Count() == 2,
            "The catalog must expose both distinct characters.");
        Assert(PetCharacterProfile.Get(null) == nuonuo && PetCharacterProfile.Get("unknown") == nuonuo &&
            PetCharacterProfile.Get("FEIBIJIUBI") == feibi,
            "Unknown character IDs must fall back to Nuonuo; valid IDs are case-insensitive.");
        Assert(nuonuo.RuntimeRelativeDirectory == Path.Combine("assets", "sprites", "runtime") &&
            nuonuo.VoiceRelativeDirectory == Path.Combine("assets", "audio", "voice") &&
            nuonuo.IconRelativePath == Path.Combine("assets", "pet.ico") && nuonuo.HasVoice,
            "Nuonuo must retain all original asset locations and recordings.");
        Assert(feibi.RuntimeRelativeDirectory == Path.Combine("assets", "characters", "feibijiubi", "runtime") &&
            feibi.VoiceRelativeDirectory == Path.Combine("assets", "characters", "feibijiubi", "audio") &&
            feibi.IconRelativePath == Path.Combine("assets", "characters", "feibijiubi", "pet.ico") && feibi.HasVoice,
            "Feibi Jiubi must never borrow Nuonuo's art or voice recordings.");
        Assert(nuonuo.MovementSpeedMultiplier == 1 && nuonuo.MaximumIdleSeconds == 5 &&
            feibi.MovementSpeedMultiplier == 1.15 && feibi.MaximumIdleSeconds == 4,
            "Feibi Jiubi's movement and idle timing must be livelier without changing Nuonuo.");
        Assert(nuonuo.Geometry == new PetCharacterGeometry(
                SpriteGeometry.FootCanvasX, SpriteGeometry.FootCanvasY,
                45, 267, 60, 334, 407, 96, 92, 416, 466, 191, 304),
            "Nuonuo's original sprite contact and collision geometry must remain unchanged.");
        Assert(feibi.Geometry.FootCanvasX == SpriteGeometry.FootCanvasX &&
            feibi.Geometry.FootCanvasY == SpriteGeometry.FootCanvasY,
            "Both normalized sprite sets must share the runtime foot baseline.");
    }

    public static void SettingsPreserveLegacyPreferencesAndNormalizeCharacterIds()
    {
        var directory = CreateTestDirectory();
        try
        {
            var store = new SettingsStore(directory);
            const string legacyJson = """
                {"Version":4,"ReactToDeletes":false,"SoundEnabled":false,"FastingMode":true,
                "QuietMode":true,"VoiceVolume":0.31,"PetSize":220,"WatchedFolders":[]}
                """;
            File.WriteAllText(store.SettingsPath, legacyJson);
            var settings = store.Load();
            Assert(settings.CharacterId == PetCharacterProfile.Nuonuo.Id &&
                !settings.ReactToDeletes && !settings.SoundEnabled && settings.FastingMode &&
                settings.QuietMode && !settings.ShowSpeechBubbles &&
                settings.VoiceVolume == 0.31 && settings.PetSize == 220,
                "Adding characters must not overwrite any existing preference.");
            Assert(settings.Version == 5 && settings.AllowMischief &&
                JsonSerializer.Deserialize<PetSettings>(File.ReadAllText(store.SettingsPath)) is { Version: 5, AllowMischief: true },
                "Version-four settings must persist the approved one-time autonomous-mischief migration without changing other preferences.");

            settings.CharacterId = "FEIBIJIUBI";
            store.Save(settings);
            Assert(store.Load().CharacterId == PetCharacterProfile.FeibiJiubi.Id,
                "The selected character must persist with its canonical ID.");
            foreach (var id in new string?[] { null, string.Empty, "missing", "../../outside" })
            {
                var json = JsonSerializer.Serialize(new { CharacterId = id });
                var normalized = JsonSerializer.Deserialize<PetSettings>(json);
                Assert(normalized?.CharacterId == PetCharacterProfile.Nuonuo.Id,
                    "Invalid and path-like character IDs must resolve to the safe legacy default.");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static void CoexistenceAndInfinitePreferencesPersistWithoutAffectingCharacterStores()
    {
        var directory = CreateTestDirectory();
        try
        {
            var store = new SettingsStore(directory);
            var settings = store.Load();
            Assert(!settings.CoexistenceMode && !settings.InfiniteMode && !settings.ShowSpeechBubbles,
                "Existing installations must keep single-character, finite and bubble-free modes by default.");
            settings.CoexistenceMode = true;
            settings.InfiniteMode = true;
            settings.ShowSpeechBubbles = true;
            settings.QuietMode = true;
            settings.PetSize = 260;
            settings.CharacterId = PetCharacterProfile.FeibiJiubi.Id;
            store.Save(settings);
            var reloaded = store.Load();
            Assert(reloaded.CoexistenceMode && reloaded.InfiniteMode && reloaded.ShowSpeechBubbles && reloaded.QuietMode &&
                reloaded.PetSize == 260 && reloaded.CharacterId == PetCharacterProfile.FeibiJiubi.Id,
                "Shared appearance and activity preferences must survive restart together.");
            Assert(PetCharacterProfile.Nuonuo.LifeStateDirectory(directory) !=
                PetCharacterProfile.FeibiJiubi.LifeStateDirectory(directory),
                "Coexistence must not merge the characters' separate life-state stores.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static void PairDialogueUsesBothVoicesAndShortAlternatingLines()
    {
        var exchanges = PairDialogueCatalog.Exchanges;
        Assert(exchanges.Count >= 8 && exchanges.Any(exchange => exchange.FeibiFirst) &&
            exchanges.Any(exchange => !exchange.FeibiFirst),
            "Pair dialogue needs variety with both characters taking the opening line.");
        Assert(exchanges.All(exchange => !string.IsNullOrWhiteSpace(exchange.First) &&
            !string.IsNullOrWhiteSpace(exchange.Reply) && exchange.First.Length <= 16 &&
            exchange.Reply.Length <= 16 && exchange.First != exchange.Reply),
            "Every exchange must have two distinct, short and visible lines.");
        Assert(exchanges.Select(exchange => (exchange.First, exchange.Reply)).Distinct().Count() == exchanges.Count,
            "Pair dialogue must not repeat an identical exchange in its catalog.");
    }

    public static void LifeStateRemainsIndependentForEachCharacter()
    {
        var appData = CreateTestDirectory();
        try
        {
            var now = new DateTimeOffset(2026, 9, 7, 1, 0, 0, TimeSpan.Zero);
            var nuonuoDirectory = PetCharacterProfile.Nuonuo.LifeStateDirectory(appData);
            var feibiDirectory = PetCharacterProfile.FeibiJiubi.LifeStateDirectory(appData);
            Assert(nuonuoDirectory == Path.Combine(appData, "SoftMochiPet") &&
                feibiDirectory == Path.Combine(appData, "SoftMochiPet", "characters", "feibijiubi"),
                "Nuonuo must keep the old save path while Feibi Jiubi receives a dedicated directory.");
            var nuonuo = new PetLifeStateStore(nuonuoDirectory, () => now);
            var feibi = new PetLifeStateStore(feibiDirectory, () => now);
            nuonuo.Save(new PetLifeState { TotalMeals = 17, Hunger = 42 });
            var oldSave = File.ReadAllText(nuonuo.StatePath);
            Assert(feibi.Load().TotalMeals == 0,
                "A new character must not inherit the other character's meal history.");
            feibi.Save(new PetLifeState { TotalMeals = 3, Hunger = 8 });
            Assert(File.ReadAllText(nuonuo.StatePath) == oldSave &&
                nuonuo.Load().TotalMeals == 17 && feibi.Load().TotalMeals == 3,
                "Saving Feibi Jiubi must leave Nuonuo's existing life state byte-for-byte intact.");
        }
        finally
        {
            Directory.Delete(appData, recursive: true);
        }
    }

    public static void FeibiDialogueIsCompleteDistinctAndShort()
    {
        var originalLines = Enum.GetValues<DialogueCue>()
            .SelectMany(cue => PetDialogueCatalog.GetLines(cue)).ToHashSet(StringComparer.Ordinal);
        var selector = new PetDialogueSelector(new Random(20260907), PetCharacterProfile.FeibiJiubi);
        foreach (var cue in Enum.GetValues<DialogueCue>())
        {
            var lines = PetDialogueCatalog.GetLines(cue, PetCharacterProfile.FeibiJiubi);
            Assert(lines.Count >= 4 && lines.Distinct(StringComparer.Ordinal).Count() == lines.Count,
                $"Feibi Jiubi needs distinct alternatives for every cue: {cue}.");
            Assert(lines.All(line => !string.IsNullOrWhiteSpace(line) && line.Length <= 7 &&
                !line.Contains("糯糯", StringComparison.Ordinal) && !originalLines.Contains(line)),
                $"Feibi Jiubi must have her own short lines, never Nuonuo's: {cue}.");
            string? previous = null;
            for (var index = 0; index < 20; index++)
            {
                var current = selector.Choose(cue, "测试点心");
                Assert(current != previous && lines.Contains(current) &&
                    !current.Contains("{item}", StringComparison.Ordinal),
                    $"Character selection and non-repeating dialogue must hold for {cue}.");
                previous = current;
            }
        }

        var defaultSelector = new PetDialogueSelector(new Random(42));
        var nuonuoSelector = new PetDialogueSelector(new Random(42), PetCharacterProfile.Nuonuo);
        foreach (var cue in Enum.GetValues<DialogueCue>())
        {
            Assert(defaultSelector.Choose(cue, "点心") == nuonuoSelector.Choose(cue, "点心"),
                "Existing Random-only selector callers must keep Nuonuo's exact dialogue behavior.");
        }
    }

    public static void FeibiExploresAndHopsMoreButStillSleepsWhenTired()
    {
        var rested = new PetLifeState { Hunger = 20, Sleepiness = 20, Curiosity = 60 };
        var sleepy = new PetLifeState { Hunger = 20, Sleepiness = 100, Curiosity = 60 };
        var actions = Enum.GetValues<AutonomousAction>().ToHashSet();
        var original = CountChoices(rested, actions, PetCharacterProfile.Nuonuo);
        var energetic = CountChoices(rested, actions, PetCharacterProfile.FeibiJiubi);
        var tired = CountChoices(sleepy, actions, PetCharacterProfile.FeibiJiubi);
        var originalTired = CountChoices(sleepy, actions, PetCharacterProfile.Nuonuo);
        Assert(energetic[AutonomousAction.Explore] > original[AutonomousAction.Explore] &&
            energetic[AutonomousAction.Hop] > original[AutonomousAction.Hop],
            "A rested Feibi Jiubi must select exploration and hopping more often than Nuonuo.");
        Assert(tired[AutonomousAction.Sleep] > energetic[AutonomousAction.Sleep] &&
            tired[AutonomousAction.Sleep] >= originalTired[AutonomousAction.Sleep] &&
            tired[AutonomousAction.Sleep] > 5000,
            "High sleepiness must favor sleep over movement despite Feibi Jiubi's energetic personality.");
        for (var index = 0; index < 100; index++)
        {
            Assert(AutonomousBehaviorPlanner.Choose(rested, true, actions, index / 100.0) ==
                AutonomousBehaviorPlanner.Choose(rested, true, actions, index / 100.0, PetCharacterProfile.Nuonuo),
                "Omitting a profile must preserve Nuonuo's existing planner behavior.");
        }
    }

    public static void CharacterTemperamentCannotBypassModesOrSupport()
    {
        var life = new PetLifeState { Hunger = 100, Curiosity = 100, Sleepiness = 90 };
        foreach (var profile in PetCharacterProfile.All)
        {
            foreach (var fasting in new[] { false, true })
            {
                foreach (var quiet in new[] { false, true })
                {
                    var allowed = Enum.GetValues<AutonomousAction>()
                        .Where(action => PetModePolicy.AllowsAutonomousAction(action, fasting, quiet)).ToHashSet();
                    for (var index = 0; index < 100; index++)
                    {
                        var chosen = AutonomousBehaviorPlanner.Choose(life, true, allowed, index / 100.0, profile);
                        Assert(allowed.Contains(chosen),
                            "Character-specific weights must never bypass fasting or quiet mode availability.");
                    }
                }
            }

            var movementOnly = new HashSet<AutonomousAction>
            {
                AutonomousAction.Wander, AutonomousAction.Explore, AutonomousAction.Hop,
            };
            Assert(AutonomousBehaviorPlanner.Choose(life, false, movementOnly, 0.99, profile) == AutonomousAction.Rest,
                "Every character must refuse autonomous movement without support.");
            Assert(AutonomousBehaviorPlanner.Choose(life, true,
                    new HashSet<AutonomousAction> { AutonomousAction.Sleep }, 0.5, profile) == AutonomousAction.Sleep,
                "Character-specific weights must honor cooldown-filtered action availability.");
        }
    }

    public static void CharacterAnimationUsesSelectiveCadence()
    {
        var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, PetCharacterProfile.Nuonuo.RuntimeRelativeDirectory);
        var nuonuo = new SpriteAnimator(runtimeDirectory);
        var feibi = new SpriteAnimator(runtimeDirectory, PetCharacterProfile.FeibiJiubi);
        foreach (var clip in new[] { "idle", "walk", "run", "jump", "curious", "climb" })
        {
            nuonuo.Play(clip);
            feibi.Play(clip);
            nuonuo.Tick(0.5);
            feibi.Tick(0.5);
            Assert(feibi.CurrentFrameIndex > nuonuo.CurrentFrameIndex,
                $"Feibi Jiubi's active animation must feel more energetic: {clip}.");
        }

        foreach (var clip in new[] { "sleep_enter", "sleep", "sleep_exit" })
        {
            nuonuo.Play(clip);
            feibi.Play(clip);
            nuonuo.Tick(0.53);
            feibi.Tick(0.53);
            Assert(feibi.CurrentFrameIndex == nuonuo.CurrentFrameIndex,
                $"Sleep transitions and breathing must retain readable timing rather than global speed-up: {clip}.");
        }

        foreach (var clip in new[] { "hungry", "chomp", "lick", "satisfied", "satisfied_quick" })
        {
            Assert(nuonuo.HasClip(clip) && !feibi.HasClip(clip),
                $"Food animation belongs only to Nuonuo and must not remain callable on Feibi: {clip}.");
            nuonuo.Play(clip);
            nuonuo.Tick(0.53);
            Assert(nuonuo.CurrentClip == clip && nuonuo.CurrentFrame is not null,
                $"Removing Feibi's food clips must preserve Nuonuo's actual playback: {clip}.");
        }

        feibi.Play("curious");
        var curiousFrames = new HashSet<int> { feibi.CurrentFrameIndex };
        var reversedAtPeak = false;
        var returnedToStart = false;
        for (var step = 0; step < 28; step++)
        {
            var previous = feibi.CurrentFrameIndex;
            feibi.Tick(0.112);
            var current = feibi.CurrentFrameIndex;
            Assert(current is >= 0 and <= 7 && Math.Abs(current - previous) <= 1,
                "Feibi Jiubi's curious loop must advance continuously within its eight native head-tilt frames.");
            reversedAtPeak |= previous == 7 && current == 6;
            returnedToStart |= previous == 1 && current == 0;
            curiousFrames.Add(current);
        }
        Assert(reversedAtPeak && returnedToStart && curiousFrames.SetEquals(Enumerable.Range(0, 8)),
            "The curious pose must reach frame 7 and return naturally through the native frames to frame 0.");

        nuonuo.Play("curious");
        for (var step = 1; step <= 32; step++)
        {
            nuonuo.Tick(0.135);
            Assert(nuonuo.CurrentFrameIndex == step % 16,
                "Nuonuo must keep its original sixteen-frame forward curious loop.");
        }

        var nuonuoFinished = false;
        var feibiFinished = false;
        nuonuo.AnimationFinished += clip => nuonuoFinished = clip == "jump";
        feibi.AnimationFinished += clip => feibiFinished = clip == "jump";
        nuonuo.Play("jump");
        feibi.Play("jump");
        nuonuo.Tick(1.1);
        feibi.Tick(1.1);
        Assert(feibiFinished && !nuonuoFinished,
            "The livelier jump must still raise its ordinary completion event exactly at the clip boundary.");
    }

    public static void PairFeedUsesUprightWaitingBeforeCatch()
    {
        var runtimeDirectory = Path.Combine(AppContext.BaseDirectory, PetCharacterProfile.Nuonuo.RuntimeRelativeDirectory);
        var animator = new SpriteAnimator(runtimeDirectory);
        animator.Play("pair_feed_wait");
        for (var step = 0; step < 40; step++)
        {
            animator.Tick(0.1);
            Assert(animator.CurrentAssetFolder == "pair_feed" && animator.CurrentFrameIndex is >= 0 and <= 2,
                "Waiting for the thrown treat must use only the upright opening frames.");
        }

        animator.Play("pair_feed_ready");
        animator.AdvanceTo(0.74);
        Assert(animator.CurrentFrameIndex == 5,
            "The raised-hands anticipation must finish immediately before the mouth opens.");
        animator.Play("pair_feed_catch");
        animator.AdvanceTo(0.66);
        Assert(animator.CurrentFrameIndex == 9,
            "The wide-open mouth must be visible when the treat reaches Nuonuo.");
    }

    public static void CharacterAssetValidationRequiresEveryFrame()
    {
        var packagedRuntime = Path.Combine(AppContext.BaseDirectory, PetCharacterProfile.Nuonuo.RuntimeRelativeDirectory);
        var expectedFolders = new[]
        {
            "idle", "run", "walk", "toss", "fall", "land", "curious", "sleep", "hungry",
            "climb", "drag", "jump", "slide", "roll", "lick", "chomp", "satisfied",
            "pair_cheek", "pair_feed", "pair_sleep",
            "pair_notice", "pair_nuzzle", "pair_ball",
        };
        var nuonuoFolders = SpriteAnimator.RequiredAssetFoldersFor(PetCharacterProfile.Nuonuo);
        Assert(nuonuoFolders.Count == 23 &&
            nuonuoFolders.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(expectedFolders),
            "Nuonuo must retain seventeen solo folders and six paired-action folders.");
        var feibiFolders = SpriteAnimator.RequiredAssetFoldersFor(PetCharacterProfile.FeibiJiubi);
        Assert(feibiFolders.Count == 40 && feibiFolders.ToHashSet(StringComparer.OrdinalIgnoreCase).SetEquals(
                expectedFolders.Except(new[] { "hungry", "lick", "chomp", "satisfied" })
                    .Concat(MischiefAnimationCatalog.All.Select(clip => clip.Name))
                    .Append("pair_icon_kick")),
            "Feibi must retain thirteen non-food solo sheets, twenty prank sheets and seven paired sheets.");
        Assert(SpriteAnimator.HasCompleteAssets(packagedRuntime),
            "The real packaged Nuonuo sprite set must satisfy the same completeness check used for switching.");
        Assert(nuonuoFolders.All(folder =>
                Directory.GetFiles(Path.Combine(packagedRuntime, folder), "frame_*.png").Length == 16),
            "Every retained runtime behavior must contain exactly 16 numbered frames.");

        var directory = CreateTestDirectory();
        try
        {
            foreach (var profile in PetCharacterProfile.All)
            {
                var runtime = Directory.CreateDirectory(Path.Combine(directory, profile.Id)).FullName;
                Assert(!SpriteAnimator.HasCompleteAssets(runtime, profile) &&
                    !SpriteAnimator.HasCompleteAssets(string.Empty, profile),
                    "An empty or missing runtime directory must not be selectable for either character.");
                foreach (var folder in SpriteAnimator.RequiredAssetFoldersFor(profile))
                {
                    var actionDirectory = Directory.CreateDirectory(Path.Combine(runtime, folder)).FullName;
                    for (var index = 0; index < 16; index++)
                    {
                        File.WriteAllBytes(Path.Combine(actionDirectory, $"frame_{index:00}.png"), []);
                    }
                }
                Assert(SpriteAnimator.HasCompleteAssets(runtime, profile),
                    "Completeness validation must check filenames without decoding assets or launching the pet.");
                var lastFolder = SpriteAnimator.RequiredAssetFoldersFor(profile)[^1];
                File.Delete(Path.Combine(runtime, lastFolder, "frame_15.png"));
                Assert(!SpriteAnimator.HasCompleteAssets(runtime, profile),
                    "One missing native frame, including a final prank frame, must reject a character switch.");
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static Dictionary<AutonomousAction, int> CountChoices(
        PetLifeState life, IReadOnlySet<AutonomousAction> actions, PetCharacterProfile profile)
    {
        var counts = Enum.GetValues<AutonomousAction>().ToDictionary(action => action, _ => 0);
        for (var index = 0; index < 10000; index++)
        {
            counts[AutonomousBehaviorPlanner.Choose(life, true, actions, (index + 0.5) / 10000, profile)]++;
        }

        return counts;
    }

    private static string CreateTestDirectory() =>
        Directory.CreateTempSubdirectory("SoftMochiPet-character-tests-").FullName;

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
