using ActorMorpher.BulkOutfit;
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

    [Fact]
    public void DecodesSheetBgrColorAsRgb()
    {
        var (red, green, blue) = EquipmentDisplayFormatting.DecodeStainColor(0x00112233);

        Assert.Equal((byte)0x33, red);
        Assert.Equal((byte)0x22, green);
        Assert.Equal((byte)0x11, blue);
    }
}
