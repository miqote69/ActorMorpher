namespace ActorMorpher.BulkOutfit;

public sealed record BulkOutfitPreview(
    int MatchingLogicalActors,
    int ExcludedLogicalActors,
    int EligibleHumanActors,
    int SkippedNonHumanActors,
    int UnavailableActors,
    IReadOnlyList<LogicalActorKey> EligibleTargets);
