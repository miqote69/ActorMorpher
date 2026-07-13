using System.Collections.Concurrent;
using Dalamud.Plugin.Services;
using Lumina.Data.Files;
using Lumina.Models.Materials;

namespace ActorMorpher.Preview;

public sealed class LuminaModelGeometrySource
{
    private readonly IDataManager dataManager;
    private readonly ModelPreviewMeshBuilder meshBuilder = new();
    private readonly MdlPreviewParser parser = new();
    private readonly MtrlPreviewParser materialParser = new();
    private readonly ConcurrentDictionary<string, MaterialRenderInfo> materialRenderCache = new(StringComparer.Ordinal);
    private readonly HumanPbdDeformer? humanDeformer;

    public LuminaModelGeometrySource(IDataManager dataManager)
    {
        this.dataManager = dataManager;
        try
        {
            var pbd = dataManager.GetFile("chara/xls/boneDeformer/human.pbd")?.Data;
            humanDeformer = pbd is null ? null : new HumanPbdDeformer(pbd);
        }
        catch
        {
            humanDeformer = null;
        }
    }

    public ModelFileGeometry? Load(string path)
    {
        var cpuModel = LoadCpuModel(path);
        return cpuModel is null
            ? null
            : new ModelFileGeometry(
                cpuModel.Meshes.Count,
                cpuModel.VertexCount,
                cpuModel.IndexCount,
                cpuModel.LodCount,
                cpuModel.Bounds,
                cpuModel.Issues.Count);
    }

    public ModelPreviewCpuModel? LoadCpuModel(string path)
        => LoadCpuModel(path, 1, 0, 0);

    public bool CanDeform(ushort targetCode, ushort modelCode)
        => targetCode == modelCode
        || humanDeformer?.CanDeform(targetCode, modelCode) == true;

    public bool ShowsBackfaces(string materialPath)
    {
        if (string.IsNullOrWhiteSpace(materialPath))
            return true;
        return GetMaterialRenderInfo(materialPath).ShowBackfaces;
    }

    public bool IsBodySkin(string materialPath)
        => !string.IsNullOrWhiteSpace(materialPath)
        && GetMaterialRenderInfo(materialPath).IsBodySkin;

    public bool IsLowerBodyEquipment(string materialPath)
        => !string.IsNullOrWhiteSpace(materialPath)
        && GetMaterialRenderInfo(materialPath).IsLowerBodyEquipment;

    public ModelPreviewCpuModel? LoadCpuModel(
        string path,
        byte requestedVariant,
        ushort humanTargetCode,
        byte facialFeatures)
    {
        var data = dataManager.GetFile(path)?.Data;
        if (data is null)
            return null;
        var imc = ResolveImcVariant(path, requestedVariant);
        var parsed = parser.Parse(
            data,
            path.Contains("/obj/face/", StringComparison.Ordinal) ? facialFeatures : null,
            imc.AttributeMask,
            ResolveTailState(path, humanTargetCode));
        IReadOnlyList<ModelPreviewSourceMesh> sources = parsed.Meshes.Select(mesh => mesh with
        {
            MaterialPath = ResolveMaterialPath(mesh.MaterialPath, imc.MaterialId, requestedVariant),
        }).ToArray();
        if (humanTargetCode != 0
            && TryGetHumanCode(path, out var modelCode)
            && modelCode != humanTargetCode)
        {
            if (humanDeformer is null
                || !humanDeformer.TryDeform(humanTargetCode, modelCode, sources, out sources))
                throw new InvalidDataException(
                    $"Human PBD does not contain a deformation path from c{modelCode:D4} to c{humanTargetCode:D4}.");
        }
        var cpuModel = meshBuilder.Build(sources);
        return cpuModel with { LodCount = parsed.LodCount };
    }

    private static bool TryGetHumanCode(string path, out ushort code)
    {
        code = 0;
        var start = path.IndexOf("/c", StringComparison.Ordinal);
        if (start < 0 || start + 6 > path.Length)
            return false;
        return ushort.TryParse(path.AsSpan(start + 2, 4), out code);
    }

    private MaterialRenderInfo GetMaterialRenderInfo(string materialPath)
        => materialRenderCache.GetOrAdd(materialPath, LoadMaterialRenderInfo);

