namespace ActorMorpher.Redraw;

public enum RedrawStage
{
    Pending,
    Apply,
    Disable,
    ApplyHidden,
    Enable,
    Recreate,
    Verify,
    Rollback,
    RollbackDisable,
    RollbackHidden,
    RollbackEnable,
    RollbackRecreate,
    RollbackVerify,
    Completed,
    Failed,
    Cancelled,
}
