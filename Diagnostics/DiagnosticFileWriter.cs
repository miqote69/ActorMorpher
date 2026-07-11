using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Channels;
using Dalamud.Plugin.Services;

namespace ActorMorpher.Diagnostics;

internal sealed class DiagnosticFileWriter : IDisposable
{
    private const int QueueCapacity = 4096;

    private readonly Channel<WriterCommand> channel;
    private readonly DiagnosticPaths paths;
    private readonly DiagnosticLogSettings settings;
    private readonly IPluginLog? pluginLog;
    private readonly JsonSerializerOptions jsonOptions;
    private readonly Task writerTask;
    private long dropped;
    private int queued;
    private int part = 1;
    private string currentSessionFile;
    private string? lastError;
    private bool errorReported;

    public DiagnosticFileWriter(DiagnosticPaths paths, DiagnosticLogSettings settings, IPluginLog? pluginLog)
    {
        this.paths = paths;
        this.settings = settings;
        this.pluginLog = pluginLog;
        currentSessionFile = paths.SessionFile;
        jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        jsonOptions.Converters.Add(new JsonStringEnumConverter());
        channel = Channel.CreateBounded<WriterCommand>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        InitializePaths();
        writerTask = Task.Run(WriterLoopAsync);
    }

    public int Queued => Volatile.Read(ref queued);
    public long Dropped => Interlocked.Read(ref dropped);
    public string? LastError => lastError;
    public string CurrentSessionFile => currentSessionFile;
    public long CurrentFileSize
    {
        get
        {
            try { return File.Exists(currentSessionFile) ? new FileInfo(currentSessionFile).Length : 0; }
            catch { return 0; }
        }
    }

    public bool Enqueue(DiagnosticLogEntry entry)
    {
        try
        {
            if (!channel.Writer.TryWrite(new WriterCommand(entry, null)))
            {
                Interlocked.Increment(ref dropped);
                return false;
            }
            Interlocked.Increment(ref queued);
            return true;
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
            return false;
        }
    }

    public async Task<bool> FlushAsync(TimeSpan timeout)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!channel.Writer.TryWrite(new WriterCommand(null, completion)))
            return false;
        try
        {
            return await completion.Task.WaitAsync(timeout).ConfigureAwait(false);
        }
        catch
        {
            return false;
        }
    }

    public bool Flush(TimeSpan timeout)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!channel.Writer.TryWrite(new WriterCommand(null, completion)))
            return false;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (!completion.Task.IsCompleted && stopwatch.Elapsed < timeout)
            Thread.Sleep(5);
        return completion.Task.IsCompletedSuccessfully;
    }

    public void Dispose()
    {
        channel.Writer.TryComplete();
        try
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (!writerTask.IsCompleted && stopwatch.Elapsed < TimeSpan.FromSeconds(2))
                Thread.Sleep(5);
            if (!writerTask.IsCompleted)
                pluginLog?.Warning("Actor Morpher diagnostic writer did not stop before timeout.");
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
    }

    private void InitializePaths()
    {
        Directory.CreateDirectory(paths.LogDirectory);
        Directory.CreateDirectory(paths.SnapshotDirectory);
        File.WriteAllText(paths.LatestFile, string.Empty, new UTF8Encoding(false));
        File.WriteAllText(paths.LatestSessionFile, Path.GetFileName(paths.SessionFile), new UTF8Encoding(false));
        if (paths.MirrorDirectory is not null)
        {
            try
            {
                Directory.CreateDirectory(paths.MirrorDirectory);
                File.WriteAllText(paths.MirrorLatestFile!, string.Empty, new UTF8Encoding(false));
                File.WriteAllText(paths.MirrorLatestSessionFile!, paths.SessionFile, new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                ReportFailure(exception);
            }
        }
        DiagnosticRetentionService.Apply(paths, settings, pluginLog);
    }

    private async Task WriterLoopAsync()
    {
        StreamWriter? session = null;
        StreamWriter? latest = null;
        StreamWriter? mirror = null;
        try
        {
            session = OpenAppend(currentSessionFile);
            latest = OpenAppend(paths.LatestFile);
            if (paths.MirrorLatestFile is not null)
            {
                try { mirror = OpenAppend(paths.MirrorLatestFile); }
                catch (Exception exception) { ReportFailure(exception); }
            }

            await foreach (var command in channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (command.Entry is not null)
                {
                    Interlocked.Decrement(ref queued);
                    var line = JsonSerializer.Serialize(command.Entry, jsonOptions);
                    if (session.BaseStream.Length + Encoding.UTF8.GetByteCount(line) + 1 > settings.MaximumFileSizeMb * 1024L * 1024L)
                    {
                        await session.DisposeAsync().ConfigureAwait(false);
                        await latest.DisposeAsync().ConfigureAwait(false);
                        if (mirror is not null)
                            await mirror.DisposeAsync().ConfigureAwait(false);
                        currentSessionFile = NextPartPath(++part);
                        session = OpenAppend(currentSessionFile);
                        File.WriteAllText(paths.LatestFile, string.Empty, new UTF8Encoding(false));
                        latest = OpenAppend(paths.LatestFile);
                        if (paths.MirrorLatestFile is not null)
                        {
                            File.WriteAllText(paths.MirrorLatestFile, string.Empty, new UTF8Encoding(false));
                            mirror = OpenAppend(paths.MirrorLatestFile);
                        }
                    }
                    await session.WriteLineAsync(line).ConfigureAwait(false);
                    await latest.WriteLineAsync(line).ConfigureAwait(false);
                    if (mirror is not null)
                        await mirror.WriteLineAsync(line).ConfigureAwait(false);
                }

                if (command.Flush is not null)
                {
                    await session.FlushAsync().ConfigureAwait(false);
                    await latest.FlushAsync().ConfigureAwait(false);
                    if (mirror is not null)
                        await mirror.FlushAsync().ConfigureAwait(false);
                    command.Flush.TrySetResult(true);
                }
            }
        }
        catch (Exception exception)
        {
            ReportFailure(exception);
        }
        finally
        {
            if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
            if (latest is not null) await latest.DisposeAsync().ConfigureAwait(false);
            if (mirror is not null) await mirror.DisposeAsync().ConfigureAwait(false);
        }
    }

    private string NextPartPath(int partNumber)
    {
        var directory = Path.GetDirectoryName(paths.SessionFile)!;
        var name = Path.GetFileNameWithoutExtension(paths.SessionFile);
        return Path.Combine(directory, $"{name}-part{partNumber:00}.jsonl");
    }

    private static StreamWriter OpenAppend(string path)
        => new(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite, 65536, FileOptions.Asynchronous), new UTF8Encoding(false));

    private void ReportFailure(Exception exception)
    {
        lastError = exception.Message;
        if (errorReported)
            return;
        errorReported = true;
        pluginLog?.Error(exception, "Actor Morpher file diagnostics are unavailable.");
    }

    private sealed record WriterCommand(DiagnosticLogEntry? Entry, TaskCompletionSource<bool>? Flush);
}
