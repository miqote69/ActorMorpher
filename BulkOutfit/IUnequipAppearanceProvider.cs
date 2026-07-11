namespace ActorMorpher.BulkOutfit;

public interface IUnequipAppearanceProvider
{
    bool TryGetNothing(OutfitSlot slot, out ArmorAppearance appearance);
    bool TryGetNoFacewear(out FacewearAppearance appearance);
}
