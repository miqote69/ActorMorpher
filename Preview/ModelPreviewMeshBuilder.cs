using System.IO;
using System.Numerics;

namespace ActorMorpher.Preview;

public sealed class ModelPreviewMeshBuilder
{
    public const int MaximumMeshCount = 4096;
    public const int MaximumVerticesPerMesh = ushort.MaxValue + 1;
    public const long MaximumVerticesPerModel = 2_000_000;
    public const long MaximumIndicesPerModel = 6_000_000;

    public ModelPreviewCpuModel Build(IReadOnlyList<ModelPreviewSourceMesh> sources)
    {
        if (sources.Count > MaximumMeshCount)
            throw new InvalidDataException("Model mesh count exceeds the preview limit.");

        var totalVertices = sources.Sum(static mesh => (long)mesh.Vertices.Length);
        var totalIndices = sources.Sum(static mesh => (long)mesh.Indices.Length);
        if (totalVertices > MaximumVerticesPerModel || totalIndices > MaximumIndicesPerModel)
            throw new InvalidDataException("Model geometry exceeds the preview limits.");

        var meshes = new List<ModelPreviewCpuMesh>(sources.Count);
        var issues = new List<ModelPreviewMeshIssue>();
        foreach (var source in sources)
        {
            if (TryBuildMesh(source, out var mesh, out var issue))
                meshes.Add(mesh!);
            else
                issues.Add(new ModelPreviewMeshIssue(source.SourceIndex, issue));
        }

        if (meshes.Count == 0)
            throw new InvalidDataException("Model contains no renderable meshes.");

        var bounds = meshes[0].Bounds;
        for (var i = 1; i < meshes.Count; ++i)
            bounds = bounds.Union(meshes[i].Bounds);
        return new ModelPreviewCpuModel(meshes, issues, bounds);
    }

    private static bool TryBuildMesh(
        ModelPreviewSourceMesh source,
        out ModelPreviewCpuMesh? mesh,
        out ModelPreviewMeshIssueKind issue)
    {
        mesh = null;
        if (source.Vertices.Length == 0 || source.Indices.Length == 0)
        {
            issue = ModelPreviewMeshIssueKind.Empty;
            return false;
        }
        if (source.Vertices.Length > MaximumVerticesPerMesh)
        {
            issue = ModelPreviewMeshIssueKind.LimitExceeded;
            return false;
        }
        if (source.Indices.Length % 3 != 0)
        {
            issue = ModelPreviewMeshIssueKind.InvalidIndexCount;
            return false;
        }

        var positions = new Vector3[source.Vertices.Length];
        var normals = new Vector3[source.Vertices.Length];
        var hasNormal = new bool[source.Vertices.Length];
        var minimum = new Vector3(float.PositiveInfinity);
        var maximum = new Vector3(float.NegativeInfinity);
        for (var i = 0; i < source.Vertices.Length; ++i)
        {
            var input = source.Vertices[i];
            if (input.Position is not { } position)
            {
                issue = ModelPreviewMeshIssueKind.MissingPosition;
                return false;
            }

            var value = new Vector3(position.X, position.Y, position.Z);
            if (!IsFinite(value))
            {
                issue = ModelPreviewMeshIssueKind.NonFinitePosition;
                return false;
            }
            positions[i] = value;
            minimum = Vector3.Min(minimum, value);
            maximum = Vector3.Max(maximum, value);

            if (input.Normal is { } normal && IsFinite(normal) && normal.LengthSquared() > 0.000001f)
            {
                normals[i] = Vector3.Normalize(normal);
                hasNormal[i] = true;
            }
        }

        var generatedNormals = new Vector3[source.Vertices.Length];
        for (var i = 0; i < source.Indices.Length; i += 3)
        {
            var first = source.Indices[i];
            var second = source.Indices[i + 1];
            var third = source.Indices[i + 2];
            if (first >= positions.Length || second >= positions.Length || third >= positions.Length)
            {
                issue = ModelPreviewMeshIssueKind.IndexOutOfRange;
                return false;
            }

            var faceNormal = Vector3.Cross(positions[second] - positions[first], positions[third] - positions[first]);
            if (!IsFinite(faceNormal) || faceNormal.LengthSquared() <= 0.000001f)
                continue;
            generatedNormals[first] += faceNormal;
            generatedNormals[second] += faceNormal;
            generatedNormals[third] += faceNormal;
        }

        var vertices = new ModelPreviewVertex[source.Vertices.Length];
        for (var i = 0; i < vertices.Length; ++i)
        {
            var input = source.Vertices[i];
            var normal = hasNormal[i]
                ? normals[i]
                : generatedNormals[i].LengthSquared() > 0.000001f && IsFinite(generatedNormals[i])
                    ? Vector3.Normalize(generatedNormals[i])
                    : Vector3.UnitY;
            var uv = input.UV is { } sourceUv && IsFinite(sourceUv)
                ? new Vector2(sourceUv.X, sourceUv.Y)
                : Vector2.Zero;
            var color = input.Color is { } sourceColor && IsFinite(sourceColor)
                ? sourceColor
                : Vector4.One;
            vertices[i] = new ModelPreviewVertex(positions[i], normal, uv, color);
        }

        issue = default;
        mesh = new ModelPreviewCpuMesh(
            source.SourceIndex,
            source.MaterialPath,
            vertices,
            source.Indices.ToArray(),
            new ModelPreviewBounds(minimum, maximum));
        return true;
    }

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

    private static bool IsFinite(Vector4 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z) && float.IsFinite(value.W);
}
