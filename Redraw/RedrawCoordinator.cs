using ActorMorpher.Interop;
using Dalamud.Plugin.Services;

namespace ActorMorpher.Redraw;

public sealed class RedrawCoordinator : IDisposable
{
    private readonly IFramework? framework;
    private readonly IActorResolver resolver;
    private readonly IRedrawBackend redrawBackend;
    private readonly IClientContext clientContext;
    private readonly IDiagnosticLog diagnostics;
    private readonly Queue<RedrawOperation> queue = new();
    private RedrawOperation? current;
    private bool disposed;

    public RedrawCoordinator(
        IFramework framework,
        IActorResolver resolver,
        IRedrawBackend redrawBackend,
        IClientContext clientContext,
        IDiagnosticLog? diagnostics = null)
        : this(resolver, redrawBackend, clientContext, diagnostics)
    {
        this.framework = framework;
        framework.Update += OnFrameworkUpdate;
    }

    public RedrawCoordinator(
        IActorResolver resolver,
        IRedrawBackend redrawBackend,
        IClientContext clientContext,
        IDiagnosticLog? diagnostics = null)
    {
        this.resolver = resolver;
        this.redrawBackend = redrawBackend;
        this.clientContext = clientContext;
        this.diagnostics = diagnostics ?? NullDiagnosticLog.Instance;
    }

    public RedrawOperation? Current => current;
    public RedrawOperation? LastResult { get; private set; }
    public event Action<RedrawOperation>? OperationFinished;

    public bool Enqueue(RedrawOperation operation)
    {
        if (disposed
            || operation.TerritoryId != clientContext.TerritoryId
            || current?.Actor == operation.Actor
            || queue.Any(queued => queued.Actor == operation.Actor))
            return false;

        queue.Enqueue(operation);
        diagnostics.Write(CreateEntry(operation, DiagnosticEventIds.RedrawOperationStarted, "Redraw operation queued."));
        return true;
    }

    public void CancelAll(string reason)
    {
        while (queue.TryDequeue(out var queued))
            ReportFinished(queued with { Stage = RedrawStage.Cancelled, Error = reason });
        if (current is not null)
        {
            diagnostics.Write(CreateEntry(current, DiagnosticEventIds.RedrawCancelled, "Redraw operation cancelled.", DiagnosticLogLevel.Warning, reason));
            Finish(current with { Stage = RedrawStage.Cancelled, Error = reason });
        }
    }

    public void Cancel(LogicalActorKey actor, string reason)
    {
        if (queue.Count > 0)
        {
            var retained = new Queue<RedrawOperation>();
            while (queue.TryDequeue(out var queued))
            {
                if (queued.Actor == actor)
                    ReportFinished(queued with { Stage = RedrawStage.Cancelled, Error = reason });
                else
                    retained.Enqueue(queued);
            }
            while (retained.TryDequeue(out var queued))
                queue.Enqueue(queued);
        }

        if (current?.Actor != actor)
            return;
        Finish(current with { Stage = RedrawStage.Cancelled, Error = reason });
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

        if (!resolver.TryResolve(operation.Actor, operation.TargetRepresentation, out var actor))
        {
            var error = resolver.TryResolve(operation.Actor, out _)
                ? "Actor representation changed."
                : "Actor is no longer available.";
            Finish(operation with { Stage = RedrawStage.Cancelled, Error = error });
            return;
        }
        if (actor.RepresentationKey != operation.TargetRepresentation)
        {
            Finish(operation with { Stage = RedrawStage.Cancelled, Error = "Actor representation changed." });
            return;
        }

        var previousStage = operation.Stage;
        bool? backendSucceeded = null;
        try
        {
            if (operation.Stage == RedrawStage.Disable)
                backendSucceeded = redrawBackend.TryDisable(actor);
            else if (operation.Stage == RedrawStage.Enable)
                backendSucceeded = redrawBackend.TryEnable(actor, operation.Desired, operation.OperationId);
        }
        catch (Exception exception)
        {
            Finish(operation with
            {
                Stage = RedrawStage.Failed,
                Error = $"Redraw stage {operation.Stage} threw: {exception.Message}",
            });
            return;
        }
        current = operation.Stage switch
        {
            RedrawStage.Pending => operation with { Stage = RedrawStage.Disable },
            RedrawStage.Disable when backendSucceeded is true
                => operation with { Stage = RedrawStage.Enable },
            RedrawStage.Enable when backendSucceeded is true
                => Complete(operation),
            RedrawStage.Disable or RedrawStage.Enable
                => FailWithoutRollback(operation, $"Redraw stage {operation.Stage} failed."),
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
        Finish(operation with { Stage = RedrawStage.Failed, Error = operation.Error ?? "Redraw failed." });
        return null!;
    }

    private RedrawOperation FailWithoutRollback(RedrawOperation operation, string error)
        => Fail(operation with { Error = error });

    private void Finish(RedrawOperation operation)
    {
        current = null;
        ReportFinished(operation);
    }

    private void ReportFinished(RedrawOperation operation)
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
        OperationFinished?.Invoke(operation);
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
