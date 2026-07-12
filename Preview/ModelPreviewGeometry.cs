using System.Numerics;

namespace ActorMorpher.Preview;

public readonly record struct ModelPreviewBounds(Vector3 Min, Vector3 Max)
{
    public Vector3 Center => (Min + Max) / 2.0f;
    public Vector3 Size => Max - Min;

    public bool IsValid
        => IsFinite(Min)
        && IsFinite(Max)
        && Min.X <= Max.X
        && Min.Y <= Max.Y
        && Min.Z <= Max.Z
        && Size.LengthSquared() > 0.000001f;

    public ModelPreviewBounds Union(ModelPreviewBounds other)
        => new(Vector3.Min(Min, other.Min), Vector3.Max(Max, other.Max));

    private static bool IsFinite(Vector3 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}

public sealed record ModelFileGeometry(
    int MeshCount,
    long VertexCount,
    long IndexCount,
    int LodCount,
    ModelPreviewBounds Bounds);

public enum ModelPreviewGeometryPartState
{
    Ready,
    Unavailable,
    InvalidBounds,
    Failed,
}

public sealed record ModelPreviewGeometryPart(
    string Label,
    string Path,
    ModelPreviewGeometryPartState State,
    ModelFileGeometry? Geometry,
    string? Failure);

public enum ModelPreviewGeometryState
{
    Unavailable,
    Ready,
    Partial,
    Failed,
}

public sealed record ModelPreviewGeometryReport(
    ModelPreviewGeometryState State,
    IReadOnlyList<ModelPreviewGeometryPart> Parts,
    int MeshCount,
    long VertexCount,
    long IndexCount,
    int MaximumLodCount,
    ModelPreviewBounds? Bounds,
    ModelPreviewCameraFrame? AutoFrame)
{
    public long TriangleCount => IndexCount / 3;
    public int ReadyPartCount => Parts.Count(static part => part.State == ModelPreviewGeometryPartState.Ready);
    public int FailedPartCount => Parts.Count - ReadyPartCount;
}

public readonly record struct ModelPreviewCameraFrame(
    Vector3 Target,
    float Distance,
    float NearPlane,
    float FarPlane);
