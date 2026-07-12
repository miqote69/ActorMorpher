using System.Collections.Generic;
using System;
using ActorMorpher.Actors;
using ActorMorpher.BulkOutfit;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class BulkOutfitTests
{
    [Fact]
    public void PreviewCountsLogicalActorsAndExcludesNonHumans()
    {
        var player = Entry(1, "Player", ObjectKind.Pc, 0, true);
        var humanNpc = Entry(2, "Human", ObjectKind.EventNpc, 0, false);
        var monster = Entry(3, "Monster", ObjectKind.BattleNpc, 100, false);
        var settings = new BulkOutfitSettings(ActorTargetType.All, 0, null, string.Empty, false);

        var preview = new BulkOutfitTargetResolver().Resolve([player, humanNpc, monster], settings);

        Assert.Equal(2, preview.MatchingLogicalActors);
        Assert.Equal(1, preview.EligibleHumanActors);
        Assert.Equal(1, preview.SkippedNonHumanActors);
    }

    [Fact]
    public void CancelDoesNotAdvancePendingTargets()
    {
        var operation = new BulkOperation(BulkOperationType.ApplyOutfit, [Entry(1, "A", ObjectKind.Pc, 0, false).Key]);

        operation.RequestCancel();

        Assert.True(operation.CancelRequested);
        Assert.Equal(0, operation.CurrentIndex);
    }

    [Fact]
    public void NonzeroHumanNpcModelRemainsEligible()
    {
        var youngNpc = Entry(2, "Young NPC", ObjectKind.EventNpc, 123, false, true);
        var settings = new BulkOutfitSettings(ActorTargetType.All, 0, null, string.Empty, false);

        var preview = new BulkOutfitTargetResolver().Resolve([youngNpc], settings);

        Assert.Equal(1, preview.EligibleHumanActors);
        Assert.Equal(0, preview.SkippedNonHumanActors);
    }

    [Fact]
    public void ExclusionWinsWhenTargetAndExclusionConditionsAreIdentical()
    {
        var humanNpc = Entry(2, "Human", ObjectKind.EventNpc, 0, false);
        var filter = new BulkOutfitFilter(ActorTargetType.Npcs, 1, 0, "Human");
        var settings = new BulkOutfitSettings(filter, filter, false);

        var preview = new BulkOutfitTargetResolver().Resolve([humanNpc], settings);

        Assert.Equal(0, preview.MatchingLogicalActors);
        Assert.Equal(1, preview.ExcludedLogicalActors);
        Assert.Empty(preview.EligibleTargets);
    }

    [Fact]
    public void ExclusionRemovesOnlyActorsMatchingAllExclusionConditions()
    {
        var first = Entry(2, "Young Human", ObjectKind.EventNpc, 0, false);
        var second = Entry(3, "Adult Human", ObjectKind.EventNpc, 0, false);
        var settings = new BulkOutfitSettings(
            new BulkOutfitFilter(ActorTargetType.Npcs, 1, null, string.Empty),
            new BulkOutfitFilter(ActorTargetType.Npcs, 1, null, "Young"),
            false);

        var preview = new BulkOutfitTargetResolver().Resolve([first, second], settings);

        Assert.Equal(1, preview.MatchingLogicalActors);
        Assert.Equal(1, preview.ExcludedLogicalActors);
        Assert.Equal(second.Key, Assert.Single(preview.EligibleTargets));
    }

    [Fact]
    public void PlayerExclusionCanRemoveIncludedLocalPlayer()
    {
        var player = Entry(1, "Player", ObjectKind.Pc, 0, true);
        var npc = Entry(2, "Human", ObjectKind.EventNpc, 0, false);
        var settings = new BulkOutfitSettings(
            new BulkOutfitFilter(ActorTargetType.All, 0, null, string.Empty),
            new BulkOutfitFilter(ActorTargetType.Players, 0, null, string.Empty),
            true);

        var preview = new BulkOutfitTargetResolver().Resolve([player, npc], settings);

        Assert.Equal(1, preview.ExcludedLogicalActors);
        Assert.Equal(npc.Key, Assert.Single(preview.EligibleTargets));
    }

    private static ActorEntry Entry(
        ushort index,
        string name,
        ObjectKind kind,
        uint modelCharaId,
        bool isLocalPlayer,
        bool? isHuman = null)
    {
        var human = isHuman ?? modelCharaId == 0;
        var logical = new LogicalActorKey(index, index, index, index, kind, 30);
        var representation = new ActorRepresentationKey(index, index, index, false);
        var snapshot = new ActorSnapshot(
            logical,
            representation,
            name,
            kind,
            index,
            modelCharaId,
            human ? (byte)1 : null,
            human ? (byte)0 : null,
            human ? (byte)1 : null,
            0,
            0,
            isLocalPlayer);
        return new ActorEntry(logical, name, kind, isLocalPlayer, new List<ActorSnapshot> { snapshot });
    }
}
