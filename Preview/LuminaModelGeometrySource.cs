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
    private readonly ConcurrentDictionary<string, bool> materialBackfaceCache = new(StringComparer.Ordinal);
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
        return materialBackfaceCache.GetOrAdd(materialPath, LoadShowsBackfaces);
    }

    public ModelPreviewCpuModel? LoadCpuModel(
        string path,
        byte requestedVariant,
        ushort humanTargetCode,
        byte facialFeatures)
    {
        var data = dataManager.GetFile(path)?.Data;
        if (data is null)
            return null;
        var parsed = parser.Parse(
            data,
            path.Contains("/obj/face/", StringComparison.Ordinal) ? facialFeatures : null);
        var materialVariant = ResolveMaterialVariant(path, requestedVariant);
        IReadOnlyList<ModelPreviewSourceMesh> sources = parsed.Meshes.Select(mesh => mesh with
        {
            MaterialPath = ResolveMaterialPath(mesh.MaterialPath, materialVariant, requestedVariant),
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

    private bool LoadShowsBackfaces(string materialPath)
    {
        try
        {
            var data = dataManager.GetFile(materialPath)?.Data;
            return data is null || materialParser.Parse(data).ShowBackfaces;
        }
        catch
        {
            // Unknown materials stay visible instead of losing geometry.
            return true;
        }
    }

    private byte ResolveMaterialVariant(string modelPath, byte requestedVariant)
    {
        if (requestedVariant == 0 || !TryGetImc(modelPath, out var imcPath, out var partIndex))
            return requestedVariant == 0 ? (byte)1 : requestedVariant;
        try
        {
            var imc = dataManager.GetFile<ImcFile>(imcPath);
            var variantIndex = requestedVariant - 1;
            if (imc is null || variantIndex >= imc.Count || partIndex >= imc.GetParts().Length)
                return requestedVariant;
            var materialId = imc.GetVariant(partIndex, variantIndex).MaterialId;
            return materialId == 0 ? (byte)1 : materialId;
        }
        catch
        {
            return requestedVariant;
        }
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
}
