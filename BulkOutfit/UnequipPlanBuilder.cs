namespace ActorMorpher.BulkOutfit;

public sealed class UnequipPlanBuilder
{
    public bool TryCreate(
        OutfitData current,
        IUnequipAppearanceProvider provider,
        out OutfitData desired,
        out string reason)
    {
        var equipment = new ArmorAppearance[Enum.GetValues<OutfitSlot>().Length];
        foreach (var slot in Enum.GetValues<OutfitSlot>())
        {
            if (!provider.TryGetNothing(slot, out equipment[(int)slot]))
            {
                desired = current;
                reason = $"Nothing appearance for {slot} is unavailable.";
                return false;
            }
        }

        if (!provider.TryGetNoFacewear(out var facewear))
        {
            desired = current;
            reason = "No-facewear appearance is unavailable.";
            return false;
        }

        desired = OutfitData.Create(equipment, facewear, current.HatVisible, current.VisorToggled);
        reason = string.Empty;
        return true;
    }
}
