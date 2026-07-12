using System.Numerics;

namespace ActorMorpher.Preview;

public readonly record struct SoftwareModelPreviewTriangle(
    Vector3 First,
    Vector3 Second,
    Vector3 Third,
    Vector3 Normal,
    Vector4 Color);

public sealed record SoftwareModelPreviewScene(
    IReadOnlyList<SoftwareModelPreviewTriangle> Triangles,
    ModelPreviewBounds Bounds,
    long SourceTriangleCount);

public readonly record struct SoftwareModelPreviewView(
    SoftwareModelPreviewScene Scene,
    float Yaw,
    float Pitch,
    float Zoom);

public readonly record struct SoftwareModelPreviewProjectedTriangle(
    Vector2 First,
    Vector2 Second,
    Vector2 Third,
    float Depth,
    Vector4 Color);
