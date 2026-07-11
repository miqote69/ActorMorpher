namespace ActorMorpher.Diagnostics;

public interface IDiagnosticLog
{
    bool IsEnabled { get; }
    FileDiagnosticMode Mode { get; }
    string SessionId { get; }
    DiagnosticStatus Status { get; }

    DiagnosticOperation BeginOperation(
        DiagnosticCategory category,
        string eventId,
        string operationName,
        string? actorKey = null,
        IReadOnlyDictionary<string, object?>? properties = null,
        string? parentOperationId = null);

    void Write(DiagnosticLogEntry entry);

    void Error(
        string eventId,
        DiagnosticCategory category,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null);
}

public sealed record DiagnosticStatus(
    string? CurrentLogFile,
    string? StandardLogDirectory,
    string? MirrorLogDirectory,
    int QueuedEvents,
    long DroppedEvents,
    long CurrentFileSize,
    string? LastFileLoggingError);
