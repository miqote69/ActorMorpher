namespace ActorMorpher.Preview;

public sealed class ModelPreviewGeometryInspector
{
    private readonly Func<string, ModelFileGeometry?> loadGeometry;

    public ModelPreviewGeometryInspector(Func<string, ModelFileGeometry?> loadGeometry)
        => this.loadGeometry = loadGeometry;

    public ModelPreviewGeometryReport Inspect(ModelPreviewAssetReport assets)
    {
        var candidates = assets.Assets
            .Where(static asset => asset.Kind == ModelPreviewAssetKind.Model && asset.IsPresent && asset.Path is not null)
            .ToArray();
        if (candidates.Length == 0)
            return Empty(ModelPreviewGeometryState.Unavailable);

        var parts = new List<ModelPreviewGeometryPart>(candidates.Length);
        foreach (var asset in candidates)
        {
            try
            {
                var geometry = loadGeometry(asset.Path!);
                if (geometry is null)
                {
                    parts.Add(new(asset.Label, asset.Path!, ModelPreviewGeometryPartState.Unavailable, null, "MDL data unavailable."));
                    continue;
                }
                if (!geometry.Bounds.IsValid)
                {
                    parts.Add(new(asset.Label, asset.Path!, ModelPreviewGeometryPartState.InvalidBounds, geometry, "MDL bounds are invalid."));
                    continue;
                }
                parts.Add(new(asset.Label, asset.Path!, ModelPreviewGeometryPartState.Ready, geometry, null));
            }
            catch (Exception exception)
            {
                parts.Add(new(asset.Label, asset.Path!, ModelPreviewGeometryPartState.Failed, null, exception.GetType().Name));
            }
        }

        var ready = parts.Where(static part => part.State == ModelPreviewGeometryPartState.Ready)
            .Select(static part => part.Geometry!)
            .ToArray();
        if (ready.Length == 0)
            return new(ModelPreviewGeometryState.Failed, parts, 0, 0, 0, 0, null, null);

        var bounds = ready[0].Bounds;
        for (var i = 1; i < ready.Length; ++i)
            bounds = bounds.Union(ready[i].Bounds);
        ModelPreviewCameraFrame? autoFrame = ModelPreviewCameraFraming.TryCalculate(bounds, 1.0f, out var frame)
            ? frame
            : null;
        return new ModelPreviewGeometryReport(
            ready.Length == parts.Count ? ModelPreviewGeometryState.Ready : ModelPreviewGeometryState.Partial,
            parts,
            ready.Sum(static geometry => geometry.MeshCount),
            ready.Sum(static geometry => geometry.VertexCount),
            ready.Sum(static geometry => geometry.IndexCount),
            ready.Max(static geometry => geometry.LodCount),
            bounds,
            autoFrame);
    }

    private static ModelPreviewGeometryReport Empty(ModelPreviewGeometryState state)
        => new(state, Array.Empty<ModelPreviewGeometryPart>(), 0, 0, 0, 0, null, null);
}
