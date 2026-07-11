using System.Collections;
using System.Diagnostics;
using Dalamud.Plugin.Services;

namespace ActorMorpher.Diagnostics;

public sealed class DiagnosticLogService : IDiagnosticLog, IDisposable
{
    private static readonly TimeSpan SuppressionWindow = TimeSpan.FromSeconds(5);

    private readonly DiagnosticFileWriter writer;
    private readonly IPluginLog? pluginLog;
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private readonly DiagnosticRingBuffer? ringBuffer;
    private readonly Dictionary<string, SuppressionState> suppression = new();
    private readonly object suppressionLock = new();
    private readonly DiagnosticRedactor redactor;
    private long operationSequence;
    private bool disposed;

    public DiagnosticLogService(
        string sessionId,
        DiagnosticLogSettings settings,
        DiagnosticPaths paths,
        IPluginLog? pluginLog)
    {
        SessionId = sessionId;
        Mode = settings.Mode;
        Paths = paths;
        this.pluginLog = pluginLog;
        ringBuffer = Mode == FileDiagnosticMode.Full ? new DiagnosticRingBuffer(500) : null;
        redactor = new DiagnosticRedactor(false, false, sessionId);
        writer = new DiagnosticFileWriter(paths, settings, pluginLog);
    }

    public bool IsEnabled => !disposed;
    public FileDiagnosticMode Mode { get; }
    public string SessionId { get; }
    public DiagnosticPaths Paths { get; }
    public DiagnosticStatus Status => new(
        writer.CurrentSessionFile,
        Paths.LogDirectory,
        Paths.MirrorDirectory,
        writer.Queued,
        writer.Dropped,
        writer.CurrentFileSize,
        writer.LastError);

    public DiagnosticOperation BeginOperation(
        DiagnosticCategory category,
        string eventId,
        string operationName,
        string? actorKey = null,
        IReadOnlyDictionary<string, object?>? properties = null,
        string? parentOperationId = null)
    {
        if (disposed)
            return DiagnosticOperation.NoOp;
        var prefix = category switch
        {
            DiagnosticCategory.Appearance => "morph",
            DiagnosticCategory.Redraw => "redraw",
            DiagnosticCategory.GPose => "gpose",
            DiagnosticCategory.BulkOutfit or DiagnosticCategory.Batch => "bulk",
            DiagnosticCategory.Restore => "restore",
            DiagnosticCategory.Snapshot => "snapshot",
            _ => "operation",
        };
        var id = $"{prefix}-{Interlocked.Increment(ref operationSequence):000000}";
        return new DiagnosticOperation(this, category, eventId, operationName, id, actorKey, parentOperationId, properties);
    }

    public void Write(DiagnosticLogEntry entry)
    {
        if (disposed || Mode == FileDiagnosticMode.ErrorsOnly
            && entry.Level < DiagnosticLogLevel.Error
            && entry.Outcome != "Failed"
            && entry.EventId != DiagnosticEventIds.PluginStarted)
            return;

        try
        {
            var normalized = entry with
            {
                TimestampUtc = entry.TimestampUtc == default ? DateTimeOffset.UtcNow : entry.TimestampUtc,
                ElapsedMilliseconds = entry.ElapsedMilliseconds == 0 ? elapsed.ElapsedMilliseconds : entry.ElapsedMilliseconds,
                EventName = string.IsNullOrEmpty(entry.EventName) ? DiagnosticEventIds.GetName(entry.EventId) : entry.EventName,
                SessionId = SessionId,
                Message = DiagnosticRedactor.NormalizeText(entry.Message, 2000),
                Properties = SanitizeProperties(entry.Properties),
                Exception = entry.Exception is null
                    ? null
                    : entry.Exception with
                    {
                        Message = redactor.RedactPath(entry.Exception.Message),
                        StackTrace = entry.Exception.StackTrace is null ? null : redactor.RedactPath(entry.Exception.StackTrace),
                    },
            };
            if (ShouldSuppress(normalized))
                return;
            ringBuffer?.Add(normalized);
            writer.Enqueue(normalized);
        }
        catch (Exception exception)
        {
            pluginLog?.Error(exception, "Actor Morpher failed to enqueue a diagnostic event.");
        }
    }

    public void Error(
        string eventId,
        DiagnosticCategory category,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        if (exception is null)
            pluginLog?.Error("Actor Morpher: {Message}", message);
        else
            pluginLog?.Error(exception, "Actor Morpher: {Message}", message);
        Write(new DiagnosticLogEntry
        {
            Level = DiagnosticLogLevel.Error,
            EventId = eventId,
            Category = category,
            Message = message,
            Properties = properties ?? new Dictionary<string, object?>(),
            Exception = exception is null ? null : DiagnosticExceptionInfo.FromException(exception),
        });
    }

