namespace ActorMorpher.BulkOutfit;

public sealed record OutfitOverrideState(
    OutfitData Original,
    OutfitData Desired,
    long Revision);
