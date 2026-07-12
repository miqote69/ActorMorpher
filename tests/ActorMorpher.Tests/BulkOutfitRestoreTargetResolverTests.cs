using System.Linq;
using ActorMorpher.Actors;
using ActorMorpher.BulkOutfit;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class BulkOutfitRestoreTargetResolverTests
{
    [Fact]
    public void PinnedActorsAreExcludedFromBulkRestore()
    {
        var first = Key(1);
        var pinned = Key(2);
        var third = Key(3);

        var targets = BulkOutfitRestoreTargetResolver.Resolve(
            [first, pinned, third, first],
            actor => actor == pinned);

        Assert.Equal([first, third], targets.ToArray());
    }

    private static LogicalActorKey Key(ushort index)
        => new(index, index, index, index, ObjectKind.Pc, 1);
}
