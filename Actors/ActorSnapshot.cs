using Dalamud.Game.ClientState.Objects.Enums;
using ActorMorpher.Appearance;
using System.Numerics;

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
    public Vector3 Position { get; init; }
    public ActorContinuityKey? ContinuityKey { get; init; }
    public NpcAttachmentKey? NpcAttachment { get; init; }
    public ulong MinionOwnerContentId { get; init; }
    internal long EquipmentLoadRevision { get; init; }

    public ActorSnapshot WithVisibleHumanCustomize(byte race, byte gender, byte bodyType)
        => this with { Race = race, Gender = gender, BodyType = bodyType };
}
