using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace ActorMorpher.Interop;

internal static unsafe class NativeNpcAttachment
{
    internal readonly record struct PlayerOwner(uint EntityId, ulong ContentId);
    internal readonly record struct AttachmentOwner(nint Draw, ulong ContentId, ushort MountId)
    {
        public ObjectKind ParentKind { get; init; } = ObjectKind.Mount;
        public uint ParentBaseId { get; init; }
    }

    // Runs on the existing registry Framework update. Does not call game functions.
    internal static IReadOnlyDictionary<nint, NpcAttachmentKey> Read(IObjectTable objects)
    {
        var owners = new List<AttachmentOwner>();
        var players = new List<PlayerOwner>();
        var npcs = new List<nint>();
        foreach (var obj in objects)
        {
            if (obj is null || obj.Address == 0 || !obj.IsValid()) continue;
            var character = (Character*)obj.Address;
            if (obj.ObjectKind is ObjectKind.EventNpc or ObjectKind.BattleNpc)
                npcs.Add(obj.Address);
            else if (obj.ObjectKind == ObjectKind.Pc && character->ContentId != 0)
            {
                players.Add(new(character->EntityId, character->ContentId));
                if (character->Mount.MountId != 0 && character->Mount.MountObject != null
                    && character->Mount.MountObject->DrawObject != null)
                    owners.Add(new((nint)character->Mount.MountObject->DrawObject,
                        character->ContentId, character->Mount.MountId));
            }
        }
        foreach (var address in npcs)
            if (ReadOwnedParent((Character*)address, players) is { } parent)
                owners.Add(parent);
        var candidates = new List<(nint Address, NpcAttachmentKey Key)>();
        foreach (var address in npcs)
            if (Read((Character*)address, owners) is { } key)
                candidates.Add((address, key));
        return UniqueTargets(candidates);
    }

    internal static IReadOnlyDictionary<nint, NpcAttachmentKey> UniqueTargets(
        IEnumerable<(nint Address, NpcAttachmentKey Key)> candidates)
        => candidates.GroupBy(static candidate => candidate.Key)
            .Where(static group => group.Count() == 1)
            .Select(static group => group.Single())
            .ToDictionary(static candidate => candidate.Address, static candidate => candidate.Key);

    internal static AttachmentOwner? ReadOwnedParent(Character* parent, IReadOnlyList<PlayerOwner> players)
    {
        if (parent == null || parent->DrawObject == null
            || (ObjectKind)parent->ObjectKind is not (ObjectKind.EventNpc or ObjectKind.BattleNpc)
            || parent->OwnerId is 0 or 0xE0000000)
            return null;
        PlayerOwner? owner = null;
        foreach (var player in players)
        {
            if (player.EntityId != parent->OwnerId || player.ContentId == 0) continue;
            if (owner is not null) return null;
            owner = player;
        }
        return owner is { } resolved
            ? new AttachmentOwner((nint)parent->DrawObject, resolved.ContentId, 0)
                { ParentKind = (ObjectKind)parent->ObjectKind, ParentBaseId = parent->BaseId }
            : null;
    }

    internal static CharacterBase* GetAttachedDraw(Character* npc)
    {
        if (npc == null || (ObjectKind)npc->ObjectKind is not (ObjectKind.EventNpc or ObjectKind.BattleNpc))
            return null;
        var draw = (CharacterBase*)npc->DrawObject;
        return draw != null && draw->Attach.ExecuteType == 3 && draw->Attach.OwnerCharacter != null ? draw : null;
    }

    internal static NpcAttachmentKey? Read(Character* npc, IReadOnlyList<AttachmentOwner> owners)
    {
        var draw = GetAttachedDraw(npc);
        if (draw == null || draw->Attach.AttachmentCount <= 0 || draw->Attach.SkeletonBoneAttachments == null)
            return null;
        AttachmentOwner? owner = null;
        foreach (var candidate in owners)
        {
            if (candidate.Draw != (nint)draw->Attach.OwnerCharacter) continue;
            if (owner is not null) return null; // Does not identify one owner.
            owner = candidate;
        }
        if (owner is not { ContentId: not 0 } resolved) return null;
        var ids = new ushort[draw->Attach.AttachmentCount];
        for (var i = 0; i < ids.Length; i++)
            // The live type3 consumer uses 0x40 stride (public SDK declares 0x58).
            // Confirmed by the existing attachment research and before/after VM_READ captures.
            ids[i] = *(ushort*)((byte*)draw->Attach.SkeletonBoneAttachments + i * 0x40 + 2);
        return new NpcAttachmentKey(resolved.ContentId, resolved.MountId, (ObjectKind)npc->ObjectKind,
            npc->BaseId, string.Join(",", ids))
            { ParentKind = resolved.ParentKind, ParentBaseId = resolved.ParentBaseId };
    }
}
