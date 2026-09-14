using System.Globalization;
using ActorMorpher.Localization;
using Dalamud.Game;

namespace ActorMorpher;

// Slot 10 denotes facewear without extending the native ten-slot armor array.
public readonly record struct EquipmentChoiceKey(int Slot, ushort Model, byte Variant, ushort FacewearId = 0,
    ulong WeaponModel = 0)
{
    public bool IsWeapon => Slot is 11 or 12;
}

public sealed record EquipmentChoice(EquipmentChoiceKey Key, string Name, uint IconId)
{
    public EquipmentItemInfo[] Items { get; init; } = [];
    public string ItemLevels => Items.Length == 0 ? "—" : string.Join(" / ", Items.Select(item => item.ItemLevel).Distinct().Order());

    public EquipmentChoice? FashionOnly()
    {
        var matches = Items.Where(item => item.EquipLevel == 1 && item.ItemLevel == 1).ToArray();
        return matches.Length == 0 ? null : this with { Items = matches,
            Name = string.Join(" / ", matches.Select(item => item.Name).Distinct()), IconId = matches[0].IconId };
    }

    public static EquipmentChoice[] FilterForPicker(IEnumerable<EquipmentChoice> choices,
        IReadOnlyCollection<EquipmentChoiceKey> favorites, EquipmentPickerFilter filter, string query, ClientLanguage language)
        => choices.Select(choice => filter == EquipmentPickerFilter.Fashion ? choice.FashionOnly() : choice)
            .OfType<EquipmentChoice>()
            .Where(choice => (filter != EquipmentPickerFilter.Favorites || favorites.Contains(choice.Key)) && choice.Matches(query, language))
            .OrderBy(choice => choice.Key.Model)
            .ThenBy(choice => choice.Key.IsWeapon ? (ushort)(choice.Key.WeaponModel >> 16) : 0)
            .ThenBy(choice => choice.DisplayVariant).ThenBy(choice => choice.Key.FacewearId).ToArray();
    public int DisplayVariant => Key.IsWeapon ? (ushort)(Key.WeaponModel >> 32) : Key.Variant;
    public string Number => Key.IsWeapon ? $"w{Key.Model:D4} / b{(ushort)(Key.WeaponModel >> 16):D4}"
        : Key.Slot == 10 ? Key.Model.ToString(CultureInfo.InvariantCulture)
        : EquipmentDisplayFormatting.FormatSet((OutfitSlot)Key.Slot, Key.Model);

    public bool Matches(string query, ClientLanguage language)
    {
        query = query.Trim();
        return query.Length == 0 || GameTextComparison.Contains(Name, query, language)
            || (TryParseModel(query, Key.Slot, out var model) && model == Key.Model);
    }

    public static bool TryParseModel(string text, int slot, out ushort model)
    {
        text = text.Trim();
        if (text.Length > 0 && char.IsLetter(text[0]))
        {
            var prefix = slot is 11 or 12 ? 'w' : slot >= (int)OutfitSlot.Ears && slot < 10 ? 'a' : 'e';
            if (slot == 10 || char.ToLowerInvariant(text[0]) != prefix)
            {
                model = 0;
                return false;
            }
            text = text[1..];
        }
        return ushort.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out model);
    }

    public static OutfitData Replace(OutfitData outfit, EquipmentChoiceKey choice)
        => choice.Slot == 10
            ? outfit with { Facewear = new FacewearAppearance(true, choice.FacewearId) }
            : outfit with { Equipment = outfit.Equipment.SetItem(choice.Slot,
                outfit.Equipment[choice.Slot] with { Set = choice.Model, Variant = choice.Variant }) };
}

public sealed record EquipmentItemInfo(uint ItemId, string Name, uint IconId, uint ItemLevel, byte EquipLevel);

public enum EquipmentPickerFilter { All, Fashion, Favorites }
