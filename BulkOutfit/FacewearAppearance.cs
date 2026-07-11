namespace ActorMorpher.BulkOutfit;

public readonly record struct FacewearAppearance(bool IsAvailable, ushort ModelId)
{
    public static FacewearAppearance Unavailable => new(false, 0);
}
