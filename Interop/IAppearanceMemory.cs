namespace ActorMorpher.Interop;

public interface IAppearanceMemory
{
    bool TryWrite(LogicalActorKey actor, AppearanceData appearance);
    bool IsApplied(LogicalActorKey actor, AppearanceData appearance);
}
