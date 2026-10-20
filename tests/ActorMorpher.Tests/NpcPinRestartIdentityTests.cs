using System;
using System.Linq;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using Dalamud.Game.ClientState.Objects.Enums;
using Newtonsoft.Json;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class NpcPinRestartIdentityTests
{
    private const ulong Owner = 123456;

    [Theory]
    [InlineData(ObjectKind.EventNpc, (byte)0)]
    [InlineData(ObjectKind.EventNpc, (byte)4)]
    [InlineData(ObjectKind.EventNpc, (byte)5)]
    [InlineData(ObjectKind.BattleNpc, (byte)0)]
    [InlineData(ObjectKind.BattleNpc, (byte)4)]
    [InlineData(ObjectKind.BattleNpc, (byte)5)]
    public void ReloadKeepsSameNumberNpcsSeparateAndRepinAndUnpinUseTheirOwnSlots(ObjectKind kind, byte source)
    {
        var first = Npc(kind, 201, 1003782, 1187, 1, source);
        var second = Npc(kind, 202, 1003782, 1187, 2, source);
        var config = new Configuration();
        var store = new PinnedOutfitStore(config, () => { }, () => Owner);
        store.PinCurrent([first], _ => Appearance(1.1f));
        store.PinCurrent([second], _ => Appearance(1.2f));
        // A saved session different from this process simulates loading after restart.
        foreach (var pin in config.PinnedOutfitsByCharacter[Owner])
            pin.Session = Guid.NewGuid();
        var loaded = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(config))!;
        PinnedOutfitStore.Normalize(loaded);
        store = new PinnedOutfitStore(loaded, () => { }, () => Owner);
        var recreatedFirst = Npc(kind, 201, 1003782, 1187, 101, source);
        var recreatedSecond = Npc(kind, 202, 1003782, 1187, 102, source);
        Assert.Equal(2, store.Count);
        Assert.True(store.TryGetAppearance(recreatedFirst, out var savedFirst));
        Assert.True(store.TryGetAppearance(recreatedSecond, out var savedSecond));
        Assert.Equal(1.1f, savedFirst.ModelScale);
        Assert.Equal(1.2f, savedSecond.ModelScale);
        Assert.True(new[] { recreatedFirst.Key, recreatedSecond.Key }.ToHashSet().SetEquals(
            store.GetPinnedActorKeysForDisplay([recreatedFirst, recreatedSecond])));
        if (source != 4)
            Assert.Equal(loaded.PinnedOutfitsByCharacter[Owner][0].IdentityKey(),
                PinnedOutfitConfiguration.Create(recreatedFirst, savedFirst).IdentityKey());
        Assert.Null(ActorRegistry.DescribeRenderedAppearance(first.Current,
            recreatedFirst.Current with { CurrentAppearance = savedFirst }));

        Assert.Equal((1, 0), store.PinCurrent([recreatedFirst], _ => Appearance(1.3f)));
        Assert.Equal(2, store.Count);
        PinnedOutfitStore.Normalize(loaded);
        Assert.Equal(2, store.Count);
        Assert.True(store.TryGetAppearance(recreatedFirst, out var updated));
        Assert.Equal(1.3f, updated.ModelScale);
        Assert.True(store.Unpin(recreatedFirst));
        Assert.False(store.IsPinned(recreatedFirst));
        Assert.True(store.TryGetAppearance(recreatedSecond, out savedSecond));
        Assert.Equal(1.2f, savedSecond.ModelScale);
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)4)]
    [InlineData((byte)5)]
    public void RestartDoesNotExpandNpcPinToAnotherSlotNumberKindOrTerritory(byte source)
    {
        var first = Npc(ObjectKind.EventNpc, 201, 1003782, 1187, 1, source);
        var pin = PinnedOutfitConfiguration.Create(first, Appearance(1));
        pin.Session = Guid.NewGuid();
        pin = JsonConvert.DeserializeObject<PinnedOutfitConfiguration>(JsonConvert.SerializeObject(pin))!;
        Assert.True(pin.Matches(Npc(ObjectKind.EventNpc, 201, 1003782, 1187, 101, source)));
        Assert.False(pin.Matches(Npc(ObjectKind.EventNpc, 202, 1003782, 1187, 101, source)));
        Assert.False(pin.Matches(Npc(ObjectKind.EventNpc, 201, 1003783, 1187, 101, source)));
        Assert.False(pin.Matches(Npc(ObjectKind.BattleNpc, 201, 1003782, 1187, 101, source)));
        Assert.False(pin.Matches(Npc(ObjectKind.EventNpc, 201, 1003782, 999, 101, source)));
    }

    [Fact]
    public void EntityNpcStillFollowsEntityWithinSessionAndNormalizationRetainsDistinctSavedRows()
    {
        var first = Npc(ObjectKind.EventNpc, 201, 1003782, 1187, 1, 4);
        var otherEntity = Npc(ObjectKind.EventNpc, 201, 1003782, 1187, 2, 4);
        var pin = PinnedOutfitConfiguration.Create(first, Appearance(1.1f));
        var otherPin = PinnedOutfitConfiguration.Create(otherEntity, Appearance(1.2f));
        Assert.True(pin.Matches(Npc(ObjectKind.EventNpc, 202, 1003782, 1187, 1, 4)));
        Assert.False(pin.Matches(otherEntity));
        Assert.NotEqual(pin.IdentityKey(), otherPin.IdentityKey());
        var priorSession = Guid.NewGuid();
        pin.Session = priorSession;
        otherPin.Session = priorSession;
        var config = new Configuration { PinnedOutfitsByCharacter = new() { [Owner] = [pin, otherPin] } };
        var loaded = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(config))!;
        PinnedOutfitStore.Normalize(loaded);
        Assert.Equal(2, loaded.PinnedOutfitsByCharacter[Owner].Count);
        var recreated = Npc(ObjectKind.EventNpc, 201, 1003782, 1187, 101, 4);
        var store = new PinnedOutfitStore(loaded, () => { }, () => Owner);
        Assert.True(store.TryGetAppearance(recreated, out var appearance));
        Assert.Equal(1.2f, appearance.ModelScale); // Existing last matching row semantics.
        Assert.True(store.Unpin(recreated));
        Assert.Empty(loaded.PinnedOutfitsByCharacter[Owner]);
    }

    [Fact]
    public void RepinningEntityAfterSlotMoveSavesCurrentRepresentationForRestart()
    {
        var first = Npc(ObjectKind.EventNpc, 201, 1003782, 1187, 1, 4);
        var persistence = new ActorAppearancePersistence(() => Owner);
        persistence.RecordModel(first.Current, Appearance(1.1f));
        var configuration = new Configuration();
        var store = new PinnedOutfitStore(configuration, () => { }, () => Owner);
        store.PinCurrent([first], _ => Appearance(1.1f));
        var moved = Npc(ObjectKind.EventNpc, 202, 1003782, 1187, 1, 4);
        var retainedKey = persistence.Resolve(moved.Current.ContinuityKey!.Value, moved.Key);
        Assert.Equal((ushort)201, retainedKey.OriginalObjectIndex);
        moved = moved with { Key = retainedKey, Representations = [moved.Current with { LogicalKey = retainedKey }] };
        Assert.True(store.Unpin(moved));
        Assert.Equal((1, 0), store.PinCurrent([moved], _ => Appearance(1.2f)));
        Assert.True(store.IsPinned(moved));
        var pin = Assert.Single(configuration.PinnedOutfitsByCharacter[Owner]);
        Assert.Equal((ushort)202, pin.ActorKey!.Value.OriginalObjectIndex);
        pin.Session = Guid.NewGuid();
        var loaded = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(configuration))!;
        PinnedOutfitStore.Normalize(loaded);
        store = new PinnedOutfitStore(loaded, () => { }, () => Owner);
        Assert.True(store.IsPinned(moved)); // Even a retained live key must use the current display slot.
        var next = Npc(ObjectKind.EventNpc, 202, 1003782, 1187, 102, 4);
        var other = Npc(ObjectKind.EventNpc, 201, 1003782, 1187, 101, 4);
        Assert.True(store.TryGetAppearance(next, out var saved));
        Assert.Equal(1.2f, saved.ModelScale);
        Assert.False(store.IsPinned(other));
        Assert.Equal(next.Key, Assert.Single(store.GetPinnedActorKeysForDisplay([other, next])));
    }

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)5)]
    public void SlotPinUsesCurrentRepresentationWhenLogicalSlotAndTerritoryAreRetained(byte source)
    {
        var first = Npc(ObjectKind.EventNpc, 201, 1003782, 1187, 1, source);
        var current = Npc(ObjectKind.EventNpc, 202, 1003782, 999, 2, source);
        current = current with { Key = first.Key, Representations = [current.Current with { LogicalKey = first.Key }] };
        var pin = PinnedOutfitConfiguration.Create(current, Appearance(1));
        Assert.True(pin.Matches(current));
        pin.Session = Guid.NewGuid();
        pin = JsonConvert.DeserializeObject<PinnedOutfitConfiguration>(JsonConvert.SerializeObject(pin))!;
        Assert.True(pin.Matches(current));
        Assert.True(pin.Matches(Npc(ObjectKind.EventNpc, 202, 1003782, 999, 102, source)));
        Assert.False(pin.Matches(Npc(ObjectKind.EventNpc, 201, 1003782, 999, 101, source)));
        Assert.False(pin.Matches(Npc(ObjectKind.EventNpc, 202, 1003782, 1187, 102, source)));
    }

    [Fact]
    public void LayoutIdentityRemainsAuthoritativeAcrossRestart()
    {
        var first = Npc(ObjectKind.EventNpc, 201, 1003782, 1187, 1, 3);
        var pin = PinnedOutfitConfiguration.Create(first, Appearance(1));
        pin.Session = Guid.NewGuid();
        Assert.True(pin.Matches(Npc(ObjectKind.EventNpc, 202, 1003782, 1187, 1, 3)));
        Assert.False(pin.Matches(Npc(ObjectKind.EventNpc, 201, 1003782, 1187, 2, 3)));
    }

    private static ActorEntry Npc(ObjectKind kind, ushort index, uint baseId, uint territory, ulong instance, byte source)
    {
        var key = new LogicalActorKey(index, instance + 1000, (uint)instance + 2000, baseId, kind, territory)
        {
            Lifetime = instance,
            Continuity = source == 0 ? null : new ActorContinuityKey(kind, source, instance + 1000, baseId, territory),
        };
        var snapshot = new ActorSnapshot(key, new ActorRepresentationKey(index, key.GameObjectId, key.EntityId, false, territory),
            "Same NPC", kind, baseId, 1, 1, 0, 1, 0, 90, false) { ContinuityKey = key.Continuity };
        return new ActorEntry(key, "Same NPC", kind, false, [snapshot]);
    }

    private static AppearanceData Appearance(float scale)
        => AppearanceData.Create(0, ModelCategory.Human, 17, AppearanceCompleteness.Complete,
            new byte[26], Enumerable.Repeat(0UL, 10), scale, 15, 16, false, 2, false);
}
