using SoftMochiPet.Core;

namespace SoftMochiPet.Services;

public static class FeatureDemoEvidencePolicy
{
    public static bool Matches(FeatureDemoAction action, long previousSequence, IntPtr handle,
        uint processId, WindowPrankCompletionEvidence? evidence)
    {
        if (evidence is null || evidence.Sequence <= previousSequence || evidence.Handle != handle ||
            evidence.ProcessId != processId) return false;
        var operation = action switch
        {
            FeatureDemoAction.HatStoreReturn => "Hat",
            FeatureDemoAction.Kick => "Kick",
            FeatureDemoAction.Punch => "Punch",
            FeatureDemoAction.Charge => "Charge",
            FeatureDemoAction.Shatter => "Shatter",
            FeatureDemoAction.Tear => "Tear",
            _ => null,
        };
        if (operation is null || evidence.Operation != operation) return false;
        if (action is FeatureDemoAction.Kick or FeatureDemoAction.Punch or FeatureDemoAction.Charge)
            return evidence.InitialOuterBounds.Size == evidence.FinalOuterBounds.Size &&
                !WindowPrankGeometry.SameBounds(evidence.InitialOuterBounds, evidence.FinalOuterBounds);
        return evidence.MinimizeConfirmed && evidence.RestoreConfirmed &&
            WindowPrankGeometry.SameBounds(evidence.InitialOuterBounds, evidence.FinalOuterBounds);
    }
}
