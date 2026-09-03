using System;
using System.Collections.Generic;
using System.Linq;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using ActorMorpher.Interop;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed unsafe class ActorAppearancePersistenceTests
{
    [Theory]
    [InlineData(ObjectKind.Pc, 1, 123ul, 124ul, false)]
    [InlineData(ObjectKind.EventNpc, 3, 90ul, 91ul, false)]
    [InlineData(ObjectKind.Companion, 2, 0x400000001ul, 0x400000002ul, false)]
    [InlineData(ObjectKind.EventNpc, 5, 0x100000007ul, 0x100000007ul, true)]
    [InlineData(ObjectKind.EventNpc, 5, 0x100000007ul, 0x200000007ul, true)]
    public void ReusedRawKeysCannotReachAnotherActorsModelOutfitOrManagedUi(
        ObjectKind kind, byte source, ulong firstId, ulong secondId, bool destroy)
    {
        var native = new NativeActorContinuity();
        var state = new ActorAppearancePersistence();
        var first = Observed(native, kind, source, firstId, 1000);
        var desired = Model(27);
        state.RecordModel(first, desired);
        state.Outfits.SetDesired(first.LogicalKey, Outfit(1), Outfit(20));
        if (destroy)
            native.Forget(1000);
        var replacement = Observed(native, kind, source, secondId, 1000);
        var canonical = state.Resolve(replacement.ContinuityKey!.Value, replacement.LogicalKey);
        replacement = replacement with { LogicalKey = canonical };
        Assert.Equal(first.RepresentationKey, replacement.RepresentationKey);
        Assert.NotEqual(first.LogicalKey, replacement.LogicalKey);
        Assert.Null(state.GetCreateAppearance(canonical, 0, out _));
        Assert.Null(state.GetRetainedAppearance(replacement));
        var published = ActorRegistry.RetainManagedAppearance(replacement,
            new Dictionary<(LogicalActorKey, ActorRepresentationKey), AppearanceData>
            { [(first.LogicalKey, first.RepresentationKey)] = desired }, state.GetRetainedAppearance);
        Assert.False(published.IsAppearanceManaged);
        Assert.Same(replacement.CurrentAppearance, published.CurrentAppearance);
        Assert.False(state.Outfits.TryGet(canonical, out _));
    }

    [Theory]
    [InlineData(ObjectKind.Pc, 4, 1ul, 1, 123ul)]
    [InlineData(ObjectKind.EventNpc, 5, 0x100000007ul, 3, 90ul)]
    [InlineData(ObjectKind.EventNpc, 3, 90ul, 5, 0x100000007ul)]
    public void SameLivingActorLinksProvisionalAndConfirmedIdsBeforeRecreation(
        ObjectKind kind, byte firstSource, ulong firstId, byte nextSource, ulong nextId)
    {
        var native = new NativeActorContinuity();
        var state = new ActorAppearancePersistence();
        var first = Observed(native, kind, firstSource, firstId, 1000);
        var desired = Model(27);
        state.RecordModel(first, desired);
        var next = Observed(native, kind, nextSource, nextId, 1000);
        Assert.Equal(first.LogicalKey.Lifetime, next.LogicalKey.Lifetime);
        var canonical = state.Resolve(next.ContinuityKey!.Value, next.LogicalKey);
        Assert.Equal(first.LogicalKey, canonical);
        Assert.Same(desired, state.GetCreateAppearance(canonical, 0, out _));
        native.Forget(1000);
        var confirmed = nextSource < firstSource ? (nextSource, nextId) : (firstSource, firstId);
        var recreated = Observed(native, kind, confirmed.Item1, confirmed.Item2, 2000);
        recreated = recreated with { LogicalKey = recreated.LogicalKey with { OriginalObjectIndex = 12 } };
        Assert.NotEqual(first.LogicalKey.Lifetime, recreated.LogicalKey.Lifetime);
        var recreatedKey = state.Resolve(recreated.ContinuityKey!.Value, recreated.LogicalKey);
        Assert.Same(desired, state.GetCreateAppearance(recreatedKey, 0, out _));
    }

    [Fact]
    public void UnidentifiedDescriptorPreservesFullGameObjectId()
    {
        var first = NativeActorContinuity.Describe(ObjectKind.EventNpc, 0, 0xE0000000, 0, 0x100000007, 17, 1, 30);
        var second = NativeActorContinuity.Describe(ObjectKind.EventNpc, 0, 0xE0000000, 0, 0x200000007, 17, 1, 30);
        Assert.NotEqual(first, second);
        Assert.Equal(0x100000007ul, first.Id);
    }

    private static ActorSnapshot Observed(NativeActorContinuity native, ObjectKind kind, byte source, ulong id, nint address)
    {
        var identity = new ActorContinuityKey(kind, source, id, 17, kind == ObjectKind.Pc ? 0u : 30u);
        var observation = native.Observe(address, identity);
        var raw = Snapshot(kind);
        return raw with
        {
            ContinuityKey = identity,
            LogicalKey = raw.LogicalKey with { Continuity = identity, Lifetime = observation.Lifetime },
            CurrentAppearance = Model(1),
        };
    }

    [Theory]
    [InlineData(ObjectKind.Pc)]
    [InlineData(ObjectKind.Companion)]
    [InlineData(ObjectKind.EventNpc)]
    [InlineData(ObjectKind.BattleNpc)]
    public void NativeCopyLinkKeepsSourceAcrossCopyChainsAndSourceDestruction(ObjectKind kind)
    {
        var actor = Snapshot(kind).LogicalKey;
        var links = new ActorCopyLinks();
        links.Link(200, 10, _ => actor);
        links.Link(201, 200, _ => throw new InvalidOperationException("Copy already has a source."));
        links.Remove(10);
        links.Remove(200);
        Assert.Equal(actor, links.Get(201));
        Assert.Null(links.Get(200));
        var other = actor with { GameObjectId = 99 };
        links.Link(200, 11, _ => other);
        Assert.Equal(other, links.Get(200));
        Assert.Equal(actor, links.Get(201));
    }

    [Theory]
    [InlineData(ObjectKind.Pc)]
    [InlineData(ObjectKind.Companion)]
    [InlineData(ObjectKind.EventNpc)]
    [InlineData(ObjectKind.BattleNpc)]
    public void SuccessfulPayloadSurvivesNewAndPreviouslyVisitedRepresentations(ObjectKind kind)
    {
        var state = new ActorAppearancePersistence();
        var actor = Snapshot(kind);
        var desired = Model(27);
        state.RecordModel(actor, desired);
        foreach (var territory in new uint[] { 30, 40, 30 })
        {
            var current = actor.LogicalKey with { OriginalObjectIndex = 19, TerritoryId = territory, Lifetime = territory };
            var canonical = state.Resolve(actor.ContinuityKey!.Value, current);
            Assert.Equal(actor.LogicalKey, canonical);
            var rep = actor.RepresentationKey with { ObjectIndex = 19, TerritoryId = territory };
            Assert.Same(desired, state.GetRetainedAppearance(actor with { LogicalKey = canonical, RepresentationKey = rep }));
            Assert.Same(desired, state.GetCreateAppearance(canonical, 999, out var outfitOnly));
            Assert.False(outfitOnly);
        }
        Assert.Null(state.GetCreateAppearance(actor.LogicalKey with { GameObjectId = 99 }, 0, out _));
        state.Restore(actor.LogicalKey);
        Assert.Null(state.GetCreateAppearance(actor.LogicalKey, 0, out _));
    }

    [Fact]
    public void NativeIdentitySeparatesPlayersOwnersAndNpcPlacements()
    {
        ActorContinuityKey Describe(ObjectKind kind, ulong content, ulong gid, uint layout, ushort index, uint territory)
            => NativeActorContinuity.Describe(kind, content, 0xE0000000, layout, gid, 17, index, territory);
        Assert.Equal(Describe(ObjectKind.Pc, 123, 1, 0, 0, 30), Describe(ObjectKind.Pc, 123, 9, 0, 20, 40));
        Assert.NotEqual(Describe(ObjectKind.Pc, 123, 1, 0, 0, 30), Describe(ObjectKind.Pc, 124, 1, 0, 0, 30));
        Assert.Equal(Describe(ObjectKind.Companion, 0, 0x400000001, 0, 1, 30), Describe(ObjectKind.Companion, 0, 0x400000001, 0, 19, 40));
        Assert.NotEqual(Describe(ObjectKind.Companion, 0, 0x400000001, 0, 1, 30), Describe(ObjectKind.Companion, 0, 0x400000002, 0, 1, 30));
        Assert.Equal(Describe(ObjectKind.EventNpc, 0, 17, 90, 1, 30), Describe(ObjectKind.EventNpc, 0, 17, 90, 19, 30));
        Assert.NotEqual(Describe(ObjectKind.EventNpc, 0, 17, 90, 1, 30), Describe(ObjectKind.EventNpc, 0, 17, 91, 1, 30));
        Assert.NotEqual(Describe(ObjectKind.EventNpc, 0, 17, 90, 1, 30), Describe(ObjectKind.EventNpc, 0, 17, 90, 1, 40));
    }

    [Fact]
    public void ModelAndBulkOrderPreservesOriginalAndExplicitRestoreOnlyRemovesSelectedActor()
    {
        var state = new ActorAppearancePersistence();
        var actor = Snapshot(ObjectKind.Pc);
        var other = actor with { LogicalKey = actor.LogicalKey with { GameObjectId = 88 }, ContinuityKey = null };
        var original = Outfit(1);
        var x = Outfit(20);
        state.RecordModel(actor, Model(2));
        state.RecordModel(other, Model(4));
        state.Outfits.SetDesired(actor.LogicalKey, original, x);
        state.RecordOutfit(actor, x);
        Assert.Equal(x.Equipment.Select(ActorRegistry.ToEquipmentModelValue), state.GetModel(actor.LogicalKey)!.Equipment);
        var d = Model(30);
        state.RecordModel(actor, d);
        Assert.Same(d, state.GetCreateAppearance(actor.LogicalKey, 0, out _));
        var y = Outfit(40);
        state.Outfits.SetDesired(actor.LogicalKey, x, y);
        state.RecordOutfit(actor, y);
        Assert.True(state.Outfits.TryGet(actor.LogicalKey, out var stored));
        Assert.Same(original, stored.Original);
        state.Outfits.CompleteRestore(actor.LogicalKey);
        state.RecordOutfit(actor, original);
        Assert.Equal(original.Equipment.Select(ActorRegistry.ToEquipmentModelValue), state.GetModel(actor.LogicalKey)!.Equipment);
        state.Restore(actor.LogicalKey);
        Assert.Null(state.GetCreateAppearance(actor.LogicalKey, 0, out _));
        Assert.NotNull(state.GetCreateAppearance(other.LogicalKey, 0, out _));
    }

    [Fact]
    public void OutfitOnlyUsesGameCustomizeAndOneCreateWithTemporaryEquipment()
    {
        var state = new ActorAppearancePersistence();
        var actor = Snapshot(ObjectKind.Companion);
        var outfit = Outfit(20);
        state.Outfits.SetDesired(actor.LogicalKey, Outfit(1), outfit);
        state.RecordOutfit(actor, outfit);
        var payload = state.GetCreateAppearance(actor.LogicalKey, 72, out var outfitOnly)!;
        Assert.True(outfitOnly);
        Assert.Equal(72u, payload.ModelCharaId);
        Assert.Empty(payload.Customize);
        Assert.Null(payload.Mainhand);
        Assert.Null(payload.Offhand);
        Assert.Null(payload.ModelScale);
        byte* customize = stackalloc byte[26];
        ulong* equipment = stackalloc ulong[10];
        new Span<byte>(customize, 26).Fill(9);
        new Span<ulong>(equipment, 10).Fill(1);
        var calls = 0;
        var result = NativeDrawObjectInjector.InvokeWithTemporaryCreateBuffers(payload, (nint)customize, (nint)equipment,
            (c, e) =>
            {
                calls++;
                Assert.All(new Span<byte>((void*)c, 26).ToArray(), b => Assert.Equal(9, b));
                Assert.Equal(payload.Equipment, new Span<ulong>((void*)e, 10).ToArray());
                return (nint)123;
            });
        Assert.Equal((nint)123, result);
        Assert.Equal(1, calls);
        Assert.All(new Span<ulong>(equipment, 10).ToArray(), value => Assert.Equal(1ul, value));
    }

    private static ActorSnapshot Snapshot(ObjectKind kind)
    {
        var identity = new ActorContinuityKey(kind, 1, 77, 17, 0);
        var key = new LogicalActorKey(1, 1, 1, 17, kind, 30) { Continuity = identity, Lifetime = 1 };
        return new(key, new(1, 1, 1, false, 30), "Actor", kind, 17, 0, 1, 0, 1, 0, 0, kind == ObjectKind.Pc)
        { ContinuityKey = identity };
    }

    private static AppearanceData Model(byte marker) => AppearanceData.Create(0, ModelCategory.Human, marker,
        AppearanceCompleteness.Complete, Enumerable.Repeat(marker, 26), Enumerable.Repeat((ulong)marker, 10));
    private static OutfitData Outfit(ushort marker) => OutfitData.Create(
        Enumerable.Repeat(new ArmorAppearance(marker, 1, 0, 0), 10), new FacewearAppearance(true, 0), true, false);
}
