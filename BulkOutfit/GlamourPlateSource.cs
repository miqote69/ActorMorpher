using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Plugin.Services;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace ActorMorpher.BulkOutfit;

internal static class GlamourPlateSource
{
    public const int PlateCount = 20;

    private const string RequestSignature = "40 53 48 83 EC 30 80 B9 68 18 00 00 00 48 8B D9 75 21 45 33 C9 C7 44 24 20 00 00 00 00 45 33 C0 33 D2 B9 34 09 00 00";
    private static nint requestAddress;
    internal static nint RequestAddress => requestAddress;

    internal static unsafe bool CanRequest => MirageManager.Instance() != null
        && FFXIVClientStructs.FFXIV.Client.UI.UIGlobals.CanApplyGlamourPlates();

    internal static unsafe bool IsRequested => MirageManager.Instance() != null
        && MirageManager.Instance()->GlamourPlatesRequested;

    internal static unsafe SavedGlamourPlates Capture(SavedGlamourPlates? previous)
        => Capture(MirageManager.Instance(), previous);

    internal static unsafe SavedGlamourPlates Capture(MirageManager* manager, SavedGlamourPlates? previous)
    {
        var equal = previous?.Plates.Length == PlateCount;
        for (var i = 0; equal && i < PlateCount; i++)
        {
            ref var plate = ref manager->GlamourPlates[i];
            var saved = previous!.Plates[i];
            equal = plate.ItemIds.SequenceEqual(saved.Items)
                && plate.Stain0Ids.SequenceEqual(saved.Stain1)
                && plate.Stain1Ids.SequenceEqual(saved.Stain2);
        }
        if (equal)
            return previous!;
        var result = new SavedGlamourPlates { AcquiredAt = DateTimeOffset.UtcNow,
            Plates = new SavedGlamourPlate[PlateCount] };
        for (var i = 0; i < PlateCount; i++)
        {
            ref var plate = ref manager->GlamourPlates[i];
            result.Plates[i] = new SavedGlamourPlate { Items = plate.ItemIds.ToArray(),
                Stain1 = plate.Stain0Ids.ToArray(), Stain2 = plate.Stain1Ids.ToArray() };
        }
        return result;
    }

    internal readonly record struct Observation(nint ManagerAddress, bool Requested, bool Loaded,
        int NonEmptyPlates, int NonEmptyItems, string DataHash);

    internal static unsafe Observation Observe() => Observe(MirageManager.Instance());

    internal static unsafe Observation Observe(MirageManager* manager)
    {
        if (manager == null)
            return default;
        var nonEmptyPlates = 0;
        var nonEmptyItems = 0;
        foreach (ref var plate in manager->GlamourPlates)
        {
            var populated = 0;
            foreach (var item in plate.ItemIds)
                if (item != 0) populated++;
            if (populated != 0) nonEmptyPlates++;
            nonEmptyItems += populated;
        }
        var hash = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(manager->GlamourPlates)));
        return new((nint)manager, manager->GlamourPlatesRequested, manager->GlamourPlatesLoaded,
            nonEmptyPlates, nonEmptyItems, hash);
    }

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
