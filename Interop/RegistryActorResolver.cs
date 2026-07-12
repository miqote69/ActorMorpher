namespace ActorMorpher.Interop;

public sealed class RegistryActorResolver(ActorRegistry registry, IClientContext? context = null) : IActorResolver
{
    public bool TryResolve(LogicalActorKey key, out ActorSnapshot snapshot)
    {
        if (registry.TryGet(key, out var actor))
        {
            snapshot = SelectRepresentation(actor, context?.IsGPosing == true)!;
            return snapshot is not null;
        }

        snapshot = null!;
        return false;
    }

    public static ActorSnapshot? SelectRepresentation(ActorEntry actor, bool isGPosing)
        => isGPosing
            ? actor.Representations.FirstOrDefault(static representation => representation.RepresentationKey.IsGPoseRepresentation)
            : actor.Representations.FirstOrDefault(static representation => !representation.RepresentationKey.IsGPoseRepresentation)
                ?? actor.Current;
}
