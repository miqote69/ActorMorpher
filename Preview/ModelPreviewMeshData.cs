using System.Numerics;

namespace ActorMorpher.Preview;

public readonly record struct ModelPreviewSourceVertex(
    Vector4? Position,
    Vector3? Normal,
    Vector4? UV,
    Vector4? Color);

public sealed record ModelPreviewSourceMesh(
    int SourceIndex,
    string MaterialPath,
    ModelPreviewSourceVertex[] Vertices,
    ushort[] Indices);

public readonly record struct ModelPreviewVertex(
    Vector3 Position,
    Vector3 Normal,
    Vector2 UV,
    Vector4 Color);

public sealed record ModelPreviewCpuMesh(
    int SourceIndex,
    string MaterialPath,
    ModelPreviewVertex[] Vertices,
    ushort[] Indices,
    ModelPreviewBounds Bounds);

public enum ModelPreviewMeshIssueKind
{
    Empty,
    LimitExceeded,
    InvalidIndexCount,
    MissingPosition,
    NonFinitePosition,
    IndexOutOfRange,
}

public sealed record ModelPreviewMeshIssue(int SourceIndex, ModelPreviewMeshIssueKind Kind);

public sealed record ModelPreviewCpuModel(
    IReadOnlyList<ModelPreviewCpuMesh> Meshes,
    IReadOnlyList<ModelPreviewMeshIssue> Issues,
    ModelPreviewBounds Bounds,
    int LodCount = 1)
{
    public long VertexCount => Meshes.Sum(static mesh => (long)mesh.Vertices.Length);
    public long IndexCount => Meshes.Sum(static mesh => (long)mesh.Indices.Length);
}
