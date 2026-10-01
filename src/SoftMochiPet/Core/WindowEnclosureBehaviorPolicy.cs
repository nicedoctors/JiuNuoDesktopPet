namespace SoftMochiPet.Core;

public static class WindowEnclosureBehaviorPolicy
{
    public static bool ShouldEscapeForMeal(bool isEnclosed, int pendingMeals, bool reactsToDeletes)
    {
        return isEnclosed && pendingMeals > 0 && reactsToDeletes;
    }

    public static bool AllowsIconLick(bool isEnclosed) => !isEnclosed;
}
