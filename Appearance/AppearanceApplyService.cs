using ActorMorpher.Interop;
using Dalamud.Plugin.Services;

namespace ActorMorpher.Appearance;

public sealed class AppearanceApplyService : IDisposable
{
    private readonly IFramework? framework;
    private readonly IActorResolver resolver;
    private readonly IAppearanceMemory memory;
    private readonly IClientContext context;
    private readonly RedrawCoordinator redraw;
    private readonly AppearanceOverrideStore store;
    private readonly IDiagnosticLog diagnostics;
    private readonly Dictionary<Guid, PendingChange> pending = new();
    private uint lastTerritory;
    private bool wasLoggedIn;
    private bool disposed;

    public AppearanceApplyService(
        IFramework framework,
        IActorResolver resolver,
        IAppearanceMemory memory,
        IClientContext context,
        RedrawCoordinator redraw,
        AppearanceOverrideStore store,
        IDiagnosticLog diagnostics)
        : this(resolver, memory, context, redraw, store, diagnostics)
    {
        this.framework = framework;
        framework.Update += OnFrameworkUpdate;
    }

    public AppearanceApplyService(
        IActorResolver resolver,
        IAppearanceMemory memory,
        IClientContext context,
        RedrawCoordinator redraw,
        AppearanceOverrideStore store,
        IDiagnosticLog diagnostics)
    {
        this.resolver = resolver;
        this.memory = memory;
        this.context = context;
        this.redraw = redraw;
        this.store = store;
        this.diagnostics = diagnostics;
        redraw.OperationFinished += OnOperationFinished;
    }

    public AppearanceOverrideStore Store => store;
    public string LastStatus { get; private set; } = string.Empty;
    public event Action<LogicalActorKey, uint, bool, bool>? OperationCompleted;

    public bool IsPending(LogicalActorKey key)
        => pending.Values.Any(change => change.Actor == key);

    public bool TryApply(LogicalActorKey key, AppearanceData desired, out string message)
    {
        if (disposed)
        {
            message = "Appearance services are shutting down.";
            return false;
        }
        if (!CanApply(desired, out message))
            return false;
        if (IsPending(key))
        {
            message = "An appearance operation is already pending for this actor.";
            return false;
        }
        if (!resolver.TryResolve(key, out var actor) || !memory.TryCapture(actor, out var current))
        {
            message = "The actor is no longer available.";
            return false;
        }

        WriteMorphLog(DiagnosticEventIds.MorphSnapshotCaptured, "Current appearance snapshot captured.", key, current.ModelCharaId, appearance: current);

        store.TryGet(key, out var previous);
        var state = store.SetDesired(key, current, desired);
        var cleanHumanTransition = previous is not null
            && previous.DesiredData.Category == ModelCategory.Human
            && desired.Category == ModelCategory.Human;
        var firstDesired = cleanHumanTransition ? state.BaseData : desired;
        var operation = RedrawOperation.Create(key, firstDesired, current, state.Revision, context.TerritoryId);
        pending[operation.OperationId] = new PendingChange(
            key,
            previous,
            false,
            cleanHumanTransition ? desired : null,
            state.Revision);
        if (!redraw.Enqueue(operation))
        {
            pending.Remove(operation.OperationId);
            store.RestoreState(key, previous);
            message = "The redraw operation could not be queued.";
            return false;
        }

        WriteMorphLog(DiagnosticEventIds.MorphOperationStarted, "Appearance operation queued.", key, desired.ModelCharaId, operation.OperationId, desired, state.Revision, state.BaseData.ModelCharaId);
        WriteMorphLog(DiagnosticEventIds.MorphDesiredUpdated, "Desired appearance state updated.", key, desired.ModelCharaId, operation.OperationId, desired, state.Revision, state.BaseData.ModelCharaId);

        message = cleanHumanTransition
            ? $"Preparing a clean Human transition to Model ID {desired.ModelCharaId}."
            : $"Applying Model ID {desired.ModelCharaId}.";
        LastStatus = message;
        return true;
    }

