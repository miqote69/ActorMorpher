namespace ActorMorpher.BulkOutfit;

public enum ActorTargetType
{
    All,
    Players,
    Npcs,
}

public sealed record BulkOutfitFilter(
    ActorTargetType ActorType,
    uint Race,
    byte? Gender,
    string Name);

public sealed record BulkOutfitSettings(
    BulkOutfitFilter Target,
    BulkOutfitFilter? Exclusion,
    bool IncludeYourself)
{
    public BulkOutfitSettings(
        ActorTargetType actorType,
        uint race,
        byte? gender,
        string name,
        bool includeYourself)
        : this(new BulkOutfitFilter(actorType, race, gender, name), null, includeYourself)
    {
    }
}
