namespace ActorMorpher.Redraw;

public sealed record RedrawOperation(
    Guid OperationId,
    LogicalActorKey Actor,
    ActorRepresentationKey TargetRepresentation,
    AppearanceData Desired,
    long Revision,
    uint TerritoryId,
    RedrawStage Stage,
    int FrameCount,
    string? Error)
{
    // Null result means the target is unattached and uses the ordinary redraw.
    internal Func<ActorSnapshot, bool?>? TryApplyAttachedEquipment { get; init; }

    public static RedrawOperation Create(
        LogicalActorKey actor,
        ActorRepresentationKey targetRepresentation,
        AppearanceData desired,
        long revision,
        uint territoryId)
        => new(
            Guid.NewGuid(),
            actor,
            targetRepresentation,
            desired,
            revision,
            territoryId,
            RedrawStage.Pending,
            0,
            null);
}
