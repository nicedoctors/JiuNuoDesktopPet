using System.IO;
using SoftMochiPet.Core;
using SoftMochiPet.Models;

internal static class MischiefTests
{
    public static void FeibiFoodCapabilityAndPlannerAreIsolated()
    {
        Assert(PetCharacterProfile.Nuonuo.SupportsFood && !PetCharacterProfile.Nuonuo.UsesMischief &&
            !PetCharacterProfile.FeibiJiubi.SupportsFood && PetCharacterProfile.FeibiJiubi.UsesMischief,
            "Only Nuonuo may consume food; only Feibi has window mischief.");
        Assert(new PetSettings().AllowMischief,
            "The approved default enables autonomous mischief while character capabilities stay separate.");
        var actions = Enum.GetValues<AutonomousAction>().ToHashSet();
        var empty = new PetLifeState { Hunger = 0, Fullness = 0, Curiosity = 60, Sleepiness = 20 };
        var hungry = new PetLifeState { Hunger = 100, Fullness = 100, Curiosity = 60, Sleepiness = 20 };
        for (var index = 0; index < 1000; index++)
        {
            var sample = index / 1000.0;
            var chosen = AutonomousBehaviorPlanner.Choose(hungry, true, actions, sample, PetCharacterProfile.FeibiJiubi);
            Assert(chosen is not (AutonomousAction.AskForFood or AutonomousAction.LickIcon or AutonomousAction.Roll),
                "A legacy Feibi save with high hunger must not enable any food behavior.");
            Assert(chosen == AutonomousBehaviorPlanner.Choose(empty, true, actions, sample, PetCharacterProfile.FeibiJiubi),
                "Feibi's choices must be independent of all hunger and fullness fields.");
        }
        var foodOnly = new HashSet<AutonomousAction> { AutonomousAction.LickIcon };
        Assert(AutonomousBehaviorPlanner.Choose(hungry, true, foodOnly, 0.5, PetCharacterProfile.Nuonuo) == AutonomousAction.LickIcon,
            "Nuonuo must retain ordinary food behavior.");
    }

    public static void MischiefChargesInActiveTimeAndBurstStartsOnlyOnce()
    {
        var controller = new MischiefController(random: new Random(1));
        for (var second = 0; second < 300; second++)
        {
            controller.Tick(1, enabled: true);
        }
        Assert(controller.Value == 100 && controller.PendingBurst && controller.AutonomousActionDue,
            "Five active minutes should fill the meter and create one intent.");
        Assert(controller.TryStart(manual: false, out var large) && large,
            "A full meter should select a large prank exactly once.");
        Assert(!controller.PendingBurst && !controller.TryStart(manual: true, out _),
            "Manual requests and later ticks must not duplicate the active performance.");
        controller.Tick(1, enabled: true);
        controller.Complete(large: true);
        Assert(controller.Value == 0 && !controller.IsPerforming && !controller.PendingBurst &&
            controller.CooldownRemainingSeconds == 120 && !controller.AutonomousActionDue,
            "A completed burst must clear the meter and enter a two-minute cooldown.");
        for (var second = 0; second < 120; second++)
        {
            controller.Tick(1, enabled: true);
        }
        Assert(controller.Value == 0 && !controller.AutonomousActionDue,
            "Cooldown time cannot refill the meter or queue another action.");
        controller.Tick(3, enabled: true);
        Assert(Math.Abs(controller.Value - 1) < 0.00000001,
            "Charging starts after the cooldown, without counting the final cooldown frame twice.");
    }

