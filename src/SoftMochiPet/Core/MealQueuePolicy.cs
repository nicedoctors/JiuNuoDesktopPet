namespace SoftMochiPet.Core;

public static class MealQueuePolicy
{
    public const int MaximumBufferedDeletions = 24;

    public static bool CanAcceptDeletion(int queuedCount, bool hasActiveDeletionMeal)
    {
        var bufferedCount = Math.Max(0, queuedCount) + (hasActiveDeletionMeal ? 1 : 0);
        return bufferedCount < MaximumBufferedDeletions;
    }
}
