using System.Collections.Generic;
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

        for (var i = 0; i < 7; ++i)
            coordinator.ProcessNextFrame();

        Assert.Equal(RedrawStage.Completed, coordinator.LastResult?.Stage);
        Assert.Equal(1, fixture.Backend.DisableCount);
        Assert.Equal(1, fixture.Backend.EnableCount);
    }

    [Fact]
    public void ActorLossCancelsWithoutWritingToStaleRepresentation()
    {
        var fixture = new Fixture();
        using var coordinator = fixture.CreateCoordinator();
        coordinator.Enqueue(fixture.Operation());
        coordinator.ProcessNextFrame();
        fixture.Resolver.Available = false;

        coordinator.ProcessNextFrame();

        Assert.Equal(RedrawStage.Cancelled, coordinator.LastResult?.Stage);
        Assert.Empty(fixture.Memory.Writes);
    }

    [Fact]
    public void FailedDesiredWriteAttemptsRollbackAndReenablesActor()
    {
        var fixture = new Fixture();
        fixture.Memory.FailDesired = true;
        using var coordinator = fixture.CreateCoordinator();
        coordinator.Enqueue(fixture.Operation());

        for (var i = 0; i < 8; ++i)
            coordinator.ProcessNextFrame();

        Assert.Equal(RedrawStage.Failed, coordinator.LastResult?.Stage);
        Assert.Equal(3, fixture.Memory.Writes.Count);
        Assert.Equal(1, fixture.Backend.EnableCount);
    }

    private sealed class Fixture
    {
        public readonly LogicalActorKey Key = new(1, 100, 10, 20, ObjectKind.Pc, 30);
        public readonly FakeMemory Memory = new();
        public readonly FakeBackend Backend = new();
        public readonly FakeContext Context = new();
        public readonly FakeResolver Resolver;
        private readonly AppearanceData original = Appearance(0, 1);
        private readonly AppearanceData desired = Appearance(100, 2);

        public Fixture()
        {
            var representation = new ActorRepresentationKey(1, 100, 10, false);
            Resolver = new FakeResolver(new ActorSnapshot(
                Key, representation, "Actor", ObjectKind.Pc, 20, 0, 1, 0, 1, 0, 0, true));
            Memory.Desired = desired;
        }

        public RedrawCoordinator CreateCoordinator()
            => new(Resolver, Memory, Backend, Context);

        public RedrawOperation Operation()
            => RedrawOperation.Create(Key, desired, original, 1, 30);
    }

    private sealed class FakeResolver(ActorSnapshot actor) : IActorResolver
    {
        public bool Available { get; set; } = true;

        public bool TryResolve(LogicalActorKey key, out ActorSnapshot snapshot)
        {
            snapshot = actor;
            return Available && key == actor.LogicalKey;
        }
    }

    private sealed class FakeMemory : IAppearanceMemory
    {
        public AppearanceData Desired { get; set; } = null!;
        public bool FailDesired { get; set; }
        public List<AppearanceData> Writes { get; } = new();

        public bool TryCapture(ActorSnapshot actor, out AppearanceData appearance)
        {
            appearance = Desired;
            return true;
        }

        public bool TryWrite(ActorSnapshot actor, AppearanceData appearance)
        {
            Writes.Add(appearance);
            return !FailDesired || appearance != Desired;
        }

        public bool IsApplied(ActorSnapshot actor, AppearanceData appearance)
            => true;
    }

    private sealed class FakeBackend : IRedrawBackend
    {
        public int DisableCount { get; private set; }
        public int EnableCount { get; private set; }

        public bool TryDisable(ActorSnapshot actor)
        {
            DisableCount++;
            return true;
        }

        public bool TryEnable(ActorSnapshot actor, AppearanceData? appearance)
        {
            EnableCount++;
            return true;
        }
    }

    private sealed class FakeContext : IClientContext
    {
        public uint TerritoryId => 30;
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
