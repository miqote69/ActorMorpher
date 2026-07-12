namespace ActorMorpher.BulkOutfit;

public static class GPoseBulkActorSelector
{
    public static IReadOnlyList<ActorEntry> Select(
        IReadOnlyList<ActorEntry> actors,
        bool isGPosing,
        bool mappingsReady,
        Func<LogicalActorKey, bool> hasDirectLocalRepresentation)
    {
        if (!isGPosing)
            return actors;
        if (!mappingsReady)
            return Array.Empty<ActorEntry>();
        return actors.Where(actor =>
            actor.Representations.Any(static representation => representation.RepresentationKey.IsGPoseRepresentation)
            || actor.IsLocalPlayer && hasDirectLocalRepresentation(actor.Key)).ToArray();
    }
}
