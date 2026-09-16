using System;
using System.Linq;
using ActorMorpher.Actors;
using ActorMorpher.Interop;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Xunit;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace ActorMorpher.Tests;

public sealed unsafe class NativeNpcAttachmentTests
{
    [Fact]
    public void NativeType3RelationshipUsesOwnerAndRecordIdsWithoutReadingPoseAsIdentity()
    {
        Character npc = default;
        CharacterBase draw = default, parent = default;
        byte* records = stackalloc byte[0x80];
        new Span<byte>(records, 0x80).Clear();
        *(ushort*)(records + 2) = 79;
        *(ushort*)(records + 0x42) = 81;
        npc.ObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.EventNpc;
        npc.BaseId = 1003782;
        npc.DrawObject = (DrawObject*)&draw;
        draw.Attach.ExecuteType = 3;
        draw.Attach.OwnerCharacter = &parent;
        draw.Attach.AttachmentCount = 2;
        draw.Attach.SkeletonBoneAttachments = (Attach.Attachment*)records;
        var owners = new[] { new NativeNpcAttachment.AttachmentOwner((nint)(&parent), 123456, 386) };
        var initial = new ReadOnlySpan<byte>(&npc, sizeof(Character)).ToArray();
        var expected = new NpcAttachmentKey(123456, 386, ObjectKind.EventNpc, 1003782, "79,81");
        Assert.Equal(expected, NativeNpcAttachment.Read(&npc, owners));
        new Span<byte>(records + 0x10, 48).Fill(0x7F);
        Assert.Equal(expected, NativeNpcAttachment.Read(&npc, owners));
        Assert.Equal(initial, new ReadOnlySpan<byte>(&npc, sizeof(Character)).ToArray());
        Assert.Null(NativeNpcAttachment.Read(&npc, []));
        Assert.Null(NativeNpcAttachment.Read(&npc, [owners[0], owners[0] with { ContentId = 888 }]));
        draw.Attach.ExecuteType = 0;
        Assert.Null(NativeNpcAttachment.Read(&npc, owners));
    }

    [Fact]
    public void SameNumberOnDifferentOwnersOrAnchorsIsIndividualButDuplicateRelationshipIsNot()
    {
        var key = new NpcAttachmentKey(123456, 386, ObjectKind.EventNpc, 1003782, "79");
        var first = (Address: (nint)1, Key: key);
        var otherOwner = (Address: (nint)2, Key: key with { OwnerContentId = 888 });
        var otherAnchor = (Address: (nint)3, Key: key with { AttachmentIds = "80" });
        var unique = NativeNpcAttachment.UniqueTargets([first, otherOwner, otherAnchor]);
        Assert.Equal(3, unique.Count);
        var ambiguous = NativeNpcAttachment.UniqueTargets([first, otherOwner, otherAnchor, ((nint)4, key)]);
        Assert.Equal(2, ambiguous.Count);
        Assert.False(ambiguous.ContainsKey(1));
        Assert.False(ambiguous.ContainsKey(4));
        Assert.Equal(otherOwner.Key, ambiguous[2]);
        Assert.Equal(otherAnchor.Key, ambiguous[3]);
    }

    [Fact]
    public void BuddyParentResolvesOwnerAcrossEntityIdChangesWithoutUsingTransientIdsInPin()
    {
        Character buddy = default, rider = default;
        CharacterBase buddyDraw = default, riderDraw = default;
        byte* record = stackalloc byte[0x40];
        new Span<byte>(record, 0x40).Clear();
        *(ushort*)(record + 2) = 79;
        buddy.ObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.BattleNpc;
        buddy.BaseId = 952;
        buddy.OwnerId = 123;
        buddy.DrawObject = (DrawObject*)&buddyDraw;
        rider.ObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.EventNpc;
        rider.BaseId = 1059561;
        rider.DrawObject = (DrawObject*)&riderDraw;
        riderDraw.Attach.ExecuteType = 3;
        riderDraw.Attach.OwnerCharacter = &buddyDraw;
        riderDraw.Attach.AttachmentCount = 1;
        riderDraw.Attach.SkeletonBoneAttachments = (Attach.Attachment*)record;
        var owner = NativeNpcAttachment.ReadOwnedParent(&buddy, [new(123, 777)]);
        Assert.NotNull(owner);
        var first = NativeNpcAttachment.Read(&rider, [owner.Value]);
        Assert.Equal(ObjectKind.BattleNpc, first!.Value.ParentKind);
        Assert.Equal(952u, first.Value.ParentBaseId);
        buddy.OwnerId = 456;
        Assert.Null(NativeNpcAttachment.ReadOwnedParent(&buddy, [new(123, 777)]));
        owner = NativeNpcAttachment.ReadOwnedParent(&buddy, [new(456, 777)]);
        Assert.Equal(first, NativeNpcAttachment.Read(&rider, [owner!.Value]));
        var other = NativeNpcAttachment.ReadOwnedParent(&buddy, [new(456, 888)]);
        Assert.NotEqual(first, NativeNpcAttachment.Read(&rider, [other!.Value]));
        Assert.Null(NativeNpcAttachment.ReadOwnedParent(&buddy, [new(456, 777), new(456, 888)]));
        buddy.OwnerId = 0xE0000000;
        Assert.Null(NativeNpcAttachment.ReadOwnedParent(&buddy, [new(0xE0000000, 777)]));
    }
}
