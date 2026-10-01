namespace SoftMochiPet.Core;

public enum PetState
{
    Idle,
    Running,
    Falling,
    Landing,
    Curious,
    Sleeping,
    Waking,
    Climbing,
    Sliding,
    Rolling,
    Licking,
    Chomping,
    Satisfied,
    Dragging,
    Pinching,
    Pranking,
    Interacting,
    Paused
}

public enum MovementPurpose
{
    None,
    Meal,
    IconLick,
    Wander,
    Explore,
    Patrol,
    ClimbApproach,
    EdgeSlideApproach,
    EdgeJumpApproach,
}