    public static void MischiefPausesWithoutOfflineOrDisabledBacklog()
    {
        foreach (var pause in new[] { "disabled", "sleep", "quiet", "locked" })
        {
            var controller = new MischiefController(new MischiefState { Value = 50 });
            for (var second = 0; second < 1200; second++)
            {
                controller.Tick(1, enabled: pause != "disabled", sleeping: pause == "sleep",
                    quiet: pause == "quiet", sessionLocked: pause == "locked");
            }
            Assert(controller.Value == 50 && !controller.PendingBurst && !controller.AutonomousActionDue,
                $"No mischief may accumulate or become overdue during {pause}.");
        }
        var gap = new MischiefController(new MischiefState { Value = 40 });
        foreach (var seconds in new[] { double.NaN, double.PositiveInfinity, -10, 0, 600, 86400 })
        {
            gap.Tick(seconds, enabled: true);
        }
        Assert(gap.Value == 40 && !gap.PendingBurst,
            "Large rendering gaps and invalid clock deltas must never charge a burst.");
        var pending = new MischiefController(new MischiefState { Value = 99 });
        pending.Tick(1, enabled: true);
        pending.Tease();
        Assert(pending.PendingBurst, "The test should begin with one pending burst.");
        pending.Tick(0, enabled: false);
        Assert(!pending.PendingBurst && !pending.AutonomousActionDue && pending.SecondsUntilNextAttempt is null &&
            !pending.TryStart(manual: false, out _),
            "Disabling autonomy must immediately cancel even an already-full intent.");
        TickFor(pending, 600, 60, enabled: false);
        Assert(pending.Value == 99 && !pending.PendingBurst && !pending.AutonomousActionDue,
            "Explicitly disabled autonomy cannot rebuild a pending burst while waiting.");
    }

    public static void MischiefManualPlayTeasingAndUnavailableTargetsAreBounded()
    {
        var controller = new MischiefController();
        Assert(controller.Tease() && !controller.Tease(),
            "Teasing should raise playfulness once and reject click bursts.");
        for (var index = 0; index < 8; index++)
        {
            controller.Tick(1.5, enabled: false);
            controller.Tease();
        }
        Assert(controller.Value == 100 && !controller.PendingBurst && !controller.AutonomousActionDue,
            "Manual teasing may fill the meter without authorizing autonomous window changes.");
        controller.Tick(0.1, enabled: false);
        Assert(controller.TryStart(manual: true, out var large) && large,
            "An explicit manual request must be able to preview the full-meter move with autonomy disabled.");
        controller.TargetUnavailable();
        Assert(controller.Value == 100 && !controller.IsPerforming && !controller.PendingBurst &&
            controller.CooldownRemainingSeconds == 0 && controller.SmallActionRemainingSeconds == 10,
            "An unavailable manual target preserves charge without authorizing disabled autonomous play.");
        Assert(controller.TryStart(manual: true, out large, allowLarge: false) && !large,
            "A deliberate manual request can choose a small action while retaining a full meter.");
        controller.Complete(large: false);
        Assert(controller.Value == 100 && !controller.IsPerforming && !controller.PendingBurst,
            "A completed small prank spends no charge and cannot turn disabled autonomy back on.");
        controller.Interrupt();
        Assert(controller.Value == 85 && controller.CooldownRemainingSeconds == 90 && !controller.AutonomousActionDue,
            "A genuine interrupted performance retains the ninety-second safety retreat and bounded charge.");
        Assert(controller.TryStart(manual: true, out large) && !large,
            "An explicit user request may preview a small prank during burst cooldown.");
        controller.Complete(large: false);
        Assert(controller.Value == 85 && controller.CooldownRemainingSeconds == 90,
            "A manual small action does not erase an existing safety cooldown or spend charge.");
    }

    public static void MischiefFullIntentWaitsWithoutExpiry()
    {
        foreach (var frameRate in new[] { 30, 60, 144 })
        {
            var waiting = new MischiefController(new MischiefState { Value = 99 });
            TickFor(waiting, 3, frameRate);
            TickFor(waiting, 600, frameRate);
            Assert(waiting.Value == 100 && waiting.PendingBurst && waiting.AutonomousActionDue &&
                waiting.CooldownRemainingSeconds == 0 && waiting.SmallActionRemainingSeconds == 0 &&
                waiting.SecondsUntilNextAttempt == 0,
                "A full unclaimed meter remains full after ten minutes and the small-action clock keeps advancing.");
        }
    }

