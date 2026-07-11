namespace ActorMorpher.Diagnostics;

public sealed record DiagnosticPaths(
    string RootDirectory,
    string LogDirectory,
    string SnapshotDirectory,
    string SessionFile,
    string LatestFile,
    string LatestSessionFile,
    string? MirrorDirectory,
    string? MirrorLatestFile,
    string? MirrorLatestSessionFile)
{
    public static DiagnosticPaths Create(string configDirectory, string? assemblyDirectory, string sessionId, bool mirror)
    {
        var root = Path.Combine(configDirectory, "diagnostics");
        var logs = Path.Combine(root, "logs");
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss");
        var sessionFile = Path.Combine(logs, $"actormorpher-{timestamp}-{sessionId}.jsonl");
        var mirrorDirectory = mirror && !string.IsNullOrWhiteSpace(assemblyDirectory)
            ? Path.Combine(assemblyDirectory, "ActorMorpherDiagnostics")
            : null;
        return new DiagnosticPaths(
            root,
            logs,
            Path.Combine(root, "snapshots"),
            sessionFile,
            Path.Combine(logs, "latest.jsonl"),
            Path.Combine(root, "latest-session.txt"),
            mirrorDirectory,
            mirrorDirectory is null ? null : Path.Combine(mirrorDirectory, "latest.jsonl"),
            mirrorDirectory is null ? null : Path.Combine(mirrorDirectory, "latest-session.txt"));
    }
}
