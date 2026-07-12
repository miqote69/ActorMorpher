using ActorMorpher.Actors;
using ActorMorpher.Interop;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class RegistryActorResolverTests
{
    [Fact]
    public void GPoseDoesNotFallBackToNormalRepresentation()
    {
        var normal = Snapshot(1, false);
        var actor = Entry(normal);

        var selected = RegistryActorResolver.SelectRepresentation(actor, true);

        Assert.Null(selected);
    }

    [Fact]
    public void GPoseSelectsMappedRepresentation()
    {
        var normal = Snapshot(1, false);
        var gpose = Snapshot(201, true);
        var actor = Entry(normal, gpose);

        var selected = RegistryActorResolver.SelectRepresentation(actor, true);

        Assert.Same(gpose, selected);
    }

    [Fact]
    public void GPoseUsesValidatedDirectLocalPlayerWhenMappingIsNotInRegistry()
    {
        var normal = Snapshot(1, false);
        var directGPose = Snapshot(201, true);
        var actor = Entry(normal);

        var selected = RegistryActorResolver.SelectRepresentation(actor, true, directGPose);

        Assert.Same(directGPose, selected);
    }

    [Fact]
    public void OutsideGPoseSelectsNormalRepresentation()
    {
        var normal = Snapshot(1, false);
        var gpose = Snapshot(201, true);
        var actor = Entry(normal, gpose);

        var selected = RegistryActorResolver.SelectRepresentation(actor, false);

        Assert.Same(normal, selected);
    }

    private static ActorEntry Entry(params ActorSnapshot[] representations)
        => new(representations[0].LogicalKey, "Player", ObjectKind.Pc, true, representations);

    private static ActorSnapshot Snapshot(ushort index, bool isGPose)
    {
        var logical = new LogicalActorKey(1, 100, 10, 0, ObjectKind.Pc, 30);
        return new ActorSnapshot(
            logical,
            new ActorRepresentationKey(index, (ulong)(100 + index), (uint)(10 + index), isGPose),
            "Player",
            ObjectKind.Pc,
            0,
            0,
            1,
            0,
            1,
            0,
            0,
            true);
    }
}
