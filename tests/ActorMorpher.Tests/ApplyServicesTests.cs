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
        Process(redraw, 7);

        Assert.Same(desired, memory.Current);
        Assert.True(store.TryGet(actor.LogicalKey, out var state));
        Assert.Same(original, state.BaseData);

        Assert.True(service.TryRestore(actor.LogicalKey, out _));
        Process(redraw, 7);

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
        Process(redraw, 8);

        Assert.Same(original, memory.Current);
        Assert.False(store.TryGet(actor.LogicalKey, out _));
    }

    [Fact]
    public void HumanToHumanApplyPerformsCleanBaseTransitionBeforeFinalAppearance()
    {
        var actor = Snapshot(1);
        var original = HumanAppearance(1, (byte)NpcAge.Normal);
        var first = HumanAppearance(2, (byte)NpcAge.Young);
        var second = HumanAppearance(3, (byte)NpcAge.Young);
        var memory = new FakeAppearanceMemory(original);
        var resolver = new FakeResolver(actor);
        using var redraw = new RedrawCoordinator(resolver, memory, new FakeRedrawBackend(), new FakeContext());
        using var service = new AppearanceApplyService(
            resolver,
            memory,
            new FakeContext(),
            redraw,
            new AppearanceOverrideStore(),
            NullDiagnosticLog.Instance);

        Assert.True(service.TryApply(actor.LogicalKey, first, out _));
        Process(redraw, 7);
        memory.Writes.Clear();

        Assert.True(service.TryApply(actor.LogicalKey, second, out _));
        Process(redraw, 14);

        Assert.Same(second, memory.Current);
        Assert.Contains(original, memory.Writes);
        Assert.Same(second, memory.Writes[^1]);
    }

    [Fact]
    public void SpecialHumanBodyNormalizesBackingToOriginalAfterVisibleApply()
    {
        var actor = Snapshot(1);
        var original = HumanAppearance(1, (byte)NpcAge.Normal);
        var youngNpc = HumanAppearance(2, (byte)NpcAge.Young);
        var memory = new FakeAppearanceMemory(original) { NormalizeBacking = true };
        var resolver = new FakeResolver(actor);
        var store = new AppearanceOverrideStore();
        using var redraw = new RedrawCoordinator(resolver, memory, new FakeRedrawBackend(), new FakeContext());
        using var service = new AppearanceApplyService(
            resolver,
            memory,
            new FakeContext(),
            redraw,
            store,
            NullDiagnosticLog.Instance);

        Assert.True(service.TryApply(actor.LogicalKey, youngNpc, out _));
        Process(redraw, 7);

        Assert.Same(original, memory.Current);
        Assert.Same(original, memory.NormalizedAppearance);
        Assert.True(store.TryGet(actor.LogicalKey, out var state));
        Assert.Same(youngNpc, state.DesiredData);
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

    [Fact]
    public void BulkApplyRollsBackFailedActorAndContinuesBatch()
    {
        var sourceActor = Snapshot(1);
        var failedActor = Snapshot(2);
        var successfulActor = Snapshot(3);
        var source = Outfit(10);
        var failedOriginal = Outfit(20);
        var successfulOriginal = Outfit(30);
        var resolver = new FakeResolver(sourceActor, failedActor, successfulActor);
        var memory = new FakeOutfitMemory(new Dictionary<LogicalActorKey, OutfitData>
        {
            [sourceActor.LogicalKey] = source,
            [failedActor.LogicalKey] = failedOriginal,
            [successfulActor.LogicalKey] = successfulOriginal,
        })
        {
            ThrowActor = failedActor.LogicalKey,
            ThrowOutfit = source,
        };
        var store = new OutfitOverrideStore();
        using var service = new BulkOutfitService(resolver, memory, new FakeContext(), store, NullDiagnosticLog.Instance);

        Assert.True(service.RefreshSource(sourceActor.LogicalKey, out _));
        Assert.True(service.StartApply([failedActor.LogicalKey, successfulActor.LogicalKey], out _));
        service.ProcessNextFrame();
        service.ProcessNextFrame();
        service.ProcessNextFrame();
        service.ProcessNextFrame();

        Assert.Same(failedOriginal, memory.Current[failedActor.LogicalKey]);
        Assert.Same(source, memory.Current[successfulActor.LogicalKey]);
        Assert.False(store.TryGet(failedActor.LogicalKey, out _));
        Assert.True(store.TryGet(successfulActor.LogicalKey, out _));
        Assert.Contains("1 succeeded", service.LastStatus);
        Assert.Contains("1 failed", service.LastStatus);
    }

    [Fact]
    public void SingleActorRestoreLeavesOtherOutfitOverridesIntact()
    {
        var first = Snapshot(2);
        var second = Snapshot(3);
        var firstOriginal = Outfit(20);
        var secondOriginal = Outfit(30);
        var desired = Outfit(10);
        var resolver = new FakeResolver(first, second);
        var memory = new FakeOutfitMemory(new Dictionary<LogicalActorKey, OutfitData>
        {
            [first.LogicalKey] = firstOriginal,
            [second.LogicalKey] = secondOriginal,
        });
        var store = new OutfitOverrideStore();
        store.SetDesired(first.LogicalKey, firstOriginal, desired);
        store.SetDesired(second.LogicalKey, secondOriginal, desired);
        memory.TryApply(first, desired);
        memory.TryApply(second, desired);
        using var service = new BulkOutfitService(resolver, memory, new FakeContext(), store, NullDiagnosticLog.Instance);

        Assert.True(service.StartRestore(first.LogicalKey, out _));
        service.ProcessNextFrame();
        service.ProcessNextFrame();

        Assert.Same(firstOriginal, memory.Current[first.LogicalKey]);
        Assert.Same(desired, memory.Current[second.LogicalKey]);
        Assert.False(store.TryGet(first.LogicalKey, out _));
        Assert.True(store.TryGet(second.LogicalKey, out _));
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

    private static AppearanceData HumanAppearance(byte marker, byte bodyType)
    {
        var customize = Enumerable.Repeat(marker, 26).ToArray();
        customize[0] = 1;
        customize[1] = 1;
        customize[2] = bodyType;
        customize[4] = 1;
        return AppearanceData.Create(
            0,
            ModelCategory.Human,
            marker,
            AppearanceCompleteness.Complete,
            customize,
            Enumerable.Repeat((ulong)marker, 10));
    }

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

    private sealed class FakeAppearanceMemory(AppearanceData current) : IAppearanceMemory, IAppearanceBackingStore
    {
        public AppearanceData Current { get; private set; } = current;
        public AppearanceData? FailedAppearance { get; init; }
        public List<AppearanceData> Writes { get; } = [];
        public bool NormalizeBacking { get; init; }
        public AppearanceData? NormalizedAppearance { get; private set; }

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
            Writes.Add(appearance);
            return true;
        }

        public bool IsApplied(ActorSnapshot actor, AppearanceData appearance)
            => ReferenceEquals(Current, appearance);

        public bool TryNormalizeBacking(ActorSnapshot actor, AppearanceData appearance)
        {
            if (!NormalizeBacking)
                return false;
            Current = appearance;
            NormalizedAppearance = appearance;
            return true;
        }
    }

    private sealed class FakeOutfitMemory(Dictionary<LogicalActorKey, OutfitData> current) : IOutfitMemory
    {
        public Dictionary<LogicalActorKey, OutfitData> Current { get; } = current;
        public LogicalActorKey? ThrowActor { get; init; }
        public OutfitData? ThrowOutfit { get; init; }

        public bool TryCapture(ActorSnapshot actor, out OutfitData outfit)
            => Current.TryGetValue(actor.LogicalKey, out outfit!);

        public bool TryApply(ActorSnapshot actor, OutfitData outfit)
        {
            if (actor.LogicalKey == ThrowActor && ReferenceEquals(outfit, ThrowOutfit))
                throw new InvalidOperationException("Simulated actor-local outfit failure.");
            Current[actor.LogicalKey] = outfit;
            return true;
        }

        public bool IsApplied(ActorSnapshot actor, OutfitData outfit)
            => Current.TryGetValue(actor.LogicalKey, out var currentOutfit) && ReferenceEquals(currentOutfit, outfit);
    }

    private sealed class FakeRedrawBackend : IRedrawBackend
    {
        public bool TryDisable(ActorSnapshot actor) => true;
        public bool TryEnable(ActorSnapshot actor, AppearanceData? appearance) => true;
    }

    private sealed class FakeContext : IClientContext
    {
        public uint TerritoryId => 30;
        public bool IsLoggedIn => true;
        public bool IsGPosing => false;
    }
}
