using Dalamud.Game.ClientState.Objects.Types;

namespace ActorMorpher.Actors;

internal enum ActorRole : byte
{
    Editable = 1,
    Support = 2,
    Inactive = 3,
}

internal readonly record struct ActorObjectReference(ushort ObjectIndex, ulong GameObjectId, ulong Address)
{
    internal bool Matches(ushort objectIndex, ulong gameObjectId, nint address)
        => address != 0 && ObjectIndex == objectIndex && GameObjectId == gameObjectId
            && Address == unchecked((ulong)address);
}

// A still-owned native object can be observed in the next territory before it is released.
internal readonly record struct ActorInstance(ActorObjectReference Object, ulong Lifetime, uint EntityId);

internal readonly record struct ActorRoleAssignment(
    ActorRole Role, ActorObjectReference Object, Guid SpawnId, string? DisplayName)
{
    internal bool BlocksEditing => Role is ActorRole.Support or ActorRole.Inactive;

    internal static ActorRoleAssignment? FindMatch(IReadOnlyList<ActorRoleAssignment> assignments, IGameObject actor)
    {
        for (var i = 0; i < assignments.Count; i++)
        {
            var assignment = assignments[i];
            if (assignment.Object.ObjectIndex == actor.ObjectIndex && assignment.SpawnId != Guid.Empty
                && assignment.Object.Matches(checked((ushort)actor.ObjectIndex), actor.GameObjectId, actor.Address))
                return assignment;
        }
        return null;
    }
}
