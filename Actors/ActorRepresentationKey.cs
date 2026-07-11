namespace ActorMorpher.Actors;

public readonly record struct ActorRepresentationKey(
    ushort ObjectIndex,
    ulong GameObjectId,
    uint EntityId,
    bool IsGPoseRepresentation);
