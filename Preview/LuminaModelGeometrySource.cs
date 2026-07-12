using Dalamud.Plugin.Services;
using Lumina.Data.Files;
using System.Numerics;

namespace ActorMorpher.Preview;

public sealed class LuminaModelGeometrySource(IDataManager dataManager)
{
    public ModelFileGeometry? Load(string path)
    {
        var file = dataManager.GetFile<MdlFile>(path);
        if (file is null)
            return null;

        var min = file.ModelBoundingBoxes.Min;
        var max = file.ModelBoundingBoxes.Max;
        var bounds = min is { Length: >= 3 } && max is { Length: >= 3 }
            ? new ModelPreviewBounds(
                new Vector3(min[0], min[1], min[2]),
                new Vector3(max[0], max[1], max[2]))
            : default;
        return new ModelFileGeometry(
            file.Meshes?.Length ?? 0,
            file.Meshes?.Sum(static mesh => (long)mesh.VertexCount) ?? 0,
            file.Meshes?.Sum(static mesh => (long)mesh.IndexCount) ?? 0,
            file.Lods?.Length ?? 0,
            bounds);
    }
}
