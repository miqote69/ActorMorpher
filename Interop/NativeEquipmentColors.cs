using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace ActorMorpher.Interop;

// Per-actor GPU textures only. Shared material resources and game equipment stay intact.
internal sealed unsafe class NativeEquipmentColors : IDisposable
{
    private readonly Hook<SetupSlot> hook;
    private readonly IObjectTable objects;
    private readonly Func<nint, LogicalActorKey> resolve;
    private readonly ActorAppearancePersistence persistence;
    [ThreadStatic] private static AppearanceData? creating;

    internal static nint DuringCreate(AppearanceData? appearance, Func<nint> create)
    {
        var previous = creating;
        creating = appearance;
        try { return create(); }
        finally { creating = previous; }
    }

    internal NativeEquipmentColors(IGameInteropProvider interop, IObjectTable objects,
        Func<nint, LogicalActorKey> resolve, ActorAppearancePersistence persistence)
    {
        this.objects = objects;
        this.resolve = resolve;
        this.persistence = persistence;
        hook = interop.HookFromAddress<SetupSlot>((nint)CharacterBase.MemberFunctionPointers.SetupSlotModel, OnSetup);
        try { hook.Enable(); }
        catch { hook.Dispose(); throw; }
    }

    public void Dispose() => hook.Dispose();

    private nint OnSetup(CharacterBase* model, uint slot)
    {
        var result = hook.Original(model, slot);
        if (slot >= 10 || model->GetModelType() != CharacterBase.ModelType.Human)
            return result;
        if (creating is { } requested)
        {
            if (requested.ColoredEquipment.Length == 10)
                ApplySlot(model, (int)slot, requested.ColoredEquipment[(int)slot], false);
            return result;
        }
        foreach (var obj in objects)
        {
            if (obj.Address == 0 || ((GameObject*)obj.Address)->GetCharacterBase() != model)
                continue;
            var actor = resolve(obj.Address);
            var outfit = persistence.GetColorOutfit(actor);
            if (outfit is { Equipment.Length: 10 })
                ApplySlot(model, (int)slot, outfit.Equipment[(int)slot], false);
            break;
        }
        return result;
    }

    internal static void ApplySlot(CharacterBase* model, int slot, ArmorAppearance armor, bool clear)
    {
        if (armor.Color1 is null && armor.Color2 is null && !clear)
            return;
        for (var materialIndex = 0; materialIndex < CharacterBase.MaterialsPerSlot; ++materialIndex)
        {
            var index = slot * CharacterBase.MaterialsPerSlot + materialIndex;
            var material = model->Materials[index];
            if (material is null || model->ColorTableTextures[index] is null || material->ColorTable is null)
                continue;
            var table = material->ColorTableSpan.ToArray();
            fixed (Half* data = table)
            {
                if (material->StainTable is not null)
                {
                    if (armor.Stain1 != 0)
                        material->ReadStainingTemplate((ushort*)material->StainTable, armor.Stain1, data, 0);
                    if (armor.Stain2 != 0)
                        material->ReadStainingTemplate((ushort*)material->StainTable, armor.Stain2, data, 1);
                }
                var changed = Transform(table, material->ColorTableWidth, material->ColorTableHeight,
                    material->StainTableSpan, material->StainTableRowByteLength, armor);
                if (!changed && !clear)
                    continue;
                var texture = Texture.CreateTexture2D(material->ColorTableWidth, material->ColorTableHeight, 1,
                    TextureFormat.R16G16B16A16_FLOAT,
                    TextureFlags.TextureType2D | TextureFlags.Managed | TextureFlags.Immutable, 7);
                if (texture is null)
                    continue;
                if (!texture->InitializeContents(data))
                {
                    texture->DecRef();
                    continue;
                }
                var old = model->ColorTableTextures[index];
                model->ColorTableTextures[index] = texture;
                old->DecRef();
            }
        }
    }

    internal static bool Transform(Span<Half> table, int width, int height,
        ReadOnlySpan<byte> dyes, int dyeRowSize, ArmorAppearance armor)
    {
        var changed = false;
        for (var row = 0; row < height && (row + 1) * dyeRowSize <= dyes.Length; ++row)
        {
            var bits = dyeRowSize == 2
                ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(dyes[(row * dyeRowSize)..])
                : System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(dyes[(row * dyeRowSize)..]);
            var channel = dyeRowSize == 2 ? 0 : (int)((bits >> 27) & 3);
            var color = channel == 0 ? armor.Color1 : channel == 1 ? armor.Color2 : null;
            if ((bits & 1) == 0 || color is not { } rgb)
                continue;
            var start = row * width * 4;
            table[start] = (Half)(rgb.R * rgb.R);
            table[start + 1] = (Half)(rgb.G * rgb.G);
            table[start + 2] = (Half)(rgb.B * rgb.B);
            changed = true;
        }
        return changed;
    }

    private delegate nint SetupSlot(CharacterBase* model, uint slot);
}
