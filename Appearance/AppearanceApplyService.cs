using ActorMorpher.Interop;
using Dalamud.Plugin.Services;

namespace ActorMorpher.Appearance;

public sealed class AppearanceApplyService : IDisposable
{
    private readonly IFramework? framework;
    private readonly IActorResolver resolver;
    private readonly IClientContext context;
    private readonly RedrawCoordinator redraw;
    private readonly IDiagnosticLog diagnostics;
    private readonly Dictionary<Guid, PendingChange> pending = new();
    private long nextRevision;
    private uint lastTerritory;
    private bool wasLoggedIn;
    private bool disposed;

    public AppearanceApplyService(
        IFramework framework,
        IActorResolver resolver,
        IClientContext context,
        RedrawCoordinator redraw,
        IDiagnosticLog diagnostics)
        : this(resolver, context, redraw, diagnostics)
    {
        this.framework = framework;
        framework.Update += OnFrameworkUpdate;
    }

    public AppearanceApplyService(
        IActorResolver resolver,
        IClientContext context,
        RedrawCoordinator redraw,
        IDiagnosticLog diagnostics)
    {
        this.resolver = resolver;
        this.context = context;
        this.redraw = redraw;
        this.diagnostics = diagnostics;
        redraw.OperationFinished += OnOperationFinished;
    }

    public string LastStatus { get; private set; } = string.Empty;
    public bool? LastSucceeded { get; private set; }
    public Guid? LastOperationId { get; private set; }
    public event Action<Guid, LogicalActorKey, ActorRepresentationKey, AppearanceData, bool>? OperationCompleted;

    public bool IsPending(LogicalActorKey key)
        => pending.Values.Any(change => change.Actor == key);

    public bool TryApply(LogicalActorKey key, AppearanceData desired, out string message)
        => TryApply(key, desired, out _, out message);

    public bool TryApply(LogicalActorKey key, AppearanceData desired, out Guid operationId, out string message)
    {
        operationId = Guid.Empty;
        if (disposed)
        {
            message = "Appearance services are shutting down.";
            return false;
        }
        if (IsPending(key))
        {
            message = "An appearance operation is already pending for this actor.";
            return false;
        }
        if (!context.IsLoggedIn)
        {
            message = "The player is not logged in.";
            return false;
        }
        if (!resolver.TryResolve(key, out var actor))
        {
            message = "The actor is no longer available.";
            return false;
        }
        if (actor.RepresentationKey.TerritoryId != context.TerritoryId)
        {
            message = "The actor belongs to a previous territory.";
            return false;
        }

        var revision = checked(++nextRevision);
        var operation = RedrawOperation.Create(
            key,
            actor.RepresentationKey,
            desired,
            revision,
            context.TerritoryId);
        pending[operation.OperationId] = new PendingChange(key);
        if (!redraw.Enqueue(operation))
        {
            pending.Remove(operation.OperationId);
            message = "The redraw operation could not be queued.";
            return false;
        }

        WriteMorphLog(DiagnosticEventIds.MorphOperationStarted, "One-shot appearance operation queued.", key, desired.ModelCharaId, operation.OperationId, desired, revision);

        message = $"Applying Model ID {desired.ModelCharaId}.";
        LastStatus = message;
        LastSucceeded = null;
        LastOperationId = operation.OperationId;
        operationId = operation.OperationId;
        return true;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (framework is not null)
            framework.Update -= OnFrameworkUpdate;
        redraw.CancelAll("Appearance services disposed.");
        redraw.OperationFinished -= OnOperationFinished;
        pending.Clear();
    }

    private void OnOperationFinished(RedrawOperation operation)
    {
        if (!pending.Remove(operation.OperationId, out var change))
            return;
        if (operation.Stage == RedrawStage.Completed)
        {
            LastStatus = "Appearance applied.";
            LastSucceeded = true;
            LastOperationId = operation.OperationId;
            WriteMorphLog(
                DiagnosticEventIds.MorphApplied,
                LastStatus,
                change.Actor,
                operation.Desired.ModelCharaId,
                operation.OperationId);
            OperationCompleted?.Invoke(
                operation.OperationId,
                change.Actor,
                operation.TargetRepresentation,
                operation.Desired,
                true);
            return;
        }

        LastStatus = operation.Error ?? "Appearance operation failed.";
        LastSucceeded = false;
        LastOperationId = operation.OperationId;
        diagnostics.Write(new DiagnosticLogEntry
        {
            Level = DiagnosticLogLevel.Error,
            EventId = DiagnosticEventIds.MorphOperationFailed,
            Category = DiagnosticCategory.Appearance,
            Message = LastStatus,
            ActorKey = DiagnosticActorKeys.Format(diagnostics, change.Actor),
            Outcome = operation.Stage.ToString(),
        });
        OperationCompleted?.Invoke(
            operation.OperationId,
            change.Actor,
            operation.TargetRepresentation,
            operation.Desired,
            false);
    }

    private void OnFrameworkUpdate(IFramework _)
        => ProcessContext();

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
        }
        lastTerritory = territory;
        wasLoggedIn = loggedIn;
    }

    private sealed record PendingChange(LogicalActorKey Actor);

    private void WriteMorphLog(
        string eventId,
        string message,
        LogicalActorKey actor,
        uint modelId,
        Guid? operationId = null,
        AppearanceData? appearance = null,
        long? revision = null)
    {
        var properties = new Dictionary<string, object?>
        {
            ["modelCharaId"] = modelId,
            ["category"] = appearance?.Category,
            ["sourceRowId"] = appearance?.SourceRowId,
            ["completeness"] = appearance?.Completeness,
            ["modelScale"] = appearance?.ModelScale,
            ["bodyType"] = appearance is { Customize.Length: > 2 } ? appearance.Customize[2] : null,
            ["customizeLength"] = appearance?.Customize.Length,
            ["customizeSignature"] = appearance is null ? null : ByteSignature(appearance.Customize),
            ["equipmentLength"] = appearance?.Equipment.Length,
            ["equipmentSignature"] = appearance is null ? null : EquipmentSignature(appearance.Equipment),
            ["revision"] = revision,
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

    private static string ByteSignature(IEnumerable<byte> values)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        foreach (var value in values)
        {
            hash ^= value;
            hash *= prime;
        }
        return hash.ToString("X16");
    }
}
