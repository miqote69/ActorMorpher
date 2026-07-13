using System.Numerics;

namespace ActorMorpher.Preview;

public static class SoftwareModelPreviewProjector
{
    private const float BodySkinDepthOffsetFactor = 0.025f;
    private static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(-0.35f, 0.55f, 0.75f));

    public static IReadOnlyList<SoftwareModelPreviewProjectedTriangle> Project(
        SoftwareModelPreviewView view,
        Vector2 viewportPosition,
        Vector2 viewportSize)
    {
        if (viewportSize.X <= 0 || viewportSize.Y <= 0
            || !float.IsFinite(viewportSize.X) || !float.IsFinite(viewportSize.Y))
            return Array.Empty<SoftwareModelPreviewProjectedTriangle>();

        var scene = view.Scene;
        var maximumExtent = Math.Max(scene.Bounds.Size.X, Math.Max(scene.Bounds.Size.Y, scene.Bounds.Size.Z));
        if (!float.IsFinite(maximumExtent) || maximumExtent <= 0)
            return Array.Empty<SoftwareModelPreviewProjectedTriangle>();
        var zoom = Math.Clamp(view.Zoom, 0.35f, 3.0f);
        var scale = Math.Min(viewportSize.X, viewportSize.Y) * 0.78f * zoom / maximumExtent;
        var center = viewportPosition + viewportSize / 2.0f;
        var rotation = Matrix4x4.CreateRotationY(view.Yaw) * Matrix4x4.CreateRotationX(view.Pitch);
        var projected = new List<SoftwareModelPreviewProjectedTriangle>(scene.Triangles.Count);
        foreach (var triangle in scene.Triangles)
        {
            var first = Vector3.Transform(triangle.First - scene.Bounds.Center, rotation);
            var second = Vector3.Transform(triangle.Second - scene.Bounds.Center, rotation);
            var third = Vector3.Transform(triangle.Third - scene.Bounds.Center, rotation);
            var faceNormal = Vector3.TransformNormal(triangle.FaceNormal, rotation);
            var isBackFacing = faceNormal.Z < 0.0f;
            if (isBackFacing && !triangle.ShowBackfaces)
                continue;
            var firstBrightness = GetBrightness(triangle.FirstNormal, faceNormal, rotation, isBackFacing);
            var secondBrightness = GetBrightness(triangle.SecondNormal, faceNormal, rotation, isBackFacing);
            var thirdBrightness = GetBrightness(triangle.ThirdNormal, faceNormal, rotation, isBackFacing);
            var brightness = (firstBrightness + secondBrightness + thirdBrightness) / 3.0f;
            var color = new Vector4(
                Math.Clamp(triangle.Color.X * brightness, 0.0f, 1.0f),
                Math.Clamp(triangle.Color.Y * brightness, 0.0f, 1.0f),
                Math.Clamp(triangle.Color.Z * brightness, 0.0f, 1.0f),
                1.0f);
            projected.Add(new(
                ToScreen(first, center, scale),
                ToScreen(second, center, scale),
                ToScreen(third, center, scale),
                triangle.FirstUv,
                triangle.SecondUv,
                triangle.ThirdUv,
                (first.Z + second.Z + third.Z) / 3.0f
                    - (triangle.IsBodySkin ? maximumExtent * BodySkinDepthOffsetFactor : 0.0f),
                color,
                Tint(firstBrightness),
                Tint(secondBrightness),
                Tint(thirdBrightness),
                triangle.MaterialPath,
                isBackFacing,
                triangle.IsBodySkin));
        }
        projected.Sort(static (left, right) =>
        {
            var facing = right.IsBackFacing.CompareTo(left.IsBackFacing);
            return facing != 0 ? facing : left.Depth.CompareTo(right.Depth);
        });
        return projected;
    }

    private static Vector2 ToScreen(Vector3 value, Vector2 center, float scale)
        => new(center.X + value.X * scale, center.Y - value.Y * scale);

    private static float GetBrightness(
        Vector3 sourceNormal,
        Vector3 faceNormal,
        Matrix4x4 rotation,
        bool isBackFacing)
    {
        var normal = Vector3.TransformNormal(sourceNormal, rotation);
        if (Vector3.Dot(normal, faceNormal) < 0.0f)
            normal = -normal;
        if (isBackFacing)
            normal = -normal;
        return 0.28f + 0.72f * MathF.Max(0.0f, Vector3.Dot(normal, LightDirection));
    }

    private static Vector4 Tint(float brightness)
        => new(brightness, brightness, brightness, 1.0f);
}
