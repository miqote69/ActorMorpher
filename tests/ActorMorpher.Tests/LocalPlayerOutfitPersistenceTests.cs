using ActorMorpher.Actors;
using ActorMorpher.BulkOutfit;
using Dalamud.Game.ClientState.Objects.Enums;
using System;
using System.Linq;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class LocalPlayerOutfitPersistenceTests
{
    [Fact]
    public void TerritoryChangeQueuesAppliedOutfitForNewActor()
    {
        var persistence = new LocalPlayerOutfitPersistence();
        var desired = Outfit(0);
        persistence.UpdateContext(10, true);
        persistence.RecordApplied(desired);

        Assert.True(persistence.UpdateContext(11, true));
        Assert.True(persistence.TryGetPending(out var pending));
        Assert.Same(desired, pending);

        var newActor = Key(11);
        Assert.True(persistence.MarkReapplyStarted(newActor));
        persistence.CompleteReapply(newActor, true);
        Assert.False(persistence.ReapplyPending);
        Assert.Same(desired, persistence.Desired);
    }

    [Fact]
    public void FailedReapplyCanRetryAndRestoreClearsPersistence()
    {
        var persistence = new LocalPlayerOutfitPersistence();
        var actor = Key(11);
        persistence.UpdateContext(10, true);
        persistence.RecordApplied(Outfit(0));
        persistence.UpdateContext(11, true);
        persistence.MarkReapplyStarted(actor);

        persistence.CompleteReapply(actor, false);
        Assert.True(persistence.TryGetPending(out _));

        persistence.RecordRestored();
        persistence.UpdateContext(12, true);
        Assert.False(persistence.TryGetPending(out _));
        Assert.Null(persistence.Desired);
    }

    private static LogicalActorKey Key(uint territory)
        => new(0, 100, 200, 300, ObjectKind.Pc, territory);

    private static OutfitData Outfit(ushort set)
        => OutfitData.Create(
            Enumerable.Repeat(new ArmorAppearance(set, 0, 0, 0), Enum.GetValues<OutfitSlot>().Length),
            FacewearAppearance.Unavailable,
            true,
            false);
}
