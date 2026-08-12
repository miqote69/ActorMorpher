using System.Collections.Generic;
using System;
using System.Linq;
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
        var filter = new BulkOutfitFilter(ActorTargetType.Npcs, 1, 0, BulkOutfitAge.All, "Human");
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
            new BulkOutfitFilter(ActorTargetType.Npcs, 1, null, BulkOutfitAge.All, string.Empty),
            new BulkOutfitFilter(ActorTargetType.Npcs, 1, null, BulkOutfitAge.All, "Young"),
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
            new BulkOutfitFilter(ActorTargetType.All, 0, null, BulkOutfitAge.All, string.Empty),
            new BulkOutfitFilter(ActorTargetType.Players, 0, null, BulkOutfitAge.All, string.Empty),
            true);

        var preview = new BulkOutfitTargetResolver().Resolve([player, npc], settings);

        Assert.Equal(1, preview.ExcludedLogicalActors);
        Assert.Equal(npc.Key, Assert.Single(preview.EligibleTargets));
    }

    [Fact]
    public void ChildAgeIncludesOnlyYoungHumanActors()
    {
        var youngNpc = Entry(2, "Young NPC", ObjectKind.EventNpc, 0, false, bodyType: (byte)NpcAge.Young);
        var adultNpc = Entry(3, "Adult NPC", ObjectKind.EventNpc, 0, false, bodyType: (byte)NpcAge.Normal);
        var player = Entry(4, "Player", ObjectKind.Pc, 0, true, bodyType: (byte)NpcAge.Young);
        var settings = new BulkOutfitSettings(
            new BulkOutfitFilter(ActorTargetType.All, 0, null, BulkOutfitAge.Child, string.Empty),
            null,
            true);

        var preview = new BulkOutfitTargetResolver().Resolve([youngNpc, adultNpc, player], settings);

        Assert.Equal(2, preview.MatchingLogicalActors);
        Assert.Contains(youngNpc.Key, preview.EligibleTargets);
        Assert.Contains(player.Key, preview.EligibleTargets);
    }

    [Fact]
    public void ChildAgeExclusionOverridesMatchingNpcTarget()
    {
        var youngNpc = Entry(2, "Young NPC", ObjectKind.EventNpc, 0, false, bodyType: (byte)NpcAge.Young);
        var adultNpc = Entry(3, "Adult NPC", ObjectKind.EventNpc, 0, false, bodyType: (byte)NpcAge.Normal);
        var settings = new BulkOutfitSettings(
            new BulkOutfitFilter(ActorTargetType.Npcs, 0, null, BulkOutfitAge.All, string.Empty),
            new BulkOutfitFilter(ActorTargetType.Npcs, 0, null, BulkOutfitAge.Child, string.Empty),
            false);

        var preview = new BulkOutfitTargetResolver().Resolve([youngNpc, adultNpc], settings);

        Assert.Equal(1, preview.ExcludedLogicalActors);
        Assert.Equal(adultNpc.Key, Assert.Single(preview.EligibleTargets));
    }

    [Fact]
    public void ChildAgeFilterUsesTheRepresentationSelectedForApplication()
    {
        var normal = Snapshot(2, ObjectKind.EventNpc, false, (byte)NpcAge.Normal);
        var gpose = Snapshot(202, ObjectKind.EventNpc, true, (byte)NpcAge.Young, normal.LogicalKey);
        var actor = new ActorEntry(normal.LogicalKey, normal.Name, normal.ObjectKind, false, [normal, gpose]);
        var settings = new BulkOutfitSettings(
            new BulkOutfitFilter(ActorTargetType.Npcs, 0, null, BulkOutfitAge.Child, string.Empty),
            null,
            false);

        var preview = new BulkOutfitTargetResolver().Resolve(
            [actor],
            settings,
            candidate => candidate.Representations.Single(representation => representation.RepresentationKey.IsGPoseRepresentation));

        Assert.Equal(1, preview.MatchingLogicalActors);
        Assert.Equal(actor.Key, Assert.Single(preview.EligibleTargets));
    }

    [Fact]
    public void AdultAgeIncludesNormalAndOldHumansButNotChildrenOrNonHumans()
    {
        var adult = Entry(2, "Adult", ObjectKind.EventNpc, 0, false, bodyType: (byte)NpcAge.Normal);
        var old = Entry(3, "Old", ObjectKind.EventNpc, 0, false, bodyType: (byte)NpcAge.Old);
        var child = Entry(4, "Child", ObjectKind.EventNpc, 0, false, bodyType: (byte)NpcAge.Young);
        var monster = Entry(5, "Monster", ObjectKind.BattleNpc, 100, false);
        var settings = new BulkOutfitSettings(
            new BulkOutfitFilter(ActorTargetType.All, 0, null, BulkOutfitAge.Adult, string.Empty),
            null,
            false);

        var preview = new BulkOutfitTargetResolver().Resolve([adult, old, child, monster], settings);

        Assert.Equal(2, preview.MatchingLogicalActors);
        Assert.Contains(adult.Key, preview.EligibleTargets);
        Assert.Contains(old.Key, preview.EligibleTargets);
        Assert.DoesNotContain(child.Key, preview.EligibleTargets);
        Assert.DoesNotContain(monster.Key, preview.EligibleTargets);
    }

    private static ActorEntry Entry(
        ushort index,
        string name,
        ObjectKind kind,
        uint modelCharaId,
        bool isLocalPlayer,
        bool? isHuman = null,
        byte? bodyType = null)
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
            human ? bodyType ?? (byte)NpcAge.Normal : null,
            0,
            0,
            isLocalPlayer);
        return new ActorEntry(logical, name, kind, isLocalPlayer, new List<ActorSnapshot> { snapshot });
    }

    private static ActorSnapshot Snapshot(
        ushort index,
        ObjectKind kind,
        bool isGPosing,
        byte bodyType,
        LogicalActorKey? logicalKey = null)
    {
        var logical = logicalKey ?? new LogicalActorKey(index, index, index, index, kind, 30);
        return new ActorSnapshot(
            logical,
            new ActorRepresentationKey(index, index, index, isGPosing),
            $"Actor {index}",
            kind,
            index,
            0,
            1,
            0,
            bodyType,
            0,
            0,
            false);
    }
}
