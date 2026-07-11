namespace ActorMorpher.Interop;

public sealed class RegistryActorResolver(ActorRegistry registry, IClientContext? context = null) : IActorResolver
{
    public bool TryResolve(LogicalActorKey key, out ActorSnapshot snapshot)
    {
        if (registry.TryGet(key, out var actor))
        {
            snapshot = context?.IsGPosing == true
                ? actor.Representations.FirstOrDefault(static representation => representation.RepresentationKey.IsGPoseRepresentation) ?? actor.Current
                : actor.Representations.FirstOrDefault(static representation => !representation.RepresentationKey.IsGPoseRepresentation) ?? actor.Current;
            return true;
        }

        snapshot = null!;
        return false;
    }
}
