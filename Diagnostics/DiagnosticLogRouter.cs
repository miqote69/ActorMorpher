using Dalamud.Plugin.Services;

namespace ActorMorpher.Diagnostics;

public sealed class DiagnosticLogRouter : IDiagnosticLog, IDisposable
{
    private readonly string configDirectory;
    private readonly string? assemblyDirectory;
    private readonly IPluginLog? pluginLog;
    private IDiagnosticLog current = NullDiagnosticLog.Instance;
    private DiagnosticLogService? service;

    public DiagnosticLogRouter(string configDirectory, string? assemblyDirectory, IPluginLog? pluginLog)
    {
        this.configDirectory = configDirectory;
        this.assemblyDirectory = assemblyDirectory;
        this.pluginLog = pluginLog;
    }

    public bool IsEnabled => current.IsEnabled;
    public FileDiagnosticMode Mode => current.Mode;
    public string SessionId => current.SessionId;
    public DiagnosticStatus Status => current.Status;
    public DiagnosticLogService? ActiveService => service;

    public bool Switch(DiagnosticLogSettings settings, string sessionId)
    {
        DiagnosticLogService? next = null;
        try
        {
            if (settings.Mode != FileDiagnosticMode.Off)
            {
                var paths = DiagnosticPaths.Create(configDirectory, assemblyDirectory, sessionId, settings.MirrorBesideAssembly);
                next = new DiagnosticLogService(sessionId, settings, paths, pluginLog);
            }
        }
        catch (Exception exception)
        {
            pluginLog?.Error(exception, "Actor Morpher failed to switch diagnostic mode.");
            next?.Dispose();
            return false;
        }

        var previous = service;
        service = next;
        current = next is null ? NullDiagnosticLog.Instance : next;
        previous?.Dispose();
        return true;
    }

    public DiagnosticOperation BeginOperation(DiagnosticCategory category, string eventId, string operationName, string? actorKey = null,
        IReadOnlyDictionary<string, object?>? properties = null, string? parentOperationId = null)
        => current.BeginOperation(category, eventId, operationName, actorKey, properties, parentOperationId);

    public void Write(DiagnosticLogEntry entry)
        => current.Write(entry);

    public void Error(string eventId, DiagnosticCategory category, string message, Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null)
        => current.Error(eventId, category, message, exception, properties);

    public void Dispose()
    {
        current = NullDiagnosticLog.Instance;
        service?.Dispose();
        service = null;
    }
}
