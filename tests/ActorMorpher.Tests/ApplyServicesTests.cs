using System;
using System.Collections.Generic;
using System.Linq;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using ActorMorpher.Diagnostics;
using ActorMorpher.Interop;
using ActorMorpher.Redraw;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class ApplyServicesTests
{
    [Fact]
    public void AppearanceApplyAndRestorePreserveTheFirstSnapshot()
    {
        var actor = Snapshot(1);
        var original = Appearance(0, 1);
        var desired = Appearance(200, 2);
        var memory = new FakeAppearanceMemory(original);
        var resolver = new FakeResolver(actor);
        var context = new FakeContext();
        using var redraw = new RedrawCoordinator(resolver, memory, new FakeRedrawBackend(), context);
        var store = new AppearanceOverrideStore();
        using var service = new AppearanceApplyService(resolver, memory, context, redraw, store, NullDiagnosticLog.Instance);

        Assert.True(service.TryApply(actor.LogicalKey, desired, out _));
        Process(redraw, 5);

        Assert.Same(desired, memory.Current);
        Assert.True(store.TryGet(actor.LogicalKey, out var state));
        Assert.Same(original, state.BaseData);

        Assert.True(service.TryRestore(actor.LogicalKey, out _));
        Process(redraw, 5);

        Assert.Same(original, memory.Current);
        Assert.False(store.TryGet(actor.LogicalKey, out _));
    }

    [Fact]
    public void FailedAppearanceApplyRollsBackTheOverrideStore()
    {
        var actor = Snapshot(1);
        var original = Appearance(0, 1);
        var desired = Appearance(300, 3);
        var memory = new FakeAppearanceMemory(original) { FailedAppearance = desired };
        var resolver = new FakeResolver(actor);
        var context = new FakeContext();
        using var redraw = new RedrawCoordinator(resolver, memory, new FakeRedrawBackend(), context);
        var store = new AppearanceOverrideStore();
        using var service = new AppearanceApplyService(resolver, memory, context, redraw, store, NullDiagnosticLog.Instance);

        Assert.True(service.TryApply(actor.LogicalKey, desired, out _));
        Process(redraw, 6);

        Assert.Same(original, memory.Current);
        Assert.False(store.TryGet(actor.LogicalKey, out _));
    }

    [Fact]
    public void BulkApplyProcessesOneActorPerFrameAndRestoreUsesStoredTargets()
    {
        var sourceActor = Snapshot(1);
        var firstActor = Snapshot(2);
        var secondActor = Snapshot(3);
        var source = Outfit(10);
        var firstOriginal = Outfit(20);
        var secondOriginal = Outfit(30);
        var resolver = new FakeResolver(sourceActor, firstActor, secondActor);
        var memory = new FakeOutfitMemory(new Dictionary<LogicalActorKey, OutfitData>
        {
            [sourceActor.LogicalKey] = source,
            [firstActor.LogicalKey] = firstOriginal,
            [secondActor.LogicalKey] = secondOriginal,
        });
        var context = new FakeContext();
        var store = new OutfitOverrideStore();
        using var service = new BulkOutfitService(resolver, memory, context, store, NullDiagnosticLog.Instance);

        Assert.True(service.RefreshSource(sourceActor.LogicalKey, out _));
        Assert.True(service.StartApply([firstActor.LogicalKey, secondActor.LogicalKey], out _));
        service.ProcessNextFrame();
        service.ProcessNextFrame();

        Assert.Same(source, memory.Current[firstActor.LogicalKey]);
        Assert.Same(secondOriginal, memory.Current[secondActor.LogicalKey]);

        service.ProcessNextFrame();
        service.ProcessNextFrame();
        Assert.Same(source, memory.Current[secondActor.LogicalKey]);
        Assert.Equal(2, store.States.Count);

        Assert.True(service.StartRestore(out _));
        service.ProcessNextFrame();
        service.ProcessNextFrame();
        service.ProcessNextFrame();

        Assert.Same(firstOriginal, memory.Current[firstActor.LogicalKey]);
        Assert.Same(secondOriginal, memory.Current[secondActor.LogicalKey]);
        Assert.Empty(store.States);
    }

    private static void Process(RedrawCoordinator coordinator, int frames)
    {
        for (var frame = 0; frame < frames; ++frame)
            coordinator.ProcessNextFrame();
    }

    private static ActorSnapshot Snapshot(ushort index)
    {
        var key = new LogicalActorKey(index, index, index, index, ObjectKind.Pc, 30);
        return new ActorSnapshot(
            key,
            new ActorRepresentationKey(index, index, index, false),
            $"Actor {index}",
            ObjectKind.Pc,
            index,
            0,
            1,
            0,
            1,
            0,
            0,
            index == 1);
    }

    private static AppearanceData Appearance(uint modelId, byte marker)
        => AppearanceData.Create(
            modelId,
            modelId == 0 ? ModelCategory.Human : ModelCategory.Monster,
            marker,
            modelId == 0 ? AppearanceCompleteness.Complete : AppearanceCompleteness.ModelOnly,
            modelId == 0 ? [marker] : [],
            modelId == 0 ? [(ulong)marker] : []);

    private static OutfitData Outfit(ushort marker)
        => OutfitData.Create(
            Enum.GetValues<OutfitSlot>().Select(slot => new ArmorAppearance(
                checked((ushort)(marker + (ushort)slot)),
                1,
                2,
                3)),
            new FacewearAppearance(true, marker),
            true,
            false);

    private sealed class FakeResolver(params ActorSnapshot[] actors) : IActorResolver
    {
        private readonly Dictionary<LogicalActorKey, ActorSnapshot> actors = actors.ToDictionary(static actor => actor.LogicalKey);

        public bool TryResolve(LogicalActorKey key, out ActorSnapshot actor)
            => actors.TryGetValue(key, out actor!);
    }

    private sealed class FakeAppearanceMemory(AppearanceData current) : IAppearanceMemory
    {
        public AppearanceData Current { get; private set; } = current;
        public AppearanceData? FailedAppearance { get; init; }

        public bool TryCapture(ActorSnapshot actor, out AppearanceData appearance)
        {
            appearance = Current;
            return true;
        }

        public bool TryWrite(ActorSnapshot actor, AppearanceData appearance)
        {
            if (ReferenceEquals(appearance, FailedAppearance))
                return false;
            Current = appearance;
            return true;
        }

        public bool IsApplied(ActorSnapshot actor, AppearanceData appearance)
            => ReferenceEquals(Current, appearance);
    }

    private sealed class FakeOutfitMemory(Dictionary<LogicalActorKey, OutfitData> current) : IOutfitMemory
    {
        public Dictionary<LogicalActorKey, OutfitData> Current { get; } = current;

        public bool TryCapture(ActorSnapshot actor, out OutfitData outfit)
            => Current.TryGetValue(actor.LogicalKey, out outfit!);

        public bool TryApply(ActorSnapshot actor, OutfitData outfit)
        {
            Current[actor.LogicalKey] = outfit;
            return true;
        }

        public bool IsApplied(ActorSnapshot actor, OutfitData outfit)
            => Current.TryGetValue(actor.LogicalKey, out var currentOutfit) && ReferenceEquals(currentOutfit, outfit);
    }

    private sealed class FakeRedrawBackend : IRedrawBackend
    {
        public bool TryDisable(ActorSnapshot actor) => true;
        public bool TryEnable(ActorSnapshot actor) => true;
    }

    private sealed class FakeContext : IClientContext
    {
        public uint TerritoryId => 30;
        public bool IsLoggedIn => true;
        public bool IsGPosing => false;
    }
}
