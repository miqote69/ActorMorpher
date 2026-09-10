using System;
using System.Linq;
using ActorMorpher.Appearance;
using Dalamud.Game;
using Lumina.Excel.Sheets;
using Newtonsoft.Json;
using Xunit;

namespace ActorMorpher.Tests;

public class WeaponSelectionTests
{
    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    public void RemovalIsIndependentOfJobCatalogButEquippingStillRequiresCurrentJob(int slot)
    {
        var removal = new EquipmentChoiceKey(slot, 0, 0);
        var allowed = new EquipmentChoice(new(slot, 9005, 0, WeaponModel: 9005), "Current job", 1);
        var otherJob = new EquipmentChoiceKey(slot, 9006, 0, WeaponModel: 9006);
        Assert.True(WeaponSelection.CanSelect(removal, []));
        Assert.True(WeaponSelection.CanSelect(removal, [allowed]));
        Assert.True(WeaponSelection.CanSelect(allowed.Key, [allowed]));
        Assert.False(WeaponSelection.CanSelect(otherJob, [allowed]));
        Assert.False(WeaponSelection.CanSelect(allowed.Key, []));
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    public void RemovalClearsTheSelectedPackedWeaponIncludingDyesAndCanBeReequipped(int slot)
    {
        var current = AppearanceData.Create(0, ModelCategory.Human, 17, AppearanceCompleteness.Complete,
            new byte[26], new ulong[10], 0.84f,
            15UL | (4UL << 48) | (5UL << 56), 16UL | (6UL << 48) | (7UL << 56));
        var removal = new EquipmentChoiceKey(slot, 0, 0);
        var removed = WeaponSelection.Replace(current, removal);
        var expected = slot == 11 ? current with { Mainhand = 0 } : current with { Offhand = 0 };
        Assert.Equal(expected, removed);
        Assert.Equal(expected, WeaponSelection.Replace(removed, removal));
        Assert.Equal(expected with { Completeness = AppearanceCompleteness.Unsupported },
            WeaponSelection.Replace(current with { Completeness = AppearanceCompleteness.Unsupported }, removal));
        const ulong model = 9005UL | (301UL << 16) | (513UL << 32);
        Assert.Equal(slot == 11 ? removed with { Mainhand = model } : removed with { Offhand = model },
            WeaponSelection.Replace(removed, new(slot, 9005, 0, WeaponModel: model)));
        Assert.Equal(15UL | (4UL << 48) | (5UL << 56), current.Mainhand);
        Assert.Equal(16UL | (6UL << 48) | (7UL << 56), current.Offhand);
    }

    [Fact]
    public void WeaponCatalogSeparatesHandsAndFavoritesCannotAddOtherJobWeapons()
    {
        const ulong main = 9005UL | (301UL << 16) | (513UL << 32);
        const ulong sub = 9006UL | (302UL << 16) | (514UL << 32);
        Assert.Equal(main, WeaponSelection.ModelForSlot(11, true, false, main, sub));
        Assert.Equal(sub, WeaponSelection.ModelForSlot(12, true, false, main, sub));
        Assert.Equal(main, WeaponSelection.ModelForSlot(12, false, true, main, 0));
        Assert.Equal(0UL, WeaponSelection.ModelForSlot(11, false, true, main, 0));
        Assert.Equal(0UL, WeaponSelection.ModelForSlot(12, false, false, main, sub));
        var currentJob = new EquipmentChoice(new(11, 9005, 0, WeaponModel: main), "Test sword", 1);
        var otherJob = new EquipmentChoice(new(11, 9006, 0, WeaponModel: sub), "Test staff", 2);
        var favorites = new System.Collections.Generic.HashSet<EquipmentChoiceKey> { currentJob.Key, otherJob.Key };
        Assert.Equal(new[] { currentJob }, WeaponSelection.Filter([currentJob], favorites, true).ToArray());
        // A job switch supplies the new job's catalog, rather than supplementing it from favorites.
        Assert.Equal(new[] { otherJob }, WeaponSelection.Filter([otherJob], favorites, true).ToArray());
        Assert.Empty(WeaponSelection.Filter([], favorites, false));
        Assert.True(currentJob.Matches("w9005", ClientLanguage.English));
        Assert.False(currentJob.Matches("e9005", ClientLanguage.English));
        Assert.Equal(513, currentJob.DisplayVariant);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    public void SelectionRetainsDyesOppositeHandAndAppearance(int slot)
    {
        var current = AppearanceData.Create(0, ModelCategory.Human, 17, AppearanceCompleteness.Complete,
            new byte[26], new ulong[10], 0.84f,
            15UL | (4UL << 48) | (5UL << 56), 16UL | (6UL << 48) | (7UL << 56));
        const ulong model = 9005UL | (301UL << 16) | (513UL << 32);
        var updated = WeaponSelection.Replace(current, new(slot, 9005, 0, WeaponModel: model));
        var expected = slot == 11 ? current with { Mainhand = model | (4UL << 48) | (5UL << 56) }
            : current with { Offhand = model | (6UL << 48) | (7UL << 56) };
        Assert.Equal(expected, updated);
        Assert.Equal(15UL | (4UL << 48) | (5UL << 56), current.Mainhand);
        // Completeness is descriptive; it does not gate a selected weapon change.
        Assert.Equal(expected with { Completeness = AppearanceCompleteness.Unsupported },
            WeaponSelection.Replace(current with { Completeness = AppearanceCompleteness.Unsupported },
                new(slot, 9005, 0, WeaponModel: model)));
    }

    [Fact]
    public void FavoritesRetainFullWeaponTupleAndReadOlderArmorKeys()
    {
        var key = new EquipmentChoiceKey(12, 9005, 0, WeaponModel: 9005UL | (301UL << 16) | (513UL << 32));
        Assert.Equal(key, JsonConvert.DeserializeObject<EquipmentChoiceKey>(JsonConvert.SerializeObject(key)));
        Assert.Equal(new EquipmentChoiceKey(1, 9005, 2),
            JsonConvert.DeserializeObject<EquipmentChoiceKey>("{\"Slot\":1,\"Model\":9005,\"Variant\":2}"));
    }

    [Fact]
    public void InstalledJobCategorySchemaHasMembershipColumnsForEveryCurrentJob()
    {
        foreach (var job in new[] { "GLA", "PGL", "MRD", "LNC", "ARC", "CNJ", "THM", "CRP", "BSM", "ARM", "GSM",
                     "LTW", "WVR", "ALC", "CUL", "MIN", "BTN", "FSH", "PLD", "MNK", "WAR", "DRG", "BRD", "WHM",
                     "BLM", "ACN", "SMN", "SCH", "ROG", "NIN", "MCH", "DRK", "AST", "SAM", "RDM", "BLU", "GNB",
                     "DNC", "RPR", "SGE", "VPR", "PCT" })
            Assert.Equal(typeof(bool), typeof(ClassJobCategory).GetProperty(job)?.PropertyType);
    }
}
