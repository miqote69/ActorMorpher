using ActorMorpher.BulkOutfit;
using Dalamud.Game;
using Newtonsoft.Json;
using Xunit;

namespace ActorMorpher.Tests;

public class EquipmentChoiceTests
{
    [Theory]
    [InlineData("e9005", 1, true, 9005)]
    [InlineData("E9005", 1, true, 9005)]
    [InlineData("9005", 1, true, 9005)]
    [InlineData("a0177", 5, true, 177)]
    [InlineData("e0177", 5, false, 0)]
    [InlineData("a9005", 1, false, 0)]
    [InlineData("65535", 1, true, 65535)]
    [InlineData("65536", 1, false, 0)]
    [InlineData("-1", 1, false, 0)]
    [InlineData("e", 1, false, 0)]
    public void ParsesExactResourceNumber(string input, int slot, bool valid, ushort expected)
    {
        Assert.Equal(valid, EquipmentChoice.TryParseModel(input, slot, out var number));
        Assert.Equal(expected, number);
    }

    [Fact]
    public void SearchUsesLocalizedNameAndExactModelAcrossVariants()
    {
        var first = new EquipmentChoice(new(1, 9005, 1), "サマートップ", 1);
        var second = first with { Key = new(1, 9005, 2) };
        Assert.True(first.Matches("さまー", ClientLanguage.Japanese));
        Assert.True(first.Matches("e9005", ClientLanguage.Japanese));
        Assert.True(second.Matches("9005", ClientLanguage.Japanese));
        Assert.False(first.Matches("e900", ClientLanguage.Japanese));
    }

    [Fact]
    public void FavoritesRoundTripKeepSlotVariantAndFacewearIdentity()
    {
        var config = new Configuration { FavoriteEquipment = [new(1, 9005, 2), new(2, 9005, 2), new(10, 900, 3, 17)] };
        var restored = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(config))!;
        Assert.Equal(3, restored.FavoriteEquipment.Count);
        Assert.Contains(new EquipmentChoiceKey(10, 900, 3, 17), restored.FavoriteEquipment);
        Assert.True(restored.FavoriteEquipment.Remove(new(1, 9005, 2)));
        Assert.Contains(new EquipmentChoiceKey(2, 9005, 2), restored.FavoriteEquipment);
    }
}
