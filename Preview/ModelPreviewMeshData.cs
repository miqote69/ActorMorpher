using System.Numerics;

namespace ActorMorpher.Preview;

public readonly record struct ModelPreviewSourceVertex(
    Vector4? Position,
    Vector3? Normal,
    Vector4? UV,
    Vector4? Color,
    Vector4? BoneWeights = null,
    ModelPreviewBoneIndices? BoneIndices = null);

public readonly record struct ModelPreviewBoneIndices(ushort X, ushort Y, ushort Z, ushort W)
{
    public ushort this[int index]
        => index switch
        {
            0 => X,
            1 => Y,
            2 => Z,
            3 => W,
            _ => throw new ArgumentOutOfRangeException(nameof(index)),
        };
}

public sealed record ModelPreviewSourceMesh(
    int SourceIndex,
    string MaterialPath,
    ModelPreviewSourceVertex[] Vertices,
    ushort[] Indices,
    IReadOnlyList<string>? Bones = null);

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
