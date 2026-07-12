using Dalamud.Plugin.Services;

namespace ActorMorpher.Preview;

public sealed class LuminaModelGeometrySource(IDataManager dataManager)
{
    private readonly ModelPreviewMeshBuilder meshBuilder = new();
    private readonly MdlPreviewParser parser = new();

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
        var data = dataManager.GetFile(path)?.Data;
        if (data is null)
            return null;
        var parsed = parser.Parse(data);
        var cpuModel = meshBuilder.Build(parsed.Meshes);
        return cpuModel with { LodCount = parsed.LodCount };
    }
}
