namespace ActorMorpher.Interop;

public sealed class UnavailableAppearanceMemory : IAppearanceMemory
{
    public bool TryWrite(LogicalActorKey actor, AppearanceData appearance)
        => false;

    public bool IsApplied(LogicalActorKey actor, AppearanceData appearance)
        => false;
}
