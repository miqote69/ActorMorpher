using System.Collections.Immutable;

namespace ActorMorpher.Appearance;

public sealed record AppearanceData(
    uint ModelCharaId,
    ModelCategory Category,
    uint SourceRowId,
    AppearanceCompleteness Completeness,
    ImmutableArray<byte> Customize,
    ImmutableArray<ulong> Equipment)
{
    public static AppearanceData Create(
        uint modelCharaId,
        ModelCategory category,
        uint sourceRowId,
        AppearanceCompleteness completeness,
        IEnumerable<byte> customize,
        IEnumerable<ulong> equipment)
        => new(
            modelCharaId,
            category,
            sourceRowId,
            completeness,
            customize.ToImmutableArray(),
            equipment.ToImmutableArray());
}
