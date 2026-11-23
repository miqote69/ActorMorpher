using System.Collections.Immutable;
using System.Numerics;

namespace ActorMorpher.Appearance;

public sealed class CustomizeEditState
{
    private readonly byte[] values;
    private readonly byte[] fieldChangeMasks;
    private readonly ImmutableArray<CustomizeColorOverride> originalColors;
    private readonly Dictionary<int, CustomizeColorOverride> colors;
    private readonly Dictionary<int, Vector3> renderedColors = new();

    public CustomizeEditState(ActorSnapshot target, ImmutableArray<byte> customize,
        ImmutableArray<CustomizeColorOverride> colorOverrides = default)
        : this(target, customize, colorOverrides, null) { }

    internal CustomizeEditState(ActorSnapshot target, ImmutableArray<byte> customize,
        ImmutableArray<CustomizeColorOverride> colorOverrides, CustomizeCopyData? savedOriginal)
    {
        Actor = target.LogicalKey;
        Representation = target.RepresentationKey;
        Original = savedOriginal?.Customize ?? customize;
        values = customize.ToArray();
        fieldChangeMasks = new byte[values.Length];
        var original = savedOriginal?.CustomizeColors ?? colorOverrides;
        originalColors = original.IsDefault ? [] : original.OrderBy(color => color.Index).ToImmutableArray();
        colors = colorOverrides.IsDefault ? new() : colorOverrides.ToDictionary(color => color.Index);
    }

    public LogicalActorKey Actor { get; }
    public ActorRepresentationKey Representation { get; }
    public ImmutableArray<byte> Original { get; }
    internal CustomizeCopyData GetOriginal() => new(Original, originalColors);
    public long Revision { get; private set; }
    internal uint ColorChangeMask { get; private set; }
    public bool HasChanges => !values.AsSpan().SequenceEqual(Original.AsSpan())
        || !ToColorOverrides().AsSpan().SequenceEqual(originalColors.AsSpan());

    public bool Matches(ActorSnapshot target)
        => Actor == target.LogicalKey && Representation == target.RepresentationKey;

    public byte GetValue(int index, byte mask = byte.MaxValue)
        => (byte)(values[index] & mask);

    public void SetValue(int index, byte value, byte mask = byte.MaxValue)
    {
        var next = (byte)((values[index] & ~mask) | (value & mask));
        var removedColor = colors.Remove(index);
        if (next == values[index] && !removedColor) return;
        fieldChangeMasks[index] |= mask;
        if (removedColor) ColorChangeMask |= 1u << index;
        values[index] = next;
        Revision++;
    }

    public bool HasColorOverride(int index) => colors.ContainsKey(index);

    public Vector3? GetColor(int index)
        => colors.TryGetValue(index, out var color) ? new(color.R, color.G, color.B)
            : renderedColors.TryGetValue(index, out var rendered) ? rendered : null;

    public void UpdateRenderedColors(ImmutableArray<CustomizeColorOverride> captured)
    {
        foreach (var color in captured)
            renderedColors[color.Index] = new(color.R, color.G, color.B);
    }

    public void SetColor(int index, Vector3 value)
    {
        var next = new CustomizeColorOverride(index, value.X, value.Y, value.Z);
        if (colors.TryGetValue(index, out var previous) && previous == next) return;
        colors[index] = next;
        ColorChangeMask |= 1u << index;
        Revision++;
    }

    internal void BeginFieldChanges()
    {
        Array.Clear(fieldChangeMasks);
        ColorChangeMask = 0;
    }

    internal ImmutableArray<byte> GetFieldChangeMasks() => fieldChangeMasks.ToImmutableArray();

    public void Reset()
    {
        if (!HasChanges) return;
        Original.AsSpan().CopyTo(values);
        colors.Clear();
        foreach (var color in originalColors) colors.Add(color.Index, color);
        Revision++;
    }

    public void Paste(CustomizeCopyData data)
    {
        var copiedColors = data.CustomizeColors.OrderBy(color => color.Index).ToImmutableArray();
        if (values.AsSpan().SequenceEqual(data.Customize.AsSpan())
            && ToColorOverrides().AsSpan().SequenceEqual(copiedColors.AsSpan())) return;
        data.Customize.AsSpan().CopyTo(values);
        colors.Clear();
        foreach (var color in copiedColors) colors.Add(color.Index, color);
        Revision++;
    }

    public ImmutableArray<byte> ToCustomize() => values.ToImmutableArray();
    public ImmutableArray<CustomizeColorOverride> ToColorOverrides()
        => colors.Values.OrderBy(color => color.Index).ToImmutableArray();

    internal AppearanceData CreateAppearance(AppearanceData current)
        => current with { Customize = ToCustomize(), CustomizeColors = ToColorOverrides() };
}
