using System.Collections.Generic;
using ActorMorpher.Actors;
using ActorMorpher.GPose;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class GPoseMappingResolverTests
{
    [Fact]
    public void ResolvesUniqueRepresentationByGameObjectId()
    {
        var source = Entry(1, 100, 10, 20, "Source");
        var copy = Entry(201, 100, 11, 20, "Source");

        var result = new GPoseMappingResolver().Resolve([source], [source, copy]);

        Assert.Single(result);
        Assert.Equal(source.Key, result[copy.Current.RepresentationKey]);
    }

    [Fact]
    public void SkipsAmbiguousNonNetworkedNpcCopies()
    {
        var source = Entry(1, 100, 0xE0000000, 20, "Same Name", ObjectKind.EventNpc);
        var copyA = Entry(201, 200, 0xE0000000, 20, "Same Name", ObjectKind.EventNpc);
        var copyB = Entry(202, 300, 0xE0000000, 20, "Same Name", ObjectKind.EventNpc);

        var result = new GPoseMappingResolver().Resolve([source], [source, copyA, copyB]);

        Assert.Empty(result);
    }

    [Fact]
    public void MapsLocalPlayerToValidatedGPosePlayerSlot()
    {
        var source = Entry(1, 100, 10, 0, "Source", isLocalPlayer: true);
        var copy = Entry(201, 900, 0xE0000000, 0, "GPose Copy");

        var result = new GPoseMappingResolver().Resolve([source], [source, copy]);

        Assert.Single(result);
        Assert.Equal(source.Key, result[copy.Current.RepresentationKey]);
    }

    [Fact]
    public void DoesNotUseGPosePlayerSlotForNonPlayerObjects()
    {
        var source = Entry(1, 100, 10, 20, "Source", isLocalPlayer: true);
        var copy = Entry(201, 900, 0xE0000000, 20, "Copy", ObjectKind.EventNpc);

        var result = new GPoseMappingResolver().Resolve([source], [source, copy]);

        Assert.Empty(result);
    }

    [Fact]
    public void ResolvesUniquePlayerCopiesByStableAppearanceWhenModelsDiffer()
    {
        var first = Entry(1, 100, 10, 0, "First", modelCharaId: 10, race: 1, gender: 0);
        var second = Entry(2, 200, 20, 0, "Second", modelCharaId: 20, race: 2, gender: 1);
        var firstCopy = Entry(201, 900, 0xE0000000, 0, "First", modelCharaId: 99, race: 1, gender: 0);
        var secondCopy = Entry(202, 901, 0xE0000000, 0, "Second", modelCharaId: 98, race: 2, gender: 1);

        var result = new GPoseMappingResolver().Resolve(
            [first, second],
            [first, second, firstCopy, secondCopy]);

        Assert.Equal(first.Key, result[firstCopy.Current.RepresentationKey]);
        Assert.Equal(second.Key, result[secondCopy.Current.RepresentationKey]);
    }

    private static ActorEntry Entry(
        ushort index,
        ulong gameObjectId,
        uint entityId,
        uint baseId,
        string name,
        ObjectKind kind = ObjectKind.Pc,
        bool isLocalPlayer = false,
        uint modelCharaId = 0,
        byte race = 1,
        byte gender = 0)
    {
        var logical = new LogicalActorKey(index, gameObjectId, entityId, baseId, kind, 30);
        var representation = new ActorRepresentationKey(index, gameObjectId, entityId, false);
        var snapshot = new ActorSnapshot(
            logical,
            representation,
            name,
            kind,
            baseId,
            modelCharaId,
            race,
            gender,
            1,
            0,
            0,
            false);
        return new ActorEntry(logical, name, kind, isLocalPlayer, new List<ActorSnapshot> { snapshot });
    }
}
