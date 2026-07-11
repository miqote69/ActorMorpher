using ActorMorpher.Interop;
using Dalamud.Plugin.Services;

namespace ActorMorpher.BulkOutfit;

public sealed class BulkOutfitService : IDisposable
{
    private readonly IFramework? framework;
    private readonly IActorResolver resolver;
    private readonly IOutfitMemory memory;
    private readonly IClientContext context;
    private readonly OutfitOverrideStore store;
    private readonly IDiagnosticLog diagnostics;
    private readonly UnequipPlanBuilder unequipBuilder = new();
    private readonly IUnequipAppearanceProvider unequipProvider = new VerifiedUnequipAppearanceProvider();
    private readonly HashSet<LogicalActorKey> pendingReapply = new();
    private BulkOperation? operation;
    private OutfitData? operationOutfit;
    private uint lastTerritory;
    private bool disposed;

    public BulkOutfitService(
        IFramework framework,
        IActorResolver resolver,
        IOutfitMemory memory,
        IClientContext context,
        OutfitOverrideStore store,
        IDiagnosticLog diagnostics)
        : this(resolver, memory, context, store, diagnostics)
    {
        this.framework = framework;
        framework.Update += OnFrameworkUpdate;
    }

    public BulkOutfitService(
        IActorResolver resolver,
        IOutfitMemory memory,
        IClientContext context,
        OutfitOverrideStore store,
        IDiagnosticLog diagnostics)
    {
        this.resolver = resolver;
        this.memory = memory;
        this.context = context;
        this.store = store;
        this.diagnostics = diagnostics;
    }

    public OutfitData? SourceOutfit { get; private set; }
    public BulkOperation? CurrentOperation => operation;
    public OutfitOverrideStore Store => store;
    public int ModifiedActorCount => store.States.Count;
    public string LastStatus { get; private set; } = string.Empty;

    public bool RefreshSource(LogicalActorKey localPlayer, out string message)
    {
        if (disposed)
        {
            message = "Bulk Outfit services are shutting down.";
            return false;
        }
        if (!resolver.TryResolve(localPlayer, out var actor) || !memory.TryCapture(actor, out var outfit))
        {
            SourceOutfit = null;
            message = "The current local player Human outfit is unavailable.";
            return false;
        }
        SourceOutfit = outfit;
        message = "Source outfit refreshed.";
        LastStatus = message;
        return true;
    }

    public bool StartApply(IReadOnlyList<LogicalActorKey> targets, out string message)
    {
        if (SourceOutfit is null)
        {
            message = "Refresh the source outfit first.";
            return false;
        }
        return Start(BulkOperationType.ApplyOutfit, targets, SourceOutfit, out message);
    }

    public bool StartUnequip(IReadOnlyList<LogicalActorKey> targets, out string message)
    {
        if (targets.Count == 0)
        {
            message = "No eligible Human actors matched the filters.";
            return false;
        }
        return Start(BulkOperationType.UnequipAll, targets, null, out message);
    }

    public bool StartRestore(out string message)
        => Start(BulkOperationType.Restore, store.States.Keys.ToArray(), null, out message);

    public void Cancel()
    {
        operation?.RequestCancel();
        LastStatus = "Cancellation requested.";
    }

    public void Reapply(LogicalActorKey key)
    {
        if (!store.TryGet(key, out _))
            return;
        pendingReapply.Add(key);
        TryStartPendingReapply();
    }

    public void ReapplyAll()
    {
        pendingReapply.UnionWith(store.States.Keys);
        TryStartPendingReapply();
    }

    public void ReapplyWithoutAppearanceOverrides(IReadOnlyCollection<LogicalActorKey> appearanceOverrides)
    {
        var excluded = appearanceOverrides.ToHashSet();
        pendingReapply.UnionWith(store.States.Keys.Where(key => !excluded.Contains(key)));
        TryStartPendingReapply();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (framework is not null)
            framework.Update -= OnFrameworkUpdate;
        operation = null;
        operationOutfit = null;
        pendingReapply.Clear();
        SourceOutfit = null;
        store.Clear();
    }

