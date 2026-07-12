namespace ActorMorpher;

public static class EquipmentDisplayFormatting
{
    public static string FormatSet(OutfitSlot slot, ushort set)
        => $"{(slot >= OutfitSlot.Ears ? 'a' : 'e')}{set:D4}";

    public static string FormatVariant(byte variant)
        => $"v{variant:D4}";

    public static (byte R, byte G, byte B) DecodeStainColor(uint seColor)
        => (
            checked((byte)(seColor & 0xFF)),
            checked((byte)((seColor >> 8) & 0xFF)),
            checked((byte)((seColor >> 16) & 0xFF)));
}
