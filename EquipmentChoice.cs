using System.Globalization;
using ActorMorpher.Localization;
using Dalamud.Game;

namespace ActorMorpher;

// Slot 10 denotes facewear without extending the native ten-slot armor array.
public readonly record struct EquipmentChoiceKey(int Slot, ushort Model, byte Variant, ushort FacewearId = 0);

public sealed record EquipmentChoice(EquipmentChoiceKey Key, string Name, uint IconId)
{
    public string Number => Key.Slot == 10 ? Key.Model.ToString(CultureInfo.InvariantCulture)
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
            var prefix = slot >= (int)OutfitSlot.Ears && slot < 10 ? 'a' : 'e';
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
