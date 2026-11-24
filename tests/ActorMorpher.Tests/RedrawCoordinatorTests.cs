using System;
using System.Collections.Generic;
using System.Linq;
using ActorMorpher;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.Diagnostics;
using ActorMorpher.Interop;
using ActorMorpher.Redraw;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class RedrawCoordinatorTests
{
    [Fact]
    public void CompletesSuccessfulOperationWithoutLeavingTheActorHiddenBetweenFrames()
    {
        var fixture = new Fixture();
        using var coordinator = fixture.CreateCoordinator();
        coordinator.Enqueue(fixture.Operation());

        for (var i = 0; i < 2; ++i)
            coordinator.ProcessNextFrame();

        Assert.Equal(RedrawStage.Completed, coordinator.LastResult?.Stage);
        Assert.Equal(1, fixture.Backend.DisableCount);
        Assert.Equal(1, fixture.Backend.EnableCount);
        Assert.Equal(2, coordinator.LastResult?.FrameCount);
    }

    [Theory]
    [InlineData(true, true, 0)]
    [InlineData(false, false, 0)]
    [InlineData(null, true, 1)]
    public void EquipmentPasteUsesSlotWritesForAnAttachedActorAndRedrawForAnUnattachedActor(
        bool? slotWriteResult, bool succeeded, int redrawCount)
    {
        var fixture = new Fixture();
        var snapshot = fixture.Resolver.Snapshot;
        var target = snapshot with
        {
            ObjectKind = ObjectKind.EventNpc,
            RepresentationKey = snapshot.RepresentationKey with { TerritoryId = fixture.Context.TerritoryId },
        };
        fixture.Resolver.Snapshot = target;
        using var coordinator = fixture.CreateCoordinator();
        using var service = new AppearanceApplyService(
            fixture.Resolver, fixture.Context, coordinator, NullDiagnosticLog.Instance);
        var completions = new List<(Guid Operation, ActorRepresentationKey Target, AppearanceData Appearance, bool Succeeded)>();
        var appliedTargets = new List<ActorSnapshot>();
        service.OperationCompleted += (id, _, representation, applied, result)
            => completions.Add((id, representation, applied, result));

        var desired = Appearance(0, 2);
        Assert.True(service.TryApplyEquipmentPaste(target, desired, actor =>
        {
            appliedTargets.Add(actor);
            return slotWriteResult;
        }, out var operationId, out _));
        Assert.True(service.IsPending(target.LogicalKey));
        Assert.Empty(appliedTargets);
        Assert.Empty(completions);

        for (var frame = 0; frame < 3; ++frame)
            coordinator.ProcessNextFrame();

        Assert.Equal(target, Assert.Single(appliedTargets));
        Assert.Equal(redrawCount, fixture.Backend.DisableCount);
        Assert.Equal(redrawCount, fixture.Backend.EnableCount);
        Assert.Equal((operationId, target.RepresentationKey, desired, succeeded), Assert.Single(completions));
        Assert.False(service.IsPending(target.LogicalKey));
    }

    [Fact]
    public void CutsceneModelRecreationCannotInterveneBetweenDisableAndSelectedAppearanceEnable()
    {
        var fixture = new Fixture();
        fixture.Backend.RecreateOriginalModelBetweenFrames = true;
        using var coordinator = fixture.CreateCoordinator();
        var operation = fixture.Operation();
        var finished = new List<RedrawOperation>();
        coordinator.OperationFinished += finished.Add;
        coordinator.Enqueue(operation);

        for (var frame = 0; frame < 2; ++frame)
        {
            fixture.Backend.BeginGameFrame();
            coordinator.ProcessNextFrame();
        }

        Assert.Equal(RedrawStage.Completed, coordinator.LastResult?.Stage);
        Assert.Equal(1, fixture.Backend.DisableCount);
        Assert.Equal(1, fixture.Backend.EnableCount);
        Assert.Equal([operation.OperationId], fixture.Backend.EnabledOperationIds);
        Assert.Equal([operation.Desired], fixture.Backend.EnabledAppearances);
        Assert.Same(operation.Desired, fixture.Memory.Rendered);
        Assert.Equal(operation.OperationId, Assert.Single(finished).OperationId);
        fixture.Backend.BeginGameFrame();
        coordinator.ProcessNextFrame();
        Assert.Same(operation.Desired, fixture.Memory.Rendered);
        Assert.Single(finished);
    }

    [Fact]
    public void PassesSelectedAppearanceOnlyToTheSingleEnable()
    {
        var fixture = new Fixture();
        using var coordinator = fixture.CreateCoordinator();
        coordinator.Enqueue(fixture.Operation());

        for (var i = 0; i < 3; ++i)
            coordinator.ProcessNextFrame();

        Assert.Equal(RedrawStage.Completed, coordinator.LastResult?.Stage);
        Assert.Equal([fixture.Memory.Desired], fixture.Backend.EnabledAppearances);
        Assert.Equal([coordinator.LastResult!.OperationId], fixture.Backend.EnabledOperationIds);
        Assert.Same(fixture.Memory.Desired, fixture.Memory.Rendered);
    }

    [Fact]
    public void ActorLossCancelsWithoutWritingToStaleRepresentation()
    {
        var fixture = new Fixture();
        using var coordinator = fixture.CreateCoordinator();
        coordinator.Enqueue(fixture.Operation());
        fixture.Resolver.Available = false;

        coordinator.ProcessNextFrame();

        Assert.Equal(RedrawStage.Cancelled, coordinator.LastResult?.Stage);
    }

    [Fact]
    public void ActorLossAfterDisableDoesNotRedrawAutomatically()
    {
        var fixture = new Fixture();
        using var coordinator = fixture.CreateCoordinator();
        coordinator.Enqueue(fixture.Operation());
        fixture.Backend.AfterDisable = () => fixture.Resolver.Available = false;
        coordinator.ProcessNextFrame();
        coordinator.ProcessNextFrame();

        Assert.Equal(RedrawStage.Cancelled, coordinator.LastResult?.Stage);
        Assert.Empty(fixture.Backend.EnabledAppearances);
    }

    [Fact]
    public void CancellationAfterDisableDoesNotRedrawAutomatically()
    {
        var fixture = new Fixture();
        using var coordinator = fixture.CreateCoordinator();
        var finished = new List<RedrawOperation>();
        coordinator.OperationFinished += finished.Add;
        coordinator.Enqueue(fixture.Operation());
        fixture.Backend.AfterDisable = () => coordinator.CancelAll("Cancelled by test.");
        coordinator.ProcessNextFrame();
        coordinator.ProcessNextFrame();

        Assert.Equal(RedrawStage.Cancelled, coordinator.LastResult?.Stage);
        Assert.Empty(fixture.Backend.EnabledAppearances);
        Assert.Single(finished);
    }

    [Fact]
    public void CancelAllReportsOneTerminalResultForEveryQueuedOperation()
    {
        var fixture = new Fixture();
        using var coordinator = fixture.CreateCoordinator();
        var finished = new List<RedrawOperation>();
        coordinator.OperationFinished += finished.Add;
        coordinator.Enqueue(fixture.Operation());
        coordinator.Enqueue(RedrawOperation.Create(
            new LogicalActorKey(2, 101, 11, 21, ObjectKind.EventNpc, 30),
            new ActorRepresentationKey(2, 101, 11, false, 30),
            fixture.Memory.Desired,
            2,
            30));

        coordinator.CancelAll("Cancelled by test.");

        Assert.Equal(2, finished.Count);
        Assert.All(finished, operation => Assert.Equal(RedrawStage.Cancelled, operation.Stage));
        Assert.Equal(2, finished.Select(static operation => operation.Actor).Distinct().Count());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExceptionAfterCancellationPreservesTheSingleCancelledResult(bool subscriberThrows)
    {
        var fixture = new Fixture();
        using var coordinator = fixture.CreateCoordinator();
        var finished = new List<RedrawOperation>();
        var expected = new InvalidOperationException("Exception after cancellation.");
        coordinator.OperationFinished += operation =>
        {
            finished.Add(operation);
            if (subscriberThrows) throw expected;
        };
        fixture.Backend.AfterDisable = () =>
        {
            coordinator.CancelAll("Cancelled by test.");
            throw expected;
        };
        coordinator.Enqueue(fixture.Operation());

        coordinator.ProcessNextFrame();
        Assert.Same(expected, Assert.Throws<InvalidOperationException>(() => coordinator.ProcessNextFrame()));
        coordinator.ProcessNextFrame();

        Assert.Equal(RedrawStage.Cancelled, Assert.Single(finished).Stage);
        Assert.Equal(RedrawStage.Cancelled, coordinator.LastResult?.Stage);
        Assert.Equal("Cancelled by test.", coordinator.LastResult?.Error);
        Assert.Null(coordinator.Current);
        Assert.Equal(1, fixture.Backend.DisableCount);
        Assert.Equal(0, fixture.Backend.EnableCount);
    }

    [Fact]
    public void TerritoryChangeWhileHiddenDoesNotRedrawAutomatically()
    {
        var fixture = new Fixture();
        using var coordinator = fixture.CreateCoordinator();
        coordinator.Enqueue(fixture.Operation());
        fixture.Backend.AfterDisable = () => fixture.Context.TerritoryId = 31;
        for (var frame = 0; frame < 2; ++frame)
            coordinator.ProcessNextFrame();

        Assert.Equal(RedrawStage.Cancelled, coordinator.LastResult?.Stage);
        Assert.Empty(fixture.Backend.EnabledAppearances);
    }

    [Fact]
    public void RepresentationChangeAfterDisableCancelsBeforeEnable()
    {
        var fixture = new Fixture();
        using var coordinator = fixture.CreateCoordinator();
        coordinator.Enqueue(fixture.Operation());
        fixture.Backend.AfterDisable = () => fixture.Resolver.Snapshot = fixture.Resolver.Snapshot with
        {
            RepresentationKey = fixture.Resolver.Snapshot.RepresentationKey with { GameObjectId = 999 },
        };
        coordinator.ProcessNextFrame();
        coordinator.ProcessNextFrame();

        Assert.Equal(RedrawStage.Cancelled, coordinator.LastResult?.Stage);
        Assert.Equal("Actor representation changed.", coordinator.LastResult?.Error);
        Assert.Equal(1, fixture.Backend.DisableCount);
        Assert.Equal(0, fixture.Backend.EnableCount);
    }

    [Theory]
    [InlineData(FailurePoint.Disable)]
    [InlineData(FailurePoint.Enable)]
    public void ApplyFailureDoesNotAutomaticallyRollback(FailurePoint failure)
    {
        var fixture = new Fixture(failure);
        using var coordinator = fixture.CreateCoordinator();
        var finished = new List<RedrawOperation>();
        coordinator.OperationFinished += finished.Add;
        coordinator.Enqueue(fixture.Operation());

        for (var i = 0; i < 4; ++i)
            coordinator.ProcessNextFrame();

        Assert.Equal(RedrawStage.Failed, coordinator.LastResult?.Stage);
        Assert.Equal(1, fixture.Backend.DisableCount);
        Assert.Equal(failure == FailurePoint.Enable ? 1 : 0, fixture.Backend.EnableCount);
        Assert.Single(finished);
        if (failure == FailurePoint.Enable)
            Assert.Equal([fixture.Memory.Desired], fixture.Backend.EnabledAppearances);
    }

    [Fact]
    public void NativeApplyExceptionIsTerminalAndIsNotRetried()
    {
        var fixture = new Fixture(FailurePoint.EnableThrows);
        using var coordinator = fixture.CreateCoordinator();
        var finished = new List<RedrawOperation>();
        coordinator.OperationFinished += finished.Add;
        coordinator.Enqueue(fixture.Operation());

        for (var i = 0; i < 8; ++i)
            coordinator.ProcessNextFrame();

        Assert.Equal(RedrawStage.Failed, coordinator.LastResult?.Stage);
        Assert.Equal(1, fixture.Backend.EnableCount);
        Assert.Single(finished);
    }

    [Fact]
    public void TerminalSubscriberExceptionDoesNotPublishASecondResultOrRetryApply()
    {
        var fixture = new Fixture();
        using var coordinator = fixture.CreateCoordinator();
        var publicationCount = 0;
        coordinator.Enqueue(fixture.Operation());
        coordinator.OperationFinished += _ =>
        {
            publicationCount++;
            throw new InvalidOperationException("Subscriber failed.");
        };

        coordinator.ProcessNextFrame();
        Assert.Throws<InvalidOperationException>(() => coordinator.ProcessNextFrame());
        for (var i = 0; i < 4; ++i)
            coordinator.ProcessNextFrame();

        Assert.Equal(RedrawStage.Completed, coordinator.LastResult?.Stage);
        Assert.Null(coordinator.Current);
        Assert.Equal(1, publicationCount);
        Assert.Equal(1, fixture.Backend.EnableCount);
    }

    private sealed class Fixture
    {
        public readonly LogicalActorKey Key = new(1, 100, 10, 20, ObjectKind.Pc, 30);
        public readonly FakeMemory Memory;
        public readonly FakeBackend Backend;
        public readonly FakeContext Context = new();
        public readonly FakeResolver Resolver;
        private readonly AppearanceData desired = Appearance(100, 2);

        public Fixture(FailurePoint failure = FailurePoint.None)
        {
            Memory = new FakeMemory();
            Backend = new FakeBackend(failure, Memory);
            var representation = new ActorRepresentationKey(1, 100, 10, false);
            Resolver = new FakeResolver(new ActorSnapshot(
                Key, representation, "Actor", ObjectKind.Pc, 20, 0, 1, 0, 1, 0, 0, true));
            Memory.Desired = desired;
        }

        public RedrawCoordinator CreateCoordinator()
            => new(Resolver, Backend, Context);

        public RedrawOperation Operation()
            => RedrawOperation.Create(Key, Resolver.Snapshot.RepresentationKey, desired, 1, 30);
    }

    private sealed class FakeResolver(ActorSnapshot actor) : IActorResolver
    {
        public bool Available { get; set; } = true;
        public ActorSnapshot Snapshot { get; set; } = actor;

        public bool TryResolve(LogicalActorKey key, out ActorSnapshot snapshot)
        {
            snapshot = Snapshot;
            return Available && key == Snapshot.LogicalKey;
        }
    }

    private sealed class FakeMemory : IAppearanceMemory
    {
        public AppearanceData Desired { get; set; } = null!;
        public AppearanceData? Rendered { get; set; }

        public bool TryCapture(ActorSnapshot actor, out AppearanceData appearance)
        {
            appearance = Desired;
            return true;
        }

    }

    private sealed class FakeBackend(FailurePoint failure, FakeMemory memory) : IRedrawBackend
    {
        public int DisableCount { get; private set; }
        public int EnableCount { get; private set; }
        public List<AppearanceData?> EnabledAppearances { get; } = new();
        public List<Guid> EnabledOperationIds { get; } = new();
        public Action? AfterDisable { get; set; }
        public bool RecreateOriginalModelBetweenFrames { get; set; }
        private bool modelPresent = true;

        public void BeginGameFrame()
        {
            if (!RecreateOriginalModelBetweenFrames || modelPresent) return;
            modelPresent = true;
            memory.Rendered = Appearance(200, 1);
        }

        public bool TryDisable(ActorSnapshot actor)
        {
            DisableCount++;
            if (failure == FailurePoint.Disable) return false;
            modelPresent = false;
            AfterDisable?.Invoke();
            return true;
        }

        public bool TryEnable(ActorSnapshot actor, AppearanceData? appearance, Guid operationId)
        {
            EnableCount++;
            EnabledAppearances.Add(appearance);
            EnabledOperationIds.Add(operationId);
            if (failure == FailurePoint.EnableThrows)
                throw new InvalidOperationException("Native apply failed.");
            // A cutscene-created draw object makes Enable a no-op: no Create hook receives the selection.
            if (RecreateOriginalModelBetweenFrames && modelPresent) return false;
            var succeeded = failure switch
            {
                FailurePoint.Enable => false,
                _ => true,
            };
            if (succeeded && appearance is not null)
            {
                modelPresent = true;
                memory.Rendered = appearance;
            }
            return succeeded;
        }
    }

    public enum FailurePoint
    {
        None,
        Disable,
        Enable,
        EnableThrows,
    }

    private sealed class FakeContext : IClientContext
    {
        public uint TerritoryId { get; set; } = 30;
        public bool IsLoggedIn => true;
        public bool IsGPosing => false;
    }

    private static AppearanceData Appearance(uint modelId, byte marker)
        => AppearanceData.Create(
            modelId,
            modelId == 0 ? ModelCategory.Human : ModelCategory.Monster,
            marker,
            AppearanceCompleteness.Complete,
            [marker],
            [marker]);
}
