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
