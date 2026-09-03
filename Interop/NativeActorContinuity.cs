using FFXIVClientStructs.FFXIV.Client.Game.Character;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace ActorMorpher.Interop;

public sealed unsafe class NativeActorContinuity
{
    private readonly Dictionary<nint, (ulong Lifetime, ActorContinuityKey Identity)> living = new();
    private ulong nextLifetime;

    public (ActorContinuityKey Key, ulong Lifetime) Read(GameObject* obj, uint territory)
    {
        var character = (Character*)obj;
        var identity = Describe((ObjectKind)obj->ObjectKind, character->ContentId, obj->EntityId,
            obj->LayoutId, obj->GetGameObjectId().Id, obj->BaseId, obj->ObjectIndex, territory);
        return Observe((nint)obj, identity);
    }

    internal (ActorContinuityKey Key, ulong Lifetime) Observe(nint address, ActorContinuityKey identity)
    {
        if (!living.TryGetValue(address, out var previous)
            || previous.Identity.Kind != identity.Kind
            || previous.Identity.BaseId != identity.BaseId
            || (previous.Identity.Source == identity.Source && identity.Source != 5
                && previous.Identity != identity))
        {
            previous = (++nextLifetime, identity);
        }
        else if (identity.Source < previous.Identity.Source)
        {
            previous.Identity = identity;
        }
        living[address] = previous;
        return (identity, previous.Lifetime);
    }

    public void Forget(nint address) => living.Remove(address);

    internal static ActorContinuityKey Describe(ObjectKind kind, ulong contentId, uint entityId,
        uint layoutId, ulong gameObjectId, uint baseId, ushort index, uint territory)
    {
        if (kind == ObjectKind.Pc && contentId != 0)
            return new(kind, 1, contentId, 0, 0);
        if (kind == ObjectKind.Companion && (gameObjectId >> 32) == 4)
            return new(kind, 2, gameObjectId, baseId, 0); // game identity contains its owner
        if (kind is ObjectKind.EventNpc or ObjectKind.BattleNpc && layoutId is not (0 or 0xE0000000))
            return new(kind, 3, layoutId, baseId, territory);
        if (entityId is not (0 or 0xE0000000))
            return new(kind, 4, entityId, baseId, kind == ObjectKind.Pc ? 0 : territory);
        // No cross-slot identity is supplied by the game for this object.
        return new(kind, 5, gameObjectId, baseId, territory);
    }
}
