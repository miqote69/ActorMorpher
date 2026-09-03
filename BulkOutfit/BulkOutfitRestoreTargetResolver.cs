namespace ActorMorpher.BulkOutfit;

public static class BulkOutfitRestoreTargetResolver
{
    public static IReadOnlyList<LogicalActorKey> Resolve(
        IEnumerable<LogicalActorKey> outfitActors,
        IEnumerable<LogicalActorKey> currentActors,
        Func<LogicalActorKey, bool> isModifiedOrPinned)
        => outfitActors
            .Concat(currentActors.Where(isModifiedOrPinned))
            .Distinct()
            .ToArray();
}
