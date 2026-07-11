using System.Collections.Immutable;

namespace ActorMorpher.BulkOutfit;

public sealed record OutfitData(
    ImmutableArray<ArmorAppearance> Equipment,
    FacewearAppearance Facewear,
    bool HatVisible,
    bool VisorToggled)
{
    public static OutfitData Create(
        IEnumerable<ArmorAppearance> equipment,
        FacewearAppearance facewear,
        bool hatVisible,
        bool visorToggled)
    {
        var slots = equipment.ToImmutableArray();
        if (slots.Length != Enum.GetValues<OutfitSlot>().Length)
            throw new ArgumentException("Outfit data must contain all armor and accessory slots.", nameof(equipment));

        return new OutfitData(slots, facewear, hatVisible, visorToggled);
    }
}
