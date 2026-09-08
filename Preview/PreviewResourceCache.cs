namespace ActorMorpher.Preview;

// Managed preview payloads only. Eviction never disposes a resource in use by a frame.
internal sealed class PreviewResourceCache<TKey, TValue>(long maximumBytes = 64L * 1024 * 1024, int maximumEntries = 128)
    where TKey : notnull
    where TValue : class
{
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value, long Bytes)>> entries = new();
    private readonly LinkedList<(TKey Key, TValue Value, long Bytes)> recent = new();
    private long bytes;

    public TValue? GetOrCreate(TKey key, Func<TValue?> create, Func<TValue, long> size)
    {
        lock (entries)
        {
            if (entries.TryGetValue(key, out var hit))
            {
                recent.Remove(hit);
                recent.AddLast(hit);
                return hit.Value.Value;
            }
            var value = create();
            if (value is null)
                return null;
            var cost = Math.Max(1, size(value));
            if (cost > maximumBytes || maximumEntries <= 0)
                return value;
            while (entries.Count >= maximumEntries || bytes + cost > maximumBytes)
            {
                var oldest = recent.First!;
                entries.Remove(oldest.Value.Key);
                bytes -= oldest.Value.Bytes;
                recent.RemoveFirst();
            }
            var node = recent.AddLast((key, value, cost));
            entries.Add(key, node);
            bytes += cost;
            return value;
        }
    }
}
