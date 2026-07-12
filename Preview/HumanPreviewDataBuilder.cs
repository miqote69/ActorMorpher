using System.Collections.Immutable;

namespace ActorMorpher.Preview;

public sealed class HumanPreviewDataBuilder
{
    public const int CustomizeLength = 26;
    public const int EquipmentSlotCount = 10;
    public const int WeaponSlotCount = 3;
    public const int GlassesSlotCount = 2;

    public bool TryBuild(ModelSearchEntry model, out HumanPreviewData data, out HumanPreviewDataFailure failure)
    {
        data = null!;
        if (model.Category != ModelCategory.Human)
        {
            failure = HumanPreviewDataFailure.NotHuman;
            return false;
        }

        if (model.HumanAppearance is not { } appearance
            || model.ModelAppearance is not { Completeness: AppearanceCompleteness.Complete } modelAppearance)
        {
            failure = HumanPreviewDataFailure.AppearanceUnavailable;
            return false;
        }

        if (appearance.Customize.Length != CustomizeLength
            || modelAppearance.Customize.Length != CustomizeLength)
        {
            failure = HumanPreviewDataFailure.InvalidCustomizeLength;
            return false;
        }

        if (appearance.Equipment.Length != EquipmentSlotCount
            || modelAppearance.Equipment.Length != EquipmentSlotCount)
        {
            failure = HumanPreviewDataFailure.InvalidEquipmentLength;
            return false;
        }

        if (modelAppearance.ModelCharaId != model.ModelId
            || modelAppearance.SourceRowId != model.SourceId
            || !appearance.Customize.AsSpan().SequenceEqual(modelAppearance.Customize.AsSpan())
            || !appearance.Equipment.AsSpan().SequenceEqual(modelAppearance.Equipment.AsSpan()))
        {
            failure = HumanPreviewDataFailure.InconsistentAppearance;
            return false;
        }

        var race = appearance.Customize[0];
        var sex = appearance.Customize[1];
        var tribe = appearance.Customize[4];
        if (race is < 1 or > 8
            || sex > 1
            || tribe == 0
            || !HumanTribeCatalog.IsValidForRace(race, tribe))
        {
            failure = HumanPreviewDataFailure.InvalidCustomizeValues;
            return false;
        }

        data = new HumanPreviewData(
            model.ModelId,
            model.SourceId,
            appearance.Customize.ToImmutableArray(),
            appearance.Equipment.ToImmutableArray(),
            ImmutableArray.Create(appearance.Mainhand, appearance.Offhand, 0UL),
            ImmutableArray.Create<ushort>(0, 0),
            false,
            false,
            appearance.VisorToggled);
        failure = HumanPreviewDataFailure.None;
        return true;
    }
}

public enum HumanPreviewDataFailure
{
    None,
    NotHuman,
    AppearanceUnavailable,
    InvalidCustomizeLength,
    InvalidEquipmentLength,
    InvalidCustomizeValues,
    InconsistentAppearance,
}
