using System.Diagnostics;
using Dalamud.Plugin.Services;

namespace ActorMorpher.Diagnostics;

public sealed class DiagnosticController : IDisposable
{
    private readonly DiagnosticLogRouter router;
    private readonly Configuration configuration;
    private readonly Action saveConfiguration;
    private readonly IPluginLog pluginLog;
    private readonly IClientState clientState;
    private readonly bool isDev;
    private readonly DiagnosticSnapshotExporter snapshotExporter;
    private FileDiagnosticMode? modeBeforeCapture;
    private int markerNumber;

    public DiagnosticController(
        DiagnosticLogRouter router,
        Configuration configuration,
        Action saveConfiguration,
        IPluginLog pluginLog,
        IClientState clientState,
        bool isDev)
    {
        this.router = router;
        this.configuration = configuration;
        this.saveConfiguration = saveConfiguration;
        this.pluginLog = pluginLog;
        this.clientState = clientState;
        this.isDev = isDev;
        snapshotExporter = new DiagnosticSnapshotExporter(pluginLog);
        SessionId = Guid.NewGuid().ToString("N")[..8];
    }

    public IDiagnosticLog Log => router;
    public string SessionId { get; }
    public bool CaptureActive => modeBeforeCapture is not null;
    public string? LastSnapshotDirectory { get; private set; }

    public string FormatActorKey(LogicalActorKey key, string? actorName = null)
        => new DiagnosticRedactor(
            configuration.IncludeActorNamesInDiagnostics,
            configuration.IncludeRawAddressesInDiagnostics,
            SessionId).ActorKey(key, actorName);

    public void Start()
    {
        if (!router.Switch(CreateSettings(configuration.FileDiagnosticMode), SessionId))
            return;
        Write(DiagnosticEventIds.PluginStarted, DiagnosticCategory.Lifecycle, "Actor Morpher diagnostic session started.",
            new Dictionary<string, object?>
            {
                ["pluginVersion"] = Plugin.DisplayVersion,
                ["diagnosticMode"] = router.Mode,
                ["isDevPlugin"] = isDev,
                ["operatingSystem"] = Environment.OSVersion.VersionString,
                ["processArchitecture"] = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            });
    }

    public bool SetPersistentMode(FileDiagnosticMode mode)
    {
        if (CaptureActive)
            return false;
        var previous = configuration.FileDiagnosticMode;
        if (!router.Switch(CreateSettings(mode), SessionId))
            return false;
        configuration.FileDiagnosticMode = mode;
        saveConfiguration();
        Write(DiagnosticEventIds.DiagnosticModeChanged, DiagnosticCategory.Configuration, "Diagnostic mode changed.",
            new Dictionary<string, object?> { ["previousMode"] = previous, ["currentMode"] = mode });
        return true;
    }

    public bool ApplySettings()
    {
        if (CaptureActive)
            return false;
        configuration.MigrateAndValidate(isDev);
        if (!router.Switch(CreateSettings(configuration.FileDiagnosticMode), SessionId))
            return false;
        saveConfiguration();
        return true;
    }

    public void AddMarker(string? note)
    {
        if (!router.IsEnabled)
            return;
        var properties = new Dictionary<string, object?> { ["markerNumber"] = ++markerNumber };
        if (!string.IsNullOrWhiteSpace(note))
            properties["note"] = DiagnosticRedactor.NormalizeText(note, 200);
        Write(DiagnosticEventIds.DiagnosticMarker, DiagnosticCategory.UserAction, "User inserted a diagnostic marker.", properties);
    }

    public bool BeginCapture()
    {
        if (CaptureActive)
            return false;
        modeBeforeCapture = configuration.FileDiagnosticMode;
        if (!router.Switch(CreateSettings(FileDiagnosticMode.Full), SessionId))
        {
            modeBeforeCapture = null;
            return false;
        }
        Write(DiagnosticEventIds.TroubleshootingCaptureStarted, DiagnosticCategory.Lifecycle, "Troubleshooting capture started.",
            new Dictionary<string, object?> { ["previousMode"] = modeBeforeCapture });
        AddMarker("Troubleshooting capture started");
        return true;
    }

    public bool EndCapture()
    {
        if (modeBeforeCapture is not { } previous)
            return false;
        Write(DiagnosticEventIds.TroubleshootingCaptureEnded, DiagnosticCategory.Lifecycle, "Troubleshooting capture ended.");
        LastSnapshotDirectory = CreateSnapshot();
        modeBeforeCapture = null;
        return router.Switch(CreateSettings(previous), SessionId);
    }

    public string? CreateSnapshot()
    {
        if (router.ActiveService is not { } service)
            return null;
        var path = snapshotExporter.Create(service, configuration, EnvironmentInfo());
        if (path is not null)
        {
            LastSnapshotDirectory = path;
            Write(DiagnosticEventIds.DiagnosticSnapshotCreated, DiagnosticCategory.Snapshot, "Diagnostic snapshot created.",
                new Dictionary<string, object?> { ["snapshotDirectory"] = new DiagnosticRedactor(false, false, SessionId).RedactPath(path) });
        }
        return path;
    }

    public void OpenLogFolder()
    {
        var directory = router.Status.StandardLogDirectory;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return;
        try { Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true }); }
        catch (Exception exception) { pluginLog.Warning(exception, "Failed to open Actor Morpher diagnostic folder."); }
    }

    public void ClearOldLogs()
    {
        if (router.ActiveService is not { } service)
            return;
        DiagnosticRetentionService.Apply(service.Paths, CreateSettings(router.Mode), pluginLog);
    }

    public void Dispose()
    {
        Write(DiagnosticEventIds.PluginStopping, DiagnosticCategory.Lifecycle, "Actor Morpher is stopping.");
        Write(DiagnosticEventIds.PluginStopped, DiagnosticCategory.Lifecycle, "Actor Morpher stopped.");
        router.Dispose();
    }

    private DiagnosticLogSettings CreateSettings(FileDiagnosticMode mode)
        => new(
            mode,
            configuration.IncludeActorNamesInDiagnostics,
            configuration.IncludeRawAddressesInDiagnostics,
            configuration.MirrorDiagnosticsBesidePluginAssembly,
            configuration.DiagnosticRetentionDays,
            configuration.DiagnosticMaximumSessions,
            configuration.DiagnosticMaximumFileSizeMb,
            configuration.DiagnosticMaximumTotalSizeMb);

    private void Write(string eventId, DiagnosticCategory category, string message,
        IReadOnlyDictionary<string, object?>? properties = null)
        => router.Write(new DiagnosticLogEntry
        {
            EventId = eventId,
            Category = category,
            Message = message,
            Properties = properties ?? new Dictionary<string, object?>(),
        });

    private IReadOnlyDictionary<string, object?> EnvironmentInfo()
        => new Dictionary<string, object?>
        {
            ["pluginVersion"] = Plugin.DisplayVersion,
            ["dalamudApiLevel"] = 15,
            ["operatingSystem"] = Environment.OSVersion.VersionString,
            ["architecture"] = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            ["isDevPlugin"] = isDev,
            ["territoryId"] = clientState.TerritoryType,
            ["gpose"] = clientState.IsGPosing,
            ["sessionId"] = SessionId,
            ["diagnosticMode"] = router.Mode,
            ["logPath"] = router.Status.CurrentLogFile,
        };
}
