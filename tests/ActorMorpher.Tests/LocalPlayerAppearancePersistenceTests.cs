using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class LocalPlayerAppearancePersistenceTests
{
    [Fact]
    public void TerritoryChangeQueuesAppliedAppearanceForNewActor()
    {
        var persistence = new LocalPlayerAppearancePersistence();
        var desired = Appearance(100);
        persistence.UpdateContext(10, true);
        persistence.RecordApplied(desired);

        Assert.True(persistence.UpdateContext(11, true));
        Assert.True(persistence.TryGetPending(out var pending));
        Assert.Same(desired, pending);

        var newActor = Key(11);
        Assert.True(persistence.MarkReapplyStarted(newActor));
        Assert.True(persistence.IsReapplyActor(newActor));
        persistence.CompleteReapply(newActor, true);

        Assert.False(persistence.ReapplyPending);
        Assert.Same(desired, persistence.Desired);
    }

    [Fact]
    public void FailedReapplyCanRetryWithoutLosingDesiredAppearance()
    {
        var persistence = new LocalPlayerAppearancePersistence();
        var actor = Key(11);
        persistence.UpdateContext(10, true);
        persistence.RecordApplied(Appearance(100));
        persistence.UpdateContext(11, true);
        persistence.MarkReapplyStarted(actor);

        persistence.CompleteReapply(actor, false);

        Assert.True(persistence.TryGetPending(out _));
    }

    [Fact]
    public void RestoreAndLogoutBothDisableFutureReapply()
    {
        var persistence = new LocalPlayerAppearancePersistence();
        persistence.UpdateContext(10, true);
        persistence.RecordApplied(Appearance(100));
        persistence.RecordRestored();
        persistence.UpdateContext(11, true);
        Assert.False(persistence.TryGetPending(out _));

        persistence.RecordApplied(Appearance(200));
        persistence.UpdateContext(11, false);
        persistence.UpdateContext(12, true);
        Assert.Null(persistence.Desired);
        Assert.False(persistence.TryGetPending(out _));
    }

    private static LogicalActorKey Key(uint territory)
        => new(0, 100, 200, 300, ObjectKind.Pc, territory);

    private static AppearanceData Appearance(uint modelId)
        => AppearanceData.Create(
            modelId,
            ModelCategory.Monster,
            modelId,
            AppearanceCompleteness.ModelOnly,
            [],
            []);
}
