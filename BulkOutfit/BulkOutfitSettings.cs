namespace ActorMorpher.BulkOutfit;

public enum ActorTargetType
{
    All,
    Players,
    Npcs,
}

public sealed record BulkOutfitSettings(
    ActorTargetType ActorType,
    uint Race,
    byte? Gender,
    string Name,
    bool IncludeYourself);
