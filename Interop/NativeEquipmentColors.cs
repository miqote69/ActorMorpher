using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using Character = FFXIVClientStructs.FFXIV.Client.Game.Character.Character;
using DrawDataContainer = FFXIVClientStructs.FFXIV.Client.Game.Character.DrawDataContainer;

namespace ActorMorpher.Interop;

// Per-actor GPU textures only. Shared material resources and game equipment stay intact.
internal sealed unsafe class NativeEquipmentColors : IDisposable
{
    private static byte[] metallicStains = [];
    private static readonly Vector3[] StainColors = new Vector3[256];
    private static Vector3[] metallicColors = [];
    // ReadStainingTemplate selects its legacy field map using this same shader resource.
    private const int CharacterLegacyShaderResourceIndex = 86;
    private readonly Hook<SetupSlot> hook;
    private readonly IObjectTable objects;
    private readonly Func<nint, LogicalActorKey> resolve;
    private readonly ActorAppearancePersistence persistence;
    private readonly IDiagnosticLog diagnostics;
    [ThreadStatic] private static AppearanceData? creating;

    internal static nint DuringCreate(AppearanceData? appearance, Func<nint> create)
    {
        var previous = creating;
        creating = appearance;
        try { return create(); }
        finally { creating = previous; }
    }

    internal NativeEquipmentColors(IGameInteropProvider interop, IObjectTable objects,
        Func<nint, LogicalActorKey> resolve, ActorAppearancePersistence persistence, IDiagnosticLog diagnostics,
        IDataManager dataManager)
    {
        this.objects = objects;
        this.resolve = resolve;
        this.persistence = persistence;
        this.diagnostics = diagnostics;
        var metallicIds = new List<byte>();
        foreach (var stain in dataManager.GetExcelSheet<Lumina.Excel.Sheets.Stain>())
        {
            if (stain.RowId >= StainColors.Length)
                continue;
            var (r, g, b) = EquipmentDisplayFormatting.DecodeStainColor(stain.Color);
            StainColors[stain.RowId] = new Vector3(r, g, b) / 255f;
            if (stain.IsMetallic)
                metallicIds.Add((byte)stain.RowId);
        }
        metallicStains = metallicIds.ToArray();
        metallicColors = metallicStains.Select(id => StainColors[id]).ToArray();
        hook = interop.HookFromAddress<SetupSlot>((nint)CharacterBase.MemberFunctionPointers.SetupSlotModel, OnSetup);
        try { hook.Enable(); }
        catch { hook.Dispose(); throw; }
    }

    public void Dispose() => hook.Dispose();

