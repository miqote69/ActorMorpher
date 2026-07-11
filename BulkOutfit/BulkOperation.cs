namespace ActorMorpher.BulkOutfit;

public enum BulkOperationType
{
    ApplyOutfit,
    UnequipAll,
    Restore,
}

public sealed class BulkOperation
{
    public BulkOperation(BulkOperationType type, IReadOnlyList<LogicalActorKey> targets)
    {
        OperationId = Guid.NewGuid();
        Type = type;
        Targets = targets.ToArray();
    }

    public Guid OperationId { get; }
    public BulkOperationType Type { get; }
    public IReadOnlyList<LogicalActorKey> Targets { get; }
    public int CurrentIndex { get; private set; }
    public int Succeeded { get; private set; }
    public int Skipped { get; private set; }
    public int Failed { get; private set; }
    public bool CancelRequested { get; private set; }

    public void RequestCancel()
        => CancelRequested = true;

    public void RecordSuccess()
    {
        Succeeded++;
        CurrentIndex++;
    }

    public void RecordSkip()
    {
        Skipped++;
        CurrentIndex++;
    }

    public void RecordFailure()
    {
        Failed++;
        CurrentIndex++;
    }
}