    private MaterialRenderInfo LoadMaterialRenderInfo(string materialPath)
    {
        var isLowerBodyEquipment = materialPath.Contains("/equipment/", StringComparison.Ordinal)
            && materialPath.Contains("_dwn_", StringComparison.OrdinalIgnoreCase);
        try
        {
            var data = dataManager.GetFile(materialPath)?.Data;
            if (data is null)
                return new MaterialRenderInfo(true, false, isLowerBodyEquipment);
            var material = materialParser.Parse(data);
            var isBodySkin = materialPath.Contains("/obj/body/", StringComparison.Ordinal)
                && material.ShaderPackage.EndsWith("skin.shpk", StringComparison.OrdinalIgnoreCase);
            return new MaterialRenderInfo(material.ShowBackfaces, isBodySkin, isLowerBodyEquipment);
        }
        catch
        {
            // Unknown materials stay visible instead of losing geometry.
            return new MaterialRenderInfo(true, false, isLowerBodyEquipment);
        }
    }

    private ImcRenderInfo ResolveImcVariant(string modelPath, byte requestedVariant)
    {
        requestedVariant = requestedVariant == 0 ? (byte)1 : requestedVariant;
        if (!TryGetImc(modelPath, out var imcPath, out var partIndex))
            return new ImcRenderInfo(requestedVariant, null);
        try
        {
            var imc = dataManager.GetFile<ImcFile>(imcPath);
            var variantIndex = requestedVariant - 1;
            if (imc is null || variantIndex >= imc.Count || partIndex >= imc.GetParts().Length)
                return new ImcRenderInfo(requestedVariant, null);
            var variant = imc.GetVariant(partIndex, variantIndex);
            return new ImcRenderInfo(
                variant.MaterialId == 0 ? (byte)1 : variant.MaterialId,
                variant.AttributeMask);
        }
        catch
        {
            return new ImcRenderInfo(requestedVariant, null);
        }
    }

    private static bool? ResolveTailState(string modelPath, ushort humanTargetCode)
    {
        if (humanTargetCode == 0 && !TryGetHumanCode(modelPath, out humanTargetCode))
            return null;
        var genderRace = humanTargetCode / 100;
        return genderRace is 7 or 8 or 13 or 14 or 15 or 16;
    }

    private string ResolveMaterialPath(string materialPath, byte materialVariant, byte requestedVariant)
    {
        if (string.IsNullOrWhiteSpace(materialPath) || !materialPath.StartsWith('/'))
            return materialPath;
        string? first = null;
        foreach (var variant in new[] { materialVariant, requestedVariant, (byte)1 }.Distinct())
        {
            var resolved = Material.ResolveRelativeMaterialPath(materialPath, variant, false);
            first ??= resolved;
            if (resolved is not null && dataManager.FileExists(resolved))
                return resolved;
        }
        return first ?? materialPath;
    }

    private static bool TryGetImc(string modelPath, out string imcPath, out int partIndex)
    {
        imcPath = string.Empty;
        partIndex = 0;
        var modelSeparator = modelPath.IndexOf("/model/", StringComparison.Ordinal);
        if (modelSeparator < 0)
            return false;
        var root = modelPath[..modelSeparator];
        var finalSegment = root[(root.LastIndexOf('/') + 1)..];
        if (modelPath.StartsWith("chara/monster/", StringComparison.Ordinal))
        {
            imcPath = $"{root}/{finalSegment}.imc";
            return true;
        }
        if (!modelPath.StartsWith("chara/demihuman/", StringComparison.Ordinal)
            && !modelPath.StartsWith("chara/equipment/", StringComparison.Ordinal)
            && !modelPath.StartsWith("chara/accessory/", StringComparison.Ordinal))
            return false;

        imcPath = $"{root}/{finalSegment}.imc";
        var suffixStart = modelPath.LastIndexOf('_');
        var suffix = suffixStart < 0 ? string.Empty : modelPath[(suffixStart + 1)..^4];
        partIndex = suffix switch
        {
            "met" => 0,
            "top" => 1,
            "glv" => 2,
            "dwn" => 3,
            "sho" => 4,
            _ => 0,
        };
        return true;
    }

    private readonly record struct MaterialRenderInfo(
        bool ShowBackfaces,
        bool IsBodySkin,
        bool IsLowerBodyEquipment);

    private readonly record struct ImcRenderInfo(byte MaterialId, ushort? AttributeMask);
}
