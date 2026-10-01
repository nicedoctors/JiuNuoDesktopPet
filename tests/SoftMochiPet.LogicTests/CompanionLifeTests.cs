using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using SoftMochiPet;
using SoftMochiPet.Core;

internal static class CompanionLifeTests
{
    private const BindingFlags Instance = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly MethodInfo ClipMethod = typeof(MainWindow).GetMethod("PairLifeClip",
        BindingFlags.Static | BindingFlags.NonPublic)!;

    public static void ContextHasDistanceCooldownAndFreshnessBoundaries()
    {
        Assert(CompanionLifePolicy.IsPassing(90, 100, true) &&
            !CompanionLifePolicy.IsPassing(90, 100, false) &&
            !CompanionLifePolicy.IsPassing(200, 100, true), "Nuzzles require a real nearby encounter.");
        Assert(CompanionLifePolicy.ShouldFollow(200, 100, true) &&
            !CompanionLifePolicy.ShouldFollow(200, 100, false) &&
            !CompanionLifePolicy.ShouldFollow(500, 100, true), "Followers watch their moving partner, not a cursor.");
        Assert(!CompanionLifePolicy.EventFresh(1, 0) && CompanionLifePolicy.EventFresh(18, 10) &&
            !CompanionLifePolicy.EventFresh(18.01, 10), "Do not replay startup or stale window events.");
        Assert(CompanionLifePolicy.ContextGap >= 18 && CompanionLifePolicy.FollowCooldown >= 60 &&
            CompanionLifePolicy.NuzzleCooldown >= 60 && CompanionLifePolicy.BallCooldown >= 90 &&
            CompanionLifePolicy.WatchCooldown >= 40, "Contextual play needs independent quiet gaps.");
    }

    public static void FollowingWaitsAndRemainsSeparatedAtEveryFrameRate()
    {
        foreach (var fps in new[] { 30, 60, 144 })
        foreach (var separation in new[] { 1.3, 2d, 4d })
        {
            double previousN = 0, previousF = 0;
            for (var i = 0; i <= fps * 9; i++)
            {
                var (f, n) = CompanionLifePolicy.FollowProgress(i / (double)fps);
                Assert(n >= previousN && f >= previousF && n is >= 0 and <= 1 && f is >= 0 and <= 1,
                    "Follow interpolation must be monotonic and bounded.");
                Assert(separation + 1.8 * f - (separation + 1.8 - .85) * n >= .7,
                    "The follower must not overtake or overlap the leader.");
                previousN = n; previousF = f;
            }
        }
        Assert(CompanionLifePolicy.FollowProgress(.7).Follower == 0 &&
            CompanionLifePolicy.FollowProgress(2.5).Leader == CompanionLifePolicy.FollowProgress(3.3).Leader,
            "The follower reacts late and the leader pauses to look back.");
    }

    private static CompanionBallSample Ball(double t, double scale = 1, bool mirror = false)
    {
        var sign = mirror ? -1 : 1;
        return CompanionBallTimeline.Sample(t, -240 * scale * sign, 405 * scale,
            240 * scale * sign, 400 * scale, 290 * scale * sign, 430 * scale,
            500 * scale, 20 * scale, 240 * scale);
    }

