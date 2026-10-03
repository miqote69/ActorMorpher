using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace ActorMorpher.Interop;

internal static unsafe class NativeMinionOwner
{
    internal readonly record struct Owner(nint Child, ulong ContentId);

    internal static IReadOnlyDictionary<nint, ulong> Read(IObjectTable objects)
    {
        var owners = new List<Owner>();
        foreach (var obj in objects)
            if (obj is not null && obj.Address != 0 && obj.IsValid() && obj.ObjectKind == ObjectKind.Pc
                && Read((Character*)obj.Address) is { } owner)
                owners.Add(owner);
        return UniqueOwners(owners);
    }

    internal static Owner? Read(Character* player)
        => player != null && (ObjectKind)player->ObjectKind == ObjectKind.Pc
            && player->ContentId != 0 && player->ChildObject != null
                ? new((nint)player->ChildObject, player->ContentId) : null;

    internal static IReadOnlyDictionary<nint, ulong> UniqueOwners(IEnumerable<Owner> owners)
        => owners.GroupBy(static owner => owner.Child)
            .Where(static group => group.Count() == 1)
            .ToDictionary(static group => group.Key, static group => group.Single().ContentId);
}
