using Lumina.Excel.Sheets;

namespace ActorMorpher;

internal static class WeaponSelection
{
    internal const ulong ModelMask = 0xFFFFFFFFFFFFUL;

    // ClassJob abbreviations are the named membership columns in ClassJobCategory.
    internal static bool AllowsJob(ClassJobCategory category, string abbreviation)
        => typeof(ClassJobCategory).GetProperty(abbreviation)?.GetValue(category) is true;

    internal static ulong ModelForSlot(int slot, bool mainhand, bool offhand, ulong main, ulong sub)
        => (slot == 11 && mainhand ? main
            : slot == 12 && offhand ? main
            : slot == 12 && mainhand ? sub : 0) & ModelMask;

    internal static IEnumerable<EquipmentChoice> Filter(IEnumerable<EquipmentChoice> choices,
        ISet<EquipmentChoiceKey> favorites, bool favoritesOnly)
        => choices.Where(choice => !favoritesOnly || favorites.Contains(choice.Key));

    internal static bool CanSelect(EquipmentChoiceKey choice, IEnumerable<EquipmentChoice> currentJobChoices)
        => choice == new EquipmentChoiceKey(choice.Slot, 0, 0)
            || currentJobChoices.Any(candidate => candidate.Key == choice);

    internal static AppearanceData Replace(AppearanceData current, EquipmentChoiceKey choice)
    {
        var previous = choice.Slot == 11 ? current.Mainhand : current.Offhand;
        var model = choice.WeaponModel & ModelMask;
        // Native LoadWeapon uses the whole packed value: removal must not retain stain-only bits.
        var weapon = model == 0 ? 0 : model | ((previous ?? 0) & ~ModelMask);
        return choice.Slot == 11 ? current with { Mainhand = weapon } : current with { Offhand = weapon };
    }
}
