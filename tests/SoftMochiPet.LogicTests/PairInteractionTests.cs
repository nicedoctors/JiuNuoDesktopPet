using SoftMochiPet.Core;

internal static class PairInteractionTests
{
    public static void InvitationsInterruptWanderingButNotOwnedActions()
    {
        foreach (var purpose in new[] { MovementPurpose.Wander, MovementPurpose.Explore, MovementPurpose.Patrol })
            Assert(PairInteractionPolicy.CanInvite(PetState.Running, purpose), "Ordinary walking must not block companionship.");
        foreach (var purpose in new[] { MovementPurpose.Meal, MovementPurpose.ClimbApproach, MovementPurpose.EdgeJumpApproach })
            Assert(!PairInteractionPolicy.CanInvite(PetState.Running, purpose), "Do not steal meals or committed terrain actions.");
        foreach (var state in new[] { PetState.Dragging, PetState.Pinching, PetState.Falling, PetState.Pranking, PetState.Chomping })
            Assert(!PairInteractionPolicy.CanInvite(state, MovementPurpose.None), "Owned actions must remain protected.");
        Assert(PairInteractionPolicy.CanInvite(PetState.Sleeping, MovementPurpose.None, sleepingTogether: true) &&
            !PairInteractionPolicy.CanInvite(PetState.Sleeping, MovementPurpose.None), "A sleeping pet may join shared sleep, not a cheek prank.");
    }

    public static void IconMealsAcceptWalkingAndStoredWindowsButNotAttacks()
    {
        var running = PairInteractionPolicy.IconPartner(PetState.Running, MovementPurpose.Patrol,
            false, false, false, true, true);
        Assert(running == IconPartnerReadiness.Ready, "A walking partner with a stored window can kick the icon.");
        Assert(PairInteractionPolicy.IconPartner(PetState.Idle, MovementPurpose.None,
            false, false, false, true, false) == IconPartnerReadiness.Unavailable,
            "An active window operation cannot be mistaken for a stored result.");
        foreach (var blocker in new[] { 0, 1, 2 })
            Assert(PairInteractionPolicy.IconPartner(PetState.Idle, MovementPurpose.None,
                blocker == 0, blocker == 1, blocker == 2, false, false) == IconPartnerReadiness.Unavailable,
                "Dragging, missing hats and attacks must not be interrupted.");
    }

    public static void IconMealsWaitForShortTransitionsThenCanStart()
    {
        foreach (var state in new[] { PetState.Falling, PetState.Landing, PetState.Sleeping, PetState.Waking })
            Assert(PairInteractionPolicy.IconPartner(state, MovementPurpose.None, false, false, false, false, false) ==
                IconPartnerReadiness.Wait, "Brief unavailable states should wait rather than immediately choose solo consumption.");
        Assert(PairInteractionPolicy.IconPartner(PetState.Idle, MovementPurpose.None, false, false, false, false, false) ==
            IconPartnerReadiness.Ready, "Landing or waking completion must release the waiting pair.");
        Assert(PairInteractionPolicy.IconPartnerWaitSeconds == 5, "Deleted icons must not wait without a bounded solo fallback.");
    }

    public static void SharedActivitiesRespondToNeedsAndDoNotRepeatPointlessly()
    {
        Assert(PairInteractionPolicy.ChooseActivity(false, false, 65, 20, 20, null, 0) == CompanionActivity.Feed,
            "A hungry Nuonuo should receive a snack from Feibi.");
        Assert(PairInteractionPolicy.ChooseActivity(false, false, 65, 75, 20, null, 0) == CompanionActivity.Sleep,
            "High sleepiness takes priority over a casual snack.");
        Assert(PairInteractionPolicy.ChooseActivity(true, false, 65, 20, 20, null, 0) == CompanionActivity.Sleep,
            "Quiet mode may only invite shared rest.");
        Assert(PairInteractionPolicy.ChooseActivity(false, true, 65, 20, 20, CompanionActivity.Feed, 0) == CompanionActivity.Cheek,
            "Fasting must not request food based on stale hunger.");
        Assert(PairInteractionPolicy.ChooseActivity(false, false, 20, 20, 20, CompanionActivity.Cheek, 0) == CompanionActivity.Feed &&
            PairInteractionPolicy.ChooseActivity(false, false, 20, 20, 20, CompanionActivity.Feed, 1) == CompanionActivity.Cheek,
            "Casual play should alternate between the existing animations.");
    }

    public static void InvitationsAndApproachHaveBoundedReadableTiming()
    {
        Assert(PairInteractionPolicy.FirstInvitationSeconds == 12 && PairInteractionPolicy.InvitationRetrySeconds == 2 &&
            PairInteractionPolicy.NextInvitationDelay(0) == 30 && PairInteractionPolicy.NextInvitationDelay(1) == 55,
            "Companionship should be visible soon after enabling coexistence and retry promptly when busy.");
        Assert(PairInteractionPolicy.ApproachSeconds(1200, 1) > PairInteractionPolicy.ApproachSeconds(300, 1),
            "Long approaches must not rush across the monitor in the old fixed 0.85 seconds.");
        Assert(PairInteractionPolicy.ApproachSeconds(1200, 1) == PairInteractionPolicy.ApproachSeconds(1500, 1.25),
            "Equivalent DIP distances should have the same approach speed on mixed DPI monitors.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
