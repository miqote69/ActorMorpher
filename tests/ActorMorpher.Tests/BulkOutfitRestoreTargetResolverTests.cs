using System.Linq;
using ActorMorpher.Actors;
using ActorMorpher.BulkOutfit;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class BulkOutfitRestoreTargetResolverTests
{
    [Fact]
    public void OutfitAndModelActorsExcludePinsFromBothSourcesWithoutDuplicates()
    {
        var first = Key(1);
        var pinned = Key(2);
        var third = Key(3);
        var untouched = Key(4);
        var pinnedModel = Key(5);

        var targets = BulkOutfitRestoreTargetResolver.Resolve(
            [first, first, pinned],
            [first, pinned, third, untouched, pinnedModel],
            actor => actor == pinned || actor == third || actor == pinnedModel,
            actor => actor == pinned || actor == pinnedModel);

        Assert.Equal([first, third], targets.ToArray());
    }

    [Fact]
    public void AllPinnedYieldsNoTargetsAndUnpinningRestoresEligibility()
    {
        var outfit = Key(1);
        var model = Key(2);
        var pinned = true;
        var targets = BulkOutfitRestoreTargetResolver.Resolve([outfit], [outfit, model], _ => true, _ => pinned);
        Assert.Empty(targets);
        pinned = false;
        targets = BulkOutfitRestoreTargetResolver.Resolve([outfit], [outfit, model], _ => true, _ => pinned);
        Assert.Equal([outfit, model], targets.ToArray());
    }

    private static LogicalActorKey Key(ushort index)
        => new(index, index, index, index, ObjectKind.Pc, 1);
}
