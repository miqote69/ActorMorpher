using ActorMorpher;
using System;
using System.Linq;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class OverrideStoreTests
{
    [Fact]
    public void AppearanceStorePreservesFirstBaseAcrossMultipleMorphs()
    {
        var store = new AppearanceOverrideStore();
        var key = Key(entityId: 10);
        var original = Appearance(0, 1);
        var monster = Appearance(100, 2);
        var demihuman = Appearance(200, 3);

        store.SetDesired(key, original, monster);
        var updated = store.SetDesired(key, monster, demihuman);

        Assert.Equal(original, updated.BaseData);
        Assert.Equal(demihuman, updated.DesiredData);
        Assert.Equal(2, updated.Revision);
    }

    [Fact]
    public void SuccessfulAppearanceRestoreRemovesState()
    {
        var store = new AppearanceOverrideStore();
        var key = Key(entityId: 10);
        store.SetDesired(key, Appearance(0, 1), Appearance(100, 2));

        Assert.True(store.CompleteRestore(key));
        Assert.False(store.TryGet(key, out _));
    }

    [Fact]
    public void LogicalKeyChangesWhenIndexEntityOrTerritoryChanges()
    {
        var original = Key(entityId: 10);

        Assert.NotEqual(original, original with { OriginalObjectIndex = 2 });
        Assert.NotEqual(original, original with { EntityId = 11 });
        Assert.NotEqual(original, original with { TerritoryId = 999 });
    }

    [Fact]
    public void OutfitStorePreservesOriginalAndIncrementsRevision()
    {
        var store = new OutfitOverrideStore();
        var key = Key(entityId: 10);
        var original = Outfit(1);
        var first = Outfit(2);
        var second = Outfit(3);

        store.SetDesired(key, original, first);
        var updated = store.SetDesired(key, first, second);

        Assert.Equal(original, updated.Original);
        Assert.Equal(second, updated.Desired);
        Assert.Equal(2, updated.Revision);
    }

    private static LogicalActorKey Key(uint entityId)
        => new(1, 100, entityId, 20, ObjectKind.Pc, 30);

    private static AppearanceData Appearance(uint modelId, byte marker)
        => AppearanceData.Create(
            modelId,
            modelId == 0 ? ModelCategory.Human : ModelCategory.Monster,
            marker,
            AppearanceCompleteness.Complete,
            [marker],
            [marker]);

    private static OutfitData Outfit(ulong marker)
        => OutfitData.Create(Enumerable.Repeat(marker, Enum.GetValues<OutfitSlot>().Length), 0, true, false);
}
