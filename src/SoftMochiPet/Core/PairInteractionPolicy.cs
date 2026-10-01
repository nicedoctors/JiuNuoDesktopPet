namespace SoftMochiPet.Core;

public enum CompanionActivity { Cheek, Sleep, Feed }
public enum IconPartnerReadiness { Ready, Wait, Unavailable }

public static class PairInteractionPolicy
{
    public const double FirstInvitationSeconds = 12;
    public const double InvitationRetrySeconds = 2;
    public const double IconPartnerWaitSeconds = 5;

    public static bool CanInvite(PetState state, MovementPurpose movement, bool sleepingTogether = false) =>
        state is PetState.Idle or PetState.Curious ||
        state == PetState.Running && movement is MovementPurpose.Wander or MovementPurpose.Explore or MovementPurpose.Patrol ||
        sleepingTogether && state == PetState.Sleeping;

    public static IconPartnerReadiness IconPartner(
        PetState state, MovementPurpose movement, bool dragging, bool hatUnavailable,
        bool attacking, bool controllerActive, bool controllerHeld)
    {
        if (dragging || hatUnavailable || attacking || controllerActive && !controllerHeld)
            return IconPartnerReadiness.Unavailable;
        if (CanInvite(state, movement)) return IconPartnerReadiness.Ready;
        return state is PetState.Falling or PetState.Landing or PetState.Sleeping or PetState.Waking
            ? IconPartnerReadiness.Wait : IconPartnerReadiness.Unavailable;
    }

    public static CompanionActivity ChooseActivity(
        bool quiet, bool fasting, double hunger, double nuonuoSleepiness, double feibiSleepiness,
        CompanionActivity? previous, double random)
    {
        if (quiet || Math.Max(nuonuoSleepiness, feibiSleepiness) >= 70) return CompanionActivity.Sleep;
        if (!fasting && hunger >= 55) return CompanionActivity.Feed;
        if (Math.Min(nuonuoSleepiness, feibiSleepiness) >= 40 && previous != CompanionActivity.Sleep && random >= .8)
            return CompanionActivity.Sleep;
        if (previous == CompanionActivity.Cheek) return CompanionActivity.Feed;
        if (previous == CompanionActivity.Feed) return CompanionActivity.Cheek;
        return random < .5 ? CompanionActivity.Cheek : CompanionActivity.Feed;
    }

    public static double NextInvitationDelay(double random) => 30 + 25 * Math.Clamp(random, 0, 1);

    public static double ApproachSeconds(double distancePixels, double monitorScale) =>
        Math.Clamp(distancePixels / (360 * Math.Max(.5, monitorScale)), .85, 5);
}
