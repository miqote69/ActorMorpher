using System.Collections.Immutable;

namespace ActorMorpher;

public static class EquipmentDisplayFormatting
{
    public static string FormatSet(OutfitSlot slot, ushort set)
        => $"{(slot >= OutfitSlot.Ears ? 'a' : 'e')}{set:D4}";

    public static string FormatVariant(byte variant)
        => variant.ToString();

    public static (byte R, byte G, byte B) DecodeStainColor(uint seColor)
        => (
            checked((byte)((seColor >> 16) & 0xFF)),
            checked((byte)((seColor >> 8) & 0xFF)),
            checked((byte)(seColor & 0xFF)));

    public static OutfitData? CreateHumanOutfit(HumanAppearance? appearance)
    {
        if (appearance is not { Equipment.Length: 10 })
            return null;
        return OutfitData.Create(
            appearance.Equipment.Select(static packed => new ArmorAppearance(
                checked((ushort)(packed & 0xFFFF)),
                checked((byte)((packed >> 16) & 0xFF)),
                checked((byte)((packed >> 24) & 0xFF)),
                checked((byte)((packed >> 32) & 0xFF)))),
            new FacewearAppearance(true, appearance.FacewearModelId),
            appearance.HatVisible,
            appearance.VisorToggled);
    }

    public static OutfitData? CreateHumanOutfit(AppearanceData? appearance)
    {
        if (appearance is not
            {
                Category: ModelCategory.Human,
                Equipment.Length: 10,
                HatVisible: { } hatVisible,
                VisorToggled: { } visor,
            })
            return null;

        return OutfitData.Create(
            appearance.Equipment.Select(static packed => new ArmorAppearance(
                checked((ushort)(packed & 0xFFFF)),
                checked((byte)((packed >> 16) & 0xFF)),
                checked((byte)((packed >> 24) & 0xFF)),
                checked((byte)((packed >> 32) & 0xFF)))),
            appearance.FacewearModelId is { } facewear
                ? new FacewearAppearance(true, facewear) : FacewearAppearance.Unavailable,
            hatVisible,
            visor) with
        {
            Equipment = appearance.ColoredEquipment.Length == 10
                ? appearance.ColoredEquipment
                : appearance.Equipment.Select(static packed => new ArmorAppearance(
                    (ushort)packed, (byte)(packed >> 16), (byte)(packed >> 24), (byte)(packed >> 32))).ToImmutableArray(),
        };
    }
}
