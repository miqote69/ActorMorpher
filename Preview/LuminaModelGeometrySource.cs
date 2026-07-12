using Dalamud.Plugin.Services;
using Lumina.Data.Files;
using Lumina.Models.Models;
using System.IO;
using LuminaModel = Lumina.Models.Models.Model;

namespace ActorMorpher.Preview;

public sealed class LuminaModelGeometrySource(IDataManager dataManager)
{
    private readonly ModelPreviewMeshBuilder meshBuilder = new();

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
    {
        var file = dataManager.GetFile<MdlFile>(path);
        if (file is null)
            return null;
        if (file.Lods is not { Length: > 0 } || file.Meshes is null || file.VertexDeclarations is null)
            throw new InvalidDataException("MDL does not contain High LOD geometry.");
        if (file.Meshes.Length > ModelPreviewMeshBuilder.MaximumMeshCount
            || file.Meshes.Sum(static mesh => (long)mesh.VertexCount) > ModelPreviewMeshBuilder.MaximumVerticesPerModel
            || file.Meshes.Sum(static mesh => (long)mesh.IndexCount) > ModelPreviewMeshBuilder.MaximumIndicesPerModel)
            throw new InvalidDataException("MDL geometry exceeds the preview limits.");

        var model = new LuminaModel(file, LuminaModel.ModelLod.High);
        var candidates = model.Meshes
            .Where(static mesh => mesh.Types.Contains(Mesh.MeshType.Main))
            .ToArray();
        if (candidates.Length == 0)
            candidates = model.Meshes;

        var sources = candidates.Select(static mesh => new ModelPreviewSourceMesh(
            mesh.MeshIndex,
            mesh.Material.MaterialPath,
            mesh.Vertices.Select(static vertex => new ModelPreviewSourceVertex(
                vertex.Position,
                vertex.Normal,
                vertex.UV,
                vertex.Color)).ToArray(),
            mesh.Indices)).ToArray();
        var cpuModel = meshBuilder.Build(sources);
        return cpuModel with { LodCount = file.Lods.Length };
    }
}
