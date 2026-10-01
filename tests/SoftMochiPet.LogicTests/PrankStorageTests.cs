using System.Reflection;
using System.Runtime.CompilerServices;
using SoftMochiPet;
using SoftMochiPet.Services;
using DrawingRectangle = System.Drawing.Rectangle;
using Point = System.Windows.Point;

internal static class PrankStorageTests
{
    private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void StoredResultsReturnOnlyForGlobalSafetyReasons()
    {
        Assert(new[]
        {
            "CharacterRequested", "CharacterChanged", "SessionUnavailable", "Suspend", "DisplayChanged",
            "WindowClosing", "PlaybackFailed", "FeatureDemoStopped", "FeatureDemoFinished",
            "FeatureDemoStepFailed", "FeatureDemoStageBoundary",
        }.All(WindowPrankHoldPolicy.MustReturnStoredResult),
            "Every global shutdown, ownership boundary and rendering failure must return the stored window.");
        Assert(new[]
        {
            "PickedUp", "BringHome", "QuietMode", "AutonomyDisabled", "SizeChanged", "PetDisplayAdapted",
            "ManualRestorePreparation", "FeatureDemoHatlessPickup", "SupportLost", "PlatformLaunch",
            "PlatformDownwardRelease", "StrikeRejected", "PreparationFailed", "foreground_changed",
            "motion_window_unavailable", "motion_refused_or_user_moved", "user_moved_window",
            "motion_completion_unconfirmed", "motion_request_failed", "user_takeover_or_window_gone",
            "target_moved_during_burst_call", "burst_call_failed", "future_local_failure", string.Empty,
        }.All(reason => !WindowPrankHoldPolicy.MustReturnStoredResult(reason)),
            "Pet interactions and failures belonging to another target must not release stored ownership.");
    }

    public static void ParkingAndActivationPreserveTheOriginalResult()
    {
        var owner = CreateFieldContainer();
        var original = CreateHeldController();
        var target = CreateTarget();
        var snapshot = (WindowPrankSnapshot)RuntimeHelpers.GetUninitializedObject(typeof(WindowPrankSnapshot));
        var evidence = new WindowPrankCompletionEvidence(7, "Tear", IntPtr.Zero, 0,
            target.OuterBounds, target.OuterBounds, true, true);
        Set(original, "<CompletionSequence>k__BackingField", 7L);
        Set(original, "<LastCompletion>k__BackingField", evidence);
        var kind = EnumValue("PrankKind", "Tear");
        var contact = new Point(804, 316);
        var mouth = new Point(926, 434);
        SeedResult(owner, original, target, snapshot, kind, contact, mouth);

        try
        {
            Invoke(owner, "ParkHeldPrank");
            var parked = Get<object>(owner, "_storedPrank");
            var working = Get<WindowPrankController>(owner, "_windowPranks");
            Assert(parked is not null && working is not null && !ReferenceEquals(working, original),
                "Parking must retain the original controller and create an independent current slot.");
            Assert(ReferenceEquals(Property<WindowPrankController>(parked!, "Controller"), original) &&
                ReferenceEquals(Property<WindowPrankTarget>(parked!, "Target"), target) &&
                ReferenceEquals(Property<WindowPrankSnapshot>(parked!, "Snapshot"), snapshot),
                "Target, snapshot and controller ownership must move together without cloning or disposal.");
            Assert(Get<object>(owner, "_prankTarget") is null && Get<object>(owner, "_prankSnapshot") is null,
                "The new current slot must not retain the stored target or bitmap owner.");
            Assert(!Get<bool>(original, "_disposed") && original.CompletionSequence == 7 &&
                ReferenceEquals(original.LastCompletion, evidence),
                "Parking must preserve controller completion evidence and live ownership.");

            Set(owner, "_prankKind", EnumValue("PrankKind", "Punch"));
            Set(owner, "_prankLarge", false);
            Set(owner, "_screenStrikeContact", new Point(10, 20));
            Set(owner, "_prankHatMouth", new Point(30, 40));
            Set(owner, "_prankHatMouthWidth", 12d);
            Set(owner, "_prankPresentationFinished", false);
            Invoke(owner, "ActivateStoredPrank");

            Assert(Get<object>(owner, "_storedPrank") is null &&
                ReferenceEquals(Get<WindowPrankController>(owner, "_windowPranks"), original) &&
                ReferenceEquals(Get<WindowPrankTarget>(owner, "_prankTarget"), target) &&
                ReferenceEquals(Get<WindowPrankSnapshot>(owner, "_prankSnapshot"), snapshot),
                "Activation must restore the exact original result and empty the storage slot.");
            Assert(Equals(Get<object>(owner, "_prankKind"), kind) && Get<bool>(owner, "_prankLarge") &&
                Get<Point>(owner, "_screenStrikeContact") == contact &&
                Get<Point>(owner, "_prankHatMouth") == mouth && Get<double>(owner, "_prankHatMouthWidth") == 76d &&
                Get<bool>(owner, "_prankPresentationFinished"),
                "Returning a stored result must restore its visual metadata and completed-presentation flag.");
            Assert(original.CompletionSequence == 7 && ReferenceEquals(original.LastCompletion, evidence),
                "Intermediate attacks must not replace the stored controller's completion evidence.");
            Assert(Get<bool>(working!, "_disposed"), "Activation must dispose the displaced empty working controller.");
        }
        finally
        {
            Invoke(owner, "ReleaseStoredPrank");
            Get<WindowPrankController>(owner, "_windowPranks")?.Dispose();
            original.Dispose();
            snapshot.Dispose();
        }
    }

