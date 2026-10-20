using System.Collections.Immutable;
using System.Linq;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using Dalamud.Game.ClientState.Objects.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class NpcAutomaticSharingTests
{
    private static readonly NpcEquipmentGroup Group = new(ObjectKind.EventNpc, 100, [100, 101, 102]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OldFullPinSharesEquipmentAndCustomizeWithoutEditingOrPinningOtherRepresentations(bool omitFlags)
    {
        var source = Actor(100, 1);
        var saved = Appearance(7, 80) with { ModelCharaId = 123, ModelScale = 1.7f };
        var config = OldConfiguration(source, saved, omitFlags);
        var originalIdentity = Assert.Single(config.PinnedOutfitsByCharacter[1]).IdentityKey();
        var store = new PinnedOutfitStore(config, () => { }, () => 1, Resolve);

        foreach (var target in new[] { Actor(101, 2), Actor(100, 201) })
        {
            AssertShared(store, target, saved);
            Assert.False(store.IsPinned(target));
            Assert.False(store.TryGetAppearance(target, out _));
            var rendered = Appearance(2, 30) with { ModelCharaId = 456, ModelScale = 0.8f };
            Assert.True(store.TryGetSharedNpcEquipment(target, out var equipment));
            Assert.True(store.TryGetSharedNpcCustomize(target, out var customize));
            var desired = Plugin.ComposeSharedNpcAppearance(rendered, null, null, equipment, customize);
            Assert.Equal(rendered.ModelCharaId, desired.ModelCharaId);
            Assert.Equal(rendered.ModelScale, desired.ModelScale);
            Assert.Equal<byte>(saved.Customize, desired.Customize);
            Assert.Equal<ulong>(saved.Equipment, desired.Equipment);
        }

        Assert.Equal(1, store.Count);
        Assert.True(store.IsPinned(source));
        Assert.Equal(originalIdentity, Assert.Single(config.PinnedOutfitsByCharacter[1]).IdentityKey());
    }

    [Fact]
    public void OldPinsForTheSameNpcStaySeparateByPlayerContentIdAcrossReloads()
    {
        var source = Actor(100, 1);
        var first = Appearance(7, 80);
        var second = Appearance(8, 90);
        var config = OldConfiguration(source, first, true);
        config.PinnedOutfitsByCharacter[2] = [PinnedOutfitConfiguration.Create(source, second)];
        ulong owner = 1;
        for (var reload = 0; reload < 2; reload++)
        {
            config = RoundTrip(config);
            var store = new PinnedOutfitStore(config, () => { }, () => owner, Resolve);
            owner = 1;
            AssertShared(store, Actor(102, 30), first);
            owner = 2;
            AssertShared(store, Actor(102, 30), second);
            owner = 3;
            Assert.False(store.TryGetSharedNpcEquipment(Actor(102, 30), out _));
            Assert.False(store.TryGetSharedNpcCustomize(Actor(102, 30), out _));
            Assert.Equal(0, store.Count);
            owner = 1;
            AssertShared(store, Actor(102, 30), first);
            Assert.Single(config.PinnedOutfitsByCharacter[1]);
            Assert.Single(config.PinnedOutfitsByCharacter[2]);
            Assert.Equal(2, config.PinnedOutfitsByCharacter.Count);
        }
    }

    [Fact]
    public void OldOutfitOnlyPinSharesItsSavedGearWithoutInventingCustomizeWeaponsOrModelScope()
    {
        var source = Actor(100, 1);
        var saved = EquipmentDisplayFormatting.CreateHumanOutfit(Appearance(7, 80))!;
        var config = RoundTrip(new Configuration
        {
            PinnedOutfitsByCharacter = new() { [1] = [PinnedOutfitConfiguration.Create(source, saved)] },
        });
        var store = new PinnedOutfitStore(config, () => { }, () => 1, Resolve);
        var target = Actor(101, 2);
        Assert.True(store.TryGetSharedNpcEquipment(target, out var shared));
        Assert.False(shared.IncludesWeapons);
        Assert.False(store.TryGetSharedNpcCustomize(target, out _));
        Assert.False(store.TryGetAppearance(source, out _));
        Assert.False(store.IsPinned(target));
        var rendered = Appearance(2, 30) with { ModelCharaId = 456, ModelScale = 0.8f };
        var desired = Plugin.ComposeSharedNpcAppearance(rendered, null, null, shared, null);
        Assert.Equal<ArmorAppearance>(saved.Equipment,
            EquipmentDisplayFormatting.CreateHumanOutfit(desired)!.Equipment);
        Assert.Equal<byte>(rendered.Customize, desired.Customize);
        Assert.Equal<CustomizeColorOverride>(rendered.CustomizeColors, desired.CustomizeColors);
        Assert.Equal(rendered.Mainhand, desired.Mainhand);
        Assert.Equal(rendered.Offhand, desired.Offhand);
        Assert.Equal(rendered.MainhandDyes, desired.MainhandDyes);
        Assert.Equal(rendered.OffhandDyes, desired.OffhandDyes);
        Assert.Equal(rendered.ModelCharaId, desired.ModelCharaId);
        Assert.Equal(rendered.ModelScale, desired.ModelScale);
        Assert.Null(Assert.Single(config.PinnedOutfitsByCharacter[1]).Appearance);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MixedOldPinsShareTheFirstSavedWeaponsAndDyesInEitherOrderAcrossRestart(bool fullFirst)
    {
        var legacyActor = Actor(100, 1);
        var firstFullActor = Actor(101, 2);
        var secondFullActor = Actor(102, 3);
        var legacyAppearance = Appearance(7, 80);
        var legacyOutfit = EquipmentDisplayFormatting.CreateHumanOutfit(legacyAppearance)! with
        {
            Facewear = new(false, 0),
            VieraEarsHidden = null,
        };
        var firstFull = Appearance(8, 90);
        var secondFull = Appearance(9, 100) with
        {
            MainhandDyes = new(null, new DyeColor(.7f, .8f, .9f)),
            OffhandDyes = new(new DyeColor(.6f, .5f, .4f), null),
        };
        var legacyPin = PinnedOutfitConfiguration.Create(legacyActor, legacyOutfit);
        var firstFullPin = PinnedOutfitConfiguration.Create(firstFullActor, firstFull);
        var secondFullPin = PinnedOutfitConfiguration.Create(secondFullActor, secondFull);
        var config = new Configuration
        {
            PinnedOutfitsByCharacter = new()
            {
                [1] = fullFirst ? [firstFullPin, legacyPin, secondFullPin]
                    : [legacyPin, firstFullPin, secondFullPin],
            },
        };
        var expected = firstFull with { Equipment = fullFirst ? firstFull.Equipment : legacyAppearance.Equipment };
        var target = Actor(102, 201);
        for (var reload = 0; reload < 2; reload++)
        {
            config = RoundTrip(config);
            var store = new PinnedOutfitStore(config, () => { }, () => 1, Resolve);
            AssertShared(store, target, expected);
            Assert.True(store.TryGetSharedNpcEquipment(target, out var shared));
            Assert.True(shared.IncludesWeapons);
            Assert.False(store.IsPinned(target));
            Assert.Equal(3, store.Count);
            foreach (var actor in new[] { firstFullActor, secondFullActor })
            {
                Assert.True(store.TryGetAppearance(actor, out var aligned));
                Assert.Equal(firstFull.Mainhand, aligned.Mainhand);
                Assert.Equal(firstFull.Offhand, aligned.Offhand);
                Assert.Equal(firstFull.MainhandDyes, aligned.MainhandDyes);
                Assert.Equal(firstFull.OffhandDyes, aligned.OffhandDyes);
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutfitOnlyPinOrExplicitEditUpdatesArmorWhileRetainingSharedWeaponsDyesAndUnspecifiedHeadSettings(bool explicitEdit)
    {
        var source = Actor(100, 1);
        var legacyActor = Actor(101, 2);
        var full = Appearance(7, 80) with { FacewearModelId = 42, VieraEarsHidden = true };
        var replacement = Appearance(8, 90);
        var outfit = EquipmentDisplayFormatting.CreateHumanOutfit(replacement)! with
        {
            Facewear = new(false, 0),
            VieraEarsHidden = null,
        };
        var config = new Configuration();
        var store = new PinnedOutfitStore(config, () => { }, () => 1, Resolve);
        Assert.Equal((1, 0), store.PinCurrent([source], _ => full));
        if (explicitEdit)
            Assert.True(store.ShareNpcEquipment(legacyActor, EquipmentCopyData.FromOutfit(outfit)));
        else
            store.Pin(legacyActor, outfit);

        var expected = full with { Equipment = replacement.Equipment };
        for (var reload = 0; reload < 2; reload++)
        {
            if (reload != 0)
            {
                config = RoundTrip(config);
                store = new(config, () => { }, () => 1, Resolve);
            }
            AssertShared(store, Actor(102, 201), expected);
            Assert.True(store.TryGetAppearance(source, out var retained));
            Assert.Equal<ulong>(replacement.Equipment, retained.Equipment);
            Assert.Equal(full.Mainhand, retained.Mainhand);
            Assert.Equal(full.Offhand, retained.Offhand);
            Assert.Equal(full.MainhandDyes, retained.MainhandDyes);
            Assert.Equal(full.OffhandDyes, retained.OffhandDyes);
            Assert.Equal(full.FacewearModelId, retained.FacewearModelId);
            Assert.Equal(full.VieraEarsHidden, retained.VieraEarsHidden);
            Assert.False(store.TryGetAppearance(legacyActor, out _));
            Assert.Equal(explicitEdit ? 1 : 2, store.Count);
        }
    }

    [Theory]
    [InlineData(false, "pin")]
    [InlineData(true, "pin")]
    [InlineData(false, "equipment")]
    [InlineData(false, "customize")]
    public void UnpinStopsAutomaticSharingAfterRestartAndANewExplicitActionCanResumeIt(bool loadBeforeUnpin, string resume)
    {
        var first = Actor(100, 1);
        var second = Actor(101, 2);
        var saved = Appearance(7, 80);
        var config = OldConfiguration(first, saved, true);
        config.PinnedOutfitsByCharacter[1].Add(PinnedOutfitConfiguration.Create(second, saved));
        config = RoundTrip(config);
        var store = new PinnedOutfitStore(config, () => { }, () => 1, Resolve);
        if (loadBeforeUnpin) AssertShared(store, Actor(102, 3), saved);

        Assert.True(store.Unpin(first));
        config = RoundTrip(config);
        store = new(config, () => { }, () => 1, Resolve);
        Assert.False(store.TryGetSharedNpcEquipment(Actor(102, 3), out _));
        Assert.False(store.TryGetSharedNpcCustomize(Actor(102, 3), out _));
        Assert.False(store.IsPinned(first));
        Assert.True(store.IsPinned(second));
        Assert.Equal(1, store.Count);
        Assert.True(store.TryGetAppearance(second, out var retained));
        Assert.True(PinnedOutfitStore.AppearanceEquals(saved, retained));

        var edited = Appearance(8, 90);
        if (resume == "pin")
            Assert.Equal((1, 0), store.PinCurrent([first], _ => edited));
        else if (resume == "equipment")
        {
            Assert.True(EquipmentCopyData.TryCreate(edited, out var equipment));
            Assert.True(store.ShareNpcEquipment(second, equipment));
        }
        else
        {
            Assert.True(CustomizeCopyData.TryCreate(edited, out var customize));
            Assert.True(store.ShareNpcCustomize(second, customize));
        }

        store = new(RoundTrip(config), () => { }, () => 1, Resolve);
        if (resume is "pin" or "equipment")
        {
            Assert.True(store.TryGetSharedNpcEquipment(Actor(102, 3), out var shared));
            Assert.Equal<ulong>(edited.Equipment, shared.Equipment);
        }
        if (resume is "pin" or "customize")
        {
            Assert.True(store.TryGetSharedNpcCustomize(Actor(102, 3), out var shared));
            Assert.Equal<byte>(edited.Customize, shared.Customize);
        }
        Assert.True(store.IsPinned(second));
        Assert.False(store.IsPinned(Actor(102, 3)));
    }

    [Theory]
    [InlineData("customize")]
    [InlineData("equipment")]
    [InlineData("pin")]
    public void ResumingStoppedMixedPinsRetainsComponentsSavedOnTheRemainingFullPin(string resume)
    {
        var legacyActor = Actor(100, 1);
        var fullActor = Actor(101, 2);
        var removedActor = Actor(102, 3);
        var saved = Appearance(7, 80);
        var config = RoundTrip(new Configuration
        {
            PinnedOutfitsByCharacter = new()
            {
                [1] = [PinnedOutfitConfiguration.Create(legacyActor,
                        EquipmentDisplayFormatting.CreateHumanOutfit(saved)!),
                    PinnedOutfitConfiguration.Create(fullActor, saved),
                    PinnedOutfitConfiguration.Create(removedActor, saved)],
            },
        });
        var store = new PinnedOutfitStore(config, () => { }, () => 1, Resolve);
        var target = Actor(101, 201);
        AssertShared(store, target, saved);
        Assert.True(store.Unpin(removedActor));
        Assert.False(store.TryGetSharedNpcEquipment(target, out _));
        Assert.False(store.TryGetSharedNpcCustomize(target, out _));
        Assert.True(store.IsPinned(legacyActor));
        Assert.True(store.TryGetAppearance(fullActor, out var retained));
        Assert.True(PinnedOutfitStore.AppearanceEquals(saved, retained));

        var edited = Appearance(9, 90);
        AppearanceData expected;
        if (resume == "customize")
        {
            Assert.True(CustomizeCopyData.TryCreate(edited, out var customize));
            Assert.True(store.ShareNpcCustomize(fullActor, customize));
            expected = saved with { Customize = edited.Customize, CustomizeColors = edited.CustomizeColors };
        }
        else
        {
            var outfit = EquipmentDisplayFormatting.CreateHumanOutfit(edited)!;
            if (resume == "equipment")
                Assert.True(store.ShareNpcEquipment(fullActor, EquipmentCopyData.FromOutfit(outfit)));
            else
                store.Pin(removedActor, outfit);
            expected = saved with { Equipment = edited.Equipment };
        }

        for (var reload = 0; reload < 2; reload++)
        {
            if (reload != 0)
            {
                config = RoundTrip(config);
                store = new(config, () => { }, () => 1, Resolve);
            }
            AssertShared(store, target, expected);
            Assert.False(store.IsPinned(target));
            Assert.Equal(resume == "pin" ? 3 : 2, store.Count);
        }
    }

    [Fact]
    public void AutomaticSharingDoesNotAffectUnknownNpcPlayersOrCompanions()
    {
        var saved = Appearance(7, 80);
        var config = OldConfiguration(Actor(100, 1), saved, true);
        var unrelated = new[] { Actor(999, 2), Actor(100, 3, ObjectKind.Pc), Actor(100, 4, ObjectKind.Companion) };
        var individual = Appearance(2, 30);
        foreach (var actor in unrelated)
            config.PinnedOutfitsByCharacter[1].Add(PinnedOutfitConfiguration.Create(actor, individual));
        config = RoundTrip(config);
        var store = new PinnedOutfitStore(config, () => { }, () => 1, Resolve);
        AssertShared(store, Actor(101, 5), saved);
        foreach (var actor in unrelated)
        {
            Assert.False(store.IsNpcEquipmentSharingCandidate(actor));
            Assert.False(store.TryGetSharedNpcEquipment(actor, out _));
            Assert.False(store.TryGetSharedNpcCustomize(actor, out _));
            Assert.True(store.TryGetAppearance(actor, out var retained));
            Assert.True(PinnedOutfitStore.AppearanceEquals(individual, retained));
        }
        Assert.Equal(4, store.Count);
    }

    [Fact]
    public void AutomaticFullPinSharingPreservesYoungBodyTypeAndAllCustomizeBytes()
    {
        var young = Appearance(7, 80);
        young = young with { Customize = young.Customize.SetItem(2, 4).SetItem(3, 27).SetItem(23, 63) };
        var config = OldConfiguration(Actor(100, 1), young, true);
        var store = new PinnedOutfitStore(config, () => { }, () => 1, Resolve);
        var target = Actor(100, 201);
        AssertShared(store, target, young);
        Assert.True(store.TryGetSharedNpcEquipment(target, out var equipment));
        Assert.True(store.TryGetSharedNpcCustomize(target, out var customize));
        var desired = Plugin.ComposeSharedNpcAppearance(Appearance(2, 30), null, null, equipment, customize);
        Assert.Equal((byte)4, desired.Customize[2]);
        Assert.Equal<byte>(young.Customize, desired.Customize);
        Assert.Equal<CustomizeColorOverride>(young.CustomizeColors, desired.CustomizeColors);
    }

    private static Configuration OldConfiguration(ActorEntry actor, AppearanceData appearance, bool omitFlags)
    {
        var config = new Configuration
        {
            PinnedOutfitsByCharacter = new() { [1] = [PinnedOutfitConfiguration.Create(actor, appearance)] },
        };
        var json = JObject.Parse(JsonConvert.SerializeObject(config));
        var pin = (JObject)json["PinnedOutfitsByCharacter"]!["1"]![0]!;
        // Both historical JSON forms predate an explicit group-unpin marker.
        pin.Remove("NpcGroupSharingStopped");
        if (omitFlags)
        {
            pin.Remove("ShareNpcEquipment");
            pin.Remove("ShareNpcCustomize");
        }
        else
        {
            pin["ShareNpcEquipment"] = false;
            pin["ShareNpcCustomize"] = false;
        }
        var restored = JsonConvert.DeserializeObject<Configuration>(json.ToString())!;
        PinnedOutfitStore.Normalize(restored);
        return restored;
    }

    private static Configuration RoundTrip(Configuration config)
    {
        var restored = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(config))!;
        PinnedOutfitStore.Normalize(restored);
        return restored;
    }

    private static void AssertShared(PinnedOutfitStore store, ActorEntry target, AppearanceData expected)
    {
        Assert.True(store.TryGetSharedNpcEquipment(target, out var equipment));
        Assert.True(store.TryGetSharedNpcCustomize(target, out var customize));
        Assert.Equal<ulong>(expected.Equipment, equipment.Equipment);
        Assert.Equal(expected.Mainhand, equipment.Mainhand);
        Assert.Equal(expected.Offhand, equipment.Offhand);
        Assert.Equal(expected.MainhandDyes, equipment.MainhandDyes);
        Assert.Equal(expected.OffhandDyes, equipment.OffhandDyes);
        Assert.Equal<byte>(expected.Customize, customize.Customize);
        Assert.Equal<CustomizeColorOverride>(expected.CustomizeColors, customize.CustomizeColors);
    }

    private static NpcEquipmentGroup? Resolve(ObjectKind kind, uint id)
        => kind == Group.Kind && Group.BaseIds.Contains(id) ? Group : null;

    private static ActorEntry Actor(uint baseId, ushort index, ObjectKind kind = ObjectKind.EventNpc)
    {
        var key = new LogicalActorKey(index, (ulong)(1000 + index), (uint)(2000 + index), baseId, kind, 282);
        var snapshot = new ActorSnapshot(key, new(index, key.GameObjectId, key.EntityId, false, 282),
            "Same NPC", kind, baseId, 0, 1, 1, 1, 0, 0, false);
        return new(key, snapshot.Name, snapshot.ObjectKind, false, [snapshot]);
    }

    private static AppearanceData Appearance(byte face, ushort body)
        => AppearanceData.Create(0, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
            Enumerable.Repeat((byte)1, 26).ToImmutableArray().SetItem(5, face),
            Enumerable.Repeat(new FFXIVClientStructs.FFXIV.Client.Game.Character.EquipmentModelId
                { Id = body, Variant = 1 }.Value, 10), 1, 0x1122000100020003UL + body,
            0x3344000400050006UL + body, false, 0, true) with
        {
            CustomizeColors = [new(0, face / 10f, .2f, .3f), new(24, .4f, .5f, .6f)],
            MainhandDyes = new(new DyeColor(.1f, .2f, .3f) { Metallic = true }, null),
            OffhandDyes = new(null, new DyeColor(.4f, .5f, .6f) { Metallic = false }),
        };
}
