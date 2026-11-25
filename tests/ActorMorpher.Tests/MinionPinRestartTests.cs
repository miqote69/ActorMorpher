using System;
using System.Collections.Generic;
using System.Linq;
using ActorMorpher;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using ActorMorpher.Interop;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Newtonsoft.Json;
using Xunit;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace ActorMorpher.Tests;

public sealed unsafe class MinionPinRestartTests
{
    [Fact]
    public void PinSurvivesJsonRestartAndFindsOnlyOwnersSameMinion()
    {
        var first = Minion(1234, 248, 1);
        var config = new Configuration();
        var store = new PinnedOutfitStore(config, () => { }, () => 1);
        var appearance = Appearance();
        Assert.Equal((1, 0), store.PinCurrent([first], _ => appearance));
        var loaded = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(config))!;
        loaded.PinnedOutfitsByCharacter[1][0].Session = Guid.NewGuid();
        PinnedOutfitStore.Normalize(loaded);
        store = new PinnedOutfitStore(loaded, () => { }, () => 1);
        var next = Minion(1234, 248, 19);
        Assert.True(store.IsPinned(next));
        Assert.True(store.TryGetAppearance(next, out var saved));
        Assert.True(PinnedOutfitStore.AppearanceEquals(appearance, saved));
        Assert.Equal(config.PinnedOutfitsByCharacter[1][0].IdentityKey(), PinnedOutfitConfiguration.Create(next, appearance).IdentityKey());

        // The normal discovery/restore comparison still converges. No live key is reused.
        var discovery = new PinnedActorDiscovery();
        long scan = 100;
        Assert.Single(discovery.GetCandidates([next], store.IsPinned, 0, ref scan));
        Assert.Empty(discovery.GetCandidates([next], store.IsPinned, 1, ref scan));
        Assert.True(store.TryGet(next, out var desired));
        Assert.True(OutfitDataValueComparer.AreEqual(EquipmentDisplayFormatting.CreateHumanOutfit(saved), desired));
        Assert.True(PinnedOutfitStore.CanMaintainWithOutfit(saved with { Equipment = saved.Equipment.SetItem(1, 42) }, saved));
        Assert.Null(ActorRegistry.DescribeRenderedAppearance(first.Current, next.Current));

