using System;
using System.Collections.Immutable;
using System.Linq;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using ActorMorpher.Interop;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class PinnedWeaponJobTests
{
    private const byte Pictomancer = 42;
    private const byte Paladin = 19;
    private const byte WhiteMage = 24;

    [Theory]
    [InlineData(Pictomancer, true)]
    [InlineData(Paladin, false)]
    public void FullPinRetainsWeaponsOnlyOnItsRecordedJobAcrossReload(byte currentJob, bool weaponsPinned)
    {
        var saved = Appearance(11, 0);
        var config = new Configuration();
        var store = Store(config);
        var source = Actor(Pictomancer, saved);
        Assert.Equal((1, 0), store.PinCurrent([source], _ => saved));
        Assert.Equal(Pictomancer, Assert.Single(config.PinnedOutfitsByCharacter[1]).WeaponClassJob);

        for (var reload = 0; reload < 2; reload++)
        {
            var current = Appearance(22, 33) with { MainhandDyes = new(new DyeColor(0, 0, 1), null) };
            var target = Actor(currentJob, current);
            Assert.True(store.TryGetAppearanceForCurrentJob(target, out var effective));
            Assert.Equal(weaponsPinned ? saved.Mainhand : current.Mainhand, effective.Mainhand);
            Assert.Equal(weaponsPinned ? saved.Offhand : current.Offhand, effective.Offhand);
            Assert.Equal(weaponsPinned ? saved.MainhandDyes : current.MainhandDyes, effective.MainhandDyes);
            Assert.Equal(weaponsPinned ? saved.OffhandDyes : current.OffhandDyes, effective.OffhandDyes);
            Assert.True(PinnedOutfitStore.AppearanceEquals(saved, effective with
            {
                Mainhand = saved.Mainhand, Offhand = saved.Offhand,
                MainhandDyes = saved.MainhandDyes, OffhandDyes = saved.OffhandDyes,
                PinnedWeaponScope = null,
            }));
            Assert.True(store.TryGetAppearance(target, out var stored));
            Assert.True(PinnedOutfitStore.AppearanceEquals(saved, stored)); // Reads never rewrite saved weapons.
            Assert.True(store.IsPinned(target));
            config = RoundTrip(config);
            store = Store(config);
        }
    }

    [Fact]
    public void ReturningToThePinnedJobAndRepinningUseTheCorrectWeapons()
    {
        var config = new Configuration();
        var store = Store(config);
        var saved = Appearance(11, 0);
        store.PinCurrent([Actor(Pictomancer, saved)], _ => saved);
        Assert.True(store.TryGetAppearanceForCurrentJob(Actor(Paladin, Appearance(22, 33)), out var other));
        Assert.Equal(22UL, other.Mainhand);
        Assert.True(store.TryGetAppearanceForCurrentJob(Actor(Pictomancer, Appearance(44, 0)), out var returned));
        Assert.Equal(saved.Mainhand, returned.Mainhand);
        var paladin = Appearance(55, 66);
        store.PinCurrent([Actor(Paladin, paladin)], _ => paladin);
        Assert.Equal(Paladin, Assert.Single(config.PinnedOutfitsByCharacter[1]).WeaponClassJob);
        Assert.True(store.TryGetAppearanceForCurrentJob(Actor(Paladin, Appearance(77, 88)), out var repinned));
        Assert.Equal(paladin.Mainhand, repinned.Mainhand);
        Assert.Equal(paladin.Offhand, repinned.Offhand);
    }

    [Theory]
    [InlineData(Pictomancer)]
    [InlineData(Paladin)]
    public void ExistingPlayerPinsWithoutARecordedJobKeepTheirModelAndArmorButNotWeapons(byte job)
    {
        var saved = Appearance(11, 0);
        var source = Actor(Pictomancer, saved);
        var pin = PinnedOutfitConfiguration.Create(source, saved);
        var json = JObject.FromObject(new Configuration
        {
            PinnedOutfitsByCharacter = new() { [1] = [pin] },
        });
        ((JObject)json["PinnedOutfitsByCharacter"]!["1"]![0]!).Remove("WeaponClassJob");
        var config = json.ToObject<Configuration>()!;
        PinnedOutfitStore.Normalize(config);
        var store = Store(config);
        var current = Appearance(22, 33);
        Assert.True(store.TryGetAppearanceForCurrentJob(Actor(job, current), out var effective));
        Assert.Equal(current.Mainhand, effective.Mainhand);
        Assert.Equal(current.Offhand, effective.Offhand);
        Assert.Equal(saved.ModelCharaId, effective.ModelCharaId);
        Assert.Equal<byte>(saved.Customize, effective.Customize);
        Assert.Equal<ulong>(saved.Equipment, effective.Equipment);
        Assert.Equal(saved.ModelScale, effective.ModelScale);
        Assert.True(PinnedOutfitStore.AppearanceEquals(saved, Assert.Single(config.PinnedOutfitsByCharacter[1]).Appearance!));
        Assert.Null(Assert.Single(config.PinnedOutfitsByCharacter[1]).WeaponClassJob);
    }

    [Fact]
    public void OtherJobEquipmentEditsDoNotReplaceTheWeaponsSavedForThePinnedJob()
    {
        var config = new Configuration();
        var store = Store(config);
        var saved = Appearance(11, 0);
        store.PinCurrent([Actor(Pictomancer, saved)], _ => saved);
        var target = Actor(Paladin, Appearance(22, 33));
        store.UpdateSelectedWeapon(target, false, 55);
        Assert.True(EquipmentCopyData.TryCreate(Appearance(66, 77) with
        { Equipment = Enumerable.Repeat(0x100025UL, 10).ToImmutableArray() }, out var gear));
        store.UpdateEquipment(target, gear);
        Assert.True(store.TryGetAppearance(target, out var stored));
        Assert.Equal(saved.Mainhand, stored.Mainhand);
        Assert.Equal(saved.Offhand, stored.Offhand);
        Assert.Equal(saved.MainhandDyes, stored.MainhandDyes);
        Assert.Equal<ulong>(gear.Equipment, stored.Equipment);
        store.UpdateSelectedWeapon(Actor(Pictomancer, saved), false, 88);
        Assert.True(store.TryGetAppearance(Actor(Pictomancer, saved), out stored));
        Assert.Equal(88UL, stored.Mainhand);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PersistentCreationAndRetainedColorsRespectTheCurrentJob(bool legacy)
    {
        var config = new Configuration();
        var store = Store(config);
        var saved = Appearance(11, 0);
        var source = Actor(Pictomancer, saved);
        store.PinCurrent([source], _ => saved);
        if (legacy) config.PinnedOutfitsByCharacter[1][0].WeaponClassJob = null;
        var state = new ActorAppearancePersistence(() => 1, store.GetWeaponPinScope);
        state.RecordModel(source.Current, saved);
        var current = Appearance(22, 33);
        var paladin = Actor(Paladin, current).Current;
        var create = state.GetCreateAppearance(paladin, 0, out var outfitOnly)!;
        Assert.False(outfitOnly);
        Assert.Null(create.Mainhand);
        Assert.Null(create.Offhand);
        Assert.Equal(default, create.MainhandDyes);
        Assert.Equal(default, create.OffhandDyes);
        Assert.Equal(saved.Equipment, create.Equipment);
        Assert.Equal(saved.Customize, create.Customize);
        var retained = state.GetRetainedAppearance(paladin)!;
        Assert.Equal(current.Mainhand, retained.Mainhand);
        Assert.Equal(current.Offhand, retained.Offhand);
        Assert.Equal(current.MainhandDyes, retained.MainhandDyes);
        Assert.Equal(default, state.GetWeaponDyes(paladin, false, saved.Mainhand));
        var sameJob = state.GetCreateAppearance(source.Current, 0, out _)!;
        Assert.Equal(legacy ? null : saved.Mainhand, sameJob.Mainhand);
        Assert.Equal(legacy ? default : saved.MainhandDyes,
            state.GetWeaponDyes(source.Current, false, saved.Mainhand));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public unsafe void UnpinningBeforeAJobChangeDoesNotBringBackOldWeaponsOnTerritoryChange(bool unpinAll)
    {
        var store = Store(new Configuration());
        var state = new ActorAppearancePersistence(() => 1, store.GetWeaponPinScope);
        var saved = Appearance(11, 0) with
        {
            Equipment = Enumerable.Repeat(0x100025UL, 10).ToImmutableArray(),
            OffhandDyes = new(null, new DyeColor(0, 1, 0)),
            HatVisible = false,
        };
        var whiteMage = Actor(WhiteMage, saved);
        state.RecordOutfit(whiteMage.Current, EquipmentDisplayFormatting.CreateHumanOutfit(saved)!);
        Assert.Equal((1, 0), store.PinCurrent([whiteMage], actor =>
        {
            state.RecordPinAppearance(actor.Current, saved);
            return saved;
        }));
        if (unpinAll)
            Assert.Equal(1, store.UnpinAll());
        else
            Assert.True(store.Unpin(whiteMage));
        Assert.False(store.IsPinned(whiteMage));
        Assert.Equal(saved.Mainhand, state.GetCreateAppearance(whiteMage.Current, 0, out _)!.Mainhand);
        Assert.False(state.HasLocalModelSearchAppearance(whiteMage.Key));

        var gameWeapons = Appearance(22, 33);
        var original = Actor(Pictomancer, gameWeapons).Current;
        var movedKey = original.LogicalKey with { OriginalObjectIndex = 4, TerritoryId = 145, Lifetime = 2 };
        var moved = original with
        {
            LogicalKey = state.Resolve(original.ContinuityKey!.Value, movedKey),
            RepresentationKey = original.RepresentationKey with { ObjectIndex = 4, TerritoryId = 145 },
        };
        Assert.Equal(whiteMage.Key, moved.LogicalKey);
        var create = state.GetCreateAppearance(moved, 0, out var outfitOnly)!;
        Assert.False(outfitOnly);
        Assert.Null(create.Mainhand);
        Assert.Null(create.Offhand);
        Assert.Equal(default, create.MainhandDyes);
        Assert.Equal(default, create.OffhandDyes);
        Assert.Equal(default, state.GetWeaponDyes(moved, false, saved.Mainhand));
        Assert.Equal(default, state.GetWeaponDyes(moved, true, saved.Offhand));
        Assert.Equal(saved.Equipment, create.Equipment);
        Assert.Equal(saved.Customize, create.Customize);
        Assert.False(create.HatVisible);

        Character native = default;
        native.CharacterData.ClassJob = Pictomancer;
        native.DrawData.Weapon(DrawDataContainer.WeaponSlot.MainHand).ModelId.Value = 22;
        native.DrawData.Weapon(DrawDataContainer.WeaponSlot.OffHand).ModelId.Value = 33;
        NativeDrawObjectInjector.SubstituteBacking(&native, create);
        Assert.Equal(22UL, native.DrawData.Weapon(DrawDataContainer.WeaponSlot.MainHand).ModelId.Value);
        Assert.Equal(33UL, native.DrawData.Weapon(DrawDataContainer.WeaponSlot.OffHand).ModelId.Value);
    }

    [Theory]
    [InlineData(WhiteMage)]
    [InlineData(Pictomancer)]
    public void AReappliedPinDoesNotRetainTheOtherJobsWeaponsAfterUnpinning(byte jobDuringReapply)
    {
        var store = Store(new Configuration());
        var saved = Appearance(11, 0);
        store.PinCurrent([Actor(WhiteMage, saved)], _ => saved);
        var current = Actor(jobDuringReapply, Appearance(22, 33));
        Assert.True(store.TryGetAppearanceForCurrentJob(current, out var reapplied));
        var state = new ActorAppearancePersistence(() => 1, store.GetWeaponPinScope);
        state.RecordModel(current.Current, reapplied);
        Assert.True(store.Unpin(current));

        var other = state.GetCreateAppearance(Actor(Paladin, Appearance(55, 66)).Current, 0, out _)!;
        Assert.Null(other.Mainhand);
        Assert.Null(other.Offhand);
        Assert.Equal(saved.Equipment, other.Equipment);
        var original = state.GetCreateAppearance(Actor(WhiteMage, saved).Current, 0, out _)!;
        Assert.Equal(jobDuringReapply == WhiteMage ? saved.Mainhand : null, original.Mainhand);
        Assert.Equal(jobDuringReapply == WhiteMage ? saved.Offhand : null, original.Offhand);
    }

    [Fact]
    public void ArmorEditsKeepTheCapturedWeaponJobsAndAWeaponEditOnlyChangesItsOwnHand()
    {
        var state = new ActorAppearancePersistence();
        var saved = Appearance(11, 12);
        var whiteMage = Actor(WhiteMage, saved).Current;
        var pictomancer = Actor(Pictomancer, Appearance(22, 33)).Current;
        state.RecordPinAppearance(whiteMage, saved);

        var armor = EquipmentDisplayFormatting.CreateHumanOutfit(saved with
        {
            Equipment = Enumerable.Repeat(0x100025UL, 10).ToImmutableArray(),
        })!;
        Plugin.RecordEquipmentCopy(state, pictomancer, EquipmentCopyData.FromOutfit(armor), null);
        Assert.Null(state.GetCreateAppearance(pictomancer, 0, out _)!.Mainhand);
        Assert.Equal(saved.Mainhand, state.GetCreateAppearance(whiteMage, 0, out _)!.Mainhand);

        state.RecordWeapon(pictomancer, false, 44);
        var differentJob = state.GetCreateAppearance(pictomancer, 0, out _)!;
        Assert.Equal(44UL, differentJob.Mainhand);
        Assert.Null(differentJob.Offhand);
        Assert.Equal(0x100025UL, differentJob.Equipment[0]);
        var originalJob = state.GetCreateAppearance(whiteMage, 0, out _)!;
        Assert.Null(originalJob.Mainhand);
        Assert.Equal(saved.Offhand, originalJob.Offhand);
    }

    [Fact]
    public void CapturingACustomizeEditUsesTheCurrentlyEquippedWeaponsAfterUnpinning()
    {
        var store = Store(new Configuration());
        var state = new ActorAppearancePersistence(() => 1, store.GetWeaponPinScope);
        var saved = Appearance(11, 0);
        var whiteMage = Actor(WhiteMage, saved);
        store.PinCurrent([whiteMage], actor =>
        {
            state.RecordPinAppearance(actor.Current, saved);
            return saved;
        });
        Assert.True(store.Unpin(whiteMage));
        var changed = Appearance(44, 0) with { Customize = saved.Customize.SetItem(3, 50) };
        state.RecordModel(Actor(WhiteMage, changed).Current, changed);

        var same = state.GetCreateAppearance(whiteMage.Current, 0, out _)!;
        Assert.Equal(44UL, same.Mainhand);
        Assert.Equal(changed.Customize, same.Customize);
        var other = state.GetCreateAppearance(Actor(Pictomancer, Appearance(22, 33)).Current, 0, out _)!;
        Assert.Null(other.Mainhand);
        Assert.Null(other.Offhand);
        Assert.Equal(changed.Customize, other.Customize);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public unsafe void QueuedPinUsesSavedWeaponsOnlyIfTheExecutionJobMatches(bool returnToPinnedJob)
    {
        var config = new Configuration();
        var store = Store(config);
        var saved = Appearance(11, 0);
        var source = Actor(Pictomancer, saved);
        store.PinCurrent([source], _ => saved);
        var queueTarget = returnToPinnedJob
            ? Actor(Paladin, Appearance(22, 33) with { ModelCharaId = 999 }) : source;
        Assert.True(store.TryGetAppearanceForCurrentJob(queueTarget, out var queued));
        if (returnToPinnedJob)
        {
            Assert.Equal(22UL, queued.Mainhand);
            Assert.False(PinnedOutfitStore.CanMaintainWithOutfit(queueTarget.Current.CurrentAppearance!, queued));
        }
        Character character = default;
        character.CharacterData.ClassJob = returnToPinnedJob ? Pictomancer : Paladin;
        character.DrawData.Weapon(DrawDataContainer.WeaponSlot.MainHand).ModelId.Value = 22;
        character.DrawData.Weapon(DrawDataContainer.WeaponSlot.OffHand).ModelId.Value = 33;
        var effective = queued.ForWeaponJob(character.CharacterData.ClassJob);
        NativeDrawObjectInjector.SubstituteBacking(&character, effective);
        Assert.Equal(returnToPinnedJob ? saved.Mainhand!.Value : 22UL,
            character.DrawData.Weapon(DrawDataContainer.WeaponSlot.MainHand).ModelId.Value);
        Assert.Equal(returnToPinnedJob ? saved.Offhand!.Value : 33UL,
            character.DrawData.Weapon(DrawDataContainer.WeaponSlot.OffHand).ModelId.Value);
        Assert.Equal(returnToPinnedJob ? saved.MainhandDyes : default, effective.MainhandDyes);
        Assert.Equal(returnToPinnedJob ? saved.OffhandDyes : default, effective.OffhandDyes);
        Assert.Equal(saved.ModelCharaId, effective.ModelCharaId);
        Assert.Equal(saved.Equipment, effective.Equipment);
        if (!returnToPinnedJob)
        {
            Assert.Null(effective.Mainhand);
            Assert.Null(effective.Offhand);
        }
    }

    [Fact]
    public void JobScopeDoesNotRestrictNpcPinsOrUnpinnedManualAppearanceChanges()
    {
        var config = new Configuration();
        var store = Store(config);
        var saved = Appearance(11, 0);
        var npc = Actor(Pictomancer, saved, ObjectKind.EventNpc);
        store.PinCurrent([npc], _ => saved);
        Assert.True(store.TryGetAppearanceForCurrentJob(Actor(Paladin, Appearance(22, 33), ObjectKind.EventNpc), out var effective));
        Assert.Same(saved, effective);
        var state = new ActorAppearancePersistence(() => 1, store.GetWeaponPinScope);
        var player = Actor(Pictomancer, saved).Current;
        state.RecordModel(player, saved);
        Assert.Equal(saved.Mainhand, state.GetCreateAppearance(Actor(Paladin, Appearance(22, 33)).Current, 0, out _)!.Mainhand);
    }

    [Fact]
    public void RuntimeRequestScopeIsNotSerializedAsAnotherSourceOfPinSettings()
    {
        var saved = Appearance(11, 0);
        var appearance = saved with
        { PinnedWeaponScope = new(Pictomancer, new(saved.Mainhand, saved.Offhand, saved.MainhandDyes, saved.OffhandDyes)) };
        Assert.DoesNotContain("PinnedWeaponScope", JsonConvert.SerializeObject(appearance));
        var state = new ActorAppearancePersistence();
        state.RecordModel(Actor(Pictomancer, appearance).Current, appearance);
        Assert.Null(state.GetModel(Actor(Pictomancer, appearance).Key)!.PinnedWeaponScope);
    }

    private static PinnedOutfitStore Store(Configuration config) => new(config, () => { }, () => 1);

    private static Configuration RoundTrip(Configuration config)
    {
        var loaded = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(config))!;
        PinnedOutfitStore.Normalize(loaded);
        return loaded;
    }

    private static ActorEntry Actor(byte job, AppearanceData appearance, ObjectKind kind = ObjectKind.Pc)
    {
        var key = new LogicalActorKey(0, 101, 102, kind == ObjectKind.Pc ? 0U : 100U, kind, 144)
        { Continuity = new(kind, 1, 12345, 0, 0) };
        var snapshot = new ActorSnapshot(key, new(0, 101, 102, false, 144), "Test Actor", kind, key.BaseId,
            0, 1, 1, 1, job, 100, kind == ObjectKind.Pc, CurrentAppearance: appearance)
        { ContinuityKey = key.Continuity };
        return new(key, snapshot.Name, kind, snapshot.IsLocalPlayer, [snapshot]);
    }

    private static AppearanceData Appearance(ulong mainhand, ulong offhand)
        => AppearanceData.Create(0, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
            Enumerable.Repeat((byte)1, 26), Enumerable.Repeat(0x100012UL, 10),
            mainhand: mainhand, offhand: offhand, visorToggled: false, facewearModelId: 0, hatVisible: true)
        with { MainhandDyes = new(new DyeColor(1, 0, 0), null) };
}
