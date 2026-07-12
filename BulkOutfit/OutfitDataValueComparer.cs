namespace ActorMorpher.BulkOutfit;

public static class OutfitDataValueComparer
{
    public static bool AreEqual(OutfitData? left, OutfitData? right)
    {
        if (ReferenceEquals(left, right))
            return true;
        if (left is null || right is null
            || left.Facewear != right.Facewear
            || left.HatVisible != right.HatVisible
            || left.VisorToggled != right.VisorToggled
            || left.Equipment.Length != right.Equipment.Length)
            return false;

        for (var index = 0; index < left.Equipment.Length; index++)
        {
            if (left.Equipment[index] != right.Equipment[index])
                return false;
        }
        return true;
    }
}
