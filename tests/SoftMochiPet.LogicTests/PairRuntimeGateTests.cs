using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using SoftMochiPet;
using SoftMochiPet.Core;
using SoftMochiPet.Services;

internal static class PairRuntimeGateTests
{
    private const BindingFlags Fields = BindingFlags.NonPublic | BindingFlags.Instance;

    public static void RuntimeGateAcceptsWalkingWithAHeldControllerAndDefersLanding()
    {
        // Field containers exercise production gates without constructing a
        // Window, creating a tray icon, reading a save or invoking native UI.
        var pet = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var animator = (SpriteAnimator)RuntimeHelpers.GetUninitializedObject(typeof(SpriteAnimator));
        var controller = (WindowPrankController)RuntimeHelpers.GetUninitializedObject(typeof(WindowPrankController));
        Set(pet, "_animator", animator);
        Set(pet, "_windowPranks", controller);
        Set(controller, "_target", new WindowPrankTarget(IntPtr.Zero, 0, 0, default, default, default, default));
        Set(controller, "<IsHeld>k__BackingField", true);
        Set(pet, "_state", PetState.Running);
        Set(pet, "_movementPurpose", MovementPurpose.Patrol);
        Assert(Readiness(pet) == IconPartnerReadiness.Ready, "Production gate must accept a walking partner with stored results.");
        Set(pet, "_state", PetState.Landing);
        Assert(Readiness(pet) == IconPartnerReadiness.Wait, "Production gate should defer landing.");
        Set(pet, "_state", PetState.Idle);
        Assert(Readiness(pet) == IconPartnerReadiness.Ready, "A landed partner must become ready.");
        Set(controller, "<IsHeld>k__BackingField", false);
        Assert(Readiness(pet) == IconPartnerReadiness.Unavailable, "An active operation must still block participation.");
        Set(controller, "<IsHeld>k__BackingField", true);
        Set(pet, "_manualMischiefPending", true);
        Assert(Readiness(pet) == IconPartnerReadiness.Unavailable, "A queued user request must not be stolen.");
    }

    public static void CancellationClearsWaitOwnershipAndArmsSoloFallback()
    {
        var owner = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        Set(owner, "_clock", Stopwatch.StartNew());
        Set(owner, "_pairScene", Enum.Parse(typeof(MainWindow).GetNestedType("PairScene", BindingFlags.NonPublic)!, "IconKick"));
        Set(owner, "_pairIconWaiting", true);
        Set(owner, "_pairActionStarted", true);
        Set(owner, "_pairIconLaunched", true);
        Invoke(owner, "CancelPairIconMeal", "PartnerWaitTimeout", false);
        Assert(Get(owner, "_pairScene") is null && !(bool)Get(owner, "_pairIconWaiting")! &&
            !(bool)Get(owner, "_pairActionStarted")! && !(bool)Get(owner, "_pairIconLaunched")!,
            "Cancellation must clear all pair ownership before a queued meal resumes.");
        Assert((double)Get(owner, "_pairIconRetryAt")! >= 8,
            "The failed meal must be allowed to use solo consumption without restarting the same wait.");
        Invoke(owner, "CancelPairIconMeal", "WindowClosing", false);
        Assert(Get(owner, "_pairScene") is null, "Repeated cleanup must remain safe.");
    }

    public static void ConsumedIconKeepsCatchClipWithoutGhostOrSoloReplay()
    {
        var owner = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var nuonuo = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var feibi = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var nAnimator = new SpriteAnimator(Path.Combine(AppContext.BaseDirectory, PetCharacterProfile.Nuonuo.RuntimeRelativeDirectory), PetCharacterProfile.Nuonuo);
        var fAnimator = new SpriteAnimator(Path.Combine(AppContext.BaseDirectory, PetCharacterProfile.FeibiJiubi.RuntimeRelativeDirectory), PetCharacterProfile.FeibiJiubi);
        Set(nuonuo, "_animator", nAnimator);
        Set(feibi, "_animator", fAnimator);
        nAnimator.Play("pair_feed_catch");
        fAnimator.Play("pair_icon_kick");
        Set(owner, "_clock", Stopwatch.StartNew());
        Set(owner, "_pairScene", Enum.Parse(typeof(MainWindow).GetNestedType("PairScene", BindingFlags.NonPublic)!, "IconKick"));
        Set(owner, "_pairNuonuo", nuonuo);
        Set(owner, "_pairFeibi", feibi);
        var position = Activator.CreateInstance(typeof(MainWindow).GetNestedType("PairPosition", BindingFlags.NonPublic)!, 0, 0, 0, 0)!;
        Set(owner, "_pairNuonuoPosition", position);
        Set(owner, "_pairFeibiPosition", position);
        Set(owner, "_pairIconLayout", PairIconKickPlanner.Plan(new(0, 0, 1920, 1080), new(800, 900), new(180, 180), new(180, 180), new(110, 100), new(30, 100), 220)!);
        Set(owner, "_pairIconApproachSeconds", 1d);
        Set(owner, "_pairSceneElapsed", 2.65d);
        Set(owner, "_pairIconConsumed", true);
        Set(owner, "_pairActionStarted", true);
        Invoke(owner, "UpdatePairIconMeal", .2d);
        Assert(Get(owner, "_pairScene") is not null && nAnimator.CurrentClip == "pair_feed_catch" && nAnimator.CurrentFrameIndex >= 10,
            "A consumed icon has no ghost but must finish its existing catch/chew clip.");
        Invoke(owner, "CancelPairIconMeal", "InterruptedAfterCatch", false);
        Assert(Get(owner, "_pairScene") is null && (double)Get(owner, "_pairIconRetryAt")! == 0 && nAnimator.CurrentClip == "pair_feed_catch",
            "Consumed cleanup must neither schedule a solo retry nor start the solo chomp clip.");
    }

    private static IconPartnerReadiness Readiness(MainWindow pet) =>
        (IconPartnerReadiness)Invoke(pet, "IconPartnerReadinessNow")!;
    private static object? Invoke(MainWindow pet, string method, params object[] args) =>
        typeof(MainWindow).GetMethod(method, Fields)!.Invoke(pet, args);
    private static void Set(object target, string field, object value) =>
        target.GetType().GetField(field, Fields)!.SetValue(target, value);
    private static object? Get(object target, string field) => target.GetType().GetField(field, Fields)!.GetValue(target);
    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
