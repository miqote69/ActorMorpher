namespace ActorMorpher.BulkOutfit;

public static class BulkOutfitRestoreTargetResolver
{
    public static IReadOnlyList<LogicalActorKey> Resolve(
        IEnumerable<LogicalActorKey> outfitActors,
        IEnumerable<LogicalActorKey> currentActors,
        Func<LogicalActorKey, bool> isModified,
        Func<LogicalActorKey, bool> isPinned)
        => outfitActors
            .Concat(currentActors.Where(isModified))
            .Distinct()
            .Where(actor => !isPinned(actor))
            .ToArray();
}
