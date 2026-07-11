namespace ActorMorpher.Redraw;

public enum RedrawStage
{
    Pending,
    Apply,
    Disable,
    ApplyHidden,
    Enable,
    Recreate,
    Finalize,
    Verify,
    Rollback,
    RollbackDisable,
    RollbackHidden,
    RollbackEnable,
    RollbackRecreate,
    RollbackFinalize,
    RollbackVerify,
    Completed,
    Failed,
    Cancelled,
}
