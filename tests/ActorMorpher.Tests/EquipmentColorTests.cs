using System;
using System.Linq;
using System.Buffers.Binary;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using ActorMorpher.Interop;
using Xunit;

namespace ActorMorpher.Tests;

public class EquipmentColorTests
{
    [Fact]
    public void ModernRowsRespectChannelsAndLeaveNonDiffuseValuesUntouched()
    {
        var table = Enumerable.Repeat((Half)0.75f, 128).ToArray();
        var dyes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(dyes, 1);
        BinaryPrimitives.WriteUInt32LittleEndian(dyes.AsSpan(4), 1u | (1u << 27));
        BinaryPrimitives.WriteUInt32LittleEndian(dyes.AsSpan(8), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(dyes.AsSpan(12), 1u | (2u << 27));
        var armor = new ArmorAppearance(51, 2, 0, 0)
        { Color1 = new(0, 0.5f, 1), Color2 = new(1, 0, 0.5f) };
        Assert.True(NativeEquipmentColors.Transform(table, 8, 4, dyes, 4, armor));
        Assert.Equal(new Half[] { (Half)0, (Half)0.25f, (Half)1 }, table[..3]);
        Assert.Equal(new Half[] { (Half)1, (Half)0, (Half)0.25f }, table[32..35]);
        Assert.All(table[3..32].Concat(table[35..]), item => Assert.Equal((Half)0.75f, item));
    }

    [Fact]
    public void LegacyAndMissingDyeRowsDoNotInventDyeableRegions()
    {
        var table = Enumerable.Repeat((Half)1, 16).ToArray();
        var armor = new ArmorAppearance(1, 1, 0, 0) { Color1 = new(0, 0, 0), Color2 = new(1, 0, 0) };
        Assert.False(NativeEquipmentColors.Transform(table, 4, 1, [], 2, armor));
        Assert.All(table, value => Assert.Equal((Half)1, value));
        Assert.True(NativeEquipmentColors.Transform(table, 4, 1, new byte[] { 1, 0 }, 2, armor));
        Assert.All(table[..3], value => Assert.Equal((Half)0, value));
        Assert.All(table[3..], value => Assert.Equal((Half)1, value));
    }

    [Fact]
    public void AppearanceAndPinSerializationPreserveBothColors()
    {
        var armor = new ArmorAppearance(51, 2, 3, 4) { Color1 = new(0.1f, 0.2f, 0.3f), Color2 = new(0, 0, 0) };
        var outfit = OutfitData.Create(Enumerable.Repeat(armor, 10), new(true, 0), true, false);
        var appearance = AppearanceData.Create(0, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
            new byte[26], Enumerable.Repeat(0UL, 10), visorToggled: false, facewearModelId: 0, hatVisible: true)
            with { ColoredEquipment = outfit.Equipment };
        var pin = new PinnedOutfitConfiguration { Appearance = appearance };
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(pin);
        var copy = Newtonsoft.Json.JsonConvert.DeserializeObject<PinnedOutfitConfiguration>(json)!;
        Assert.True(copy.TryCreateOutfit(out var restored));
        Assert.Equal(armor, restored.Equipment[0]);
        Assert.True(PinnedOutfitStore.AppearanceEquals(appearance, copy.Appearance!));
        Assert.False(PinnedOutfitStore.AppearanceEquals(appearance, appearance with { ColoredEquipment = [] }));
    }
}