    internal void WriteOperation(
        string eventId,
        DiagnosticCategory category,
        string message,
        string operationId,
        string? parentOperationId,
        string? actorKey,
        string? phase,
        string? outcome,
        IReadOnlyDictionary<string, object?>? properties,
        DiagnosticLogLevel level = DiagnosticLogLevel.Information,
        Exception? exception = null)
        => Write(new DiagnosticLogEntry
        {
            Level = level,
            EventId = eventId,
            Category = category,
            Message = message,
            OperationId = operationId,
            ParentOperationId = parentOperationId,
            ActorKey = actorKey,
            Phase = phase,
            Outcome = outcome,
            Properties = properties ?? new Dictionary<string, object?>(),
            Exception = exception is null ? null : DiagnosticExceptionInfo.FromException(exception),
        });

    public IReadOnlyList<DiagnosticLogEntry> RecentEntries()
        => ringBuffer?.Snapshot() ?? Array.Empty<DiagnosticLogEntry>();

    public Task<bool> FlushAsync(TimeSpan timeout)
        => writer.FlushAsync(timeout);

    public bool Flush(TimeSpan timeout)
        => writer.Flush(timeout);

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        writer.Dispose();
    }

    public static IReadOnlyDictionary<string, object?> Merge(
        IReadOnlyDictionary<string, object?>? source,
        params (string Key, object? Value)[] values)
    {
        var result = source is null
            ? new Dictionary<string, object?>()
            : new Dictionary<string, object?>(source);
        foreach (var (key, value) in values)
            if (value is not null)
                result[key] = value;
        return result;
    }

    private bool ShouldSuppress(DiagnosticLogEntry entry)
    {
        if (entry.Level < DiagnosticLogLevel.Warning)
            return false;
        var reason = entry.Properties.TryGetValue("reason", out var value) ? value?.ToString() : null;
        var key = $"{entry.EventId}|{entry.ActorKey}|{entry.OperationId}|{entry.Phase}|{reason}";
        lock (suppressionLock)
        {
            var now = DateTimeOffset.UtcNow;
            if (suppression.TryGetValue(key, out var state) && now - state.FirstSeen < SuppressionWindow)
            {
                suppression[key] = state with { Count = state.Count + 1 };
                return true;
            }
            if (Mode == FileDiagnosticMode.Full && state is { Count: > 0 })
            {
                var summary = new DiagnosticLogEntry
                {
                    ElapsedMilliseconds = elapsed.ElapsedMilliseconds,
                    EventId = DiagnosticEventIds.RepeatedEventsSuppressed,
                    EventName = DiagnosticEventIds.GetName(DiagnosticEventIds.RepeatedEventsSuppressed),
                    Category = DiagnosticCategory.Safety,
                    Message = "Repeated diagnostic events were suppressed.",
                    SessionId = SessionId,
                    Properties = new Dictionary<string, object?>
                    {
                        ["originalEventId"] = entry.EventId,
                        ["suppressedCount"] = state.Count,
                        ["windowMilliseconds"] = (long)SuppressionWindow.TotalMilliseconds,
                    },
                };
                ringBuffer?.Add(summary);
                writer.Enqueue(summary);
            }
            suppression[key] = new SuppressionState(now, 0);
            foreach (var stale in suppression.Where(pair => now - pair.Value.FirstSeen >= SuppressionWindow).Select(pair => pair.Key).ToArray())
                suppression.Remove(stale);
            return false;
        }
    }

    private static IReadOnlyDictionary<string, object?> SanitizeProperties(IReadOnlyDictionary<string, object?> properties)
    {
        var result = new Dictionary<string, object?>(properties.Count);
        foreach (var (key, value) in properties)
            result[key] = SanitizeValue(value);
        return result;
    }

    private static object? SanitizeValue(object? value)
        => value switch
        {
            null => null,
            string text => DiagnosticRedactor.NormalizeText(text, 2000),
            bool or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => value,
            Enum enumeration => enumeration.ToString(),
            DateTimeOffset timestamp => timestamp.ToString("O"),
            IEnumerable enumerable => enumerable.Cast<object?>().Take(100).Select(SanitizeValue).ToArray(),
            _ => DiagnosticRedactor.NormalizeText(value.ToString() ?? string.Empty, 500),
        };

    private sealed record SuppressionState(DateTimeOffset FirstSeen, int Count);
}
