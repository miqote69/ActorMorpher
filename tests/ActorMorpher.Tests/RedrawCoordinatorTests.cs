using System;
using System.Collections.Generic;
using System.Linq;
using ActorMorpher;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.Interop;
using ActorMorpher.Redraw;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class RedrawCoordinatorTests
{
    [Fact]
    public void CompletesSuccessfulOperationInFrameworkSizedSteps()
    {
        var fixture = new Fixture();
        using var coordinator = fixture.CreateCoordinator();
        coordinator.Enqueue(fixture.Operation());

        for (var i = 0; i < 3; ++i)
            coordinator.ProcessNextFrame();

        Assert.Equal(RedrawStage.Completed, coordinator.LastResult?.Stage);
        Assert.Equal(1, fixture.Backend.DisableCount);
        Assert.Equal(1, fixture.Backend.EnableCount);
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
        coordinator.ProcessNextFrame();
        coordinator.ProcessNextFrame();
        fixture.Resolver.Available = false;

        coordinator.ProcessNextFrame();

        Assert.Equal(RedrawStage.Cancelled, coordinator.LastResult?.Stage);
        Assert.Empty(fixture.Backend.EnabledAppearances);
    }

    [Fact]
    public void CancellationAfterDisableDoesNotRedrawAutomatically()
    {
        var fixture = new Fixture();
        using var coordinator = fixture.CreateCoordinator();
        coordinator.Enqueue(fixture.Operation());
        coordinator.ProcessNextFrame();
        coordinator.ProcessNextFrame();

        coordinator.CancelAll("Cancelled by test.");

        Assert.Equal(RedrawStage.Cancelled, coordinator.LastResult?.Stage);
        Assert.Empty(fixture.Backend.EnabledAppearances);
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

    [Fact]
    public void TerritoryChangeWhileHiddenDoesNotRedrawAutomatically()
    {
        var fixture = new Fixture();
        using var coordinator = fixture.CreateCoordinator();
        coordinator.Enqueue(fixture.Operation());
        for (var frame = 0; frame < 2; ++frame)
            coordinator.ProcessNextFrame();
        fixture.Context.TerritoryId = 31;

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
        coordinator.ProcessNextFrame();
        coordinator.ProcessNextFrame();
        fixture.Resolver.Snapshot = fixture.Resolver.Snapshot with
        {
            RepresentationKey = fixture.Resolver.Snapshot.RepresentationKey with { GameObjectId = 999 },
        };

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
        coordinator.Enqueue(fixture.Operation());

        for (var i = 0; i < 4; ++i)
            coordinator.ProcessNextFrame();

        Assert.Equal(RedrawStage.Failed, coordinator.LastResult?.Stage);
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

        public bool TryDisable(ActorSnapshot actor)
        {
            DisableCount++;
            return failure != FailurePoint.Disable;
        }

        public bool TryEnable(ActorSnapshot actor, AppearanceData? appearance, Guid operationId)
        {
            EnableCount++;
            EnabledAppearances.Add(appearance);
            EnabledOperationIds.Add(operationId);
            if (failure == FailurePoint.EnableThrows)
                throw new InvalidOperationException("Native apply failed.");
            var succeeded = failure switch
            {
                FailurePoint.Enable => false,
                _ => true,
            };
            if (succeeded && appearance is not null)
                memory.Rendered = appearance;
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