    public static void SecondParkCannotOverwriteAnOccupiedSlot()
    {
        var owner = CreateFieldContainer();
        var original = CreateHeldController();
        SeedResult(owner, original, CreateTarget(), null, EnumValue("PrankKind", "Hat"), default, default);
        try
        {
            Invoke(owner, "ParkHeldPrank");
            var parked = Get<object>(owner, "_storedPrank");
            var working = Get<WindowPrankController>(owner, "_windowPranks")!;
            Set(working, "<IsHeld>k__BackingField", true);
            Invoke(owner, "ParkHeldPrank");
            Assert(ReferenceEquals(Get<object>(owner, "_storedPrank"), parked) &&
                ReferenceEquals(Get<WindowPrankController>(owner, "_windowPranks"), working),
                "A second park must leave both occupied slots untouched instead of losing an owned window.");
        }
        finally
        {
            Invoke(owner, "ReleaseStoredPrank");
            Get<WindowPrankController>(owner, "_windowPranks")?.Dispose();
            original.Dispose();
        }
    }

    public static void ReleasingStoredOwnershipDoesNotClearTheCurrentAttack()
    {
        var owner = CreateFieldContainer();
        var original = CreateHeldController();
        SeedResult(owner, original, CreateTarget(), null, EnumValue("PrankKind", "Tear"), default, default);
        try
        {
            Invoke(owner, "ParkHeldPrank");
            var working = Get<WindowPrankController>(owner, "_windowPranks")!;
            var currentTarget = CreateTarget();
            var currentSnapshot = (WindowPrankSnapshot)RuntimeHelpers.GetUninitializedObject(typeof(WindowPrankSnapshot));
            var currentKind = EnumValue("PrankKind", "Charge");
            var currentContact = new Point(111, 222);
            var currentMouth = new Point(333, 444);
            SeedResult(owner, working, currentTarget, currentSnapshot, currentKind, currentContact, currentMouth);
            Set(owner, "_prankLarge", false);
            Set(owner, "_prankPresentationFinished", false);

            Invoke(owner, "ReleaseStoredPrank");
            Assert(Get<object>(owner, "_storedPrank") is null && Get<bool>(original, "_disposed"),
                "Releasing stored ownership must dispose and remove only the stored result.");
            Assert(ReferenceEquals(Get<WindowPrankController>(owner, "_windowPranks"), working) &&
                !Get<bool>(working, "_disposed") &&
                ReferenceEquals(Get<WindowPrankTarget>(owner, "_prankTarget"), currentTarget) &&
                ReferenceEquals(Get<WindowPrankSnapshot>(owner, "_prankSnapshot"), currentSnapshot) &&
                Equals(Get<object>(owner, "_prankKind"), currentKind) && !Get<bool>(owner, "_prankLarge") &&
                !Get<bool>(owner, "_prankPresentationFinished") &&
                Get<Point>(owner, "_screenStrikeContact") == currentContact &&
                Get<Point>(owner, "_prankHatMouth") == currentMouth,
                "A stored target disappearing must not clear, dispose or change an unrelated current attack.");
            Invoke(owner, "ReleaseStoredPrank");
            Assert(ReferenceEquals(Get<WindowPrankController>(owner, "_windowPranks"), working),
                "Repeated storage release must be harmless to the current slot.");
            currentSnapshot.Dispose();
        }
        finally
        {
            Invoke(owner, "ReleaseStoredPrank");
            Get<WindowPrankController>(owner, "_windowPranks")?.Dispose();
            original.Dispose();
        }
    }

    private static MainWindow CreateFieldContainer() =>
        // No Window constructor, visual tree or window handle is created by these ownership-transfer tests.
        (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));

    private static WindowPrankController CreateHeldController()
    {
        var controller = new WindowPrankController(IntPtr.Zero);
        Set(controller, "<IsHeld>k__BackingField", true);
        return controller;
    }

    private static WindowPrankTarget CreateTarget() => new(IntPtr.Zero, 0, 0,
        new DrawingRectangle(300, 200, 640, 400), new DrawingRectangle(292, 192, 656, 416),
        new DrawingRectangle(0, 0, 1920, 1040), new DrawingRectangle(0, 0, 1920, 1080));

    private static void SeedResult(MainWindow owner, WindowPrankController controller, WindowPrankTarget target,
        WindowPrankSnapshot? snapshot, object kind, Point contact, Point mouth)
    {
        Set(owner, "_windowPranks", controller);
        Set(owner, "_prankTarget", target);
        Set(owner, "_prankSnapshot", snapshot);
        Set(owner, "_prankKind", kind);
        Set(owner, "_prankLarge", true);
        Set(owner, "_screenStrikeContact", contact);
        Set(owner, "_prankHatMouth", mouth);
        Set(owner, "_prankHatMouthWidth", 76d);
        Set(owner, "_prankPresentationFinished", true);
    }

    private static object EnumValue(string type, string name) =>
        Enum.Parse(typeof(MainWindow).GetNestedType(type, BindingFlags.NonPublic)!, name);

    private static T? Get<T>(object owner, string name) => (T?)owner.GetType().GetField(name, PrivateInstance)!.GetValue(owner);
    private static void Set(object owner, string name, object? value) =>
        owner.GetType().GetField(name, PrivateInstance)!.SetValue(owner, value);
    private static T? Property<T>(object owner, string name) => (T?)owner.GetType().GetProperty(name)!.GetValue(owner);
    private static void Invoke(MainWindow owner, string name) =>
        typeof(MainWindow).GetMethod(name, PrivateInstance)!.Invoke(owner, null);

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