    private bool Start(BulkOperationType type, IReadOnlyList<LogicalActorKey> targets, OutfitData? outfit, out string message)
    {
        if (disposed)
        {
            message = "Bulk Outfit services are shutting down.";
            return false;
        }
        if (operation is not null)
        {
            message = "Another Bulk Outfit operation is already running.";
            return false;
        }
        if (targets.Count == 0)
        {
            message = "There are no actors to process.";
            return false;
        }
        operation = new BulkOperation(type, targets.Distinct().ToArray());
        operationOutfit = outfit;
        message = $"Started {type} for {operation.Targets.Count} actors.";
        LastStatus = message;
        var startedEventId = type switch
        {
            BulkOperationType.UnequipAll => DiagnosticEventIds.UnequipBatchStarted,
            BulkOperationType.Restore => DiagnosticEventIds.RestoreBatchStarted,
            _ => DiagnosticEventIds.BulkBatchStarted,
        };
        diagnostics.Write(new DiagnosticLogEntry
        {
            EventId = startedEventId,
            Category = DiagnosticCategory.BulkOutfit,
            Message = message,
            OperationId = $"bulk-{operation.OperationId:N}",
            Properties = new Dictionary<string, object?> { ["targetCount"] = operation.Targets.Count, ["type"] = type },
        });
        return true;
    }

    private void OnFrameworkUpdate(IFramework framework)
        => ProcessNextFrame();

    public void ProcessNextFrame()
    {
        if (lastTerritory == 0)
        {
            lastTerritory = context.TerritoryId;
            return;
        }
        if (lastTerritory != context.TerritoryId || !context.IsLoggedIn)
        {
            operation = null;
            operationOutfit = null;
            pendingReapply.Clear();
            store.Clear();
            lastTerritory = context.TerritoryId;
            return;
        }
        lastTerritory = context.TerritoryId;
        if (operation is null)
            return;
        if (operation.CancelRequested || operation.CurrentIndex >= operation.Targets.Count)
        {
            FinishOperation(operation.CancelRequested);
            return;
        }

        var activeOperation = operation;
        var key = activeOperation.Targets[activeOperation.CurrentIndex];
        ActorSnapshot? actor = null;
        OutfitData? current = null;
        store.TryGet(key, out var storeBeforeOperation);
        try
        {
            if (!resolver.TryResolve(key, out actor) || !memory.TryCapture(actor, out current))
            {
                activeOperation.RecordSkip();
                WriteActorLog(DiagnosticEventIds.OutfitSkipped, "Actor outfit could not be captured.", key, "Skipped");
                return;
            }
            ProcessActor(activeOperation, key, actor, current);
        }
        catch (Exception exception)
        {
            var rolledBack = TryRollback(actor, current);
            store.RestoreState(key, storeBeforeOperation);
            if (activeOperation.CurrentIndex < activeOperation.Targets.Count
                && activeOperation.Targets[activeOperation.CurrentIndex] == key)
                activeOperation.RecordFailure();
            diagnostics.Write(new DiagnosticLogEntry
            {
                Level = DiagnosticLogLevel.Error,
                EventId = DiagnosticEventIds.BulkActorFailed,
                Category = DiagnosticCategory.BulkOutfit,
                Message = "Bulk Outfit actor processing threw an exception.",
                OperationId = $"bulk-{activeOperation.OperationId:N}",
                ActorKey = DiagnosticActorKeys.Format(diagnostics, key),
                Outcome = rolledBack ? "RolledBack" : "RollbackFailed",
                Exception = DiagnosticExceptionInfo.FromException(exception),
            });
        }
    }

