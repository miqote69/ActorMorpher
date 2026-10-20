using System.Collections.Immutable;
using System.Linq;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using Dalamud.Game.ClientState.Objects.Enums;
using Newtonsoft.Json;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class NpcCustomizeSharingTests
{
    private static readonly NpcEquipmentGroup Group = new(ObjectKind.EventNpc, 100, [100, 101, 102]);
    private static NpcEquipmentGroup? Resolve(ObjectKind kind, uint id)
        => kind == Group.Kind && Group.BaseIds.Contains(id) ? Group : null;

    [Fact]
    public void CustomizeEditSharesOnlyCustomizeAndColorsWithoutCreatingEquipmentSharingOrPins()
    {
        var config = new Configuration();
        var saves = 0;
        var store = new PinnedOutfitStore(config, () => saves++, () => 1, Resolve);
        var source = Actor(100, 1);
        var target = Actor(101, 2);
        var edited = Customize(7);
        Assert.True(store.ShareNpcCustomize(source, edited));
        Assert.True(store.TryGetSharedNpcCustomize(target, out var shared));
        Assert.False(store.TryGetSharedNpcEquipment(target, out _));
        Assert.False(store.IsPinned(target));
        Assert.Equal(0, store.Count);
        Assert.Equal(0, saves);
        Assert.Empty(config.PinnedOutfitsByCharacter);

        var original = Appearance(2, 30) with { ModelCharaId = 345, ModelScale = 1.7f };
        var applied = shared.ApplyTo(original);
        AssertCustomize(edited, applied);
        Assert.Equal(original, applied with
        {
            Customize = original.Customize,
            CustomizeColors = original.CustomizeColors,
        });
    }

    [Fact]
    public void AlternatingEquipmentAndCustomizeEditsPreservesBothComponentsAndIndividualPinIdentity()
    {
        var config = new Configuration();
        var store = new PinnedOutfitStore(config, () => { }, () => 1, Resolve);
        var first = Actor(100, 1);
        var second = Actor(101, 2);
        store.PinCurrent([first], _ => Appearance(2, 20) with { ModelCharaId = 123, ModelScale = 1.2f });
        store.PinCurrent([second], _ => Appearance(3, 30) with { ModelCharaId = 456, ModelScale = 1.5f });
        var identities = config.PinnedOutfitsByCharacter[1].Select(pin => pin.IdentityKey()).ToArray();
        Assert.True(store.ShareNpcEquipment(first, Equipment(80)));
        var edited = Customize(7);
        Assert.True(store.ShareNpcCustomize(second, edited));
        Assert.True(store.TryGetSharedNpcEquipment(Actor(102, 3), out var retainedGear));
        Assert.Equal((ushort)80, retainedGear.Outfit.Equipment[1].Set);
        Assert.True(store.ShareNpcEquipment(first, Equipment(90)));
        Assert.True(store.TryGetSharedNpcCustomize(Actor(102, 3), out var retainedCustomize));
        AssertCustomize(edited, retainedCustomize.ApplyTo(Appearance(1, 10)));
        Assert.True(store.TryGetAppearance(first, out var firstPin));
        Assert.True(store.TryGetAppearance(second, out var secondPin));
        AssertCustomize(edited, firstPin);
        AssertCustomize(edited, secondPin);
        Assert.Equal((uint)123, firstPin.ModelCharaId);
        Assert.Equal(1.2f, firstPin.ModelScale);
        Assert.Equal((uint)456, secondPin.ModelCharaId);
        Assert.Equal(1.5f, secondPin.ModelScale);
        Assert.True(store.TryGet(first, out var firstOutfit));
        Assert.True(store.TryGet(second, out var secondOutfit));
        Assert.Equal((ushort)90, firstOutfit.Equipment[1].Set);
        Assert.Equal((ushort)90, secondOutfit.Equipment[1].Set);
        Assert.Equal(identities, config.PinnedOutfitsByCharacter[1].Select(pin => pin.IdentityKey()).ToArray());
        Assert.Equal(2, store.Count);
        Assert.False(store.IsPinned(Actor(100, 20)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CustomizeSharingSurvivesJsonForFullAndLegacyPinsWithoutCrossingOwners(bool legacy)
    {
        ulong owner = 1;
        var config = new Configuration();
        var store = new PinnedOutfitStore(config, () => { }, () => owner, Resolve);
        var source = Actor(100, 1);
        if (legacy) store.Pin(source, Equipment(20).Outfit);
        else store.PinCurrent([source], _ => Appearance(2, 20));
        var edited = Customize(7);
        Assert.True(store.ShareNpcCustomize(source, edited));
        var restarted = new PinnedOutfitStore(RoundTrip(config), () => { }, () => owner, Resolve);
        var later = Actor(102, 50);
        Assert.True(restarted.TryGetSharedNpcCustomize(later, out var shared));
        AssertCustomize(edited, shared.ApplyTo(Appearance(1, 10)));
        Assert.True(restarted.TryGetSharedNpcEquipment(later, out _));
        Assert.Equal(!legacy, restarted.TryGetAppearance(source, out _));
        Assert.True(restarted.TryGet(source, out var outfit));
        Assert.Equal((ushort)20, outfit.Equipment[1].Set);
        Assert.False(restarted.IsPinned(later));
        owner = 2;
        Assert.False(restarted.TryGetSharedNpcCustomize(later, out _));
        Assert.False(restarted.IsPinned(source));
        owner = 1;
        Assert.True(restarted.TryGetSharedNpcCustomize(later, out shared));
        AssertCustomize(edited, shared.ApplyTo(Appearance(1, 10)));
    }

    [Fact]
    public void UnpinClearsBothSharedComponentsAcrossRestartAndRetainsOtherIndividualPin()
    {
        var config = new Configuration();
        var store = new PinnedOutfitStore(config, () => { }, () => 1, Resolve);
        var first = Actor(100, 1);
        var second = Actor(101, 2);
        store.PinCurrent([first], _ => Appearance(2, 20));
        store.Pin(second, Equipment(30).Outfit);
        store.ShareNpcEquipment(first, Equipment(80));
        store.ShareNpcCustomize(first, Customize(7));
        config = RoundTrip(config);
        store = new(config, () => { }, () => 1, Resolve);
        Assert.True(store.Unpin(first));
        Assert.True(store.IsPinned(second));
        Assert.False(store.IsPinned(first));
        Assert.False(store.TryGetSharedNpcCustomize(Actor(102, 3), out _));
        Assert.False(store.TryGetSharedNpcEquipment(Actor(102, 3), out _));
        var remaining = Assert.Single(config.PinnedOutfitsByCharacter[1]);
        Assert.False(remaining.ShareNpcCustomize);
        Assert.Null(remaining.SharedCustomize);
        Assert.False(remaining.ShareNpcEquipment);
        store = new(RoundTrip(config), () => { }, () => 1, Resolve);
        Assert.False(store.TryGetSharedNpcCustomize(Actor(102, 3), out _));
        Assert.False(store.TryGetSharedNpcEquipment(Actor(102, 3), out _));
        Assert.True(store.IsPinned(second));
        Assert.False(store.TryGetAppearance(second, out _));
        Assert.True(store.TryGet(second, out var retained));
        Assert.Equal((ushort)80, retained.Equipment[1].Set);
    }

    [Fact]
    public void PinningAndReadingIndividualPinsAutomaticallySharesTheLatestPinnedCustomize()
    {
        var config = new Configuration();
        var store = new PinnedOutfitStore(config, () => { }, () => 1, Resolve);
        var first = Actor(100, 1);
        var second = Actor(101, 2);
        var otherAppearance = Appearance(3, 30);
        store.PinCurrent([first], _ => Appearance(2, 20));
        store.PinCurrent([second], _ => otherAppearance);
        store.PinCurrent([first], _ => Appearance(7, 80));
        Assert.True(store.TryGetSharedNpcCustomize(Actor(102, 3), out var shared));
        Assert.True(store.TryGetAppearance(second, out var otherPin));
        Assert.Equal<byte>(Appearance(7, 80).Customize, otherPin.Customize);
        Assert.Equal<byte>(otherPin.Customize, shared.Customize);
        Assert.All(config.PinnedOutfitsByCharacter[1], pin => Assert.True(pin.ShareNpcCustomize));
        var restarted = new PinnedOutfitStore(RoundTrip(config), () => { }, () => 1, Resolve);
        Assert.True(restarted.TryGetSharedNpcCustomize(Actor(102, 3), out var loaded));
        Assert.Equal<byte>(shared.Customize, loaded.Customize);
    }

    [Fact]
    public void ExplicitCustomizeReeditMakesPreviouslyAttemptedUnpinnedTargetEligibleAgain()
    {
        var store = new PinnedOutfitStore(new Configuration(), () => { }, () => 1, Resolve);
        var source = Actor(100, 1);
        var target = Actor(101, 2);
        store.ShareNpcCustomize(source, Customize(7));
        Assert.True(store.WasSharedNpcChangeAttempted(source));
        Assert.False(store.WasSharedNpcChangeAttempted(target));
        Assert.True(store.TryGetSharedNpcCustomize(target, out _));
        store.MarkSharedNpcEquipmentAttempted(target);
        Assert.True(store.WasSharedNpcChangeAttempted(target));
        Assert.False(store.TryGetSharedNpcCustomize(target, out _));
        var edited = Customize(8);
        store.ShareNpcCustomize(source, edited);
        Assert.False(store.WasSharedNpcChangeAttempted(target));
        Assert.True(store.TryGetSharedNpcCustomize(target, out var shared));
        AssertCustomize(edited, shared.ApplyTo(Appearance(1, 10)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EditingAnotherComponentBeforeSharingArrivesKeepsBothComponentsPendingOnTheNewSource(bool customizeFirst)
    {
        var store = new PinnedOutfitStore(new Configuration(), () => { }, () => 1, Resolve);
        var first = Actor(100, 1);
        var second = Actor(101, 2);
        var body = Customize(7);
        var gear = Equipment(80);
        if (customizeFirst)
        {
            store.ShareNpcCustomize(first, body);
            store.ShareNpcEquipment(second, gear);
        }
        else
        {
            store.ShareNpcEquipment(first, gear);
            store.ShareNpcCustomize(second, body);
        }
        Assert.False(store.WasSharedNpcChangeAttempted(second));
        Assert.True(store.TryGetSharedNpcEquipment(second, out var sharedGear));
        Assert.True(store.TryGetSharedNpcCustomize(second, out var sharedBody));
        var rendered = customizeFirst ? gear.ApplyTo(Appearance(1, 10)) : body.ApplyTo(Appearance(1, 10));
        var desired = Plugin.ComposeSharedNpcAppearance(rendered, null, null, sharedGear, sharedBody);
        AssertCustomize(body, desired);
        Assert.Equal<ulong>(gear.Equipment, desired.Equipment);
        store.MarkSharedNpcEquipmentAttempted(second);
        Assert.False(store.TryGetSharedNpcEquipment(second, out _));
        Assert.False(store.TryGetSharedNpcCustomize(second, out _));
    }

    private static Configuration RoundTrip(Configuration config)
        => JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(config))!;

    private static void AssertCustomize(CustomizeCopyData expected, AppearanceData actual)
    {
        Assert.Equal(expected.Customize.ToArray(), actual.Customize.ToArray());
        Assert.Equal(expected.CustomizeColors.ToArray(), actual.CustomizeColors.ToArray());
    }

    private static CustomizeCopyData Customize(byte value)
    {
        Assert.True(CustomizeCopyData.TryCreate(Appearance(value, 99), out var data));
        return data;
    }

    private static EquipmentCopyData Equipment(ushort body)
    {
        Assert.True(EquipmentCopyData.TryCreate(Appearance(1, body), out var data));
        return data;
    }

    private static AppearanceData Appearance(byte customizeValue, ushort body)
        => AppearanceData.Create(0, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
            Enumerable.Repeat(customizeValue, 26).ToImmutableArray(),
            Enumerable.Repeat(new FFXIVClientStructs.FFXIV.Client.Game.Character.EquipmentModelId
                { Id = body, Variant = 1 }.Value, 10), 1, 0, 0, false, 0, true) with
        {
            CustomizeColors = [new(0, customizeValue / 10f, .2f, .3f), new(24, .4f, .5f, .6f)],
        };

    private static ActorEntry Actor(uint baseId, ushort index)
    {
        var key = new LogicalActorKey(index, (ulong)(1000 + index), (uint)(2000 + index), baseId, ObjectKind.EventNpc, 282);
        var snapshot = new ActorSnapshot(key, new(index, key.GameObjectId, key.EntityId, false, 282),
            "パンプキンヘッドの女", ObjectKind.EventNpc, baseId, 0, 1, 1, 1, 0, 0, false);
        return new(key, snapshot.Name, snapshot.ObjectKind, false, [snapshot]);
    }
}
