using Dalamud.Game.ClientState.Objects.Enums;

namespace ActorMorpher.Actors;

// Identity of the game actor, independent of the current object-table slot / DrawObject.
public readonly record struct ActorContinuityKey(ObjectKind Kind, byte Source, ulong Id, uint BaseId, uint Territory);
