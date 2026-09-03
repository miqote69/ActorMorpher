using ActorMorpher.BulkOutfit;
using ActorMorpher.Appearance;
using ActorMorpher.Actors;
using System.Linq;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class EquipmentDisplayFormattingTests
{
    [Theory]
    [InlineData(OutfitSlot.Head, 21, "e0021")]
    [InlineData(OutfitSlot.Feet, 321, "e0321")]
    [InlineData(OutfitSlot.Ears, 21, "a0021")]
    [InlineData(OutfitSlot.LeftRing, 9999, "a9999")]
    public void FormatsArmorAndAccessoryModelIds(OutfitSlot slot, ushort set, string expected)
        => Assert.Equal(expected, EquipmentDisplayFormatting.FormatSet(slot, set));

    [Theory]
    [InlineData(0, "0")]
    [InlineData(23, "23")]
    public void FormatsVariantAsPlainNumber(byte variant, string expected)
        => Assert.Equal(expected, EquipmentDisplayFormatting.FormatVariant(variant));

    [Fact]
    public void DecodesSheetColorAsRgb()
    {
        var (red, green, blue) = EquipmentDisplayFormatting.DecodeStainColor(0x00112233);

        Assert.Equal((byte)0x11, red);
        Assert.Equal((byte)0x22, green);
        Assert.Equal((byte)0x33, blue);
    }

    [Fact]
    public void CreatesHumanOutfitWithBothStains()
    {
        var packed = 21UL | (3UL << 16) | (7UL << 24) | (9UL << 32);
        var appearance = new HumanAppearance(
            new byte[26],
            Enumerable.Repeat(packed, 10).ToArray(),
            0,
            0,
            true,
            27,
            false);

        var outfit = Assert.IsType<OutfitData>(EquipmentDisplayFormatting.CreateHumanOutfit(appearance));

        Assert.Equal(new ArmorAppearance(21, 3, 7, 9), outfit.Equipment[0]);
        Assert.Equal((ushort)27, outfit.Facewear.ModelId);
        Assert.False(outfit.HatVisible);
        Assert.True(outfit.VisorToggled);
    }

    [Fact]
    public void CreatesAppliedCurrentOutfitFromNpcOwnedAppearanceFields()
    {
        var packed = 31UL | (4UL << 16);
        var appearance = AppearanceData.Create(
            101,
            ModelCategory.Human,
            202,
            AppearanceCompleteness.Complete,
            new byte[26],
            Enumerable.Repeat(packed, 10),
            0.84f,
            0,
            0,
            false,
            27,
            true);

        var outfit = Assert.IsType<OutfitData>(EquipmentDisplayFormatting.CreateHumanOutfit(appearance));

        Assert.Equal((ushort)27, outfit.Facewear.ModelId);
        Assert.True(outfit.HatVisible);
        Assert.False(outfit.VisorToggled);
    }

    [Fact]
    public void HumanModelPayloadUsesSelectedNpcFacewearHatAndScale()
    {
        var source = new HumanAppearance(
            new byte[26],
            new ulong[10],
            1,
            2,
            false,
            27,
            true);

        var payload = Plugin.CreateHumanModelAppearance(101, 202, source, 0.84f);

        Assert.Equal((ushort)27, payload.FacewearModelId);
        Assert.True(payload.HatVisible);
        Assert.Equal(0.84f, payload.ModelScale);
    }

    [Fact]
    public void NpcFacewearSelectionIsIndependentOfEquipmentSourceSelection()
    {
        Assert.Equal((ushort)27, Plugin.SelectNpcFacewearModelId(27));
        Assert.Equal((ushort)0, Plugin.SelectNpcFacewearModelId(null));
    }

    [Fact]
    public void SuccessfulDemihumanApplyPublishesTheCategoryContractAsCurrentC()
    {
        var applied = AppearanceData.Create(
            101,
            ModelCategory.Demihuman,
            202,
            AppearanceCompleteness.Complete,
            new byte[26],
            new ulong[10],
            0.84f);

        var published = Plugin.CreatePublishedAppearance(applied);

        Assert.Empty(published.Customize);
        Assert.True(ActorRegistry.IsCompleteCurrentAppearance(published));
    }
}
