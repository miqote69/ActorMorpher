using ActorMorpher.Interop;
using Dalamud.Plugin.Services;

namespace ActorMorpher.Redraw;

public sealed class RedrawCoordinator : IDisposable
{
    private const int TimeoutFrames = 180;

    private readonly IFramework? framework;
    private readonly IActorResolver resolver;
    private readonly IAppearanceMemory appearanceMemory;
    private readonly IRedrawBackend redrawBackend;
    private readonly IClientContext clientContext;
    private readonly IDiagnosticLog diagnostics;
    private readonly Queue<RedrawOperation> queue = new();
    private RedrawOperation? current;
    private bool disposed;

    public RedrawCoordinator(
        IFramework framework,
        IActorResolver resolver,
        IAppearanceMemory appearanceMemory,
        IRedrawBackend redrawBackend,
        IClientContext clientContext,
        IDiagnosticLog? diagnostics = null)
        : this(resolver, appearanceMemory, redrawBackend, clientContext, diagnostics)
    {
        this.framework = framework;
        framework.Update += OnFrameworkUpdate;
    }

    public RedrawCoordinator(
        IActorResolver resolver,
        IAppearanceMemory appearanceMemory,
        IRedrawBackend redrawBackend,
        IClientContext clientContext,
        IDiagnosticLog? diagnostics = null)
    {
        this.resolver = resolver;
        this.appearanceMemory = appearanceMemory;
        this.redrawBackend = redrawBackend;
        this.clientContext = clientContext;
        this.diagnostics = diagnostics ?? NullDiagnosticLog.Instance;
    }

    public RedrawOperation? Current => current;
    public RedrawOperation? LastResult { get; private set; }

    public bool Enqueue(RedrawOperation operation)
    {
        if (disposed || operation.TerritoryId != clientContext.TerritoryId)
            return false;

        queue.Enqueue(operation);
        diagnostics.Write(CreateEntry(operation, DiagnosticEventIds.RedrawOperationStarted, "Redraw operation queued."));
        return true;
    }

    public void CancelAll(string reason)
    {
        queue.Clear();
        if (current is not null)
        {
            diagnostics.Write(CreateEntry(current, DiagnosticEventIds.RedrawCancelled, "Redraw operation cancelled.", DiagnosticLogLevel.Warning, reason));
            Finish(current with { Stage = RedrawStage.Cancelled, Error = reason });
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        if (framework is not null)
            framework.Update -= OnFrameworkUpdate;
        CancelAll("Plugin disposed.");
    }

    public void ProcessNextFrame()
    {
        if (disposed)
            return;

        if (!clientContext.IsLoggedIn)
        {
            CancelAll("Logged out.");
            return;
        }

        current ??= queue.Count > 0 ? queue.Dequeue() : null;
        if (current is null)
            return;

        var operation = current with { FrameCount = current.FrameCount + 1 };
        if (operation.TerritoryId != clientContext.TerritoryId)
        {
            Finish(operation with { Stage = RedrawStage.Cancelled, Error = "Territory changed." });
            return;
        }

        if (operation.FrameCount > TimeoutFrames)
        {
            if (operation.Stage is RedrawStage.Rollback or RedrawStage.RollbackEnable)
                Finish(operation with { Stage = RedrawStage.Failed, Error = "Rollback timed out." });
            else
                current = operation with { Stage = RedrawStage.Rollback, FrameCount = 0, Error = "Redraw timed out." };
            return;
        }

        if (!resolver.TryResolve(operation.Actor, out var actor))
        {
            Finish(operation with { Stage = RedrawStage.Cancelled, Error = "Actor is no longer available." });
            return;
        }

        var previousStage = operation.Stage;
        current = operation.Stage switch
        {
            RedrawStage.Pending => operation with { Stage = RedrawStage.Disable },
            RedrawStage.Disable when redrawBackend.TryDisable(actor) => operation with { Stage = RedrawStage.Apply },
            RedrawStage.Apply when appearanceMemory.TryWrite(operation.Actor, operation.Desired) => operation with { Stage = RedrawStage.Enable },
            RedrawStage.Enable when redrawBackend.TryEnable(actor) => operation with { Stage = RedrawStage.Verify },
            RedrawStage.Verify when appearanceMemory.IsApplied(operation.Actor, operation.Desired) => Complete(operation),
            RedrawStage.Rollback when appearanceMemory.TryWrite(operation.Actor, operation.Rollback) => operation with { Stage = RedrawStage.RollbackEnable },
            RedrawStage.RollbackEnable when redrawBackend.TryEnable(actor) => Fail(operation),
            RedrawStage.Disable or RedrawStage.Apply or RedrawStage.Enable or RedrawStage.Verify
                => operation with { Stage = RedrawStage.Rollback, Error = operation.Error ?? "Redraw stage failed." },
            RedrawStage.Rollback or RedrawStage.RollbackEnable => Fail(operation),
            _ => operation,
        };
        if (current is { } changed && changed.Stage != previousStage)
            diagnostics.Write(CreateEntry(changed, DiagnosticEventIds.RedrawStateChanged, "Redraw state changed.",
                properties: new Dictionary<string, object?> { ["previousState"] = previousStage, ["nextState"] = changed.Stage }));
    }

    private void OnFrameworkUpdate(IFramework _)
        => ProcessNextFrame();

    private RedrawOperation Complete(RedrawOperation operation)
    {
        Finish(operation with { Stage = RedrawStage.Completed });
        return null!;
    }

    private RedrawOperation Fail(RedrawOperation operation)
    {
        Finish(operation with { Stage = RedrawStage.Failed, Error = operation.Error ?? "Rollback failed." });
        return null!;
    }

    private void Finish(RedrawOperation operation)
    {
        LastResult = operation;
        var eventId = operation.Stage switch
        {
            RedrawStage.Completed => DiagnosticEventIds.RedrawCompleted,
            RedrawStage.Cancelled => DiagnosticEventIds.RedrawCancelled,
            _ => DiagnosticEventIds.RedrawFailed,
        };
        diagnostics.Write(CreateEntry(
            operation,
            eventId,
            $"Redraw operation {operation.Stage}.",
            operation.Stage is RedrawStage.Failed ? DiagnosticLogLevel.Error : DiagnosticLogLevel.Information,
            operation.Error));
        current = null;
    }

    private DiagnosticLogEntry CreateEntry(
        RedrawOperation operation,
        string eventId,
        string message,
        DiagnosticLogLevel level = DiagnosticLogLevel.Information,
        string? reason = null,
        IReadOnlyDictionary<string, object?>? properties = null)
        => new()
        {
            Level = level,
            EventId = eventId,
            Category = DiagnosticCategory.Redraw,
            Message = message,
            OperationId = $"redraw-{operation.OperationId:N}",
            ActorKey = DiagnosticActorKeys.Format(diagnostics, operation.Actor),
            Phase = operation.Stage.ToString(),
            Outcome = operation.Stage.ToString(),
            Properties = DiagnosticLogService.Merge(properties, ("revision", operation.Revision), ("frameCount", operation.FrameCount), ("reason", reason)),
        };
}
