using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Plugin.Services;

namespace ActorMorpher.BulkOutfit;

internal static class GlamourPlateSource
{
    public const int PlateCount = 20;

    private const string RequestSignature = "40 53 48 83 EC 30 80 B9 68 18 00 00 00 48 8B D9 75 21 45 33 C9 C7 44 24 20 00 00 00 00 45 33 C0 33 D2 B9 34 09 00 00";
    private static nint requestAddress;

    internal static unsafe bool IsLoaded
    {
        get
        {
            var manager = MirageManager.Instance();
            return manager != null && manager->GlamourPlatesLoaded;
        }
    }

    // The game's data-request function only; no plate UI, Apply, or Save.
    internal static unsafe bool Request(ISigScanner scanner)
    {
        var manager = MirageManager.Instance();
        if (manager == null)
            return false;
        if (requestAddress == 0 && !scanner.TryScanText(RequestSignature, out requestAddress))
            return false;
        ((delegate* unmanaged<MirageManager*, void>)requestAddress)(manager);
        return true;
    }

    public static unsafe bool TryRead(int index, Func<uint, ulong?> resolveModel,
        out ArmorAppearance[] equipment, out uint missingItem)
    {
        equipment = [];
        missingItem = 0;
        var manager = MirageManager.Instance();
        if (manager == null || !manager->GlamourPlatesLoaded
            || (uint)index >= (uint)manager->GlamourPlates.Length)
            return false;
        ref var plate = ref manager->GlamourPlates[index];
        return TryConvert(plate.ItemIds, plate.Stain0Ids, plate.Stain1Ids,
            resolveModel, out equipment, out missingItem);
    }

    internal static bool TryConvert(ReadOnlySpan<uint> items, ReadOnlySpan<byte> stain1,
        ReadOnlySpan<byte> stain2, Func<uint, ulong?> resolveModel,
        out ArmorAppearance[] equipment, out uint missingItem)
    {
        equipment = new ArmorAppearance[10];
        missingItem = 0;
        // Plate order: main/off hand, head/body/hands/legs/feet, ears/neck/wrists/right/left ring.
        for (var slot = 0; slot < equipment.Length; ++slot)
        {
            var plateSlot = slot + 2;
            var itemId = items[plateSlot];
            if (itemId == 0)
                continue;
            var model = resolveModel(itemId);
            if (model is null)
            {
                equipment = [];
                missingItem = itemId;
                return false;
            }
            equipment[slot] = new ArmorAppearance((ushort)(model.Value & 0xFFFF),
                (byte)((model.Value >> 16) & 0xFF), stain1[plateSlot], stain2[plateSlot]);
        }
        return true;
    }
}
