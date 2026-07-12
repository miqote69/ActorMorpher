namespace ActorMorpher;

public sealed record EquipmentDisplayEntry(
    OutfitSlot Slot,
    ushort Set,
    byte Variant,
    string Name,
    uint IconId);

public sealed record EquipmentItemDisplay(string Name, uint IconId);
