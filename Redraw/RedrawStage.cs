namespace ActorMorpher.Redraw;

public enum RedrawStage
{
    Pending,
    Disable,
    Apply,
    Enable,
    Verify,
    Rollback,
    RollbackEnable,
    Completed,
    Failed,
    Cancelled,
}
