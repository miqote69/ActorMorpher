using System.Reflection;
using System.Runtime.CompilerServices;
using Dalamud.Game;
using Xunit;

namespace ActorMorpher.Tests;

public class FacewearPickerFilterTests
{
    [Theory]
    [InlineData(10, EquipmentPickerFilter.Fashion, EquipmentPickerFilter.All)]
    [InlineData(10, EquipmentPickerFilter.All, EquipmentPickerFilter.All)]
    [InlineData(10, EquipmentPickerFilter.Favorites, EquipmentPickerFilter.Favorites)]
    [InlineData(1, EquipmentPickerFilter.Fashion, EquipmentPickerFilter.Fashion)]
    [InlineData(11, EquipmentPickerFilter.Fashion, EquipmentPickerFilter.Fashion)]
    public void FacewearHasNoHiddenFashionRestriction(int slot, EquipmentPickerFilter selected, EquipmentPickerFilter expected)
    {
        // Read the actual UI filter without constructing a game window or plugin.
        var window = (MainWindow)RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MainWindow).GetField("equipmentPickerSlot", flags)!.SetValue(window, slot);
        typeof(MainWindow).GetField("equipmentFilter", flags)!.SetValue(window, selected);
        var active = (EquipmentPickerFilter)typeof(MainWindow).GetProperty("ActiveEquipmentFilter", flags)!.GetValue(window)!;
        Assert.Equal(expected, active);
        Assert.Equal(selected, typeof(MainWindow).GetField("equipmentFilter", flags)!.GetValue(window));
        if (slot == 10)
        {
            var facewear = new EquipmentChoice(new(10, 5545, 8, 24), "Facewear", 1);
            var result = EquipmentChoice.FilterForPicker([facewear], [], active, "", ClientLanguage.English);
            if (selected == EquipmentPickerFilter.Favorites) Assert.Empty(result);
            else Assert.Same(facewear, Assert.Single(result));
        }
    }
}
