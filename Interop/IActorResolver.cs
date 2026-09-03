namespace ActorMorpher.Interop;

public interface IActorResolver
{
    bool TryResolve(LogicalActorKey key, out ActorSnapshot snapshot);

    bool TryResolve(LogicalActorKey key, ActorRepresentationKey representation, out ActorSnapshot snapshot)
        => TryResolve(key, out snapshot) && snapshot.RepresentationKey == representation;
}
