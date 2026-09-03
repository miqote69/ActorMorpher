using Dalamud.Game.ClientState.Objects.Enums;

namespace ActorMorpher.Actors;

public readonly record struct LogicalActorKey(
    ushort OriginalObjectIndex,
    ulong GameObjectId,
    uint EntityId,
    uint BaseId,
    ObjectKind ObjectKind,
    uint TerritoryId)
{
    public ActorContinuityKey? Continuity { get; init; }
    public ulong Lifetime { get; init; }
}
