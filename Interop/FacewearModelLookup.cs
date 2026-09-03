namespace ActorMorpher.Interop;

internal sealed class FacewearModelLookup
{
    private readonly Dictionary<(ushort Model, byte Variant), ushort> rows;

    internal FacewearModelLookup(IEnumerable<(ushort RowId, ushort Model, byte Variant)> entries)
        => rows = entries.Where(entry => entry.RowId != 0)
            .GroupBy(entry => (entry.Model, entry.Variant))
            .ToDictionary(group => group.Key, group => group.First().RowId);

    internal FacewearAppearance Resolve(ushort model, byte variant)
        => model == 0 ? new FacewearAppearance(true, 0)
            : rows.TryGetValue((model, variant), out var row)
                ? new FacewearAppearance(true, row)
                : FacewearAppearance.Unavailable;
}
