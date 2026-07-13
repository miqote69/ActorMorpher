using System.Numerics;

namespace ActorMorpher.Preview;

public readonly record struct SoftwareModelPreviewTriangle(
    Vector3 First,
    Vector3 Second,
    Vector3 Third,
    Vector2 FirstUv,
    Vector2 SecondUv,
    Vector2 ThirdUv,
    Vector3 FirstNormal,
    Vector3 SecondNormal,
    Vector3 ThirdNormal,
    Vector3 FaceNormal,
    Vector4 Color,
    string MaterialPath,
    bool ShowBackfaces,
    bool IsBodySkin);

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
    Vector2 FirstUv,
    Vector2 SecondUv,
    Vector2 ThirdUv,
    float Depth,
    Vector4 Color,
    Vector4 FirstTextureTint,
    Vector4 SecondTextureTint,
    Vector4 ThirdTextureTint,
    string MaterialPath,
    bool IsBackFacing,
    bool IsBodySkin);
