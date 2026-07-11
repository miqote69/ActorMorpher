using Dalamud.Game.ClientState.Objects.Enums;

namespace ActorMorpher.Actors;

public sealed record ActorEntry(
    LogicalActorKey Key,
    string Name,
    ObjectKind Kind,
    bool IsLocalPlayer,
    IReadOnlyList<ActorSnapshot> Representations)
{
    public ActorSnapshot Current => Representations[0];
    public ulong GameObjectId => Current.RepresentationKey.GameObjectId;
    public uint EntityId => Current.RepresentationKey.EntityId;
    public uint BaseId => Current.BaseId;
    public byte? Race => Current.Race;
    public byte? Gender => Current.Gender;
}
