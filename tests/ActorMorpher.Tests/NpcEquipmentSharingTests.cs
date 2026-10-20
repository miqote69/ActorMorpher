using System.Collections.Immutable;
using System.Linq;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using Dalamud.Game.ClientState.Objects.Enums;
using Newtonsoft.Json;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class NpcEquipmentSharingTests
{
    private static readonly NpcEquipmentGroup Group = new(ObjectKind.EventNpc, 100, [100, 101, 102]);
    private static NpcEquipmentGroup? Resolve(ObjectKind kind, uint id)
        => kind == Group.Kind && Group.BaseIds.Contains(id) ? Group : null;

    [Fact]
    public void EquipmentEditSharesToOtherIdsWithoutCreatingPinsOrRepeatingAnUnpinnedAttempt()
    {
        var config = new Configuration();
        var saves = 0;
        var store = new PinnedOutfitStore(config, () => saves++, () => 1, Resolve);
        var source = Actor(100, 1);
        var other = Actor(101, 2);
        var equipment = Equipment(50);
        Assert.True(store.ShareNpcEquipment(source, equipment));
        Assert.Equal(0, store.Count);
        Assert.Equal(0, saves);
        Assert.Empty(config.PinnedOutfitsByCharacter);
        Assert.False(store.TryGetSharedNpcEquipment(source, out _));
        Assert.True(store.TryGetSharedNpcEquipment(other, out var shared));
        Assert.Same(equipment, shared);
        Assert.False(store.IsPinned(other));
        Assert.False(store.IsNpcEquipmentSharingCandidate(Actor(999, 3)));
        store.MarkSharedNpcEquipmentAttempted(other);
        Assert.False(store.TryGetSharedNpcEquipment(other, out _));
        Assert.True(store.ShareNpcEquipment(source, Equipment(60)));
        Assert.True(store.TryGetSharedNpcEquipment(other, out var changed));
        Assert.Equal((ushort)60, changed.Outfit.Equipment[1].Set);
    }

    [Fact]
    public void LatestUserEquipmentAlignsIndividualPinsButKeepsCustomizationAndIdentitySeparate()
    {
        var config = new Configuration();
        var store = new PinnedOutfitStore(config, () => { }, () => 1, Resolve);
        var first = Actor(100, 1);
        var second = Actor(101, 2);
        var firstAppearance = Appearance(10, 20);
        var secondAppearance = Appearance(11, 30);
        store.PinCurrent([first], _ => firstAppearance);
        store.PinCurrent([second], _ => secondAppearance);
        var sharedEquipment = Equipment(80);
        Assert.True(store.ShareNpcEquipment(second, sharedEquipment));
        Assert.True(store.TryGetAppearance(first, out var firstPin));
        Assert.True(store.TryGetAppearance(second, out var secondPin));
        Assert.Equal(firstAppearance, firstPin with
        {
            Customize = firstAppearance.Customize,
            Equipment = firstAppearance.Equipment, ColoredEquipment = firstAppearance.ColoredEquipment,
            Mainhand = firstAppearance.Mainhand, Offhand = firstAppearance.Offhand,
        });
        Assert.Equal(secondAppearance.Customize, secondPin.Customize);
        Assert.Equal(firstPin.Equipment, secondPin.Equipment);
        Assert.Equal((ushort)80, new FFXIVClientStructs.FFXIV.Client.Game.Character.EquipmentModelId
            { Value = firstPin.Equipment[1] }.Id);
        Assert.True(store.IsPinned(first));
        Assert.True(store.IsPinned(second));
        Assert.False(store.IsPinned(Actor(100, 20)));
        Assert.Equal(2, store.Count);
    }

    [Fact]
    public void SharedPinnedEquipmentSurvivesSerializationForOnlyItsPlayerCharacter()
    {
        ulong owner = 1;
        var config = new Configuration();
        var store = new PinnedOutfitStore(config, () => { }, () => owner, Resolve);
        var source = Actor(100, 1);
        store.PinCurrent([source], _ => Appearance(10, 20));
        store.ShareNpcEquipment(source, Equipment(80));
        var restored = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(config))!;
        var restarted = new PinnedOutfitStore(restored, () => { }, () => owner, Resolve);
        var laterId = Actor(102, 50);
        Assert.True(restarted.TryGetSharedNpcEquipment(laterId, out var shared));
        Assert.Equal((ushort)80, shared.Outfit.Equipment[1].Set);
        Assert.False(restarted.IsPinned(laterId));
        owner = 2;
        Assert.False(restarted.HasSharedNpcEquipment);
        Assert.False(restarted.TryGetSharedNpcEquipment(laterId, out _));
        owner = 0;
        Assert.False(restarted.ShareNpcEquipment(source, Equipment(90)));
        Assert.Single(restored.PinnedOutfitsByCharacter);
        owner = 1;
        Assert.True(restarted.TryGetSharedNpcEquipment(laterId, out shared));
        Assert.Equal((ushort)80, shared.Outfit.Equipment[1].Set);
    }

    [Fact]
    public void PinningAnotherIdSharesTheAppearanceCapturedByThatPinOperation()
    {
        var config = new Configuration();
        var store = new PinnedOutfitStore(config, () => { }, () => 1, Resolve);
        store.ShareNpcEquipment(Actor(100, 1), Equipment(80));
        var other = Actor(101, 2);
        var appearance = Appearance(11, 30);
        store.PinCurrent([other], _ => appearance);
        Assert.True(store.TryGetAppearance(other, out var pin));
        Assert.Equal(appearance.Customize, pin.Customize);
        Assert.Equal((ushort)30, new FFXIVClientStructs.FFXIV.Client.Game.Character.EquipmentModelId
            { Value = pin.Equipment[1] }.Id);
    }

    [Fact]
    public void ReadingOldIndividualPinsAutomaticallyAlignsAndSavesTheirGroupOnce()
    {
        var first = Actor(100, 1);
        var second = Actor(101, 2);
        var config = new Configuration
        {
            PinnedOutfitsByCharacter = new()
            {
                [1] = [PinnedOutfitConfiguration.Create(first, Appearance(10, 20)),
                    PinnedOutfitConfiguration.Create(second, Appearance(11, 30))],
            },
        };
        var saves = 0;
        var definitionReads = 0;
        var store = new PinnedOutfitStore(config, () => saves++, () => 1,
            (kind, id) => { definitionReads++; return Resolve(kind, id); });
        Assert.True(store.HasSharedNpcEquipment);
        Assert.True(store.TryGetSharedNpcEquipment(first, out var initial));
        Assert.True(store.TryGetAppearance(first, out _));
        Assert.All(config.PinnedOutfitsByCharacter[1], pin =>
            Assert.Equal<ulong>(initial.Equipment, pin.Appearance!.Equipment));
        Assert.Equal(1, saves);
        Assert.True(definitionReads > 0);
        Assert.True(store.HasSharedNpcEquipment);
        Assert.Equal(1, saves);
        Assert.True(store.ShareNpcEquipment(second, Equipment(80)));
        Assert.Equal(2, saves);
        Assert.True(store.TryGetSharedNpcEquipment(first, out var shared));
        Assert.Equal((ushort)80, shared.Outfit.Equipment[1].Set);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PinningWithoutAnEquipmentEditAutomaticallyAlignsAllGroupPins(bool batch)
    {
        var first = Actor(100, 1);
        var second = Actor(101, 2);
        var third = Actor(102, 3);
        var config = new Configuration
        {
            PinnedOutfitsByCharacter = new()
            {
                [1] = [PinnedOutfitConfiguration.Create(first, Appearance(10, 20)),
                    PinnedOutfitConfiguration.Create(second, Appearance(11, 30)),
                    PinnedOutfitConfiguration.Create(third, Appearance(12, 40))],
            },
        };
        var definitionReads = 0;
        var store = new PinnedOutfitStore(config, () => { }, () => 1,
            (kind, id) => { definitionReads++; return Resolve(kind, id); });
        store.PinCurrent(batch ? [first, second] : [first], actor => Appearance(15, actor == first ? (ushort)80 : (ushort)81));
        Assert.True(store.HasSharedNpcEquipment);
        Assert.True(definitionReads > 0);
        Assert.All(config.PinnedOutfitsByCharacter[1], pin => Assert.True(pin.ShareNpcEquipment));
        Assert.True(store.TryGet(second, out var secondOutfit));
        Assert.Equal(batch ? (ushort)81 : (ushort)80, secondOutfit.Equipment[1].Set);
        Assert.True(store.TryGet(third, out var thirdOutfit));
        Assert.Equal(secondOutfit.Equipment[1].Set, thirdOutfit.Equipment[1].Set);
    }

    [Theory]
    [InlineData("legacy-only")]
    [InlineData("legacy-first")]
    [InlineData("full-first")]
    public void LegacyPinsKeepSharedWeaponsAndDyesAcrossRestartWithoutPinningCustomization(string order)
    {
        var first = Actor(100, 1);
        var second = Actor(101, 2);
        var config = new Configuration();
        var store = new PinnedOutfitStore(config, () => { }, () => 1, Resolve);
        var original = Appearance(10, 20);
        if (order == "full-first") store.PinCurrent([second], _ => Appearance(11, 30));
        store.Pin(first, EquipmentDisplayFormatting.CreateHumanOutfit(original)!);
        if (order == "legacy-first") store.PinCurrent([second], _ => Appearance(11, 30));
        var desired = Appearance(1, 80) with
        {
            Mainhand = 0x1122000100020003UL, Offhand = 0x3344000400050006UL,
            MainhandDyes = new(new DyeColor(.1f, .2f, .3f) { Metallic = true }, null),
            OffhandDyes = new(null, new DyeColor(.4f, .5f, .6f) { Metallic = false }),
        };
        Assert.True(EquipmentCopyData.TryCreate(desired, out var edited));
        Assert.True(store.ShareNpcEquipment(first, edited));
        var loaded = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(config))!;
        var restarted = new PinnedOutfitStore(loaded, () => { }, () => 1, Resolve);
        Assert.True(restarted.TryGetSharedNpcEquipment(Actor(102, 30), out var shared));
        Assert.True(shared.IncludesWeapons);
        Assert.Equal(desired.Mainhand, shared.Mainhand);
        Assert.Equal(desired.Offhand, shared.Offhand);
        Assert.Equal(desired.MainhandDyes, shared.MainhandDyes);
        Assert.Equal(desired.OffhandDyes, shared.OffhandDyes);
        Assert.False(restarted.TryGetAppearance(first, out _));
        Assert.True(restarted.TryGet(first, out var outfit));
        Assert.Equal((ushort)80, outfit.Equipment[1].Set);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnpinStopsTheWholeGroupAcrossRestartButKeepsOtherPinsGroupsAndOwners(bool loadBeforeUnpin)
    {
        ulong owner = 1;
        var otherGroup = new NpcEquipmentGroup(ObjectKind.EventNpc, 200, [200, 201]);
        NpcEquipmentGroup? ResolveBoth(ObjectKind kind, uint id)
            => Resolve(kind, id) ?? (kind == otherGroup.Kind && otherGroup.BaseIds.Contains(id) ? otherGroup : null);
        var config = new Configuration();
        var store = new PinnedOutfitStore(config, () => { }, () => owner, ResolveBoth);
        var first = Actor(100, 1);
        var second = Actor(101, 2);
        var unrelated = Actor(200, 3);
        store.PinCurrent([first, unrelated], _ => Appearance(10, 20));
        store.Pin(second, Equipment(30).Outfit);
        store.ShareNpcEquipment(first, Equipment(80));
        store.ShareNpcEquipment(unrelated, Equipment(70));
        owner = 2;
        store.PinCurrent([first], _ => Appearance(11, 40));
        store.ShareNpcEquipment(first, Equipment(60));
        var otherOwnerBefore = JsonConvert.SerializeObject(config.PinnedOutfitsByCharacter[2]);
        config = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(config))!;
        owner = 1;
        var saves = 0;
        store = new(config, () => saves++, () => owner, ResolveBoth);
        if (loadBeforeUnpin) Assert.True(store.HasSharedNpcEquipment);

        Assert.True(store.Unpin(first));
        Assert.Equal(1, saves);
        Assert.False(store.IsPinned(first));
        Assert.True(store.IsPinned(second));
        Assert.True(store.IsPinned(unrelated));
        Assert.Equal(2, store.Count);
        Assert.True(store.TryGet(second, out var retained));
        Assert.Equal((ushort)80, retained.Equipment[1].Set);
        Assert.False(store.TryGetSharedNpcEquipment(Actor(102, 30), out _));
        Assert.True(store.TryGetSharedNpcEquipment(Actor(201, 31), out var otherShared));
        Assert.Equal((ushort)70, otherShared.Outfit.Equipment[1].Set);
        var remaining = config.PinnedOutfitsByCharacter[1].Single(pin => pin.BaseId == second.BaseId);
        Assert.False(remaining.ShareNpcEquipment);
        Assert.Null(remaining.SharedWeapons);
        Assert.Equal(otherOwnerBefore, JsonConvert.SerializeObject(config.PinnedOutfitsByCharacter[2]));

        var reloaded = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(config))!;
        store = new(reloaded, () => { }, () => owner, ResolveBoth);
        Assert.False(store.TryGetSharedNpcEquipment(Actor(102, 30), out _));
        Assert.True(store.IsPinned(second));
        Assert.True(store.TryGetSharedNpcEquipment(Actor(201, 31), out _));
        owner = 2;
        Assert.True(store.IsPinned(first));
        Assert.True(store.TryGetSharedNpcEquipment(Actor(102, 30), out var retainedOtherOwner));
        Assert.Equal((ushort)60, retainedOtherOwner.Outfit.Equipment[1].Set);
    }

    [Fact]
    public void UnpinAllAlsoStopsTransientSharingWithoutChangingAnotherOwnerOrSavingEmptyPins()
    {
        ulong owner = 1;
        var saves = 0;
        var store = new PinnedOutfitStore(new Configuration(), () => saves++, () => owner, Resolve);
        store.ShareNpcEquipment(Actor(100, 1), Equipment(80));
        owner = 2;
        store.ShareNpcEquipment(Actor(100, 1), Equipment(60));
        owner = 1;
        Assert.Equal(0, store.UnpinAll());
        Assert.False(store.HasSharedNpcEquipment);
        Assert.Equal(0, saves);
        owner = 2;
        Assert.True(store.TryGetSharedNpcEquipment(Actor(102, 30), out var shared));
        Assert.Equal((ushort)60, shared.Outfit.Equipment[1].Set);
    }

    private static ActorEntry Actor(uint baseId, ushort index)
    {
        var key = new LogicalActorKey(index, (ulong)(1000 + index), (uint)(2000 + index), baseId, ObjectKind.EventNpc, 282);
        var snapshot = new ActorSnapshot(key, new(index, key.GameObjectId, key.EntityId, false, 282),
            "パンプキンヘッドの女", ObjectKind.EventNpc, baseId, 0, 1, 1, 1, 0, 0, false);
        return new(key, snapshot.Name, snapshot.ObjectKind, false, [snapshot]);
    }

    private static AppearanceData Appearance(byte face, ushort body)
    {
        var customize = Enumerable.Repeat((byte)1, 26).ToImmutableArray().SetItem(5, face);
        var equipment = Enumerable.Repeat(new FFXIVClientStructs.FFXIV.Client.Game.Character.EquipmentModelId
            { Id = body, Variant = 1 }.Value, 10);
        return AppearanceData.Create(0, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
            customize, equipment, 1, 0, 0, false, 0, true);
    }

    private static EquipmentCopyData Equipment(ushort body)
    {
        Assert.True(EquipmentCopyData.TryCreate(Appearance(1, body), out var data));
        return data;
    }
}
