using Dalamud.Game.ClientState.Objects.Enums;
using ActorMorpher.Appearance;

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
    bool IsLocalPlayer,
    bool IsOwnMinion = false,
    AppearanceData? CurrentAppearance = null,
    bool IsAppearanceManaged = false)
{
    public ActorSnapshot WithVisibleHumanCustomize(byte race, byte gender, byte bodyType)
        => this with { Race = race, Gender = gender, BodyType = bodyType };
}
