namespace ActorMorpher.Actors;

public sealed class ActorIdentityService
{
    public bool Matches(LogicalActorKey expected, ActorSnapshot current)
        => expected == current.LogicalKey
        && expected.GameObjectId == current.RepresentationKey.GameObjectId
        && expected.EntityId == current.RepresentationKey.EntityId;

    public bool TryResolve(ActorRegistry registry, LogicalActorKey key, out ActorEntry actor)
        => registry.TryGet(key, out actor);
}
