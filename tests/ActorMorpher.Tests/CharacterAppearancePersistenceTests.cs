using System;
using System.Linq;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class CharacterAppearancePersistenceTests
{
    [Fact]
    public void CharacterSwitchPartitionsNativeModelOutfitWeaponsColorsAndIdentityWithoutReplacingOutfits()
    {
        ulong owner = 11;
        var state = new ActorAppearancePersistence(() => owner);
        var outfits = state.Outfits; // BulkOutfitService retains this reference.
        var actor = Snapshot(90, 1);
        var partialActor = Snapshot(91, 2);
        var red = new WeaponDyes(new DyeColor(1, 0, 0) { Metallic = true }, new(0, 1, 0));
        var blue = new WeaponDyes(new DyeColor(0, 0, 1), null);
        var firstOutfit = Outfit(20);
        state.RecordModel(actor, Model(7));
        state.RecordOutfit(actor, firstOutfit);
        state.RecordWeapon(actor, false, 11, red);
        state.SetColorOutfit(actor.LogicalKey, firstOutfit);
        outfits.SetDesired(actor.LogicalKey, Outfit(1), firstOutfit);
        outfits.SetDesired(partialActor.LogicalKey, Outfit(1), firstOutfit);
        state.RecordWeapon(partialActor, false, 11, red);
        var firstModel = state.GetCreateAppearance(actor.LogicalKey, 0, out _);

        owner = 22;
        Assert.Same(outfits, state.Outfits);
        var recreated = Snapshot(90, 3);
        Assert.Equal(recreated.LogicalKey, state.Resolve(recreated.ContinuityKey!.Value, recreated.LogicalKey));
        var sameLifetime = recreated.LogicalKey with { Lifetime = actor.LogicalKey.Lifetime };
        Assert.Equal(sameLifetime, state.Resolve(recreated.ContinuityKey.Value, sameLifetime));
        foreach (var key in new[] { actor.LogicalKey, partialActor.LogicalKey })
        {
            Assert.Null(state.GetCreateAppearance(key, 0, out _));
            Assert.Null(state.GetModel(key));
            Assert.Null(state.GetRetainedOutfit(key));
            Assert.Null(state.GetColorOutfit(key));
            Assert.False(state.HasWeaponOverride(key));
            Assert.Equal(default, state.GetWeaponDyes(key, false, 11));
            Assert.False(outfits.TryGet(key, out _));
        }
        Assert.Null(state.GetRetainedAppearance(actor));
        Assert.Empty(outfits.States);

        var secondOutfit = Outfit(30);
        state.RecordModel(actor, Model(9));
        state.RecordWeapon(actor, false, 12, blue);
        state.SetColorOutfit(actor.LogicalKey, secondOutfit);
        outfits.SetDesired(actor.LogicalKey, Outfit(2), secondOutfit);
        outfits.SetDesired(partialActor.LogicalKey, Outfit(2), secondOutfit);
        state.RecordWeapon(partialActor, false, 12, blue);
        var secondModel = state.GetCreateAppearance(actor.LogicalKey, 0, out _);

        owner = 0;
        Assert.Null(state.GetCreateAppearance(actor.LogicalKey, 0, out _));
        Assert.Empty(outfits.States);
        state.RecordModel(actor, Model(99));
        outfits.SetDesired(actor.LogicalKey, Outfit(3), Outfit(99));

        owner = 11;
        Assert.Same(outfits, state.Outfits);
        Assert.Same(firstModel, state.GetCreateAppearance(actor.LogicalKey, 0, out var full));
        Assert.False(full);
        Assert.Same(firstOutfit, state.GetColorOutfit(actor.LogicalKey));
        Assert.Equal(red, state.GetWeaponDyes(actor.LogicalKey, false, 11));
        Assert.Equal(actor.LogicalKey, state.Resolve(recreated.ContinuityKey.Value, recreated.LogicalKey));
        Assert.True(outfits.TryGet(actor.LogicalKey, out var firstState));
        Assert.Same(firstOutfit, firstState.Desired);
        var firstPartial = state.GetCreateAppearance(partialActor.LogicalKey, 72, out var partial)!;
        Assert.True(partial);
        Assert.Equal(firstOutfit.Equipment.Select(ActorRegistry.ToEquipmentModelValue), firstPartial.Equipment);
        Assert.Equal(11UL, firstPartial.Mainhand);
        Assert.Equal(red, firstPartial.MainhandDyes);
        Assert.Same(firstOutfit, state.GetRetainedOutfit(partialActor.LogicalKey));
        state.Restore(actor.LogicalKey);
        outfits.Clear();

        owner = 22;
        Assert.Same(secondModel, state.GetCreateAppearance(actor.LogicalKey, 0, out _));
        Assert.Same(secondOutfit, state.GetColorOutfit(actor.LogicalKey));
        Assert.Equal(blue, state.GetWeaponDyes(actor.LogicalKey, false, 12));
        Assert.True(outfits.TryGet(actor.LogicalKey, out var secondState));
        Assert.Same(secondOutfit, secondState.Desired);
        Assert.Equal(2, outfits.States.Count);
    }

    [Fact]
    public void CompositeOperationsKeepTheOwnerCapturedAtTheirStart()
    {
        ulong owner = 11;
        var changeAfterRead = false;
        var state = new ActorAppearancePersistence(() =>
        {
            var captured = owner;
            if (changeAfterRead) owner = 22;
            return captured;
        });
        var actor = Snapshot(90, 1);
        var partialActor = Snapshot(91, 2);
        state.Outfits.SetDesired(partialActor.LogicalKey, Outfit(1), Outfit(20));
        changeAfterRead = true;
        state.RecordModel(actor, Model(7));
        Assert.Null(state.GetModel(actor.LogicalKey)); // Now reading B.
        owner = 11;
        Assert.NotNull(state.GetModel(actor.LogicalKey));
        owner = 11;
        var recreated = Snapshot(90, 3);
        Assert.Equal(actor.LogicalKey, state.Resolve(recreated.ContinuityKey!.Value, recreated.LogicalKey));
        owner = 11;
        state.RecordWeapon(partialActor, false, 11);
        owner = 11;
        var partial = state.GetCreateAppearance(partialActor.LogicalKey, 72, out _)!;
        Assert.Equal(11UL, partial.Mainhand);
        Assert.Equal(Outfit(20).Equipment.Select(ActorRegistry.ToEquipmentModelValue), partial.Equipment);
        owner = 11;
        Assert.NotNull(state.GetRetainedOutfit(partialActor.LogicalKey));
        owner = 11;
        state.Restore(partialActor.LogicalKey);
        owner = 11;
        Assert.Null(state.GetCreateAppearance(partialActor.LogicalKey, 72, out _));
    }

    [Fact]
    public void OutfitRestoreStateAndCompleteRestoreOnlyChangeCurrentCharacter()
    {
        ulong owner = 11;
        var store = new OutfitOverrideStore(() => owner);
        var actor = Snapshot(90, 1).LogicalKey;
        var first = store.SetDesired(actor, Outfit(1), Outfit(20));
        owner = 22;
        var second = store.SetDesired(actor, Outfit(2), Outfit(30));
        Assert.True(store.CompleteRestore(actor));
        Assert.Empty(store.States);
        store.RestoreState(actor, second);
        Assert.True(store.TryGet(actor, out var restored));
        Assert.Same(second, restored);
        store.RestoreState(actor, null);
        owner = 11;
        Assert.True(store.TryGet(actor, out var retained));
        Assert.Same(first, retained);
    }

    private static ActorSnapshot Snapshot(ulong layout, ulong lifetime)
    {
        var identity = new ActorContinuityKey(ObjectKind.EventNpc, 3, layout, 17, 30);
        var key = new LogicalActorKey((ushort)lifetime, lifetime, (uint)lifetime, 17, ObjectKind.EventNpc, 30)
            { Continuity = identity, Lifetime = lifetime };
        return new(key, new((ushort)lifetime, lifetime, (uint)lifetime, false, 30),
            "NPC", ObjectKind.EventNpc, 17, 0, 1, 0, 1, 0, 0, false) { ContinuityKey = identity };
    }

    private static AppearanceData Model(byte marker) => AppearanceData.Create(0, ModelCategory.Human, marker,
        AppearanceCompleteness.Complete, Enumerable.Repeat(marker, 26), Enumerable.Repeat((ulong)marker, 10));

    private static OutfitData Outfit(ushort marker) => OutfitData.Create(
        Enumerable.Repeat(new ArmorAppearance(marker, 1, 0, 0) { Color1 = new DyeColor(0.2f, 0.3f, 0.4f) }, 10),
        new FacewearAppearance(true, 0), true, false);
}
