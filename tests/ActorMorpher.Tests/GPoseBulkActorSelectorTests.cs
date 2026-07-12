using ActorMorpher.Actors;
using ActorMorpher.BulkOutfit;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class GPoseBulkActorSelectorTests
{
    [Fact]
    public void GPoseIncludesOnlyMappedOrValidatedLocalRepresentations()
    {
        var hiddenNormal = Entry(1, false, false);
        var mapped = Entry(2, true, false);
        var local = Entry(3, false, true);

        var selected = GPoseBulkActorSelector.Select(
            [hiddenNormal, mapped, local],
            true,
            true,
            key => key == local.Key);

        Assert.DoesNotContain(hiddenNormal, selected);
        Assert.Contains(mapped, selected);
        Assert.Contains(local, selected);
    }

    [Fact]
    public void GPoseReturnsNoTargetsUntilMappingsAreReady()
        => Assert.Empty(GPoseBulkActorSelector.Select([Entry(1, true, false)], true, false, _ => false));

    private static ActorEntry Entry(ushort index, bool isGPose, bool isLocal)
    {
        var logical = new LogicalActorKey(index, index, index, index, ObjectKind.Pc, 30);
        var snapshot = new ActorSnapshot(
            logical,
            new ActorRepresentationKey(index, index, index, isGPose),
            $"Actor {index}",
            ObjectKind.Pc,
            index,
            0,
            1,
            0,
            1,
            0,
            0,
            isLocal);
        return new ActorEntry(logical, snapshot.Name, ObjectKind.Pc, isLocal, [snapshot]);
    }
}
