namespace SoftMochiPet.Core;

/// <summary>
/// User input may interrupt autonomous locomotion. Only short, atomic actions
/// that must finish their current animation reject a new drag gesture.
/// </summary>
public static class DragInteractionPolicy
{
    public static bool CanStart(PetState state) => state is not (
        PetState.Chomping or
        PetState.Climbing or
        PetState.Dragging or
        PetState.Pinching or
        PetState.Rolling or
        PetState.Waking);
}