    public static void BallContactsAndTransitionsStayContinuousAcrossDpi()
    {
        foreach (var scale in new[] { .6, 1, 1.25, 1.5, 2 })
        foreach (var mirror in new[] { false, true })
        {
            var boundaries = new List<double> { 1.65, 2.8, 4.6, 5.2 };
            for (var i = 0; i <= CompanionLifePolicy.RallyCount; i++)
                boundaries.Add(CompanionLifePolicy.BallStart + i * CompanionLifePolicy.RallySeconds);
            boundaries.AddRange(new[] { .35, 1.1, 1.9, 2.2, 2.5, 4.35 }.Select(t => CompanionLifePolicy.BallEnd + t));
            foreach (var time in boundaries)
            {
                var a = Ball(time - .00001, scale, mirror);
                var b = Ball(time + .00001, scale, mirror);
                Assert(Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) < .05 * scale,
                    $"Ball should not teleport at {time} seconds.");
            }
            for (var i = 0; i < CompanionLifePolicy.RallyCount; i++)
            {
                var sample = Ball(CompanionLifePolicy.BallStart + i * CompanionLifePolicy.RallySeconds + .000001, scale, mirror);
                var expectedX = (i % 2 == 0 ? 240 : -240) * scale * (mirror ? -1 : 1);
                var actor = i % 2 == 0 ? sample.Feibi : sample.Nuonuo;
                Assert(Math.Abs(sample.X - expectedX) < .01 && actor.Clip == "pair_ball" &&
                    Math.Abs(actor.Seconds - CompanionLifePolicy.KickContact) < .01,
                    "The ball must reverse at the striker's real contact frame.");
            }
        }
        Assert(Ball(1.5).Opacity == 0 && Ball(2).Opacity == 1 &&
            Ball(CompanionLifePolicy.BallEnd + 2.3).Opacity == 0 &&
            Ball(CompanionLifePolicy.BallEnd + 4.35).Complete, "Ball must emerge, be collected, and finish.");
    }

    private static (MainWindow Pet, SpriteAnimator Animator) Actor(PetCharacterProfile profile)
    {
        var pet = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var animator = new SpriteAnimator(Path.Combine(AppContext.BaseDirectory, profile.RuntimeRelativeDirectory), profile);
        typeof(MainWindow).GetField("_animator", Instance)!.SetValue(pet, animator);
        return (pet, animator);
    }

    private static void Pose(MainWindow actor, CompanionPose pose) =>
        ClipMethod.Invoke(null, new object[] { actor, pose.Clip, pose.Seconds });

    public static void RepeatedKicksRestartActualProductionAnimators()
    {
        var (n, na) = Actor(PetCharacterProfile.Nuonuo);
        var (f, fa) = Actor(PetCharacterProfile.FeibiJiubi);
        var seen = new HashSet<int>();
        for (var i = 0; i < 21 * 120; i++)
        {
            var t = i / 120d;
            var sample = Ball(t);
            Pose(n, sample.Nuonuo); Pose(f, sample.Feibi);
            var rally = (int)((t - CompanionLifePolicy.BallStart) / CompanionLifePolicy.RallySeconds);
            if (t >= CompanionLifePolicy.BallStart && rally < 6)
            {
                var local = t - CompanionLifePolicy.BallStart - rally * CompanionLifePolicy.RallySeconds;
                if (local < .06)
                {
                    var animator = rally % 2 == 0 ? fa : na;
                    Assert(animator.CurrentFrameIndex == 7, $"Rally {rally} must restart and reach frame 7.");
                    seen.Add(rally);
                }
            }
        }
        Assert(seen.Count == 6 && fa.CurrentClip == "hat_wear" && fa.CurrentFrameIndex == 15,
            "All six real kicks and final hat recovery must be exercised.");
    }

    public static void LifeCancellationIsIdempotentAndReleasesOwnership()
    {
        foreach (var scene in new[] { "Follow", "Nuzzle", "Ball", "Watch" })
        {
            var owner = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
            typeof(MainWindow).GetField("_clock", Instance)!.SetValue(owner, Stopwatch.StartNew());
            var sceneType = typeof(MainWindow).GetNestedType("PairScene", BindingFlags.NonPublic)!;
            typeof(MainWindow).GetField("_pairScene", Instance)!.SetValue(owner, Enum.Parse(sceneType, scene));
            var cancel = typeof(MainWindow).GetMethod("CancelPairScene", Instance)!;
            cancel.Invoke(owner, new object[] { "DeletionPriority" });
            cancel.Invoke(owner, new object[] { "PickedUp" });
            Assert(typeof(MainWindow).GetField("_pairScene", Instance)!.GetValue(owner) is null &&
                typeof(MainWindow).GetField("_pairLifeLane", Instance)!.GetValue(owner) is null &&
                (double)typeof(MainWindow).GetField("_nextPairContextAt", Instance)!.GetValue(owner)! >= 18,
                "Each ambient scene must release its state and wait before another invitation.");
        }
    }

    public static void StartupAndQuietModeDoNotInventActivity()
    {
        var owner = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var companion = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var settings = new SoftMochiPet.Models.PetSettings { QuietMode = true };
        typeof(MainWindow).GetField("_settings", Instance)!.SetValue(owner, settings);
        typeof(MainWindow).GetField("_companionWindow", Instance)!.SetValue(owner, companion);
        var sceneType = typeof(MainWindow).GetNestedType("PairScene", BindingFlags.NonPublic)!;
        foreach (var scene in new[] { "Follow", "Nuzzle", "Ball", "Watch" })
            Assert(!(bool)typeof(MainWindow).GetMethod("TryStartPairScene", Instance)!.Invoke(owner,
                new[] { Enum.Parse(sceneType, scene) })!, "Quiet mode must reject manual and automatic life scenes.");
        settings.QuietMode = false;
        typeof(MainWindow).GetField("_surfaceProvider", Instance)!.SetValue(owner,
            new SoftMochiPet.Services.WindowSurfaceProvider());
        typeof(MainWindow).GetMethod("ObserveCompanionWindows", Instance)!.Invoke(owner, new object[] { 5d });
        Assert(!(bool)typeof(MainWindow).GetField("_pairWindowSampleReady", Instance)!.GetValue(owner)!,
            "An unrefreshed window map cannot be treated as a valid empty startup snapshot.");
    }

    public static void WritePreviewTrace(string path)
    {
        var frames = new List<object>();
        var (n, na) = Actor(PetCharacterProfile.Nuonuo);
        var (f, fa) = Actor(PetCharacterProfile.FeibiJiubi);
        for (var i = 0; i <= 21 * 20; i++)
        {
            var t = i / 20d;
            // 240px character viewbox, feet at y=500, left/right centers 260/800.
            var radius = 20d;
            double px(double center, double canvas) => center - 120 + canvas / 512 * 240;
            double py(double canvas) => 500 - 374d / 384 * 240 + canvas / 512 * 240;
            var sample = CompanionBallTimeline.Sample(t, px(260, 404) + radius, py(392),
                px(800, 100) - radius, py(384), px(800, 235), py(424), 500, radius, 252);
            Pose(n, sample.Nuonuo); Pose(f, sample.Feibi);
            frames.Add(new { t, n = na.CurrentAssetFolder, nf = na.CurrentFrameIndex,
                f = fa.CurrentAssetFolder, ff = fa.CurrentFrameIndex,
                sample.X, sample.Y, sample.Opacity });
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(frames));
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