    public static void MischiefCadenceIsRefreshRateIndependent()
    {
        foreach (var frameRate in new[] { 30, 60, 144 })
        {
            var controller = new MischiefController(random: new FixedRandom(0.5));
            controller.Tick(0, enabled: true);
            Assert(controller.SmallActionRemainingSeconds == 90 && controller.SecondsUntilNextAttempt == 90,
                "The midpoint of the one-to-two-minute small-action range is ninety seconds.");
            var smallActions = 0;
            for (var frame = 0; frame < 300 * frameRate; frame++)
            {
                controller.Tick(1d / frameRate, enabled: true);
                if (controller.PendingBurst || !controller.AutonomousActionDue) continue;
                Assert(controller.TryStart(manual: false, out var large) && !large,
                    "A due small action below full charge must remain a small action.");
                var charge = controller.Value;
                controller.Complete(large: false);
                Assert(controller.Value == charge && controller.SmallActionRemainingSeconds == 90,
                    "Successful small actions preserve charge and restart the regular interval.");
                smallActions++;
            }
            Assert(smallActions == 3 && controller.Value == 100 && controller.PendingBurst && controller.AutonomousActionDue,
                "At 30, 60 and 144 Hz, three ninety-second small actions must not delay the five-minute full meter.");
        }
        Assert(new MischiefController(random: new FixedRandom(0)).SmallActionRemainingSeconds == 60 &&
            new MischiefController(random: new FixedRandom(0.999999)).SmallActionRemainingSeconds is >= 119.99 and < 120,
            "Randomized regular small-action delays must stay within sixty to one hundred twenty seconds.");
    }

    public static void MischiefBlockedBurstKeepsSmallActionsAndFullIntent()
    {
        foreach (var frameRate in new[] { 30, 60, 144 })
        {
            var controller = new MischiefController(new MischiefState { Value = 99 }, new FixedRandom(0));
            TickFor(controller, 3, frameRate, burstBlocked: true);
            Assert(controller.Value == 100 && controller.PendingBurst && !controller.AutonomousActionDue &&
                Math.Abs(controller.SmallActionRemainingSeconds - 57) < 0.0000001,
                "Stored hat contents defer the burst but keep charging and the small-action countdown.");
            Assert(!controller.TryStart(manual: false, out _, allowLarge: false),
                "A blocked full meter must not cause an early small action before its own interval is due.");
            TickFor(controller, 57, frameRate, burstBlocked: true);
            Assert(controller.AutonomousActionDue && controller.TryStart(manual: false, out var large, allowLarge: false) && !large,
                "When the small-action clock expires, a stored result still permits kicking, punching or charging.");
            Assert(controller.PendingBurst && controller.Value == 100 && controller.SecondsUntilNextAttempt is null,
                "Starting a small action cannot consume the waiting full-meter intent.");
            controller.Tick(3, enabled: true, burstBlocked: true);
            controller.Complete(large: false);
            Assert(controller.Value == 100 && controller.PendingBurst && !controller.AutonomousActionDue &&
                controller.SmallActionRemainingSeconds == 60,
                "A completed small action keeps the full meter and waits for the next small interval while storage is occupied.");
            controller.Tick(0, enabled: true, burstBlocked: false);
            Assert(controller.AutonomousActionDue && !controller.TryStart(manual: false, out _, allowLarge: false),
                "Unblocking prioritizes the burst, without letting an explicit small-only request bypass its interval.");
            Assert(controller.TryStart(manual: false, out large) && large && !controller.PendingBurst,
                "Returning the held window permits exactly one waiting burst.");
        }
    }

    public static void MischiefUnavailableTargetsRetryAfterTenSecondsWithoutLoss()
    {
        foreach (var frameRate in new[] { 30, 60, 144 })
        foreach (var full in new[] { false, true })
        {
            var controller = new MischiefController(new MischiefState { Value = full ? 99 : 20 });
            controller.Tick(0, enabled: true);
            if (full) controller.Tease();
            for (var retry = 0; retry < 5; retry++)
            {
                var charge = controller.Value;
                controller.TargetUnavailable();
                Assert(controller.Value == charge && controller.PendingBurst == full &&
                    controller.CooldownRemainingSeconds == 0 && controller.SmallActionRemainingSeconds == 10 &&
                    controller.SecondsUntilNextAttempt == 10 && !controller.AutonomousActionDue,
                    "Missing targets preserve current charge and intent, with a ten-second retry instead of a safety cooldown.");
                for (var frame = 0; frame < 10 * frameRate - 1; frame++)
                    controller.Tick(1d / frameRate, enabled: true);
                Assert(!controller.AutonomousActionDue && !controller.TryStart(manual: false, out _),
                    "Neither a full burst nor a small action can retry before ten active seconds.");
                controller.Tick(1d / frameRate, enabled: true);
                Assert(controller.AutonomousActionDue && controller.SecondsUntilNextAttempt == 0 &&
                    (!full || controller.Value == 100 && controller.PendingBurst),
                    "The retry becomes due at ten seconds across refresh rates without draining a full meter.");
            }
        }
    }