    private nint OnSetup(CharacterBase* model, uint slot)
    {
        NativeOutfitMemory.ObserveAnimation(diagnostics, model, "BeforeSlotSetup", slot);
        var result = hook.Original(model, slot);
        NativeOutfitMemory.ObserveAnimation(diagnostics, model, "AfterSlotSetup", slot);
        if (model->GetModelType() == CharacterBase.ModelType.Weapon)
        {
            ApplyRetainedWeaponSlot(model, (int)slot);
            return result;
        }
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

    private void ApplyRetainedWeaponSlot(CharacterBase* model, int slot)
    {
        foreach (var obj in objects)
        {
            if (obj is not Dalamud.Game.ClientState.Objects.Types.ICharacter || obj.Address == 0)
                continue;
            var character = (Character*)obj.Address;
            for (var hand = 0; hand < 2; ++hand)
            {
                var weaponSlot = hand == 0 ? DrawDataContainer.WeaponSlot.MainHand : DrawDataContainer.WeaponSlot.OffHand;
                if ((CharacterBase*)character->DrawData.Weapon(weaponSlot).Weapon != model)
                    continue;
                var packed = NativeAppearanceMemory.CaptureRenderedWeapon(character, weaponSlot);
                var dyes = persistence.GetWeaponDyes(resolve(obj.Address), hand == 1, packed);
                ApplySlot(model, slot, dyes.AsArmor(packed), false);
                return;
            }
        }
    }

    internal readonly record struct ColorApplyResult(int Updated, bool Failed)
    {
        public bool Applied => Updated > 0 && !Failed;
        public static ColorApplyResult operator +(ColorApplyResult left, ColorApplyResult right)
            => new(left.Updated + right.Updated, left.Failed || right.Failed);
    }

    internal static ColorApplyResult ApplySlot(CharacterBase* model, int slot, ArmorAppearance armor, bool clear,
        int? reportChannel = null)
    {
        var result = new ColorApplyResult();
        if (armor.Color1 is null && armor.Color2 is null && !clear)
            return result;
        for (var materialIndex = 0; materialIndex < CharacterBase.MaterialsPerSlot; ++materialIndex)
        {
            var index = slot * CharacterBase.MaterialsPerSlot + materialIndex;
            var material = model->Materials[index];
            if (material is null || material->ColorTable is null)
                continue;
            if (model->ColorTableTextures[index] is null)
            {
                result = result with { Failed = true };
                continue;
            }
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
                Half[] metallicTable = [];
                if (material->StainTable is not null &&
                    (armor.Color1?.Metallic == true || armor.Color2?.Metallic == true))
                {
                    var profiles = new Half[metallicStains.Length][];
                    for (var dye = 0; dye < metallicStains.Length; ++dye)
                    {
                        profiles[dye] = (Half[])table.Clone();
                        fixed (Half* metallicData = profiles[dye])
                        {
                            if (armor.Color1?.Metallic == true)
                                material->ReadStainingTemplate((ushort*)material->StainTable, metallicStains[dye], metallicData, 0);
                            if (armor.Color2?.Metallic == true)
                                material->ReadStainingTemplate((ushort*)material->StainTable, metallicStains[dye], metallicData, 1);
                        }
                    }
                    metallicTable = BlendMetallicTables(table, material->ColorTableWidth, material->ColorTableHeight,
                        material->StainTableSpan, material->StainTableRowByteLength, armor, metallicColors, profiles,
                        armor.Stain1 == 0 ? null : StainColors[armor.Stain1],
                        armor.Stain2 == 0 ? null : StainColors[armor.Stain2]);
                }
                var changed = Transform(table, material->ColorTableWidth, material->ColorTableHeight,
                    material->StainTableSpan, material->StainTableRowByteLength, armor, reportChannel, clear, metallicTable,
                    UsesLegacyShader(material, CharacterUtility.Instance()));
                if (!changed && !clear)
                    continue;
                var texture = Texture.CreateTexture2D(material->ColorTableWidth, material->ColorTableHeight, 1,
                    TextureFormat.R16G16B16A16_FLOAT,
                    TextureFlags.TextureType2D | TextureFlags.Managed | TextureFlags.Immutable, 7);
                if (texture is null)
                {
                    result = result with { Failed = true };
                    continue;
                }
                if (!texture->InitializeContents(data))
                {
                    texture->DecRef();
                    result = result with { Failed = true };
                    continue;
                }
                var old = model->ColorTableTextures[index];
                model->ColorTableTextures[index] = texture;
                old->DecRef();
                if (changed)
                    result = result with { Updated = result.Updated + 1 };
            }
        }
        return result;
    }

    internal static bool UsesLegacyShader(MaterialResourceHandle* material, CharacterUtility* utility)
        => material->ShaderPackageResourceHandle ==
            (ShaderPackageResourceHandle*)utility->ResourceHandles[CharacterLegacyShaderResourceIndex].Value;

    internal static Half[] BlendMetallicTables(ReadOnlySpan<Half> original, int width, int height,
        ReadOnlySpan<byte> dyes, int dyeRowSize, ArmorAppearance armor, ReadOnlySpan<Vector3> swatches,
        Half[][] profiles, Vector3? stain1, Vector3? stain2)
    {
        var blended = original.ToArray();
        Span<float> weights = stackalloc float[swatches.Length];
        for (var row = 0; row < height && (row + 1) * dyeRowSize <= dyes.Length; ++row)
        {
            var bits = dyeRowSize == 2
                ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(dyes[(row * dyeRowSize)..])
                : System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(dyes[(row * dyeRowSize)..]);
            var channel = dyeRowSize == 2 ? 0 : (int)((bits >> 27) & 3);
            var color = channel == 0 ? armor.Color1 : channel == 1 ? armor.Color2 : null;
            if (color is not { Metallic: true } rgb)
                continue;
            var start = row * width * 4;
            var selected = rgb.UseOriginalColor
                ? (channel == 0 ? stain1 : stain2) ?? new Vector3(
                    MathF.Sqrt((float)original[start]), MathF.Sqrt((float)original[start + 1]), MathF.Sqrt((float)original[start + 2]))
                : new Vector3(rgb.R, rgb.G, rgb.B);
            MetallicWeights(selected, swatches, weights);
            var strongest = 0;
            for (var dye = 1; dye < weights.Length; ++dye)
                if (weights[dye] > weights[strongest]) strongest = dye;
            for (var field = 0; field < width * 4; ++field)
            {
                // Sphere-map index is categorical data, not a value that can be interpolated.
                if (field == 27)
                {
                    blended[start + field] = profiles[strongest][start + field];
                    continue;
                }
                var value = 0f;
                for (var dye = 0; dye < weights.Length; ++dye)
                    if (weights[dye] != 0)
                        value += (float)profiles[dye][start + field] * weights[dye];
                blended[start + field] = (Half)value;
            }
        }
        return blended;
    }

