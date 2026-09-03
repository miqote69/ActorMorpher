using System.Collections.Immutable;

namespace ActorMorpher.Appearance;

public sealed record AppearanceData(
    uint ModelCharaId,
    ModelCategory Category,
    uint SourceRowId,
    AppearanceCompleteness Completeness,
    ImmutableArray<byte> Customize,
    ImmutableArray<ulong> Equipment,
    float? ModelScale,
    ulong? Mainhand,
    ulong? Offhand,
    bool? VisorToggled,
    ushort? FacewearModelId = null,
    bool? HatVisible = null)
{
    public static AppearanceData Create(
        uint modelCharaId,
        ModelCategory category,
        uint sourceRowId,
        AppearanceCompleteness completeness,
        IEnumerable<byte> customize,
        IEnumerable<ulong> equipment,
        float? modelScale = null,
        ulong? mainhand = null,
        ulong? offhand = null,
        bool? visorToggled = null,
        ushort? facewearModelId = null,
        bool? hatVisible = null)
        => new(
            modelCharaId,
            category,
            sourceRowId,
            completeness,
            customize.ToImmutableArray(),
            equipment.ToImmutableArray(),
            modelScale,
            mainhand,
            offhand,
            visorToggled,
            facewearModelId,
            hatVisible);

    public AppearanceData WithOutfit(IEnumerable<ulong> equipment, bool visorToggled)
        => this with
        {
            Equipment = equipment.ToImmutableArray(),
            VisorToggled = visorToggled,
        };

    public AppearanceData WithOutfit(
        IEnumerable<ulong> equipment,
        bool visorToggled,
        ushort? facewearModelId,
        bool hatVisible)
        => this with
        {
            Equipment = equipment.ToImmutableArray(),
            VisorToggled = visorToggled,
            FacewearModelId = facewearModelId,
            HatVisible = hatVisible,
        };
}
