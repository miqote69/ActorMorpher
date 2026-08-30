using System.Collections.Immutable;

namespace ActorMorpher.Appearance;

public sealed record AppearanceData(
    uint ModelCharaId,
    ModelCategory Category,
    uint SourceRowId,
    AppearanceCompleteness Completeness,
    ImmutableArray<byte> Customize,
    ImmutableArray<ulong> Equipment,
    float? ModelScale)
{
    public static AppearanceData Create(
        uint modelCharaId,
        ModelCategory category,
        uint sourceRowId,
        AppearanceCompleteness completeness,
        IEnumerable<byte> customize,
        IEnumerable<ulong> equipment,
        float? modelScale = null)
        => new(
            modelCharaId,
            category,
            sourceRowId,
            completeness,
            customize.ToImmutableArray(),
            equipment.ToImmutableArray(),
            NormalizeModelScale(modelScale));

    public static float? NormalizeModelScale(float? modelScale)
        => modelScale is { } value && float.IsFinite(value) && value > 0
            ? value
            : null;
}
