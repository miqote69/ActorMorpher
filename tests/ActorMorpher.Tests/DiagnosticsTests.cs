using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ActorMorpher;
using ActorMorpher.Actors;
using ActorMorpher.Diagnostics;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class DiagnosticsTests
{
    [Fact]
    public void ReleaseAndDevDefaultsDifferAndValidationClampsValues()
    {
        Assert.Equal(FileDiagnosticMode.Off, Configuration.Create(false).FileDiagnosticMode);
        Assert.Equal(FileDiagnosticMode.Full, Configuration.Create(true).FileDiagnosticMode);

        var configuration = new Configuration
        {
            Version = 2,
            DiagnosticRetentionDays = -10,
            DiagnosticMaximumSessions = 1000,
            DiagnosticMaximumFileSizeMb = 0,
            DiagnosticMaximumTotalSizeMb = 9999,
        };
        configuration.MigrateAndValidate(false);

        Assert.Equal(1, configuration.DiagnosticRetentionDays);
        Assert.Equal(100, configuration.DiagnosticMaximumSessions);
        Assert.Equal(1, configuration.DiagnosticMaximumFileSizeMb);
        Assert.Equal(1000, configuration.DiagnosticMaximumTotalSizeMb);
    }

    [Fact]
    public void OffModeCreatesNoDiagnosticDirectoriesOrFiles()
    {
        using var temp = new TemporaryDirectory();
        using var router = new DiagnosticLogRouter(temp.Path, null, null);

        Assert.True(router.Switch(Settings(FileDiagnosticMode.Off), "offtest1"));

        Assert.False(Directory.Exists(System.IO.Path.Combine(temp.Path, "diagnostics")));
        Assert.False(router.IsEnabled);
        using var operation = router.BeginOperation(DiagnosticCategory.Appearance, DiagnosticEventIds.MorphOperationStarted, "Morph");
        operation.SetPhase("Apply");
        operation.Complete();
    }

    [Fact]
    public async Task ErrorsOnlyFiltersInformationAndWritesErrors()
    {
        using var temp = new TemporaryDirectory();
        var paths = DiagnosticPaths.Create(temp.Path, null, "errors01", false);
        using var service = new DiagnosticLogService("errors01", Settings(FileDiagnosticMode.ErrorsOnly), paths, null);
        service.Write(Entry(DiagnosticLogLevel.Information, "Information line"));
        service.Write(Entry(DiagnosticLogLevel.Error, "Error line"));
        Assert.True(await service.FlushAsync(TimeSpan.FromSeconds(2)));

        var lines = ReadAllLinesShared(paths.LatestFile);
        Assert.Single(lines);
        Assert.Contains("Error line", lines[0]);
    }

    [Fact]
    public async Task FullModeWritesIndependentUtf8JsonLinesWithNullsOmitted()
    {
        using var temp = new TemporaryDirectory();
        var paths = DiagnosticPaths.Create(temp.Path, null, "full0001", false);
        using var service = new DiagnosticLogService("full0001", Settings(FileDiagnosticMode.Full), paths, null);
        service.Write(Entry(DiagnosticLogLevel.Information, "Line one\ncontinued"));
        service.Error(DiagnosticEventIds.HandledException, DiagnosticCategory.Exception, "Failure", new InvalidOperationException("broken"));
        Assert.True(await service.FlushAsync(TimeSpan.FromSeconds(2)));

        var lines = ReadAllLinesShared(paths.LatestFile);
        Assert.Equal(2, lines.Length);
        using var first = JsonDocument.Parse(lines[0]);
        using var second = JsonDocument.Parse(lines[1]);
        Assert.Equal(1, first.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.False(first.RootElement.TryGetProperty("operationId", out _));
        Assert.Equal("System.InvalidOperationException", second.RootElement.GetProperty("exception").GetProperty("type").GetString());
    }

    [Fact]
    public async Task OperationRecordsPhaseCompleteFailureAndAbandonedOutcomes()
    {
        using var temp = new TemporaryDirectory();
        var paths = DiagnosticPaths.Create(temp.Path, null, "ops00001", false);
        using var service = new DiagnosticLogService("ops00001", Settings(FileDiagnosticMode.Full), paths, null);
        using (var complete = service.BeginOperation(DiagnosticCategory.Appearance, DiagnosticEventIds.MorphOperationStarted, "Morph"))
        {
            complete.SetPhase("Validate");
            complete.Complete();
        }
        using (var failed = service.BeginOperation(DiagnosticCategory.Redraw, DiagnosticEventIds.RedrawOperationStarted, "Redraw"))
            failed.Fail(new InvalidOperationException("failed"));
        service.BeginOperation(DiagnosticCategory.Restore, DiagnosticEventIds.RestoreBatchStarted, "Restore").Dispose();
        Assert.True(await service.FlushAsync(TimeSpan.FromSeconds(2)));

        var text = string.Join(Environment.NewLine, ReadAllLinesShared(paths.LatestFile));
        Assert.Contains("\"outcome\":\"Success\"", text);
        Assert.Contains("\"outcome\":\"Failed\"", text);
        Assert.Contains("\"outcome\":\"Abandoned\"", text);
        Assert.Contains("morph-000001", text);
    }

    [Fact]
    public void ActorHashIsStableWithinSessionAndChangesAcrossSessions()
    {
        var key = new LogicalActorKey(1, 100, 10, 20, ObjectKind.Pc, 30);
        var first = new DiagnosticRedactor(false, false, "session-a");
        var second = new DiagnosticRedactor(false, false, "session-b");

        Assert.Equal(first.ActorKey(key, "Hidden Name"), first.ActorKey(key, "Other Name"));
        Assert.NotEqual(first.ActorKey(key), second.ActorKey(key));
        Assert.DoesNotContain("Hidden Name", first.ActorKey(key, "Hidden Name"));
        Assert.Contains("Shown Name", new DiagnosticRedactor(true, false, "session-a").ActorKey(key, "Shown Name"));
        Assert.Null(first.Address((nint)123));
    }

    [Fact]
    public void RingBufferDropsOldestAndPreservesOrder()
    {
        var buffer = new DiagnosticRingBuffer(2);
        buffer.Add(Entry(DiagnosticLogLevel.Information, "one"));
        buffer.Add(Entry(DiagnosticLogLevel.Information, "two"));
        buffer.Add(Entry(DiagnosticLogLevel.Information, "three"));

        Assert.Equal(new[] { "two", "three" }, buffer.Snapshot().Select(entry => entry.Message));
    }

    [Fact]
    public async Task RuntimeModeSwitchCreatesFilesOnlyWhenEnabled()
    {
        using var temp = new TemporaryDirectory();
        using var router = new DiagnosticLogRouter(temp.Path, null, null);
        Assert.True(router.Switch(Settings(FileDiagnosticMode.Off), "switch01"));
        Assert.False(Directory.Exists(System.IO.Path.Combine(temp.Path, "diagnostics")));

        Assert.True(router.Switch(Settings(FileDiagnosticMode.Full), "switch01"));
        router.Write(Entry(DiagnosticLogLevel.Information, "enabled"));
        Assert.True(await router.ActiveService!.FlushAsync(TimeSpan.FromSeconds(2)));
        Assert.True(File.Exists(router.ActiveService.Paths.LatestFile));

        Assert.True(router.Switch(Settings(FileDiagnosticMode.Off), "switch01"));
        Assert.False(router.IsEnabled);
    }

    [Fact]
    public async Task SnapshotContainsRequiredExpandedFiles()
    {
        using var temp = new TemporaryDirectory();
        var paths = DiagnosticPaths.Create(temp.Path, null, "snapshot", false);
        using var service = new DiagnosticLogService("snapshot", Settings(FileDiagnosticMode.Full), paths, null);
        service.Write(Entry(DiagnosticLogLevel.Error, "snapshot error"));
        Assert.True(await service.FlushAsync(TimeSpan.FromSeconds(2)));

        var directory = new DiagnosticSnapshotExporter(null).Create(
            service,
            Configuration.Create(false),
            new Dictionary<string, object?> { ["territoryId"] = 30, ["gpose"] = false });

        Assert.NotNull(directory);
        Assert.True(File.Exists(System.IO.Path.Combine(directory!, "latest.jsonl")));
        Assert.True(File.Exists(System.IO.Path.Combine(directory, "recent-context.jsonl")));
        Assert.True(File.Exists(System.IO.Path.Combine(directory, "environment.json")));
        Assert.True(File.Exists(System.IO.Path.Combine(directory, "configuration.redacted.json")));
        Assert.True(File.Exists(System.IO.Path.Combine(directory, "summary.txt")));
        foreach (var line in File.ReadAllLines(System.IO.Path.Combine(directory, "recent-context.jsonl")))
            using (JsonDocument.Parse(line)) { }
    }

    [Fact]
    public async Task RepeatedWarningsWithinWindowAreSuppressed()
    {
        using var temp = new TemporaryDirectory();
        var paths = DiagnosticPaths.Create(temp.Path, null, "suppress", false);
        using var service = new DiagnosticLogService("suppress", Settings(FileDiagnosticMode.Full), paths, null);
        var warning = Entry(DiagnosticLogLevel.Warning, "same warning") with
        {
            EventId = DiagnosticEventIds.ActorUnavailable,
            ActorKey = "Pc#ABC123",
            Phase = "Resolve",
        };
        service.Write(warning);
        service.Write(warning);
        service.Write(warning);
        Assert.True(await service.FlushAsync(TimeSpan.FromSeconds(2)));

        Assert.Single(ReadAllLinesShared(paths.LatestFile));
    }

    [Fact]
    public void LocalUserProfileIsMaskedFromPaths()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = System.IO.Path.Combine(profile, "ActorMorpher", "latest.jsonl");

        var redacted = new DiagnosticRedactor(false, false, "paths").RedactPath(path);

        Assert.DoesNotContain(profile, redacted, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("%USERPROFILE%", redacted);
    }

    private static DiagnosticLogSettings Settings(FileDiagnosticMode mode)
        => new(mode, false, false, false, 14, 10, 10, 100);

    private static DiagnosticLogEntry Entry(DiagnosticLogLevel level, string message)
        => new()
        {
            Level = level,
            EventId = DiagnosticEventIds.DiagnosticMarker,
            Category = DiagnosticCategory.UserAction,
            Message = message,
            Properties = new Dictionary<string, object?>(),
        };

    private static string[] ReadAllLinesShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line)
            lines.Add(line);
        return lines.ToArray();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ActorMorpherTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try { Directory.Delete(Path, true); }
            catch { }
        }
    }
}
