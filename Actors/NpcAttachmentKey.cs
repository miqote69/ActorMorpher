using Dalamud.Game.ClientState.Objects.Enums;

namespace ActorMorpher.Actors;

// Pin identity of an attached NPC, not an identity for live writes or persistence.
[method: Newtonsoft.Json.JsonConstructor]
public readonly record struct NpcAttachmentKey(
    ulong OwnerContentId, ushort MountId, ObjectKind Kind, uint BaseId, string AttachmentIds)
{
    // Absent in saved mount pins: preserve their original parent meaning.
    public ObjectKind ParentKind { get; init; } = ObjectKind.Mount;
    public uint ParentBaseId { get; init; }
}
