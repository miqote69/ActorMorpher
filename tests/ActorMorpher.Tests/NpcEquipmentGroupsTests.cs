using System;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class NpcEquipmentGroupsTests
{
    [Fact]
    public void DifferentIdsWithIdenticalOriginalDefinitionsShareAGroup()
    {
        var first = Entry(20);
        var second = Entry(10);
        var definitions = new[] { first, second };
        var groups = new NpcEquipmentGroups(definitions);

        Assert.True(groups.TryGet(ObjectKind.EventNpc, 20, out var group));
        Assert.True(groups.TryGet(ObjectKind.EventNpc, 10, out var other));
        Assert.Same(group, other);
        Assert.Equal(ObjectKind.EventNpc, group.Kind);
        Assert.Equal(10u, group.CanonicalBaseId);
        Assert.Equal(new uint[] { 10, 20 }, group.BaseIds);
        Assert.Same(first, definitions[0]);
        Assert.Same(second, definitions[1]);
    }

    [Theory]
    [InlineData("equipment")]
    [InlineData("dye")]
    [InlineData("customize")]
    [InlineData("model")]
    [InlineData("name")]
    [InlineData("source")]
    public void DifferentOriginalDefinitionsDoNotShare(string difference)
    {
        var first = Entry(10);
        var second = Entry(20);
        var appearance = second.ModelAppearance;
        second = difference switch
        {
            "equipment" => second with { ModelAppearance = appearance with { Equipment = appearance.Equipment.SetItem(0, 1UL) } },
            "dye" => second with { ModelAppearance = appearance with { Equipment = appearance.Equipment.SetItem(0, 1UL << 32) } },
            "customize" => second with { ModelAppearance = appearance with { Customize = appearance.Customize.SetItem(6, 2) } },
            "model" => second with { ModelAppearance = appearance with { ModelCharaId = 123 } },
            "name" => second with { Name = "別のNPC" },
            "source" => second with { Source = ModelSource.BattleNpc },
            _ => throw new ArgumentOutOfRangeException(nameof(difference)),
        };

        var groups = new NpcEquipmentGroups([first, second]);
        Assert.False(groups.TryGet(ObjectKind.EventNpc, 10, out _));
        Assert.False(groups.TryGet(ObjectKind.EventNpc, 20, out _));
        Assert.False(groups.TryGet(ObjectKind.BattleNpc, 20, out _));
    }

    [Fact]
    public void RepeatedSameIdDoesNotCreateAGroup()
    {
        var groups = new NpcEquipmentGroups([Entry(10), Entry(10)]);
        Assert.False(groups.TryGet(ObjectKind.EventNpc, 10, out _));
    }

    [Fact]
    public void BattleNpcAliasesCompareAsWholeSetsRegardlessOfOrder()
    {
        var groups = new NpcEquipmentGroups([
            Entry(10, "A", ModelSource.BattleNpc), Entry(10, "B", ModelSource.BattleNpc),
            Entry(20, "B", ModelSource.BattleNpc), Entry(20, "A", ModelSource.BattleNpc),
            Entry(30, "B", ModelSource.BattleNpc), Entry(30, "C", ModelSource.BattleNpc),
        ]);

        Assert.True(groups.TryGet(ObjectKind.BattleNpc, 10, out var first));
        Assert.True(groups.TryGet(ObjectKind.BattleNpc, 20, out var second));
        Assert.Same(first, second);
        Assert.False(groups.TryGet(ObjectKind.BattleNpc, 30, out _));
    }

    [Fact]
    public void ModelCharaAndNonEditableAppearancesAreNotGrouped()
    {
        var first = Entry(10);
        var second = Entry(20);
        var groups = new NpcEquipmentGroups([
            first with { Source = ModelSource.ModelChara }, second with { Source = ModelSource.ModelChara },
            first with { ModelAppearance = first.ModelAppearance with { Category = ModelCategory.Monster } },
            second with { ModelAppearance = second.ModelAppearance with { Category = ModelCategory.Monster } },
        ]);
        Assert.False(groups.TryGet(ObjectKind.EventNpc, 10, out _));
        Assert.False(groups.TryGet(ObjectKind.EventNpc, 20, out _));
    }

    private static ModelSearchEntry Entry(uint id, string name = "パンプキンヘッドの女", ModelSource source = ModelSource.EventNpc)
    {
        var customize = new byte[26];
        customize[0] = 1;
        customize[4] = 1;
        var appearance = AppearanceData.Create(0, ModelCategory.Human, id,
            AppearanceCompleteness.Complete, customize, new ulong[10],
            visorToggled: false, hatVisible: true);
        return new ModelSearchEntry(0, ModelCategory.Human, source, id, name,
            1, 0, 0, 0, 1, 0, 0, null, AppearanceCompleteness.Complete, appearance);
    }
}
