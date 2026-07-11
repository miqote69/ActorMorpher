namespace ActorMorpher.Diagnostics;

public sealed class DiagnosticRingBuffer(int capacity)
{
    private readonly Queue<DiagnosticLogEntry> entries = new(capacity);
    private readonly object syncRoot = new();

    public void Add(DiagnosticLogEntry entry)
    {
        lock (syncRoot)
        {
            if (entries.Count == capacity)
                entries.Dequeue();
            entries.Enqueue(entry);
        }
    }

    public IReadOnlyList<DiagnosticLogEntry> Snapshot()
    {
        lock (syncRoot)
            return entries.ToArray();
    }
}
