namespace ActorMorpher.GPose;

public enum GPoseState
{
    Outside,
    Entering,
    WaitingForRepresentations,
    ResolvingMappings,
    ApplyingOverrides,
    Ready,
    Exiting,
    TimedOut,
}
