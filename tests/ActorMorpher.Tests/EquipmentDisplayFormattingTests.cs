using ActorMorpher.BulkOutfit;
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
        var appearance = new HumanAppearance(new byte[26], Enumerable.Repeat(packed, 10).ToArray(), 0, 0, true);

        var outfit = Assert.IsType<OutfitData>(EquipmentDisplayFormatting.CreateHumanOutfit(appearance));

        Assert.Equal(new ArmorAppearance(21, 3, 7, 9), outfit.Equipment[0]);
        Assert.True(outfit.VisorToggled);
    }
}
