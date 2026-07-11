namespace ActorMorpher.Diagnostics;

public sealed class NullDiagnosticLog : IDiagnosticLog
{
    public static NullDiagnosticLog Instance { get; } = new();
    private static readonly DiagnosticStatus EmptyStatus = new(null, null, null, 0, 0, 0, null);

    private NullDiagnosticLog()
    {
    }

    public bool IsEnabled => false;
    public FileDiagnosticMode Mode => FileDiagnosticMode.Off;
    public string SessionId => string.Empty;
    public DiagnosticStatus Status => EmptyStatus;

    public DiagnosticOperation BeginOperation(
        DiagnosticCategory category,
        string eventId,
        string operationName,
        string? actorKey = null,
        IReadOnlyDictionary<string, object?>? properties = null,
        string? parentOperationId = null)
        => DiagnosticOperation.NoOp;

    public void Write(DiagnosticLogEntry entry)
    {
    }

    public void Error(
        string eventId,
        DiagnosticCategory category,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
    }
}