        foreach (var other in new[] { Minion(5678, 248, 19), Minion(1234, 332, 19), Minion(0, 248, 19) })
        {
            Assert.False(store.IsPinned(other));
            Assert.False(store.TryGetAppearance(other, out _));
            Assert.False(store.Unpin(other));
        }
        Assert.False(store.IsPinned(next with { Kind = ObjectKind.EventNpc }));
        Assert.Equal((1, 0), store.PinCurrent([next], _ => appearance));
        Assert.Single(loaded.PinnedOutfitsByCharacter[1]);
        Assert.True(store.Unpin(next));
        Assert.Empty(loaded.PinnedOutfitsByCharacter[1]);
    }

    [Fact]
    public void LegacyTransientPinDoesNotGuessOwnerAfterRestart()
    {
        var old = Minion(0, 248, 1);
        var pin = PinnedOutfitConfiguration.Create(old, Appearance());
        Assert.Null(pin.MinionOwnerContentId);
        pin.Session = Guid.NewGuid();
        Assert.False(pin.Matches(Minion(1234, 248, 1)));
        Assert.False(pin.Matches(old));
    }

    [Fact]
    public void SwitchDefersDestinationPinUntilTheGameStartsItsModel()
    {
        var native = new NativeActorContinuity(null, new Dictionary<uint, uint> { [248] = 1639, [145] = 1165 });
        var lyse = Minion(1234, 248, 1);
        var ysayle = Minion(1234, 145, 1);
        var store = new PinnedOutfitStore(new Configuration(), () => { }, () => 1);
        var pin = Appearance();
        Assert.Equal((1, 0), store.PinCurrent([ysayle], _ => pin));
        var discovery = new PinnedActorDiscovery();
        long nextScan = 0;

        var old = Observe(lyse, 0, CompanionSpawnState.Active);
        Assert.False(old.Current.MinionSwitchPending);
        var early = Observe(ysayle, 0, CompanionSpawnState.Active);
        Assert.NotEqual(old.Key.Lifetime, early.Key.Lifetime);
        Assert.True(store.IsPinned(early));
        Assert.True(early.Current.MinionSwitchPending);
        // Even an ordinary scan must not apply the destination pin to the old draw object.
        Assert.Empty(discovery.GetCandidates([early], store.IsPinned, 0, ref nextScan));
        Assert.True(Observe(ysayle, 0, CompanionSpawnState.Despawning).Current.MinionSwitchPending);
        Assert.Equal(500, nextScan);

        var replacement = Observe(ysayle, 1165, CompanionSpawnState.Spawning);
        Assert.Equal(early.Key.Lifetime, replacement.Key.Lifetime);
        Assert.False(replacement.Current.MinionSwitchPending);
        Assert.Same(replacement, Assert.Single(discovery.GetCandidates([replacement], store.IsPinned, 1, ref nextScan)));
        Assert.True(store.TryGetAppearance(replacement, out var selected));
        Assert.Same(pin, selected);
        Assert.False(Observe(ysayle, 0, CompanionSpawnState.Spawning).Current.MinionSwitchPending);

        ActorEntry Observe(ActorEntry actor, int nativeModel, CompanionSpawnState state)
        {
            var identity = actor.Key.Continuity!.Value;
            var sample = new NativeActorContinuity.MinionObservation(1, 1188, nativeModel, state);
            var (_, lifetime) = native.Observe(123, identity, null, sample, out var pending);
            var key = actor.Key with { Lifetime = lifetime };
            return actor with
            {
                Key = key,
                Representations = [actor.Current with { LogicalKey = key, MinionSwitchPending = pending }],
            };
        }
    }

    [Fact]
    public void ASharedNativeModelDoesNotProveTheNewMinionHasSpawned()
    {
        var native = new NativeActorContinuity(null, new Dictionary<uint, uint> { [10] = 870, [11] = 870 });
        var outgoing = Minion(1234, 10, 1).Key.Continuity!.Value;
        var incoming = Minion(1234, 11, 1).Key.Continuity!.Value;
        var sample = new NativeActorContinuity.MinionObservation(1, 1188, 0, CompanionSpawnState.Active);
        native.Observe(123, outgoing, null, sample, out _);
        sample = sample with { Model = 870 };
        native.Observe(123, incoming, null, sample, out var pending);
        Assert.True(pending);
        native.Observe(123, incoming, null, sample with { State = CompanionSpawnState.Despawning }, out pending);
        Assert.True(pending);
        native.Observe(123, incoming, null, sample with { State = CompanionSpawnState.Spawning }, out pending);
        Assert.False(pending);
    }

    [Fact]
    public void OwnerComesFromPcChildRelationshipAndNotMinionOwnerId()
    {
        Character player = default, child = default;
        player.ObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.Pc;
        player.ContentId = 1234;
        player.ChildObject = &child;
        child.OwnerId = 0xE0000000;
        var before = new ReadOnlySpan<byte>(&player, sizeof(Character)).ToArray();
        var owner = NativeMinionOwner.Read(&player)!.Value;
        Assert.Equal((nint)(&child), owner.Child);
        Assert.Equal(1234UL, owner.ContentId);
        Assert.Equal(before, new ReadOnlySpan<byte>(&player, sizeof(Character)).ToArray());
        Assert.Equal(1234UL, NativeMinionOwner.UniqueOwners([owner])[owner.Child]);
        Assert.Empty(NativeMinionOwner.UniqueOwners([owner, owner with { ContentId = 5678 }]));
        player.ContentId = 0;
        Assert.Null(NativeMinionOwner.Read(&player));
        player.ContentId = 1234;
        player.ChildObject = null;
        Assert.Null(NativeMinionOwner.Read(&player));
        Assert.Null(NativeMinionOwner.Read(&child));
    }

    private static ActorEntry Minion(ulong owner, uint baseId, ushort index)
    {
        var transient = 0x400000000UL + index;
        var key = new LogicalActorKey(index, transient, 0xE0000000, baseId, ObjectKind.Companion, index)
        { Continuity = new(ObjectKind.Companion, 2, transient, baseId, 0), Lifetime = index };
        var snapshot = new ActorSnapshot(key, new(index, transient, 0xE0000000, false, index),
            "Minion", ObjectKind.Companion, baseId, 0, 1, 1, 1, 0, 1, false,
            CurrentAppearance: Appearance())
        { ContinuityKey = key.Continuity, MinionOwnerContentId = owner };
        return new(key, "Minion", ObjectKind.Companion, false, [snapshot]);
    }

    private static AppearanceData Appearance()
        => new(0, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
            System.Collections.Immutable.ImmutableArray.CreateRange(new byte[26]),
            System.Collections.Immutable.ImmutableArray.CreateRange(Enumerable.Repeat(1UL, 10)),
            1, 0, 0, false, 0, true);
}