    public static void DirectMischiefControlsDoNotRelaxOrdinaryGates()
    {
        var ordinary = new MischiefController();
        ordinary.Tick(0, enabled: false);
        Assert(!ordinary.TryStart(manual: false, out _) &&
            ordinary.TryStart(manual: true, out var large) && !large,
            "Disabled autonomy and an empty meter still prohibit automatic actions and ordinary manual bursts.");
        ordinary.Complete(large: false);
        ordinary.Interrupt();
        Assert(ordinary.TryStart(manual: true, out large) && !large,
            "An ordinary menu request during cooldown remains a small action.");
        ordinary.Complete(large: false);
        Assert(ordinary.TryStartDirect(large: true) && ordinary.IsPerforming &&
            !ordinary.TryStartDirect(large: false) && !ordinary.TryStart(manual: true, out _) &&
            !ordinary.FillForDirectControl(),
            "Only direct control may explicitly start a low-charge cooldown burst, and no API may duplicate an active action.");
        ordinary.Complete(large: true);
        Assert(ordinary.Value == 0 && !ordinary.IsPerforming && !ordinary.PendingBurst &&
            ordinary.CooldownRemainingSeconds == MischiefController.BurstCooldownSeconds && !ordinary.AutonomousActionDue,
            "A directly selected burst must complete through the same clear-and-cooldown lifecycle.");

        var charged = new MischiefController(new MischiefState { Value = 99 });
        charged.Tick(3, enabled: true, burstBlocked: true);
        charged.TargetUnavailable();
        Assert(charged.TryStartDirect(large: false) && charged.Value == 100 && charged.PendingBurst,
            "A directly selected small action can bypass the retry wait without consuming a blocked full-meter intent.");
        charged.Complete(large: false);
        Assert(charged.Value == 100 && charged.PendingBurst && !charged.AutonomousActionDue,
            "Direct small-action completion retains both the full meter and occupied-hat timing semantics.");
        charged.Tick(0, enabled: true, burstBlocked: false);
        Assert(charged.AutonomousActionDue && charged.SecondsUntilNextAttempt == 0,
            "The direct request clears its old retry delay, so an unblocked full intent is ready normally.");
    }

    public static void DirectMischiefFillAndStartRespectPauseAndActionOwnership()
    {
        foreach (var pause in new[] { "sleep", "quiet", "locked" })
        {
            var controller = new MischiefController(new MischiefState { Value = 42, CooldownRemainingSeconds = 90 });
            controller.Tick(0, enabled: true, sleeping: pause == "sleep", quiet: pause == "quiet", sessionLocked: pause == "locked");
            Assert(!controller.TryStartDirect(large: false) && !controller.TryStartDirect(large: true) &&
                !controller.FillForDirectControl() && !controller.TryStart(manual: true, out _) &&
                controller.Value == 42 && controller.CooldownRemainingSeconds == 90 && !controller.IsPerforming,
                $"Direct commands must not bypass the {pause} safety pause or mutate its state.");
        }
        foreach (var enabled in new[] { false, true })
        {
            var controller = new MischiefController(new MischiefState { Value = 20, CooldownRemainingSeconds = 90 });
            controller.Tick(0, enabled);
            controller.TargetUnavailable();
            Assert(controller.FillForDirectControl() && controller.Value == 100 &&
                controller.CooldownRemainingSeconds == 0 && controller.PendingBurst == enabled &&
                controller.AutonomousActionDue == enabled,
                "Direct filling clears the meter cooldown and stale target retry without enabling opted-out autonomy.");
            Assert(controller.TryStartDirect(large: true) && !controller.PendingBurst &&
                !controller.FillForDirectControl(),
                "Filling is a separate state edit and cannot refill an already-owned performance.");
        }
    }

