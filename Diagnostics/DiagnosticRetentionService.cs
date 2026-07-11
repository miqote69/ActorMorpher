using Dalamud.Plugin.Services;

namespace ActorMorpher.Diagnostics;

internal static class DiagnosticRetentionService
{
    public static void Apply(DiagnosticPaths paths, DiagnosticLogSettings settings, IPluginLog? pluginLog)
    {
        try
        {
            var current = Path.GetFullPath(paths.SessionFile);
            var currentSessionPrefix = Path.GetFileNameWithoutExtension(current);
            var files = Directory.EnumerateFiles(paths.LogDirectory, "actormorpher-*.jsonl")
                .Select(path => new FileInfo(path))
                .Where(file => !Path.GetFileNameWithoutExtension(file.Name).StartsWith(currentSessionPrefix, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToList();
            var cutoff = DateTime.UtcNow.AddDays(-settings.RetentionDays);
            foreach (var file in files.Where(file => file.LastWriteTimeUtc < cutoff))
                TryDelete(file, pluginLog);

            files = files.Where(file => file.Exists).OrderByDescending(file => file.LastWriteTimeUtc).ToList();
            foreach (var file in files.Skip(settings.MaximumSessions))
                TryDelete(file, pluginLog);

            var maxBytes = settings.MaximumTotalSizeMb * 1024L * 1024L;
            var retained = files.Where(file => file.Exists).OrderByDescending(file => file.LastWriteTimeUtc).ToList();
            var total = retained.Sum(file => file.Length);
            foreach (var file in retained.AsEnumerable().Reverse())
            {
                if (total <= maxBytes)
                    break;
                var length = file.Length;
                if (TryDelete(file, pluginLog))
                    total -= length;
            }
        }
        catch (Exception exception)
        {
            pluginLog?.Warning(exception, "Actor Morpher diagnostic retention failed.");
        }
    }

    private static bool TryDelete(FileInfo file, IPluginLog? pluginLog)
    {
        try { file.Delete(); return true; }
        catch (Exception exception) { pluginLog?.Warning(exception, "Failed to delete old Actor Morpher diagnostic file {Path}.", file.FullName); return false; }
    }
}
