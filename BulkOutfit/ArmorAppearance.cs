namespace ActorMorpher.BulkOutfit;

public readonly record struct ArmorAppearance(
    ushort Set,
    byte Variant,
    byte Stain1,
    byte Stain2)
{
    public DyeColor? Color1 { get; init; }
    public DyeColor? Color2 { get; init; }

    public ArmorAppearance WithDye(int channel, DyeColor? color, bool clearDye = false)
        => channel == 0 ? this with { Color1 = color, Stain1 = clearDye ? (byte)0 : Stain1 }
            : this with { Color2 = color, Stain2 = clearDye ? (byte)0 : Stain2 };
}

public readonly record struct DyeColor(float R, float G, float B)
{
    public bool? Metallic { get; init; }
    // A metallic-only edit must not turn the palette's display color into an RGB override.
    public bool UseOriginalColor { get; init; }
}

public readonly record struct WeaponDyes(DyeColor? Color1, DyeColor? Color2)
{
    public WeaponDyes WithDye(int channel, DyeColor? color)
        => channel == 0 ? this with { Color1 = color } : this with { Color2 = color };

    internal ArmorAppearance AsArmor(ulong packed)
        => new(0, 0, (byte)(packed >> 48), (byte)(packed >> 56)) { Color1 = Color1, Color2 = Color2 };
}