    private void ProcessActor(BulkOperation activeOperation, LogicalActorKey key, ActorSnapshot actor, OutfitData current)
    {
        WriteActorLog(DiagnosticEventIds.OutfitSnapshotCaptured, "Actor outfit snapshot captured.", key, "Captured");

        if (activeOperation.Type == BulkOperationType.Restore)
        {
            if (!store.TryGet(key, out var state))
            {
                activeOperation.RecordSkip();
                return;
            }
            if (memory.TryApply(actor, state.Original) && memory.IsApplied(actor, state.Original))
            {
                store.CompleteRestore(key);
                activeOperation.RecordSuccess();
                WriteActorLog(DiagnosticEventIds.OutfitApplied, "Original actor outfit restored.", key, "Restored");
            }
            else
            {
                var rolledBack = TryRollback(actor, current);
                activeOperation.RecordFailure();
                WriteActorLog(DiagnosticEventIds.OutfitRolledBack, "Outfit restore failed and the current outfit was reapplied.", key, rolledBack ? "RolledBack" : "RollbackFailed");
            }
            return;
        }

        OutfitData? desired = operationOutfit;
        if (activeOperation.Type == BulkOperationType.UnequipAll
            && !unequipBuilder.TryCreate(current, unequipProvider, out desired, out _))
        {
            activeOperation.RecordFailure();
            return;
        }
        if (desired is null && store.TryGet(key, out var stored))
            desired = stored.Desired;
        if (desired is null)
        {
            activeOperation.RecordSkip();
            return;
        }
        store.TryGet(key, out var previous);
        store.SetDesired(key, current, desired);
        if (memory.TryApply(actor, desired) && memory.IsApplied(actor, desired))
        {
            activeOperation.RecordSuccess();
            WriteActorLog(DiagnosticEventIds.OutfitApplied, "Desired actor outfit applied.", key, "Success");
        }
        else
        {
            var rolledBack = TryRollback(actor, current);
            store.RestoreState(key, previous);
            activeOperation.RecordFailure();
            WriteActorLog(DiagnosticEventIds.OutfitRolledBack, "Outfit apply failed and was rolled back.", key, rolledBack ? "RolledBack" : "RollbackFailed");
        }
    }

    private bool TryRollback(ActorSnapshot? actor, OutfitData? outfit)
    {
        if (actor is null || outfit is null)
            return false;
        try
        {
            return memory.TryApply(actor, outfit) && memory.IsApplied(actor, outfit);
        }
        catch
        {
            return false;
        }
    }

    private void FinishOperation(bool cancelled)
    {
        if (operation is null)
            return;
        LastStatus = $"{operation.Type}: {operation.Succeeded} succeeded, {operation.Skipped} skipped, {operation.Failed} failed.";
        diagnostics.Write(new DiagnosticLogEntry
        {
            EventId = cancelled ? DiagnosticEventIds.BulkBatchCancelled : DiagnosticEventIds.BulkBatchCompleted,
            Category = DiagnosticCategory.BulkOutfit,
            Message = LastStatus,
            OperationId = $"bulk-{operation.OperationId:N}",
            Outcome = cancelled ? "Cancelled" : operation.Failed > 0 ? "Failed" : "Success",
            Properties = new Dictionary<string, object?>
            {
                ["succeeded"] = operation.Succeeded,
                ["skipped"] = operation.Skipped,
                ["failed"] = operation.Failed,
            },
        });
        operation = null;
        operationOutfit = null;
        TryStartPendingReapply();
    }

    private void TryStartPendingReapply()
    {
        if (disposed || operation is not null || pendingReapply.Count == 0)
            return;
        var targets = pendingReapply.Where(key => store.TryGet(key, out _)).ToArray();
        pendingReapply.Clear();
        if (targets.Length > 0)
            Start(BulkOperationType.ApplyOutfit, targets, null, out _);
    }

    private void WriteActorLog(string eventId, string message, LogicalActorKey actor, string outcome)
        => diagnostics.Write(new DiagnosticLogEntry
        {
            EventId = eventId,
            Category = DiagnosticCategory.BulkOutfit,
            Message = message,
            OperationId = operation is { } current ? $"bulk-{current.OperationId:N}" : null,
            ActorKey = DiagnosticActorKeys.Format(diagnostics, actor),
            Outcome = outcome,
        });
}
