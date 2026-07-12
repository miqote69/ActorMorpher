namespace ActorMorpher.Interop;

public sealed class RegistryActorResolver(ActorRegistry registry, IClientContext? context = null) : IActorResolver
{
    public bool TryResolve(LogicalActorKey key, out ActorSnapshot snapshot)
    {
        if (registry.TryGet(key, out var actor))
        {
            ActorSnapshot? directGPose = null;
            if (context?.IsGPosing == true
                && actor.IsLocalPlayer
                && registry.TryGetGPoseLocalPlayer(key, out var resolvedGPose))
                directGPose = resolvedGPose;
            snapshot = SelectRepresentation(actor, context?.IsGPosing == true, directGPose)!;
            return snapshot is not null;
        }

        snapshot = null!;
        return false;
    }

    public static ActorSnapshot? SelectRepresentation(
        ActorEntry actor,
        bool isGPosing,
        ActorSnapshot? directGPoseRepresentation = null)
        => isGPosing
            ? actor.Representations.FirstOrDefault(static representation => representation.RepresentationKey.IsGPoseRepresentation)
                ?? directGPoseRepresentation
            : actor.Representations.FirstOrDefault(static representation => !representation.RepresentationKey.IsGPoseRepresentation)
                ?? actor.Current;
}
