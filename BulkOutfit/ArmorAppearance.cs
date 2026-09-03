namespace ActorMorpher.BulkOutfit;

public readonly record struct ArmorAppearance(
    ushort Set,
    byte Variant,
    byte Stain1,
    byte Stain2)
{
    public DyeColor? Color1 { get; init; }
    public DyeColor? Color2 { get; init; }
}

public readonly record struct DyeColor(float R, float G, float B);
