namespace ActorMorpher.Interop;

public sealed class RegistryActorResolver(ActorRegistry registry) : IActorResolver
{
    public bool TryResolve(LogicalActorKey key, out ActorSnapshot snapshot)
    {
        if (registry.TryGet(key, out var actor))
        {
            snapshot = actor.Current;
            return true;
        }

        snapshot = null!;
        return false;
    }
}
