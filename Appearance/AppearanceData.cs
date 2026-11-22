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
    public ImmutableArray<ArmorAppearance> ColoredEquipment { get; init; } = [];
    public WeaponDyes MainhandDyes { get; init; }
    public WeaponDyes OffhandDyes { get; init; }
    public bool? VieraEarsHidden { get; init; }
    public ImmutableArray<CustomizeColorOverride> CustomizeColors { get; init; } = [];
    // Request metadata only. The saved pin owns the job; this travels with a queued
    // maintenance operation so a later job change cannot reapply its old weapons.
    internal WeaponPinScope? PinnedWeaponScope { get; init; }

    internal AppearanceData ForWeaponJob(byte classJob, AppearanceData? current = null)
        => PinnedWeaponScope is not { } scope ? this
            : scope.AppliesTo(classJob)
            ? this with
            {
                Mainhand = scope.Weapons.Mainhand, Offhand = scope.Weapons.Offhand,
                MainhandDyes = scope.Weapons.MainhandDyes, OffhandDyes = scope.Weapons.OffhandDyes,
            }
            : this with
            {
                Mainhand = current?.Mainhand, Offhand = current?.Offhand,
                MainhandDyes = current?.MainhandDyes ?? default,
                OffhandDyes = current?.OffhandDyes ?? default,
            };

    internal AppearanceData WithCurrentEarVisibility(AppearanceData current)
        => VieraEarsHidden is null && current.VieraEarsHidden is { } hidden
            ? this with { VieraEarsHidden = hidden } : this;

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

internal sealed record WeaponPinScope(byte? ClassJob, PinnedSharedWeaponsConfiguration Weapons)
{
    internal bool AppliesTo(byte classJob) => ClassJob == classJob;
}