    public bool TryRestore(LogicalActorKey key, out string message)
    {
        if (disposed)
        {
            message = "Appearance services are shutting down.";
            return false;
        }
        if (!store.TryGet(key, out var state))
        {
            message = "No Actor Morpher appearance snapshot is available.";
            return false;
        }
        if (IsPending(key))
        {
            message = "An appearance operation is already pending for this actor.";
            return false;
        }
        if (!resolver.TryResolve(key, out var actor) || !memory.TryCapture(actor, out var current))
        {
            message = "The actor is no longer available.";
            return false;
        }

        var operation = RedrawOperation.Create(key, state.BaseData, current, state.Revision + 1, context.TerritoryId);
        pending[operation.OperationId] = new PendingChange(key, state, true, null, state.Revision + 1);
        if (!redraw.Enqueue(operation))
        {
            pending.Remove(operation.OperationId);
            message = "The restore operation could not be queued.";
            return false;
        }
        WriteMorphLog(DiagnosticEventIds.MorphOperationStarted, "Appearance restore operation queued.", key, state.BaseData.ModelCharaId, operation.OperationId, state.BaseData, state.Revision + 1, state.BaseData.ModelCharaId);
        message = "Restoring the original appearance.";
        LastStatus = message;
        return true;
    }

    public void ReapplyAll()
    {
        foreach (var (key, state) in store.States)
        {
            if (pending.Values.Any(change => change.Actor == key)
                || !resolver.TryResolve(key, out var actor)
                || !memory.TryCapture(actor, out var current))
                continue;
            var operation = RedrawOperation.Create(key, state.DesiredData, current, state.Revision, context.TerritoryId);
            pending[operation.OperationId] = new PendingChange(key, state, false, null, state.Revision);
            if (!redraw.Enqueue(operation))
                pending.Remove(operation.OperationId);
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (framework is not null)
            framework.Update -= OnFrameworkUpdate;
        redraw.OperationFinished -= OnOperationFinished;
        pending.Clear();
        store.Clear();
    }

    private static bool CanApply(AppearanceData desired, out string message)
    {
        var supported = desired.Category switch
        {
            ModelCategory.Human or ModelCategory.Demihuman => desired.Completeness == AppearanceCompleteness.Complete,
            ModelCategory.Monster => desired.Completeness is AppearanceCompleteness.Complete or AppearanceCompleteness.ModelOnly,
            _ => false,
        };
        message = supported ? string.Empty : "The selected model does not contain a supported appearance payload.";
        return supported;
    }

    private void OnOperationFinished(RedrawOperation operation)
    {
        if (!pending.Remove(operation.OperationId, out var change))
            return;
        if (operation.Stage == RedrawStage.Completed)
        {
            if (change.FinalDesired is { } finalDesired)
            {
                if (!resolver.TryResolve(change.Actor, out var actor)
                    || !memory.TryCapture(actor, out _))
                {
                    store.RestoreState(change.Actor, change.PreviousState);
                    LastStatus = "The actor became unavailable during the Human transition.";
                    OperationCompleted?.Invoke(change.Actor, finalDesired.ModelCharaId, false, false);
                    return;
                }

                var rollback = change.PreviousState?.DesiredData ?? operation.Desired;
                var continuation = RedrawOperation.Create(
                    change.Actor,
                    finalDesired,
                    rollback,
                    change.Revision,
                    context.TerritoryId);
                pending[continuation.OperationId] = change with { FinalDesired = null };
                if (!redraw.Enqueue(continuation))
                {
                    pending.Remove(continuation.OperationId);
                    store.RestoreState(change.Actor, change.PreviousState);
                    LastStatus = "The final Human transition could not be queued.";
                    OperationCompleted?.Invoke(change.Actor, finalDesired.ModelCharaId, false, false);
                    return;
                }

                WriteMorphLog(
                    DiagnosticEventIds.MorphOperationStarted,
                    "Clean Human transition reset completed; final appearance queued.",
                    change.Actor,
                    finalDesired.ModelCharaId,
                    continuation.OperationId,
                    finalDesired,
                    change.Revision,
                    store.TryGet(change.Actor, out var state) ? state.BaseData.ModelCharaId : null);
                LastStatus = $"Applying Model ID {finalDesired.ModelCharaId}.";
                return;
            }

            if (change.IsRestore)
                store.CompleteRestore(change.Actor);
            else
                NormalizeSpecialBodyBacking(change.Actor, operation.Desired);
            LastStatus = change.IsRestore ? "Original appearance restored." : "Appearance applied.";
            WriteMorphLog(
                change.IsRestore ? DiagnosticEventIds.MorphRestored : DiagnosticEventIds.MorphApplied,
                LastStatus,
                change.Actor,
                operation.Desired.ModelCharaId,
                operation.OperationId);
            OperationCompleted?.Invoke(change.Actor, operation.Desired.ModelCharaId, change.IsRestore, true);
            return;
        }

        if (!change.IsRestore)
        {
            store.RestoreState(change.Actor, change.PreviousState);
            if (change.PreviousState is { } previous)
                NormalizeSpecialBodyBacking(change.Actor, previous.DesiredData);
        }
        LastStatus = operation.Error ?? "Appearance operation failed and was rolled back.";
        diagnostics.Write(new DiagnosticLogEntry
        {
            Level = DiagnosticLogLevel.Error,
            EventId = DiagnosticEventIds.MorphOperationFailed,
            Category = change.IsRestore ? DiagnosticCategory.Restore : DiagnosticCategory.Appearance,
            Message = LastStatus,
            ActorKey = DiagnosticActorKeys.Format(diagnostics, change.Actor),
            Outcome = operation.Stage.ToString(),
        });
        OperationCompleted?.Invoke(change.Actor, operation.Desired.ModelCharaId, change.IsRestore, false);
    }

    private void OnFrameworkUpdate(IFramework _)
        => ProcessContext();

    private void NormalizeSpecialBodyBacking(LogicalActorKey key, AppearanceData desired)
    {
        if (desired.Category != ModelCategory.Human
            || desired.Customize.Length <= 2
            || desired.Customize[2] == (byte)NpcAge.Normal
            || memory is not IAppearanceBackingStore backing
            || !store.TryGet(key, out var state)
            || !resolver.TryResolve(key, out var actor))
            return;

        if (backing.TryNormalizeBacking(actor, state.BaseData))
        {
            diagnostics.Write(new DiagnosticLogEntry
            {
                EventId = DiagnosticEventIds.MorphDesiredUpdated,
                Category = DiagnosticCategory.Safety,
                Message = "Special-body backing data normalized after draw object creation.",
                ActorKey = DiagnosticActorKeys.Format(diagnostics, key),
                Properties = new Dictionary<string, object?>
                {
                    ["desiredBodyType"] = desired.Customize[2],
                    ["backingBodyType"] = state.BaseData.Customize.Length > 2 ? state.BaseData.Customize[2] : null,
                    ["reason"] = "External redraw compatibility",
                },
            });
        }
    }

    public void ProcessContext()
    {
        var territory = context.TerritoryId;
        var loggedIn = context.IsLoggedIn;
        if (lastTerritory == 0)
        {
            lastTerritory = territory;
            wasLoggedIn = loggedIn;
            return;
        }
        if (territory != lastTerritory || wasLoggedIn && !loggedIn)
        {
            redraw.CancelAll(territory != lastTerritory ? "Territory changed." : "Logged out.");
            pending.Clear();
            store.Clear();
        }
        lastTerritory = territory;
        wasLoggedIn = loggedIn;
    }

    private sealed record PendingChange(
        LogicalActorKey Actor,
        AppearanceOverrideState? PreviousState,
        bool IsRestore,
        AppearanceData? FinalDesired,
        long Revision);

    private void WriteMorphLog(
        string eventId,
        string message,
        LogicalActorKey actor,
        uint modelId,
        Guid? operationId = null,
        AppearanceData? appearance = null,
        long? revision = null,
        uint? baseModelId = null)
    {
        var properties = new Dictionary<string, object?>
        {
            ["modelCharaId"] = modelId,
            ["category"] = appearance?.Category,
            ["sourceRowId"] = appearance?.SourceRowId,
            ["completeness"] = appearance?.Completeness,
            ["bodyType"] = appearance is { Customize.Length: > 2 } ? appearance.Customize[2] : null,
            ["customizeLength"] = appearance?.Customize.Length,
            ["equipmentLength"] = appearance?.Equipment.Length,
            ["equipmentSignature"] = appearance is null ? null : EquipmentSignature(appearance.Equipment),
            ["revision"] = revision,
            ["baseModelCharaId"] = baseModelId,
        };
        diagnostics.Write(new DiagnosticLogEntry
        {
            EventId = eventId,
            Category = DiagnosticCategory.Appearance,
            Message = message,
            ActorKey = DiagnosticActorKeys.Format(diagnostics, actor),
            OperationId = operationId is { } id ? $"redraw-{id:N}" : null,
            Properties = properties,
        });
    }

    private static string EquipmentSignature(IEnumerable<ulong> equipment)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        foreach (var value in equipment)
        {
            var remaining = value;
            for (var index = 0; index < sizeof(ulong); ++index)
            {
                hash ^= (byte)remaining;
                hash *= prime;
                remaining >>= 8;
            }
        }
        return hash.ToString("X16");
    }
}
