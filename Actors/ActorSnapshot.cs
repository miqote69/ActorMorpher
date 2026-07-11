using Dalamud.Game.ClientState.Objects.Enums;

namespace ActorMorpher.Actors;

public sealed record ActorSnapshot(
    LogicalActorKey LogicalKey,
    ActorRepresentationKey RepresentationKey,
    string Name,
    ObjectKind ObjectKind,
    uint BaseId,
    uint ModelCharaId,
    byte? Race,
    byte? Gender,
    byte? BodyType,
    byte ClassJob,
    byte Level,
    bool IsLocalPlayer);
