namespace SoftMochiPet.Services;

public static class WindowPrankHoldPolicy
{
    public static bool KeepResultOnPetInterruption(string reason) => reason is
        "PickedUp" or "BringHome" or "QuietMode" or "AutonomyDisabled" or "SizeChanged" or
        "FeatureDemoHatlessPickup" or "ManualRestorePreparation" or "PetDisplayAdapted" or
        "BehaviorControlStop";

    // An unrelated attack losing its target does not invalidate a window already stored in the hat.
    public static bool MustReturnStoredResult(string reason) => reason is
        "CharacterRequested" or "CharacterChanged" or "SessionUnavailable" or "Suspend" or
        "DisplayChanged" or "WindowClosing" or "PlaybackFailed" or "FeatureDemoStopped" or
        "FeatureDemoFinished" or "FeatureDemoStepFailed" or "FeatureDemoStageBoundary";

    public static bool CanHold(bool snapshotMinimized, bool restoring, bool sameLiveIdentity,
        bool minimized, bool targetForeground, bool maximized, bool placementUnchanged) =>
        snapshotMinimized && !restoring && sameLiveIdentity && minimized &&
        !targetForeground && !maximized && placementUnchanged;

    public static bool ShouldRelinquishOwnership(bool sameLiveIdentity, bool targetForeground, bool maximized) =>
        !sameLiveIdentity || targetForeground || maximized;

    // A finished result belongs to the pet until explicitly returned. Switching
    // to another application is only a cancellation condition during the action.
    public static bool ShouldAbortForForegroundChange(bool held, bool foregroundChanged,
        bool foregroundIsOwnUi = false) =>
        !held && foregroundChanged && !foregroundIsOwnUi;
}
