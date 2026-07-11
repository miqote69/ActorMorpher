namespace ActorMorpher.BulkOutfit;

public sealed class VerifiedUnequipAppearanceProvider : IUnequipAppearanceProvider
{
    public bool TryGetNothing(OutfitSlot slot, out ArmorAppearance appearance)
    {
        appearance = new ArmorAppearance(0, 0, 0, 0);
        return Enum.IsDefined(slot);
    }

    public bool TryGetNoFacewear(out FacewearAppearance appearance)
    {
        appearance = new FacewearAppearance(true, 0);
        return true;
    }
}
