using System.Diagnostics;

namespace ActorMorpher.Diagnostics;

public sealed class DiagnosticOperation : IDisposable
{
    internal static DiagnosticOperation NoOp { get; } = new();

    private readonly DiagnosticLogService? log;
    private readonly Stopwatch? stopwatch;
    private readonly DiagnosticCategory category;
    private readonly string eventId = string.Empty;
    private readonly string operationName = string.Empty;
    private readonly string? actorKey;
    private readonly string? parentOperationId;
    private bool finished;
    private string? phase;

    private DiagnosticOperation()
    {
        OperationId = string.Empty;
    }

    internal DiagnosticOperation(
        DiagnosticLogService log,
        DiagnosticCategory category,
        string eventId,
        string operationName,
        string operationId,
        string? actorKey,
        string? parentOperationId,
        IReadOnlyDictionary<string, object?>? properties)
    {
        this.log = log;
        this.category = category;
        this.eventId = eventId;
        this.operationName = operationName;
        this.actorKey = actorKey;
        this.parentOperationId = parentOperationId;
        OperationId = operationId;
        stopwatch = Stopwatch.StartNew();
        if (log.Mode == FileDiagnosticMode.Full)
            log.WriteOperation(eventId, category, operationName + " started.", operationId, parentOperationId, actorKey, null, null, properties);
    }

    public string OperationId { get; }

    public void SetPhase(string nextPhase, IReadOnlyDictionary<string, object?>? properties = null)
    {
        if (log is null || finished || log.Mode != FileDiagnosticMode.Full || phase == nextPhase)
            return;

        var previous = phase;
        phase = nextPhase;
        log.WriteOperation(
            eventId,
            category,
            operationName + " phase changed.",
            OperationId,
            parentOperationId,
            actorKey,
            phase,
            null,
            DiagnosticLogService.Merge(properties, ("previousPhase", previous), ("nextPhase", nextPhase)));
    }

    public void Complete(string outcome = "Success", IReadOnlyDictionary<string, object?>? properties = null)
    {
        if (log is null || finished)
            return;
        finished = true;
        if (log.Mode == FileDiagnosticMode.Full)
            log.WriteOperation(eventId, category, operationName + " completed.", OperationId, parentOperationId, actorKey, phase, outcome,
                DiagnosticLogService.Merge(properties, ("durationMilliseconds", stopwatch?.ElapsedMilliseconds)));
    }

    public void Fail(Exception? exception = null, string message = "Operation failed.", IReadOnlyDictionary<string, object?>? properties = null)
    {
        if (log is null || finished)
            return;
        finished = true;
        log.WriteOperation(eventId, category, message, OperationId, parentOperationId, actorKey, phase, "Failed",
            DiagnosticLogService.Merge(properties, ("durationMilliseconds", stopwatch?.ElapsedMilliseconds)), DiagnosticLogLevel.Error, exception);
    }

    public void Dispose()
    {
        if (log is null || finished)
            return;
        finished = true;
        if (log.Mode == FileDiagnosticMode.Full)
            log.WriteOperation(eventId, category, operationName + " abandoned.", OperationId, parentOperationId, actorKey, phase, "Abandoned",
                DiagnosticLogService.Merge(null, ("durationMilliseconds", stopwatch?.ElapsedMilliseconds)), DiagnosticLogLevel.Warning);
    }
}