    public static void MischiefStateIsIndependentAndNeverPersistsAnAction()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"SoftMochiPet-Mischief-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var originalLifePath = Path.Combine(directory, "life-state.json");
            const string oldLife = "{\"TotalMeals\":23,\"Hunger\":42}";
            File.WriteAllText(originalLifePath, oldLife);
            var store = new MischiefStateStore(directory);
            store.Save(new MischiefState { Value = 100, CooldownRemainingSeconds = 123 });
            var state = store.Load();
            Assert(state.Value == 99 && state.CooldownRemainingSeconds == 120 &&
                File.ReadAllText(originalLifePath) == oldLife,
                "Mischief saving preserves life-state data, clamps legacy cooldowns and never persists an active burst.");
            var restored = new MischiefController(state);
            Assert(!restored.PendingBurst && !restored.IsPerforming && !restored.AutonomousActionDue,
                "A restarted pet cannot resume manipulating a previously controlled window.");
            File.WriteAllText(store.StatePath, "broken");
            Assert(store.Load().Value == 0, "A damaged mischief save must safely reset without blocking startup.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    public static void MischiefDialogueAndAnimationContractAreComplete()
    {
        var selector = new MischiefDialogueSelector(new Random(2));
        foreach (var cue in Enum.GetValues<MischiefCue>())
        {
            var lines = MischiefDialogueSelector.GetLines(cue);
            Assert(lines.Count == 4 && lines.Distinct().Count() == 4 && lines.All(line => line.Length <= 8),
                $"Mischief reactions need short, distinct alternatives: {cue}.");
            string? previous = null;
            for (var index = 0; index < 20; index++)
            {
                var current = selector.Choose(cue);
                Assert(current != previous, "Repeated situations should not repeat the same line immediately.");
                previous = current;
            }
        }
        var foodWords = new[] { "饿", "舔", "吃", "饭饭", "点心", "肚", "好香", "口水" };
        Assert(Enum.GetValues<DialogueCue>().SelectMany(cue => PetDialogueCatalog.GetLines(cue, PetCharacterProfile.FeibiJiubi))
            .All(line => foodWords.All(word => !line.Contains(word, StringComparison.Ordinal))),
            "Feibi's dialogue catalog must not retain food, licking, or hunger lines.");
        var required = SpriteAnimator.RequiredAssetFoldersFor(PetCharacterProfile.FeibiJiubi);
        Assert(required.Count == 40 && new[] { "hungry", "lick", "chomp", "satisfied", "pinch", "pinch_right" }.All(folder => !required.Contains(folder)) &&
            new[] { "pair_cheek", "pair_feed", "pair_sleep", "pair_icon_kick" }.All(required.Contains),
            "Feibi requires her solo, prank and paired-action sheets without solo food clips.");
        Assert(MischiefAnimationCatalog.All.Count == 20 && MischiefAnimationCatalog.All.All(clip =>
            required.Contains(clip.Name) && clip.FrameSeconds > 0 &&
            clip.Markers.All(marker => marker.FrameIndex is >= 0 and < 16)),
            "Every prank clip and contact event must fit the native sixteen-frame contract.");
    }

    public static void MischiefAnimationMarkersSurviveSkippedFramesAndReentry()
    {
        WithLogicSpriteFixture(directory =>
        {
            var animator = new SpriteAnimator(directory, PetCharacterProfile.FeibiJiubi);
            var markers = new List<(string Clip, string Marker, int Frame)>();
            var finished = new List<string>();
            animator.MarkerReached += (clip, marker) => markers.Add((clip, marker, animator.CurrentFrameIndex));
            animator.AnimationFinished += finished.Add;
            animator.Play("kick");
            animator.AdvanceTo(0.71);
            Assert(markers.Count == 0, "The window must not be hit during wind-up.");
            animator.AdvanceTo(20);
            animator.AdvanceTo(10);
            animator.Tick(20);
            Assert(markers.Count == 1 && markers[0] == ("kick", "contact", 8) && finished.SequenceEqual(new[] { "kick" }) &&
                animator.IsCompleted && animator.Progress == 1,
                "A skipped contact frame must emit exactly once at its native frame before completion.");
            markers.Clear();
            animator.Play("bare_tear");
            animator.AdvanceTo(5);
            Assert(markers.Select(item => (item.Marker, item.Frame)).SequenceEqual(new[] { ("grab", 3), ("tear", 8) }),
                "A low-frame-rate tear must emit both grab and split in order.");

            var reentrant = new SpriteAnimator(directory, PetCharacterProfile.FeibiJiubi);
            var staleFinished = false;
            reentrant.AnimationFinished += _ => staleFinished = true;
            reentrant.MarkerReached += (_, _) => reentrant.Play("idle");
            reentrant.Play("kick");
            reentrant.Tick(10);
            Assert(reentrant.CurrentClip == "idle" && reentrant.CurrentFrameIndex == 0 && !staleFinished,
                "A contact callback which changes action must not advance or complete the superseded clip.");
        });
    }

    public static void MischiefHatlessInterruptionsKeepLogicalNamesAndOriginalLoops()
    {
        WithLogicSpriteFixture(directory =>
        {
            var animator = new SpriteAnimator(directory, PetCharacterProfile.FeibiJiubi);
            animator.SetHatless(true);
            var aliases = new Dictionary<string, string>
            {
                ["idle"] = "bare_idle", ["walk"] = "bare_run", ["run"] = "bare_run",
                ["drag"] = "bare_drag", ["fall"] = "bare_fall", ["toss"] = "bare_fall",
                ["land"] = "bare_recover",
            };
            foreach (var (name, folder) in aliases)
            {
                animator.Play(name);
                Assert(animator.CurrentClip == name && animator.CurrentAssetFolder == folder && animator.Hatless,
                    $"A hatless {name} interruption must never flash a wearing-hat frame or rename its state callback.");
            }
            var completed = new List<string>();
            animator.AnimationFinished += completed.Add;
            animator.Play("toss");
            animator.Tick(2);
            Assert(animator.CurrentFrameIndex == 7 && completed.SequenceEqual(new[] { "toss" }),
                "A hatless toss must still use only eight frames and emit the original toss completion.");
            animator.Play("land");
            animator.Tick(2);
            Assert(completed.SequenceEqual(new[] { "toss", "land" }),
                "Hatless landing must preserve the ordinary landing completion callback.");
            animator.Play("idle");
            animator.SetHatless(false);
            Assert(animator.CurrentClip == "idle" && animator.CurrentAssetFolder == "idle" && !animator.Hatless,
                "Putting the hat back must restore ordinary idle assets without a new logical state.");
            Assert(!animator.HasClip("hungry") && !animator.HasClip("lick") && !animator.HasClip("chomp") &&
                !animator.HasClip("satisfied"), "Food clips must not remain callable on Feibi.");
        });
    }

    private static void TickFor(MischiefController controller, int seconds, int frameRate,
        bool enabled = true, bool burstBlocked = false)
    {
        for (var frame = 0; frame < seconds * frameRate; frame++)
            controller.Tick(1d / frameRate, enabled, burstBlocked: burstBlocked);
    }

    private sealed class FixedRandom(double sample) : Random
    {
        public override double NextDouble() => sample;
    }

    private static void WithLogicSpriteFixture(Action<string> test)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"SoftMochiPet-MischiefSprites-{Guid.NewGuid():N}");
        var source = Path.Combine(AppContext.BaseDirectory, PetCharacterProfile.Nuonuo.RuntimeRelativeDirectory,
            "idle", "frame_00.png");
        string[] folders = ["idle", "kick", "bare_tear", "bare_idle", "bare_run", "bare_drag", "bare_fall", "bare_recover"];
        try
        {
            foreach (var folder in folders)
            {
                Directory.CreateDirectory(Path.Combine(directory, folder));
                for (var frame = 0; frame < 16; frame++)
                {
                    // Identical pixels isolate timing and state rules; production art is audited separately.
                    File.Copy(source, Path.Combine(directory, folder, $"frame_{frame:00}.png"));
                }
            }
            test(directory);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
