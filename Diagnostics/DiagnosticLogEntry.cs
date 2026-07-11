using System.Text.Json.Serialization;

namespace ActorMorpher.Diagnostics;

public sealed record DiagnosticLogEntry
{
    public int SchemaVersion { get; init; } = 1;
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public long ElapsedMilliseconds { get; init; }
    public DiagnosticLogLevel Level { get; init; } = DiagnosticLogLevel.Information;
    public string EventId { get; init; } = string.Empty;
    public string EventName { get; init; } = string.Empty;
    public DiagnosticCategory Category { get; init; }
    public string Message { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? OperationId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ParentOperationId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? ActorKey { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? RepresentationKey { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Phase { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Outcome { get; init; }
    public IReadOnlyDictionary<string, object?> Properties { get; init; } = new Dictionary<string, object?>();
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DiagnosticExceptionInfo? Exception { get; init; }
}
