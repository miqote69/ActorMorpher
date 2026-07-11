using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dalamud.Plugin.Services;

namespace ActorMorpher.Diagnostics;

public sealed class DiagnosticSnapshotExporter
{
    private readonly IPluginLog? pluginLog;
    private readonly JsonSerializerOptions options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };
    private readonly JsonSerializerOptions jsonLineOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public DiagnosticSnapshotExporter(IPluginLog? pluginLog)
        => this.pluginLog = pluginLog;

    public string? Create(
        DiagnosticLogService service,
        Configuration configuration,
        IReadOnlyDictionary<string, object?> environment)
    {
        try
        {
            service.Flush(TimeSpan.FromSeconds(1));
            var directory = Path.Combine(service.Paths.SnapshotDirectory, $"diagnostics-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}");
            Directory.CreateDirectory(directory);
            if (File.Exists(service.Paths.LatestFile))
            {
                using var source = new FileStream(service.Paths.LatestFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var target = new FileStream(Path.Combine(directory, "latest.jsonl"), FileMode.Create, FileAccess.Write, FileShare.Read);
                source.CopyTo(target);
            }

            var recentPath = Path.Combine(directory, "recent-context.jsonl");
            using (var writer = new StreamWriter(recentPath, false, new UTF8Encoding(false)))
                foreach (var entry in service.RecentEntries())
                    writer.WriteLine(JsonSerializer.Serialize(entry, jsonLineOptions));

            File.WriteAllText(Path.Combine(directory, "environment.json"), JsonSerializer.Serialize(environment, options), new UTF8Encoding(false));
            var redactedConfiguration = new
            {
                configuration.Version,
                configuration.FileDiagnosticMode,
                configuration.IncludeActorNamesInDiagnostics,
                configuration.IncludeRawAddressesInDiagnostics,
                configuration.MirrorDiagnosticsBesidePluginAssembly,
                configuration.DiagnosticRetentionDays,
                configuration.DiagnosticMaximumSessions,
                configuration.DiagnosticMaximumFileSizeMb,
                configuration.DiagnosticMaximumTotalSizeMb,
            };
            File.WriteAllText(Path.Combine(directory, "configuration.redacted.json"), JsonSerializer.Serialize(redactedConfiguration, options), new UTF8Encoding(false));
            var summary = $"""
                Actor Morpher Diagnostic Snapshot

                Plugin Version: {Plugin.DisplayVersion}
                Session: {service.SessionId}
                Created UTC: {DateTimeOffset.UtcNow:O}
                Diagnostic Mode: {service.Mode}
                Current Territory: {environment.GetValueOrDefault("territoryId")}
                GPose: {environment.GetValueOrDefault("gpose")}
                Current Log: latest.jsonl
                """;
            File.WriteAllText(Path.Combine(directory, "summary.txt"), summary, new UTF8Encoding(false));
            return directory;
        }
        catch (Exception exception)
        {
            pluginLog?.Error(exception, "Actor Morpher diagnostic snapshot export failed.");
            return null;
        }
    }
}
