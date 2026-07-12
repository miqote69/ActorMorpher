using System.IO;
using System.Numerics;

namespace ActorMorpher.Preview;

public sealed class SoftwareModelPreviewSceneBuilder
{
    public const int MaximumTriangleCount = 200_000;
    private const float MinimumNormalLengthSquared = 1e-20f;

    private static readonly Vector4[] Palette =
    [
        new(0.45f, 0.68f, 0.78f, 1.0f),
        new(0.48f, 0.70f, 0.55f, 1.0f),
        new(0.72f, 0.48f, 0.50f, 1.0f),
        new(0.76f, 0.66f, 0.38f, 1.0f),
        new(0.62f, 0.52f, 0.74f, 1.0f),
    ];

    public SoftwareModelPreviewScene Build(IReadOnlyList<ModelPreviewCpuModel> models)
    {
        if (models.Count == 0)
            throw new InvalidDataException("No CPU models are available for software preview.");

        var sourceTriangleCount = models.Sum(static model => model.IndexCount / 3);
        if (sourceTriangleCount <= 0)
            throw new InvalidDataException("CPU models contain no triangles.");
        if (sourceTriangleCount > MaximumTriangleCount)
            throw new InvalidDataException("CPU models exceed the complete-preview triangle limit.");
        var triangles = new List<SoftwareModelPreviewTriangle>(checked((int)sourceTriangleCount));
        var meshIndex = 0;
        foreach (var model in models)
        {
            foreach (var mesh in model.Meshes)
            {
                var color = GetMeshColor(mesh.MaterialPath, meshIndex++);
                for (var index = 0; index < mesh.Indices.Length; index += 3)
                {
                    var first = mesh.Vertices[mesh.Indices[index]];
                    var second = mesh.Vertices[mesh.Indices[index + 1]];
                    var third = mesh.Vertices[mesh.Indices[index + 2]];
                    var normal = Vector3.Cross(second.Position - first.Position, third.Position - first.Position);
                    if (!IsFinite(normal) || normal.LengthSquared() <= MinimumNormalLengthSquared)
                        continue;
                    triangles.Add(new(
                        first.Position,
                        second.Position,
                        third.Position,
                        first.UV,
                        second.UV,
                        third.UV,
                        Vector3.Normalize(normal),
                        color,
                        mesh.MaterialPath));
                }
            }
        }
        if (triangles.Count == 0)
            throw new InvalidDataException("CPU models contain no non-degenerate preview triangles.");

        var bounds = models[0].Bounds;
        for (var index = 1; index < models.Count; index++)
            bounds = bounds.Union(models[index].Bounds);
        if (!bounds.IsValid)
            throw new InvalidDataException("Combined software preview bounds are invalid.");
        return new SoftwareModelPreviewScene(triangles, bounds, sourceTriangleCount);
    }

    private static Vector4 GetMeshColor(string materialPath, int meshIndex)
    {
        uint hash = 2166136261;
        foreach (var character in materialPath)
            hash = unchecked((hash ^ character) * 16777619);
        return Palette[(int)((hash + (uint)meshIndex) % Palette.Length)];
    }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
