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
    public void EquipmentPickerEditsSourceAndExactActorWhilePreservingDyesAndOriginal()
    {
        var actor = Snapshot(1);
        var other = Snapshot(2);
        var original = Outfit(10);
        original = original with { Equipment = original.Equipment.SetItem(1,
            original.Equipment[1] with { Color1 = new(0.2f, 0.3f, 0.4f) }) };
        var memory = new FakeOutfitMemory(new() { [actor.LogicalKey] = original, [other.LogicalKey] = Outfit(20) });
        var store = new OutfitOverrideStore();
        using var service = new BulkOutfitService(new FakeResolver(actor, other), memory,
            new FakeContext(), store, NullDiagnosticLog.Instance);
        service.ProcessNextFrame();
        Assert.True(service.RefreshSource(actor.LogicalKey, out _));
        Assert.True(service.SelectEquipment(new(1, 9005, 2), null, out _));
        Assert.Empty(memory.ApplyCalls);
        Assert.Equal(original.Equipment[1] with { Set = 9005, Variant = 2 }, service.SourceOutfit!.Equipment[1]);
        Assert.True(service.SelectEquipment(new(10, 900, 3, 17), actor.LogicalKey, out _));
        service.ProcessNextFrame();
        service.ProcessNextFrame();
        Assert.Equal(new FacewearAppearance(true, 17), memory.Rendered[actor.LogicalKey].Facewear);
        Assert.Equal(original.Equipment, memory.Rendered[actor.LogicalKey].Equipment);
        Assert.Equal(original.Facewear, service.SourceOutfit.Facewear);
        Assert.Single(memory.ApplyCalls);
        Assert.Equal(actor.LogicalKey, memory.ApplyCalls[0].Actor);
        Assert.True(store.TryGet(actor.LogicalKey, out var state));
        Assert.Same(original, state.Original);
        Assert.True(service.SelectEquipment(new(1, 9006, 4), actor.LogicalKey, out _));
        service.ProcessNextFrame();
        service.ProcessNextFrame();
        Assert.Equal(original.Equipment[1] with { Set = 9006, Variant = 4 }, memory.Rendered[actor.LogicalKey].Equipment[1]);
        Assert.Equal(new FacewearAppearance(true, 17), memory.Rendered[actor.LogicalKey].Facewear);
        Assert.True(service.StartRestore(actor.LogicalKey, out _));
        service.ProcessNextFrame();
        service.ProcessNextFrame();
        Assert.True(OutfitDataValueComparer.AreEqual(original, memory.Rendered[actor.LogicalKey]));
    }

    [Fact]
    public void FreeColorSourceApplyCopyAndRestorePreserveOriginal()
    {
        var actor = Snapshot(1);
        var original = Outfit(10);
        var memory = new FakeOutfitMemory(new() { [actor.LogicalKey] = original });
        var store = new OutfitOverrideStore();
        using var service = new BulkOutfitService(new FakeResolver(actor), memory,
            new FakeContext(), store, NullDiagnosticLog.Instance);
        service.ProcessNextFrame();
        Assert.True(service.RefreshSource(actor.LogicalKey, out _));
        service.SetSourceColor(OutfitSlot.Body, 0, new(0, 0.4f, 1));
        service.SetSourceColor(OutfitSlot.Body, 1, new(1, 0, 0));
        Assert.Empty(memory.ApplyCalls);
        Assert.True(service.StartApply([actor.LogicalKey], out _));
        service.SetSourceColor(OutfitSlot.Body, 0, null);
        service.ProcessNextFrame();
        service.ProcessNextFrame();
        Assert.Equal(new DyeColor(0, 0.4f, 1), memory.Rendered[actor.LogicalKey].Equipment[1].Color1);
        Assert.True(service.RefreshSource(actor.LogicalKey, out _));
        Assert.Equal(new DyeColor(1, 0, 0), service.SourceOutfit!.Equipment[1].Color2);
        Assert.True(store.TryGet(actor.LogicalKey, out var state));
        Assert.Same(original, state.Original);
        Assert.True(service.StartRestore(actor.LogicalKey, out _));
        service.ProcessNextFrame();
        service.ProcessNextFrame();
        Assert.True(OutfitDataValueComparer.AreEqual(original, memory.Rendered[actor.LogicalKey]));
        Assert.False(store.TryGet(actor.LogicalKey, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ClearSourceDyeRemovesOnlySelectedChannelAndRestoresOriginal(int channel)
    {
        // TEST_ONLY memory; normal source edit, Apply and Restore service path.
        var actor = Snapshot(1);
        var original = Outfit(10);
        original = original with { Equipment = original.Equipment.SetItem(1,
            original.Equipment[1] with { Stain1 = 7, Stain2 = 9,
                Color1 = new(1, 1, 1), Color2 = new(0.2f, 0.3f, 0.4f) }) };
        var memory = new FakeOutfitMemory(new() { [actor.LogicalKey] = original });
        var store = new OutfitOverrideStore();
        using var service = new BulkOutfitService(new FakeResolver(actor), memory,
            new FakeContext(), store, NullDiagnosticLog.Instance);
        service.ProcessNextFrame();
        Assert.True(service.RefreshSource(actor.LogicalKey, out _));
        service.ClearSourceDye(OutfitSlot.Body, channel);
        var armor = original.Equipment[1];
        var expected = original with { Equipment = original.Equipment.SetItem(1,
            channel == 0 ? armor with { Stain1 = 0, Color1 = null }
                : armor with { Stain2 = 0, Color2 = null }) };
        Assert.True(OutfitDataValueComparer.AreEqual(expected, service.SourceOutfit!));
        Assert.Empty(memory.ApplyCalls);
        Assert.True(service.StartApply([actor.LogicalKey], out _));
        service.ProcessNextFrame();
        service.ProcessNextFrame();
        Assert.True(OutfitDataValueComparer.AreEqual(expected, memory.Rendered[actor.LogicalKey]));
        Assert.True(store.TryGet(actor.LogicalKey, out var state));
        Assert.Same(original, state.Original);
        service.SetSourceColor(OutfitSlot.Body, channel, new(1, 1, 1));
        var white = service.SourceOutfit!.Equipment[1];
        Assert.Equal(new DyeColor(1, 1, 1), channel == 0 ? white.Color1 : white.Color2);
        Assert.Equal((byte)0, channel == 0 ? white.Stain1 : white.Stain2);
        Assert.True(service.StartRestore(actor.LogicalKey, out _));
        service.ProcessNextFrame();
        service.ProcessNextFrame();
        Assert.True(OutfitDataValueComparer.AreEqual(original, memory.Rendered[actor.LogicalKey]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TargetApplyBindsExactRepresentationAndDoesNotSwitchWhenItDisappears(bool disappears)
    {
        var actor = Snapshot(1);
        var target = actor with
        {
            RepresentationKey = actor.RepresentationKey with { ObjectIndex = 202, GameObjectId = 202, IsGPoseRepresentation = true },
        };
        var desired = HumanAppearance(2, (byte)NpcAge.Young);
        var memory = new FakeAppearanceMemory(HumanAppearance(1, (byte)NpcAge.Normal));
        var resolver = new ExactTargetResolver(actor, target);
        var context = new FakeContext();
        using var redraw = new RedrawCoordinator(resolver, new FakeRedrawBackend(memory), context);
        using var service = new AppearanceApplyService(resolver, context, redraw, NullDiagnosticLog.Instance);
        ActorSnapshot? published = null;
        var persistence = new ActorAppearancePersistence();
        service.OperationCompleted += (_, key, representation, applied, succeeded) =>
        {
            // TEST_ONLY binding of the same exact resolution and existing completion
            // helpers; the native Plugin callback wiring is reviewed separately.
            if (!resolver.TryResolve(key, representation, out var current)
                || !Plugin.CanPublishAppearanceCompletion(succeeded, representation, current))
                return;
            published = ActorRegistry.ApplyManagedAppearance(current, applied);
            persistence.RecordModel(current, applied);
        };
        Assert.True(service.TryApply(target, desired, out _, out _));
        resolver.TargetAvailable = !disappears;
        Process(redraw, 3);
        Assert.Equal(disappears ? RedrawStage.Cancelled : RedrawStage.Completed, redraw.LastResult!.Stage);
        Assert.Equal(target.RepresentationKey, redraw.LastResult.TargetRepresentation);
        // On disappearance only, the normal lookup classifies the existing error;
        // it is never used as a replacement recipient.
        Assert.Equal(disappears ? 1 : 0, resolver.DefaultResolveCalls);
        if (!disappears)
        {
            Assert.Same(desired, memory.Current);
            Assert.Equal(target.RepresentationKey, published!.RepresentationKey);
            Assert.True(published.IsAppearanceManaged);
            Assert.False(actor.IsAppearanceManaged);
            Assert.Same(desired, persistence.GetModel(actor.LogicalKey));
        }
        else
        {
            Assert.NotSame(desired, memory.Current);
            Assert.Null(published);
            Assert.Null(persistence.GetModel(actor.LogicalKey));
        }
    }

    private sealed class ExactTargetResolver(ActorSnapshot normal, ActorSnapshot target) : IActorResolver
    {
        public bool TargetAvailable { get; set; } = true;
        public int DefaultResolveCalls { get; private set; }
        public bool TryResolve(LogicalActorKey key, out ActorSnapshot snapshot)
        {
            DefaultResolveCalls++;
            snapshot = normal;
            return key == normal.LogicalKey;
        }
        public bool TryResolve(LogicalActorKey key, ActorRepresentationKey representation, out ActorSnapshot snapshot)
        {
            snapshot = target;
            return TargetAvailable && key == target.LogicalKey && representation == target.RepresentationKey;
        }
    }

    [Fact]
    public void ActorRestoreBatchUsesItsDelegateOncePerFrameWithoutHumanCapture()
    {
        var first = Snapshot(1);
        var second = Snapshot(2);
        var memory = new FakeOutfitMemory(new()); // No Human outfit is available.
        using var service = new BulkOutfitService(new FakeResolver(first, second), memory,
            new FakeContext(), new OutfitOverrideStore(), NullDiagnosticLog.Instance);
        service.ProcessNextFrame();
        var calls = new List<LogicalActorKey>();
        Assert.True(service.StartActorRestore([first.LogicalKey, second.LogicalKey], key =>
        {
            calls.Add(key);
            return key == first.LogicalKey;
        }, out _));
        Assert.False(service.StartActorRestore([first.LogicalKey], _ => throw new Exception("Replaced"), out _));
        service.ProcessNextFrame();
        Assert.Equal([first.LogicalKey], calls);
        service.ProcessNextFrame();
        Assert.Equal([first.LogicalKey, second.LogicalKey], calls);
        Assert.Equal(1, service.CurrentOperation!.Succeeded);
        Assert.Equal(1, service.CurrentOperation.Failed);
        Assert.Empty(memory.ApplyCalls);
        service.ProcessNextFrame();
        Assert.Null(service.CurrentOperation);
        Assert.False(service.StartActorRestore([], _ => throw new Exception("Empty"), out _));
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("logout")]
    [InlineData("territory")]
    public void ActorRestoreBatchDoesNotInvokeUnprocessedTargetsAfterCancellation(string reason)
    {
        var actor = Snapshot(1);
        var context = new FakeContext();
        using var service = new BulkOutfitService(new FakeResolver(actor), new FakeOutfitMemory(new()),
            context, new OutfitOverrideStore(), NullDiagnosticLog.Instance);
        service.ProcessNextFrame();
        var calls = 0;
        Assert.True(service.StartActorRestore([actor.LogicalKey], _ => { ++calls; return true; }, out _));
        if (reason == "cancel") service.Cancel();
        if (reason == "logout") context.IsLoggedIn = false;
        if (reason == "territory") context.TerritoryId++;
        service.ProcessNextFrame();
        Assert.Equal(0, calls);
        Assert.Null(service.CurrentOperation);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ActorRestoreBatchUsesStoredOriginalAndCountsTerminalRestoreResult(bool canApply)
    {
        var actor = Snapshot(1);
        var original = Outfit(1);
        var desired = Outfit(9);
        var store = new OutfitOverrideStore();
        store.SetDesired(actor.LogicalKey, original, desired);
        var memory = new FakeOutfitMemory(new() { [actor.LogicalKey] = desired }) { ApplySucceeds = canApply };
        using var service = new BulkOutfitService(new FakeResolver(actor), memory,
            new FakeContext(), store, NullDiagnosticLog.Instance);
        service.ProcessNextFrame();
        var completions = 0;
        Assert.True(service.StartActorRestore([actor.LogicalKey], key =>
        {
            if (!service.RestoreOriginalOutfitNow(key)) return false;
            ++completions; // TEST_ONLY stand-in for shared Plugin clear/pin/redraw completion.
            return false; // Native redraw failure must not be counted as success.
        }, out _));
        service.ProcessNextFrame();
        Assert.Equal(original, Assert.Single(memory.ApplyCalls).Outfit);
        Assert.Equal(canApply ? 1 : 0, completions);
        Assert.Equal(!canApply, store.TryGet(actor.LogicalKey, out _));
        Assert.Equal(1, service.CurrentOperation!.Failed);
        Assert.Equal(0, service.CurrentOperation.Succeeded);
    }

    [Fact]
    public void ActorRestoreExceptionDoesNotRetryOrPreventTheNextActor()
    {
        var first = Snapshot(1);
        var second = Snapshot(2);
        using var service = new BulkOutfitService(new FakeResolver(first, second), new FakeOutfitMemory(new()),
            new FakeContext(), new OutfitOverrideStore(), NullDiagnosticLog.Instance);
        service.ProcessNextFrame();
        var calls = new List<LogicalActorKey>();
        service.StartActorRestore([first.LogicalKey, second.LogicalKey], key =>
        {
            calls.Add(key);
            if (key == first.LogicalKey) throw new InvalidOperationException("Native failure");
            return true;
        }, out _);
        service.ProcessNextFrame();
        service.ProcessNextFrame();
        Assert.Equal([first.LogicalKey, second.LogicalKey], calls);
        Assert.Equal(1, service.CurrentOperation!.Failed);
        Assert.Equal(1, service.CurrentOperation.Succeeded);
    }

    [Fact]
    public void TerritoryChangeKeepsCompletedOutfitOriginalAndDesired()
    {
        var actor = Snapshot(1);
        var original = Outfit(1);
        var desired = Outfit(10);
        var store = new OutfitOverrideStore();
        store.SetDesired(actor.LogicalKey, original, desired);
        var context = new FakeContext();
        using var service = new BulkOutfitService(new FakeResolver(actor), new FakeOutfitMemory(new()), context, store, NullDiagnosticLog.Instance);
        service.ProcessNextFrame();
        context.TerritoryId = 40;
        service.ProcessNextFrame();
        Assert.True(store.TryGet(actor.LogicalKey, out var retained));
        Assert.Same(original, retained.Original);
        Assert.Same(desired, retained.Desired);
    }

    [Fact]
    public void AppearanceApplyRedrawsSelectedNpcWithoutCapturingOrWritingOriginal()
    {
        var actor = Snapshot(1);
        var original = HumanAppearance(1, (byte)NpcAge.Normal, 1.16f);
        var desired = HumanAppearance(2, (byte)NpcAge.Young, 0.84f);
        var memory = new FakeAppearanceMemory(original);
        var resolver = new FakeResolver(actor);
        var context = new FakeContext();
        using var redraw = new RedrawCoordinator(resolver, new FakeRedrawBackend(memory), context);
        using var service = new AppearanceApplyService(resolver, context, redraw, NullDiagnosticLog.Instance);

        Assert.True(service.TryApply(actor.LogicalKey, desired, out var operationId, out _));
        Assert.NotEqual(Guid.Empty, operationId);
        Assert.Equal(operationId, service.LastOperationId);
        Assert.Null(service.LastSucceeded);
        Process(redraw, 3);

        Assert.Same(desired, memory.Current);
        Assert.Equal(0, memory.CaptureCount);
        Assert.True(service.LastSucceeded);
    }

    [Fact]
    public void AppearanceApplyDoesNotReadmitTheSelectedPayload()
    {
        var actor = Snapshot(1);
        var desired = AppearanceData.Create(
            0,
            ModelCategory.Human,
            0,
            AppearanceCompleteness.ModelOnly,
            Array.Empty<byte>(),
            Array.Empty<ulong>(),
            0f);
        var memory = new FakeAppearanceMemory(HumanAppearance(1, (byte)NpcAge.Normal));
        var resolver = new FakeResolver(actor);
        var context = new FakeContext();
        using var redraw = new RedrawCoordinator(resolver, new FakeRedrawBackend(memory), context);
        using var service = new AppearanceApplyService(resolver, context, redraw, NullDiagnosticLog.Instance);

        Assert.True(service.TryApply(actor.LogicalKey, desired, out _));
        Process(redraw, 3);

        Assert.Same(desired, memory.Current);
        Assert.True(service.LastSucceeded);
    }

    [Fact]
    public void SecondAppearanceApplyUsesOnlyTheSecondSelectedNpc()
    {
        var actor = Snapshot(1);
        var original = HumanAppearance(1, (byte)NpcAge.Normal, 1.16f);
        var first = HumanAppearance(2, (byte)NpcAge.Normal, 0.9f);
        var second = HumanAppearance(3, (byte)NpcAge.Normal, 0.84f);
        var memory = new FakeAppearanceMemory(original);
        var resolver = new FakeResolver(actor);
        var context = new FakeContext();
        using var redraw = new RedrawCoordinator(resolver, new FakeRedrawBackend(memory), context);
        using var service = new AppearanceApplyService(resolver, context, redraw, NullDiagnosticLog.Instance);

        Assert.True(service.TryApply(actor.LogicalKey, first, out _));
        Process(redraw, 3);
        Assert.True(service.TryApply(actor.LogicalKey, second, out _));
        Process(redraw, 3);

        Assert.Same(second, memory.Current);
    }

    [Fact]
    public void ExplicitSecondApplyIsRejectedWithoutCancellingPendingFirstApply()
    {
        var actor = Snapshot(1);
        var memory = new FakeAppearanceMemory(HumanAppearance(1, (byte)NpcAge.Normal));
        var resolver = new FakeResolver(actor);
        var context = new FakeContext();
        using var redraw = new RedrawCoordinator(resolver, new FakeRedrawBackend(memory), context);
        using var service = new AppearanceApplyService(resolver, context, redraw, NullDiagnosticLog.Instance);
        var first = HumanAppearance(2, (byte)NpcAge.Normal);
        var second = HumanAppearance(3, (byte)NpcAge.Young);

        Assert.True(service.TryApply(actor.LogicalKey, first, out _));
        Assert.False(service.TryApply(actor.LogicalKey, second, out var message));
        Assert.Contains("already pending", message);
        Process(redraw, 3);

        Assert.Same(first, memory.Current);
    }

    [Fact]
    public void ApplyRejectsLoggedOutOrPreviousTerritoryActorBeforeQueueing()
    {
        var loggedOutContext = new FakeContext { IsLoggedIn = false };
        var actor = Snapshot(1);
        var memory = new FakeAppearanceMemory(HumanAppearance(1, (byte)NpcAge.Normal));
        var resolver = new FakeResolver(actor);
        using var loggedOutRedraw = new RedrawCoordinator(resolver, new FakeRedrawBackend(memory), loggedOutContext);
        using var loggedOutService = new AppearanceApplyService(resolver, loggedOutContext, loggedOutRedraw, NullDiagnosticLog.Instance);

        Assert.False(loggedOutService.TryApply(actor.LogicalKey, HumanAppearance(2, (byte)NpcAge.Young), out _));
        Assert.False(loggedOutService.IsPending(actor.LogicalKey));

        var previousTerritory = actor with
        {
            RepresentationKey = actor.RepresentationKey with { TerritoryId = 29 },
        };
        var context = new FakeContext();
        using var staleRedraw = new RedrawCoordinator(new FakeResolver(previousTerritory), new FakeRedrawBackend(memory), context);
        using var staleService = new AppearanceApplyService(new FakeResolver(previousTerritory), context, staleRedraw, NullDiagnosticLog.Instance);

        Assert.False(staleService.TryApply(previousTerritory.LogicalKey, HumanAppearance(2, (byte)NpcAge.Young), out _));
        Assert.False(staleService.IsPending(previousTerritory.LogicalKey));
    }

    [Fact]
    public void DisposeReportsOneTerminalCancellationForAcceptedApply()
    {
        var actor = Snapshot(1);
        var memory = new FakeAppearanceMemory(HumanAppearance(1, (byte)NpcAge.Normal));
        var resolver = new FakeResolver(actor);
        var context = new FakeContext();
        using var redraw = new RedrawCoordinator(resolver, new FakeRedrawBackend(memory), context);
        using var service = new AppearanceApplyService(resolver, context, redraw, NullDiagnosticLog.Instance);
        var completions = new List<bool>();
        service.OperationCompleted += (_, _, _, _, succeeded) => completions.Add(succeeded);

        Assert.True(service.TryApply(actor.LogicalKey, HumanAppearance(2, (byte)NpcAge.Young), out _));
        Assert.Null(service.LastSucceeded);
        service.Dispose();

        Assert.Equal([false], completions);
        Assert.False(service.IsPending(actor.LogicalKey));
        Assert.False(service.LastSucceeded);
    }

    [Fact]
    public void CompletionCarriesTheExactRepresentationResolvedWhenApplyWasAccepted()
    {
        var actor = Snapshot(1);
        var memory = new FakeAppearanceMemory(HumanAppearance(1, (byte)NpcAge.Normal));
        var resolver = new FakeResolver(actor);
        using var redraw = new RedrawCoordinator(resolver, new FakeRedrawBackend(memory), new FakeContext());
        using var service = new AppearanceApplyService(
            resolver,
            new FakeContext(),
            redraw,
            NullDiagnosticLog.Instance);
        ActorRepresentationKey? completedRepresentation = null;
        service.OperationCompleted += (_, _, representation, _, succeeded) =>
        {
            Assert.True(succeeded);
            completedRepresentation = representation;
        };

        Assert.True(service.TryApply(actor.LogicalKey, HumanAppearance(2, (byte)NpcAge.Young), out _));
        Process(redraw, 3);

        Assert.Equal(actor.RepresentationKey, completedRepresentation);
    }

    [Fact]
    public void CompletionPublishesOnlyWhileTheAcceptedRepresentationIsStillCurrent()
    {
        var accepted = Snapshot(1);
        var changed = accepted with
        {
            RepresentationKey = accepted.RepresentationKey with { GameObjectId = 999 },
        };

        Assert.True(Plugin.CanPublishAppearanceCompletion(true, accepted.RepresentationKey, accepted));
        Assert.False(Plugin.CanPublishAppearanceCompletion(true, accepted.RepresentationKey, changed));
        Assert.False(Plugin.CanPublishAppearanceCompletion(false, accepted.RepresentationKey, accepted));
    }

    [Fact]
    public void LaterActorChangeIsNotOverwrittenByTheSelectedNpc()
    {
        var actor = Snapshot(1);
        var original = HumanAppearance(1, (byte)NpcAge.Normal, 1.16f);
        var selectedNpc = HumanAppearance(2, (byte)NpcAge.Young, 0.84f);
        var laterActorState = HumanAppearance(3, (byte)NpcAge.Normal, 1.01f);
        var memory = new FakeAppearanceMemory(original);
        var resolver = new FakeResolver(actor);
        var context = new FakeContext();
        using var redraw = new RedrawCoordinator(resolver, new FakeRedrawBackend(memory), context);
        using var service = new AppearanceApplyService(
            resolver,
            context,
            redraw,
            NullDiagnosticLog.Instance);

        Assert.True(service.TryApply(actor.LogicalKey, selectedNpc, out _));
        Process(redraw, 3);
        memory.SetRendered(laterActorState);

        service.ProcessContext();
        Process(redraw, 8);

        Assert.Same(laterActorState, memory.Current);
    }

    [Fact]
    public void AppearanceApplyDoesNotRequireOriginalScale()
    {
        var actor = Snapshot(1);
        var original = HumanAppearance(1, (byte)NpcAge.Normal);
        var desired = HumanAppearance(2, (byte)NpcAge.Normal, 0.84f);
        var memory = new FakeAppearanceMemory(original);
        var resolver = new FakeResolver(actor);
        using var redraw = new RedrawCoordinator(resolver, new FakeRedrawBackend(memory), new FakeContext());
        using var service = new AppearanceApplyService(
            resolver,
            new FakeContext(),
            redraw,
            NullDiagnosticLog.Instance);

        Assert.True(service.TryApply(actor.LogicalKey, desired, out _));
        Process(redraw, 3);

        Assert.Equal(0.84f, memory.Current.ModelScale);
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

        Assert.Same(source, memory.Rendered[firstActor.LogicalKey]);
        Assert.Same(secondOriginal, memory.Rendered[secondActor.LogicalKey]);

        service.ProcessNextFrame();
        service.ProcessNextFrame();
        Assert.Same(source, memory.Rendered[secondActor.LogicalKey]);
        Assert.Equal(2, store.States.Count);

        Assert.True(service.StartRestore(out _));
        service.ProcessNextFrame();
        service.ProcessNextFrame();
        service.ProcessNextFrame();

        Assert.Same(firstOriginal, memory.Current[firstActor.LogicalKey]);
        Assert.Same(secondOriginal, memory.Current[secondActor.LogicalKey]);
        Assert.Same(firstOriginal, memory.Rendered[firstActor.LogicalKey]);
        Assert.Same(secondOriginal, memory.Rendered[secondActor.LogicalKey]);
        Assert.Empty(store.States);
    }

    [Fact]
    public void BulkApplyWithoutSourceRefreshAppliesVisibleEmptySourceState()
    {
        var actor = Snapshot(2);
        var original = Outfit(20) with { HatVisible = false, VisorToggled = true };
        var memory = new FakeOutfitMemory(new Dictionary<LogicalActorKey, OutfitData>
        {
            [actor.LogicalKey] = original,
        });
        var store = new OutfitOverrideStore();
        using var service = new BulkOutfitService(
            new FakeResolver(actor),
            memory,
            new FakeContext(),
            store,
            NullDiagnosticLog.Instance);

        var initialSource = Assert.IsType<OutfitData>(service.SourceOutfit);
        Assert.All(initialSource.Equipment, static armor => Assert.Equal(default, armor));
        Assert.Equal(new FacewearAppearance(true, 0), initialSource.Facewear);
        Assert.Empty(memory.ApplyCalls);
        Assert.True(service.StartApply([actor.LogicalKey], out _));
        service.ProcessNextFrame();
        service.ProcessNextFrame();

        var applied = memory.Rendered[actor.LogicalKey];
        Assert.All(applied.Equipment, static armor => Assert.Equal(default, armor));
        Assert.Equal(new FacewearAppearance(true, 0), applied.Facewear);
        Assert.True(OutfitDataValueComparer.AreEqual(initialSource, applied));
        Assert.True(store.TryGet(actor.LogicalKey, out var state));
        Assert.Same(original, state.Original);
        Assert.True(OutfitDataValueComparer.AreEqual(applied, state.Desired));
    }

    [Fact]
    public void InitialEmptySourceCanBeEditedBeforeCopyAndCopyReplacesIt()
    {
        var actor = Snapshot(1);
        var original = Outfit(20);
        var memory = new FakeOutfitMemory(new() { [actor.LogicalKey] = original });
        using var service = new BulkOutfitService(new FakeResolver(actor), memory,
            new FakeContext(), new OutfitOverrideStore(), NullDiagnosticLog.Instance);
        Assert.True(service.SelectEquipment(new(1, 9005, 2), null, out _));
        service.SetSourceColor(OutfitSlot.Body, 0, new(1, 0, 0));
        Assert.Equal(new ArmorAppearance(9005, 2, 0, 0) { Color1 = new(1, 0, 0) },
            service.SourceOutfit!.Equipment[1]);
        Assert.All(service.SourceOutfit.Equipment.Where((_, index) => index != 1),
            armor => Assert.Equal(default, armor));
        Assert.Empty(memory.ApplyCalls);
        Assert.True(service.RefreshSource(actor.LogicalKey, out _));
        Assert.True(OutfitDataValueComparer.AreEqual(original, service.SourceOutfit!));
        Assert.Empty(memory.ApplyCalls);
    }

    [Fact]
    public void BulkApplyDoesNotWriteAutomaticRollbackAndContinuesBatch()
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
        Assert.Single(memory.ApplyCalls, call => call.Actor == failedActor.LogicalKey);
        Assert.Same(source, memory.Rendered[successfulActor.LogicalKey]);
        Assert.False(store.TryGet(failedActor.LogicalKey, out _));
        Assert.True(store.TryGet(successfulActor.LogicalKey, out _));
        Assert.Contains("1 succeeded", service.LastStatus);
        Assert.Contains("1 failed", service.LastStatus);
    }

    [Fact]
    public void ExplicitBulkCancelPublishesOneTerminalFailureForTheUnprocessedActor()
    {
        var actor = Snapshot(2);
        var current = Outfit(20);
        var desired = Outfit(30);
        var memory = new FakeOutfitMemory(new Dictionary<LogicalActorKey, OutfitData>
        {
            [actor.LogicalKey] = current,
        });
        using var service = new BulkOutfitService(
            new FakeResolver(actor),
            memory,
            new FakeContext(),
            new OutfitOverrideStore(),
            NullDiagnosticLog.Instance);
        var completions = new List<(LogicalActorKey Actor, OutfitData? Desired, bool Succeeded)>();
        service.ActorOperationCompleted += (key, _, completedDesired, succeeded)
            => completions.Add((key, completedDesired, succeeded));

        Assert.True(service.StartPersistentApply(actor.LogicalKey, desired, out _));
        service.Cancel();
        service.ProcessNextFrame();
        service.ProcessNextFrame();

        Assert.Equal([(actor.LogicalKey, desired, false)], completions);
        Assert.Empty(memory.ApplyCalls);
        Assert.Null(service.CurrentOperation);
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

        Assert.Same(firstOriginal, memory.Rendered[first.LogicalKey]);
        Assert.Same(desired, memory.Rendered[second.LogicalKey]);
        Assert.False(store.TryGet(first.LogicalKey, out _));
        Assert.True(store.TryGet(second.LogicalKey, out _));
    }

    [Fact]
    public void UnequipPublishesTheAppliedDesiredOutfit()
    {
        var actor = Snapshot(1);
        var original = Outfit(20);
        var memory = new FakeOutfitMemory(new Dictionary<LogicalActorKey, OutfitData>
        {
            [actor.LogicalKey] = original,
        });
        using var service = new BulkOutfitService(
            new FakeResolver(actor),
            memory,
            new FakeContext(),
            new OutfitOverrideStore(),
            NullDiagnosticLog.Instance);
        (LogicalActorKey Actor, BulkOperationType Type, OutfitData? Desired, bool Succeeded)? completed = null;
        service.ActorOperationCompleted += (key, type, desired, succeeded)
            => completed = (key, type, desired, succeeded);

        Assert.True(service.StartUnequip([actor.LogicalKey], out _));
        service.ProcessNextFrame();
        service.ProcessNextFrame();

        Assert.NotNull(completed);
        Assert.Equal(actor.LogicalKey, completed.Value.Actor);
        Assert.Equal(BulkOperationType.UnequipAll, completed.Value.Type);
        Assert.True(completed.Value.Succeeded);
        var desired = Assert.IsType<OutfitData>(completed.Value.Desired);
        Assert.All(desired.Equipment, static armor => Assert.Equal((ushort)0, armor.Set));
    }

    [Fact]
    public void SourceSlotUnequipClearsOnlyTheSelectedEquipmentSlot()
    {
        var actor = Snapshot(1);
        var source = Outfit(20);
        var memory = new FakeOutfitMemory(new Dictionary<LogicalActorKey, OutfitData>
        {
            [actor.LogicalKey] = source,
        });
        using var service = new BulkOutfitService(
            new FakeResolver(actor),
            memory,
            new FakeContext(),
            new OutfitOverrideStore(),
            NullDiagnosticLog.Instance);

        Assert.True(service.RefreshSource(actor.LogicalKey, out _));
        Assert.True(service.TryUnequipSourceSlot(OutfitSlot.Body, out _));

        var edited = Assert.IsType<OutfitData>(service.SourceOutfit);
        Assert.Equal(default, edited.Equipment[(int)OutfitSlot.Body]);
        foreach (var slot in Enum.GetValues<OutfitSlot>().Where(static slot => slot != OutfitSlot.Body))
            Assert.Equal(source.Equipment[(int)slot], edited.Equipment[(int)slot]);
        Assert.Equal(source.Facewear, edited.Facewear);
        Assert.Equal(source.HatVisible, edited.HatVisible);
        Assert.Equal(source.VisorToggled, edited.VisorToggled);
    }

    [Fact]
    public void RefreshSourceUsesRenderedOutfit()
    {
        var actor = Snapshot(1);
        var backing = Outfit(10);
        var rendered = Outfit(20);
        var memory = new FakeOutfitMemory(new Dictionary<LogicalActorKey, OutfitData>
        {
            [actor.LogicalKey] = backing,
        });
        memory.SetRendered(actor.LogicalKey, rendered);
        using var service = new BulkOutfitService(
            new FakeResolver(actor),
            memory,
            new FakeContext(),
            new OutfitOverrideStore(),
            NullDiagnosticLog.Instance);

        Assert.True(service.RefreshSource(actor.LogicalKey, out _));

        Assert.Same(rendered, service.SourceOutfit);
        Assert.Same(backing, memory.Current[actor.LogicalKey]);
    }

    [Fact]
    public void PinComparisonReadsRenderedOutfitWithoutMistakingBackingForDrift()
    {
        var actor = Snapshot(1);
        var backing = Outfit(10);
        var desired = Outfit(20);
        var memory = new FakeOutfitMemory(new() { [actor.LogicalKey] = backing });
        memory.SetRendered(actor.LogicalKey, desired);
        using var service = new BulkOutfitService(new FakeResolver(actor), memory, new FakeContext(),
            new OutfitOverrideStore(), NullDiagnosticLog.Instance);

        Assert.True(service.TryCaptureOutfit(actor.LogicalKey, out var current));
        Assert.True(OutfitDataValueComparer.AreEqual(current, desired));
        Assert.Same(backing, memory.Current[actor.LogicalKey]);
        Assert.Equal(0, memory.CaptureCount);

        memory.SetRendered(actor.LogicalKey, Outfit(30));
        Assert.True(service.TryCaptureOutfit(actor.LogicalKey, out current));
        Assert.False(OutfitDataValueComparer.AreEqual(current, desired));
        Assert.Empty(memory.ApplyCalls);
    }

    [Fact]
    public void OutfitModifiedComparesRenderedOutfitWithOriginal()
    {
        var actor = Snapshot(1);
        var original = Outfit(10);
        var changed = Outfit(20);
        var memory = new FakeOutfitMemory(new Dictionary<LogicalActorKey, OutfitData>
        {
            [actor.LogicalKey] = original,
        });
        using var service = new BulkOutfitService(
            new FakeResolver(actor),
            memory,
            new FakeContext(),
            new OutfitOverrideStore(),
            NullDiagnosticLog.Instance);

        memory.SetRendered(actor.LogicalKey, changed);
        Assert.False(service.IsOutfitModified(actor.LogicalKey));

        memory.SetRendered(actor.LogicalKey, original);
        Assert.False(service.IsOutfitModified(actor.LogicalKey));

        service.Store.SetDesired(actor.LogicalKey, original, changed);
        memory.TryApply(actor, changed);
        Assert.True(service.IsOutfitModified(actor.LogicalKey));

        memory.TryApply(actor, original);
        Assert.False(service.IsOutfitModified(actor.LogicalKey));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BulkOriginalSurvivesModelAndOutfitSwitches(bool appearanceManaged)
    {
        var actor = Snapshot(1) with { IsAppearanceManaged = appearanceManaged };
        var sourceActor = Snapshot(2);
        var backing = Outfit(10);
        var rendered = Outfit(20);
        var desired = Outfit(30);
        var memory = new FakeOutfitMemory(new Dictionary<LogicalActorKey, OutfitData>
        {
            [actor.LogicalKey] = backing,
            [sourceActor.LogicalKey] = desired,
        });
        memory.SetRendered(actor.LogicalKey, rendered);
        var store = new OutfitOverrideStore();
        using var service = new BulkOutfitService(
            new FakeResolver(actor, sourceActor),
            memory,
            new FakeContext(),
            store,
            NullDiagnosticLog.Instance);

        Assert.True(service.RefreshSource(sourceActor.LogicalKey, out _));
        Assert.True(service.StartApply([actor.LogicalKey], out _));
        service.ProcessNextFrame();
        service.ProcessNextFrame();

        Assert.True(store.TryGet(actor.LogicalKey, out var state));
        var expectedOriginal = appearanceManaged ? backing : rendered;
        Assert.Same(expectedOriginal, state.Original);
        Assert.Same(desired, state.Desired);
        service.ProcessNextFrame();

        Assert.Same(backing, memory.Current[actor.LogicalKey]);
        // A second model changes rendered C; Bulk armor never replaces backing A.
        var nextModel = Outfit(40);
        var nextBulk = Outfit(50);
        memory.SetRendered(actor.LogicalKey, nextModel);
        memory.SetRendered(sourceActor.LogicalKey, nextBulk);
        memory.CaptureUnavailable = true; // An existing Original must not be recaptured.
        Assert.True(service.RefreshSource(sourceActor.LogicalKey, out _));
        Assert.True(service.StartApply([actor.LogicalKey], out _));
        service.ProcessNextFrame();
        service.ProcessNextFrame();
        Assert.True(store.TryGet(actor.LogicalKey, out state));
        Assert.Same(expectedOriginal, state.Original);
        Assert.Same(nextBulk, state.Desired);
        Assert.Same(backing, memory.Current[actor.LogicalKey]);

        Assert.True(service.StartRestore(actor.LogicalKey, out _));
        service.ProcessNextFrame();
        service.ProcessNextFrame();
        Assert.Same(backing, memory.Current[actor.LogicalKey]);
        Assert.Same(expectedOriginal, memory.Rendered[actor.LogicalKey]);
        Assert.False(store.TryGet(actor.LogicalKey, out _));
        Assert.Equal([desired, nextBulk, expectedOriginal], memory.ApplyCalls.Select(call => call.Outfit).ToArray());
        Assert.Equal(appearanceManaged ? 1 : 0, memory.CaptureCount);
    }

    [Fact]
    public void MissingFirstManagedOriginalDoesNotWriteNpcOutfitAsOriginal()
    {
        var actor = Snapshot(1) with { IsAppearanceManaged = true };
        var backing = Outfit(10);
        var rendered = Outfit(20);
        var memory = new FakeOutfitMemory(new() { [actor.LogicalKey] = backing })
        {
            CaptureUnavailable = true,
        };
        memory.SetRendered(actor.LogicalKey, rendered);
        using var service = new BulkOutfitService(new FakeResolver(actor), memory, new FakeContext(),
            new OutfitOverrideStore(), NullDiagnosticLog.Instance);
        Assert.True(service.RefreshSource(actor.LogicalKey, out _));
        Assert.Same(rendered, service.SourceOutfit);
        Assert.True(service.StartApply([actor.LogicalKey], out _));
        service.ProcessNextFrame();
        service.ProcessNextFrame();
        Assert.Empty(memory.ApplyCalls);
        Assert.False(service.Store.TryGet(actor.LogicalKey, out _));
        Assert.Same(backing, memory.Current[actor.LogicalKey]);
        Assert.Same(rendered, memory.Rendered[actor.LogicalKey]);
    }

    [Fact]
    public void ManagedUnequipUsesRenderedMetadataButKeepsBackingOriginal()
    {
        var actor = Snapshot(1) with { IsAppearanceManaged = true };
        var backing = Outfit(10);
        var rendered = Outfit(20) with { HatVisible = false, VisorToggled = true };
        var memory = new FakeOutfitMemory(new() { [actor.LogicalKey] = backing });
        memory.SetRendered(actor.LogicalKey, rendered);
        using var service = new BulkOutfitService(new FakeResolver(actor), memory, new FakeContext(),
            new OutfitOverrideStore(), NullDiagnosticLog.Instance);
        Assert.True(service.StartUnequip([actor.LogicalKey], out _));
        service.ProcessNextFrame();
        service.ProcessNextFrame();
        Assert.True(service.Store.TryGet(actor.LogicalKey, out var state));
        Assert.Same(backing, state.Original);
        Assert.Equal(rendered.HatVisible, state.Desired.HatVisible);
        Assert.Equal(rendered.VisorToggled, state.Desired.VisorToggled);
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
            new ActorRepresentationKey(index, index, index, false, 30),
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

    private static AppearanceData Appearance(uint modelId, byte marker, float? modelScale = null)
        => AppearanceData.Create(
            modelId,
            modelId == 0 ? ModelCategory.Human : ModelCategory.Monster,
            marker,
            modelId == 0 ? AppearanceCompleteness.Complete : AppearanceCompleteness.ModelOnly,
            modelId == 0 ? [marker] : [],
            modelId == 0 ? [(ulong)marker] : [],
            modelScale);

    private static AppearanceData HumanAppearance(byte marker, byte bodyType, float? modelScale = null)
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
            Enumerable.Repeat((ulong)marker, 10),
            modelScale);
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

    private sealed class FakeAppearanceMemory(AppearanceData current) : IAppearanceMemory
    {
        public AppearanceData Current { get; private set; } = current;
        public int CaptureCount { get; private set; }

        public bool TryCapture(ActorSnapshot actor, out AppearanceData appearance)
        {
            CaptureCount++;
            appearance = Current;
            return true;
        }

        public void SetRendered(AppearanceData appearance)
            => Current = appearance;
    }

    private sealed class FakeOutfitMemory(Dictionary<LogicalActorKey, OutfitData> current) : IOutfitMemory
    {
        public Dictionary<LogicalActorKey, OutfitData> Current { get; } = current;
        public Dictionary<LogicalActorKey, OutfitData> Rendered { get; } = new(current);
        public List<(LogicalActorKey Actor, OutfitData Outfit)> ApplyCalls { get; } = [];
        public LogicalActorKey? ThrowActor { get; init; }
        public OutfitData? ThrowOutfit { get; init; }
        public bool CaptureUnavailable { get; set; }
        public bool ApplySucceeds { get; init; } = true;
        public int CaptureCount { get; private set; }

        public bool TryCapture(ActorSnapshot actor, out OutfitData outfit)
        {
            ++CaptureCount;
            if (CaptureUnavailable)
            {
                outfit = null!;
                return false;
            }
            return Current.TryGetValue(actor.LogicalKey, out outfit!);
        }

        public bool TryCaptureRendered(ActorSnapshot actor, out OutfitData outfit)
            => Rendered.TryGetValue(actor.LogicalKey, out outfit!);

        public void SetRendered(LogicalActorKey actor, OutfitData outfit)
            => Rendered[actor] = outfit;

        public bool TryApply(ActorSnapshot actor, OutfitData outfit)
        {
            ApplyCalls.Add((actor.LogicalKey, outfit));
            if (actor.LogicalKey == ThrowActor && ReferenceEquals(outfit, ThrowOutfit))
                throw new InvalidOperationException("Simulated actor-local outfit failure.");
            if (!ApplySucceeds)
                return false;
            Rendered[actor.LogicalKey] = outfit;
            return true;
        }

    }

    private sealed class FakeRedrawBackend(FakeAppearanceMemory memory) : IRedrawBackend
    {
        public bool TryDisable(ActorSnapshot actor) => true;
        public bool TryEnable(ActorSnapshot actor, AppearanceData? appearance, Guid operationId)
        {
            if (appearance is not null)
                memory.SetRendered(appearance);
            return true;
        }
    }

    private sealed class FakeContext : IClientContext
    {
        public uint TerritoryId { get; set; } = 30;
        public bool IsLoggedIn { get; set; } = true;
        public bool IsGPosing => false;
    }
}
