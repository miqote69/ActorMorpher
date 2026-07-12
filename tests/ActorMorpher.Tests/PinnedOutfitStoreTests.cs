using System;
using System.Linq;
using ActorMorpher.Actors;
using ActorMorpher.BulkOutfit;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class PinnedOutfitStoreTests
{
    [Fact]
    public void PinRoundTripsOutfitAndSurvivesLogicalKeyChanges()
    {
        var configuration = new Configuration();
        var saveCount = 0;
        var store = new PinnedOutfitStore(configuration, () => saveCount++);
        var first = Actor("Test Player", true, 1, 10);
        var recreated = Actor("Test Player", true, 99, 20);
        var outfit = Outfit(42);

        store.Pin(first, outfit);

        Assert.True(store.IsPinned(recreated));
        Assert.True(store.TryGet(recreated, out var restored));
        Assert.True(OutfitDataValueComparer.AreEqual(outfit, restored));
        Assert.Equal(1, saveCount);
    }

    [Fact]
    public void UnpinRemovesOnlyMatchingActor()
    {
        var configuration = new Configuration();
        var store = new PinnedOutfitStore(configuration, static () => { });
        var first = Actor("First", false, 1, 100);
        var second = Actor("Second", false, 2, 200);
        store.Pin(first, Outfit(10));
        store.Pin(second, Outfit(20));

        Assert.True(store.Unpin(first));

        Assert.False(store.IsPinned(first));
        Assert.True(store.IsPinned(second));
    }

    [Fact]
    public void ValueComparerComparesEquipmentContents()
    {
        Assert.True(OutfitDataValueComparer.AreEqual(Outfit(7), Outfit(7)));
        Assert.False(OutfitDataValueComparer.AreEqual(Outfit(7), Outfit(8)));
    }

    private static ActorEntry Actor(string name, bool local, ushort index, uint territory)
    {
        var key = new LogicalActorKey(index, index, index, local ? 0U : 100U, ObjectKind.Pc, territory);
        var snapshot = new ActorSnapshot(
            key,
            new ActorRepresentationKey(index, index, index, false),
            name,
            ObjectKind.Pc,
            key.BaseId,
            1,
            1,
            0,
            1,
            0,
            90,
            local);
        return new ActorEntry(key, name, ObjectKind.Pc, local, [snapshot]);
    }

    private static OutfitData Outfit(ushort set)
        => OutfitData.Create(
            Enum.GetValues<OutfitSlot>().Select((_, index) => new ArmorAppearance(
                checked((ushort)(set + index)),
                1,
                2,
                3)),
            new FacewearAppearance(true, 12),
            true,
            false);
}
