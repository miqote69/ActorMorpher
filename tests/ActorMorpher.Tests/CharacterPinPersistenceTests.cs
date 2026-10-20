using System;
using System.Collections.Immutable;
using System.Linq;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using Dalamud.Game.ClientState.Objects.Enums;
using Newtonsoft.Json;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class CharacterPinPersistenceTests
{
    [Fact]
    public void CharactersKeepSeparateAppearancesAfterSerializationAndUnpinOnlyTheirOwnPins()
    {
        ulong owner = 101;
        var actor = Npc();
        var config = new Configuration();
        var store = new PinnedOutfitStore(config, () => { }, () => owner);
        var first = Appearance(11);
        var second = Appearance(22);
        Assert.Equal((1, 0), store.PinCurrent([actor], _ => first));
        owner = 202;
        Assert.Equal(0, store.Count);
        Assert.False(store.IsPinned(actor));
        Assert.Equal((1, 0), store.PinCurrent([actor], _ => second));

        var loaded = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(config))!;
        PinnedOutfitStore.Normalize(loaded);
        store = new PinnedOutfitStore(loaded, () => { }, () => owner);
        Assert.Empty(loaded.PinnedOutfits);
        Assert.Equal(2, loaded.PinnedOutfitsByCharacter.Count);
        foreach (var (id, expected) in new[] { (101UL, first), (202UL, second) })
        {
            owner = id;
            Assert.Equal(1, store.Count);
            Assert.True(store.IsPinned(actor));
            Assert.Equal(actor.Key, Assert.Single(store.GetPinnedActorKeysForDisplay([actor])));
            Assert.True(store.TryGetAppearance(actor, out var actual));
            Assert.True(PinnedOutfitStore.AppearanceEquals(expected, actual));
            Assert.True(store.TryGet(actor, out var outfit));
            Assert.True(OutfitDataValueComparer.AreEqual(EquipmentDisplayFormatting.CreateHumanOutfit(expected), outfit));
        }

        owner = 101;
        Assert.True(store.Unpin(actor));
        Assert.Equal(0, store.Count);
        owner = 202;
        Assert.True(store.TryGetAppearance(actor, out var retained));
        Assert.True(PinnedOutfitStore.AppearanceEquals(second, retained));
        store.Pin(actor, EquipmentDisplayFormatting.CreateHumanOutfit(second)!);
        Assert.Equal(1, store.UnpinAll());
        Assert.Empty(loaded.PinnedOutfitsByCharacter[101]);
        Assert.Empty(loaded.PinnedOutfitsByCharacter[202]);
    }

    [Fact]
    public void NoLoggedInOwnerCannotReadChangeOrClaimSavedPins()
    {
        var actor = Npc();
        var appearance = Appearance(11);
        var config = new Configuration
        {
            PinnedOutfits = [PinnedOutfitConfiguration.Create(actor, appearance)],
            PinnedOutfitsByCharacter = new() { [101] = [PinnedOutfitConfiguration.Create(actor, appearance)] },
        };
        var before = JsonConvert.SerializeObject(config);
        Assert.False(config.MigrateLegacyPins(0));
        Assert.Equal(before, JsonConvert.SerializeObject(config));
        var saves = 0;
        var store = new PinnedOutfitStore(config, () => saves++, () => 0);
        Assert.Equal(0, store.Count);
        Assert.False(store.IsPinned(actor));
        Assert.False(store.TryGetAppearance(actor, out _));
        Assert.False(store.TryGet(actor, out _));
        Assert.Empty(store.GetPinnedActorKeysForDisplay([actor]));
        Assert.Equal((0, 1), store.PinCurrent([actor], _ => throw new InvalidOperationException()));
        store.Pin(actor, EquipmentDisplayFormatting.CreateHumanOutfit(Appearance(22))!);
        foreach (var update in Updates(actor)) update(store);
        Assert.False(store.Unpin(actor));
        Assert.Equal(0, store.UnpinAll());
        Assert.Equal(0, saves);
        Assert.Equal(before, JsonConvert.SerializeObject(config));

        var knownOwner = new PinnedOutfitStore(config, () => saves++, () => 202);
        Assert.False(knownOwner.IsPinned(actor));
        Assert.Equal(0, knownOwner.Count);
        Assert.Equal(before, JsonConvert.SerializeObject(config));
    }

    [Fact]
    public void LegacyPinsMoveOnceToTheSpecifiedCharacterAndKeepExistingCharacterPins()
    {
        var actors = new[] { Npc(), Npc(202), Npc(203) };
        var existing = PinnedOutfitConfiguration.Create(actors[0], Appearance(22));
        var config = new Configuration
        {
            PinnedOutfits = actors.Select(actor => PinnedOutfitConfiguration.Create(actor, Appearance(11))).ToList(),
            PinnedOutfitsByCharacter = new()
            {
                [101] = [existing],
                [202] = [PinnedOutfitConfiguration.Create(actors[0], Appearance(33))],
            },
        };
        var otherBefore = JsonConvert.SerializeObject(config.PinnedOutfitsByCharacter[202]);
        Assert.True(config.MigrateLegacyPins(101));
        Assert.Empty(config.PinnedOutfits);
        Assert.Equal(3, config.PinnedOutfitsByCharacter[101].Count);
        Assert.Contains(existing, config.PinnedOutfitsByCharacter[101]);
        Assert.Equal(otherBefore, JsonConvert.SerializeObject(config.PinnedOutfitsByCharacter[202]));

        var loaded = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(config))!;
        var beforeRepeatedMigration = JsonConvert.SerializeObject(loaded);
        Assert.False(loaded.MigrateLegacyPins(101));
        Assert.False(loaded.MigrateLegacyPins(202));
        Assert.Equal(beforeRepeatedMigration, JsonConvert.SerializeObject(loaded));
        var store = new PinnedOutfitStore(loaded, () => { }, () => 101);
        foreach (var actor in actors)
        {
            Assert.True(store.TryGetAppearance(actor, out var actual));
            Assert.True(PinnedOutfitStore.AppearanceEquals(actor == actors[0] ? Appearance(22) : Appearance(11), actual));
        }
        var otherStore = new PinnedOutfitStore(loaded, () => { }, () => 202);
        Assert.Equal(1, otherStore.Count);
        Assert.True(otherStore.TryGetAppearance(actors[0], out var other));
        Assert.True(PinnedOutfitStore.AppearanceEquals(Appearance(33), other));
    }

    [Fact]
    public void EveryPinEditChangesOnlyTheLoggedInCharactersAppearance()
    {
        var actor = Npc();
        var original = Appearance(11);
        foreach (var update in Updates(actor))
        {
            ulong owner = 101;
            var saves = 0;
            var config = new Configuration();
            var store = new PinnedOutfitStore(config, () => saves++, () => owner);
            store.PinCurrent([actor], _ => original);
            owner = 202;
            store.PinCurrent([actor], _ => original);
            owner = 101;
            update(store);
            Assert.Equal(3, saves);
            Assert.True(store.TryGetAppearance(actor, out var changed));
            Assert.False(PinnedOutfitStore.AppearanceEquals(original, changed));
            owner = 202;
            Assert.True(store.TryGetAppearance(actor, out var retained));
            Assert.True(PinnedOutfitStore.AppearanceEquals(original, retained));
        }
    }

    [Fact]
    public void OnePinOperationRetainsItsOwnerWhenCaptureChangesTheCurrentCharacter()
    {
        ulong owner = 101;
        var reads = 0;
        var config = new Configuration();
        var store = new PinnedOutfitStore(config, () => { }, () => { reads++; return owner; });
        Assert.Equal((1, 0), store.PinCurrent([Npc()], _ => { owner = 202; return Appearance(11); }));
        Assert.Equal(1, reads);
        Assert.Single(config.PinnedOutfitsByCharacter[101]);
        Assert.False(config.PinnedOutfitsByCharacter.ContainsKey(202));
    }

    private static Action<PinnedOutfitStore>[] Updates(ActorEntry actor)
    {
        var applied = EquipmentDisplayFormatting.CreateHumanOutfit(Appearance(22))! with { HatVisible = false };
        return
        [
            store => store.UpdateSelectedEquipment(actor, new EquipmentChoiceKey(0, 22, 0), applied),
            store => store.UpdateEquipment(actor, EquipmentCopyData.FromOutfit(applied)),
            store => store.UpdateCustomize(actor, Appearance(11).Customize.SetItem(0, 2)),
            store => store.UpdateSelectedWeapon(actor, false, 99),
            store => store.UpdateHeadgearSettings(actor, applied),
        ];
    }

    private static ActorEntry Npc(ushort index = 201)
    {
        var key = new LogicalActorKey(index, 1001, 0xE0000000, 1003782, ObjectKind.EventNpc, 1187)
        { Lifetime = 1, Continuity = new(ObjectKind.EventNpc, 5, 1001, 1003782, 1187) };
        var snapshot = new ActorSnapshot(key, new(index, 1001, 0xE0000000, false, 1187),
            "NPC", ObjectKind.EventNpc, key.BaseId, 1, 1, 0, 1, 0, 90, false)
        { ContinuityKey = key.Continuity };
        return new ActorEntry(key, "NPC", ObjectKind.EventNpc, false, [snapshot]);
    }

    private static AppearanceData Appearance(ulong equipment)
        => new(0, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
            ImmutableArray.CreateRange(new byte[26]),
            ImmutableArray.CreateRange(Enumerable.Repeat(equipment, 10)), 1, 0, 0, false, 0, true);
}