    private static void MetallicWeights(Vector3 selected, ReadOnlySpan<Vector3> swatches, Span<float> weights)
    {
        var total = 0f;
        for (var dye = 0; dye < swatches.Length; ++dye)
        {
            var distanceSquared = Vector3.DistanceSquared(selected, swatches[dye]);
            // Exact game swatches reproduce their native template, with no tint or neutralization.
            if (distanceSquared < 1e-12f)
            {
                weights.Clear();
                weights[dye] = 1;
                return;
            }
            weights[dye] = 1 / (distanceSquared * distanceSquared);
            total += weights[dye];
        }
        for (var dye = 0; dye < weights.Length; ++dye)
            weights[dye] /= total;
    }

    internal static bool Transform(Span<Half> table, int width, int height,
        ReadOnlySpan<byte> dyes, int dyeRowSize, ArmorAppearance armor, int? reportChannel = null, bool clear = false,
        ReadOnlySpan<Half> metallicTable = default, bool legacyShader = false)
    {
        var changed = false;
        for (var row = 0; row < height && (row + 1) * dyeRowSize <= dyes.Length; ++row)
        {
            var bits = dyeRowSize == 2
                ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian(dyes[(row * dyeRowSize)..])
                : System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(dyes[(row * dyeRowSize)..]);
            var channel = dyeRowSize == 2 ? 0 : (int)((bits >> 27) & 3);
            var report = channel < 2 && (reportChannel is null || reportChannel == channel);
            var color = channel == 0 ? armor.Color1 : channel == 1 ? armor.Color2 : null;
            var finishFlags = bits & (legacyShader ? 0x1fu : 0xfffu);
            if (color is not { } rgb)
            {
                // ApplySlot has already rebuilt the native table before clearing a custom dye.
                if (clear && report && finishFlags != 0)
                    changed = true;
                continue;
            }
            var start = row * width * 4;
            if (rgb.Metallic == true && finishFlags != 0)
            {
                var profile = metallicTable.Slice(start, width * 4);
                var target = table.Slice(start, width * 4);
                ApplyMetallicProfile(target, profile, finishFlags, legacyShader);
                changed |= report;
                continue;
            }
            if ((bits & 1) != 0 && !rgb.UseOriginalColor)
            {
                table[start] = (Half)(rgb.R * rgb.R);
                table[start + 1] = (Half)(rgb.G * rgb.G);
                table[start + 2] = (Half)(rgb.B * rgb.B);
                changed |= report;
            }
            // OFF uploads the rebuilt native finish, including when RGB was not edited.
            if (rgb.Metallic == false && finishFlags != 0)
                changed |= report;
        }
        return changed;
    }

    private static void ApplyMetallicProfile(Span<Half> target, ReadOnlySpan<Half> profile, uint flags,
        bool legacy)
    {
        for (var color = 0; color < 3; ++color)
            if ((flags & (1u << color)) != 0)
                profile.Slice(color * 4, 3).CopyTo(target.Slice(color * 4, 3));
        ReadOnlySpan<int> offsets = legacy ? [3, 7] : [11, 18, 16, 12, 13, 14, 19, 27, 21];
        for (var scalar = 0; scalar < offsets.Length; ++scalar)
            if ((flags & (8u << scalar)) != 0)
                target[offsets[scalar]] = profile[offsets[scalar]];
    }

    private delegate nint SetupSlot(CharacterBase* model, uint slot);
}
