using Dalamud.Game;
using System.Linq;
using Xunit;

namespace ActorMorpher.Tests;

public class EquipmentCategoryTests
{
    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(1, 5, false)]
    [InlineData(5, 1, false)]
    public void FashionRequiresBothApprovedLevels(byte equipLevel, uint itemLevel, bool expected)
    {
        var choice = new EquipmentChoice(new(1, 100, 2), "Item", 9)
            { Items = [new(1, "Item", 9, itemLevel, equipLevel)] };
        Assert.Equal(expected, choice.FashionOnly() is not null);
    }

    [Fact]
    public void FashionKeepsMatchingAliasAndItsLevelWithoutChangingAppearanceKey()
    {
        var choice = new EquipmentChoice(new(11, 100, 0, WeaponModel: 0x100020064), "Fashion / Dungeon / Raid", 9)
        { Items = [new(1, "Fashion", 9, 1, 1), new(2, "Dungeon", 10, 500, 80), new(3, "Raid", 11, 600, 90)] };
        var filtered = choice.FashionOnly()!;
        Assert.Equal(choice.Key, filtered.Key);
        Assert.Equal("Fashion", filtered.Name);
        Assert.Equal("1", filtered.ItemLevels);
        Assert.True(filtered.Matches("Fashion", ClientLanguage.English));
        Assert.False(filtered.Matches("Raid", ClientLanguage.English));
        Assert.Equal("1 / 500 / 600", choice.ItemLevels);
    }

    [Fact]
    public void UnknownFacewearAndManualChoicesRemainAvailableInAll()
    {
        foreach (var key in new EquipmentChoiceKey[] { new(10, 55, 3, 24), new(1, 9005, 2) })
        {
            var choice = new EquipmentChoice(key, "Manual", 0);
            Assert.Same(choice, Assert.Single(EquipmentChoice.FilterForPicker([choice], [], EquipmentPickerFilter.All, "", ClientLanguage.English)));
            Assert.Equal("—", choice.ItemLevels);
            Assert.Null(choice.FashionOnly());
        }
    }

    [Theory]
    [InlineData(EquipmentPickerFilter.All)]
    [InlineData(EquipmentPickerFilter.Fashion)]
    [InlineData(EquipmentPickerFilter.Favorites)]
    public void EveryViewSortsNumericModelThenVariant(EquipmentPickerFilter filter)
    {
        var choices = new[] { new EquipmentChoice(new(1, 10000, 1), "AAA", 1),
            new EquipmentChoice(new(1, 2, 3), "BBB", 1), new EquipmentChoice(new(1, 1, 2), "ZZZ", 1),
            new EquipmentChoice(new(1, 2, 1), "CCC", 1) }
            .Select(choice => choice with { Items = [new(1, choice.Name, 1, 1, 1)] }).ToArray();
        var result = EquipmentChoice.FilterForPicker(choices, choices.Select(choice => choice.Key).ToArray(), filter, "", ClientLanguage.English);
        Assert.Equal(new ushort[] { 1, 2, 2, 10000 }, result.Select(choice => choice.Key.Model));
        Assert.Equal(new byte[] { 2, 1, 3, 1 }, result.Select(choice => choice.Key.Variant));
    }

    [Fact]
    public void FavoritesDoesNotKeepFashionRestrictionAndSearchStillApplies()
    {
        var fashion = new EquipmentChoice(new(1, 1, 1), "Fashion", 1) { Items = [new(1, "Fashion", 1, 1, 1)] };
        var battle = new EquipmentChoice(new(1, 2, 1), "Battle", 2) { Items = [new(2, "Battle", 2, 500, 80)] };
        var result = EquipmentChoice.FilterForPicker([fashion, battle], [battle.Key], EquipmentPickerFilter.Favorites, "e0002", ClientLanguage.English);
        Assert.Same(battle, Assert.Single(result));
        Assert.Empty(EquipmentChoice.FilterForPicker([fashion, battle], [battle.Key], EquipmentPickerFilter.Favorites, "Fashion", ClientLanguage.English));
    }
}
