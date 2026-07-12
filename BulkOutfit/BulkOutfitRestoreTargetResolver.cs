namespace ActorMorpher.BulkOutfit;

public static class BulkOutfitRestoreTargetResolver
{
    public static IReadOnlyList<LogicalActorKey> Resolve(
        IEnumerable<LogicalActorKey> modifiedActors,
        Func<LogicalActorKey, bool> isPinned)
        => modifiedActors
            .Where(actor => !isPinned(actor))
            .Distinct()
            .ToArray();
}
