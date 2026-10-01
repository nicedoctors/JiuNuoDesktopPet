using SoftMochiPet.Core;
using SoftMochiPet.Services;

internal static class WindowPrankHoldTests
{
    public static void HoldingRequiresConfirmedMinimizeAndUnchangedOwnership()
    {
        bool CanHold(bool confirmed = true, bool restoring = false, bool identity = true,
            bool minimized = true, bool foreground = false, bool maximized = false, bool placement = true) =>
            WindowPrankHoldPolicy.CanHold(confirmed, restoring, identity, minimized, foreground, maximized, placement);
        Assert(CanHold(), "A confirmed, still-owned minimized snapshot can wait for an explicit return.");
        Assert(!CanHold(confirmed: false) && !CanHold(restoring: true) && !CanHold(identity: false) &&
            !CanHold(minimized: false) && !CanHold(foreground: true) && !CanHold(maximized: true) &&
            !CanHold(placement: false),
            "A queued minimize, an in-flight return, or any external takeover must not become a held result.");
    }

    public static void HeldResultsSurviveUnrelatedForegroundChanges()
    {
        Assert(!WindowPrankHoldPolicy.ShouldAbortForForegroundChange(held: true, foregroundChanged: true) &&
            !WindowPrankHoldPolicy.ShouldAbortForForegroundChange(held: true, foregroundChanged: false),
            "A held result must survive switching between applications without a timed or foreground-based return.");
        Assert(WindowPrankHoldPolicy.ShouldAbortForForegroundChange(held: false, foregroundChanged: true) &&
            !WindowPrankHoldPolicy.ShouldAbortForForegroundChange(held: false, foregroundChanged: false),
            "The original foreground safety cancellation remains active during a kick or snapshot animation.");
    }

    public static void PetInteractionKeepsResultsButExplicitAndSafetyReturnsDoNot()
    {
        Assert(new[] { "PickedUp", "BringHome", "QuietMode", "AutonomyDisabled", "SizeChanged",
                "FeatureDemoHatlessPickup", "ManualRestorePreparation", "PetDisplayAdapted" }
                .All(WindowPrankHoldPolicy.KeepResultOnPetInterruption),
            "Ordinary pet interactions must retain an already confirmed held window.");
        Assert(new[] { "ManualReturn", "WindowClosing", "CharacterRequested", "CharacterChanged", "SessionUnavailable",
                "Suspend", "DisplayChanged", "PlaybackFailed", "FeatureDemoStopped" }
                .All(reason => !WindowPrankHoldPolicy.KeepResultOnPetInterruption(reason)),
            "Explicit returns, shutdown, ownership changes and failures must keep their safe restore path.");
    }

    public static void HeldMonitoringReleasesExternalRestoreWithoutRehiding()
    {
        Assert(!WindowPrankHoldPolicy.ShouldRelinquishOwnership(true, false, false) &&
            WindowPrankHoldPolicy.ShouldRelinquishOwnership(false, false, false) &&
            WindowPrankHoldPolicy.ShouldRelinquishOwnership(true, true, false) &&
            WindowPrankHoldPolicy.ShouldRelinquishOwnership(true, false, true),
            "A closed/replaced handle or the target's own foreground/maximize takeover releases ownership immediately.");
        Assert(!WindowPrankGeometry.ShouldRelinquishSnapshot(restoring: false, iconic: true, placementUnchanged: true),
            "An unchanged minimized target stays owned while the pet is idle, with no holding-time input.");
        Assert(WindowPrankGeometry.ShouldRelinquishSnapshot(restoring: false, iconic: false, placementUnchanged: true) &&
            WindowPrankGeometry.ShouldRelinquishSnapshot(restoring: false, iconic: true, placementUnchanged: false),
            "A taskbar restore or external placement change relinquishes the snapshot rather than requesting another minimize.");
        Assert(!WindowPrankGeometry.ShouldRelinquishSnapshot(restoring: true, iconic: false, placementUnchanged: true) &&
            !WindowPrankGeometry.ShouldRelinquishSnapshot(restoring: true, iconic: true, placementUnchanged: true),
            "The pet's explicit asynchronous return remains valid before and after native acknowledgement.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
