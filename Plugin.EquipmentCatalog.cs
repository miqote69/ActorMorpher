using Dalamud.Game;
using Lumina.Excel.Sheets;



namespace ActorMorpher;

public sealed partial class Plugin
{
    private readonly Dictionary<ClientLanguage, Dictionary<(int Slot, uint Model), EquipmentItemInfo[]>> equipmentItemInfos = new();
    public string GetClassJobName(uint id)
        => DataManager.GetExcelSheet<ClassJob>(ClientState.ClientLanguage).TryGetRow(id, out var row) && !row.Name.IsEmpty
            ? $"{row.Name} ({id})" : id.ToString();

    private EquipmentItemInfo[] GetEquipmentItemInfos(int slot, uint model)
    {
        var language = ClientState.ClientLanguage;
        if (!equipmentItemInfos.TryGetValue(language, out var cache))
        {
            var entries = new List<((int Slot, uint Model) Key, EquipmentItemInfo Item)>();
            foreach (var item in DataManager.GetExcelSheet<Item>(language))
            {
                if (item.Name.IsEmpty || item.ModelMain == 0 || !item.EquipSlotCategory.IsValid || item.EquipSlotCategory.RowId == 0)
                    continue;
                var info = CreateEquipmentItemInfo(item);
                foreach (var itemSlot in GetOutfitSlots(item.EquipSlotCategory.Value))
                    entries.Add((((int)itemSlot, (uint)(item.ModelMain & 0xFFFFFF)), info));
            }
            cache = entries.GroupBy(entry => entry.Key).ToDictionary(group => group.Key, group => group.Select(entry => entry.Item).ToArray());
            equipmentItemInfos[language] = cache;
        }
        return cache.GetValueOrDefault((slot, model)) ?? [];
    }

    private static EquipmentItemInfo CreateEquipmentItemInfo(Item item)
        => new(item.RowId, item.Name.ToString(), item.Icon, item.LevelItem.RowId, item.LevelEquip);
}
