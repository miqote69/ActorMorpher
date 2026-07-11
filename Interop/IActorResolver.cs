namespace ActorMorpher.Interop;

public interface IActorResolver
{
    bool TryResolve(LogicalActorKey key, out ActorSnapshot snapshot);
}
