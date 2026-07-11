namespace ActorMorpher.Redraw;

public sealed record RedrawOperation(
    Guid OperationId,
    LogicalActorKey Actor,
    AppearanceData Desired,
    AppearanceData Rollback,
    long Revision,
    uint TerritoryId,
    RedrawStage Stage,
    int FrameCount,
    string? Error)
{
    public static RedrawOperation Create(
        LogicalActorKey actor,
        AppearanceData desired,
        AppearanceData rollback,
        long revision,
        uint territoryId)
        => new(Guid.NewGuid(), actor, desired, rollback, revision, territoryId, RedrawStage.Pending, 0, null);
}
