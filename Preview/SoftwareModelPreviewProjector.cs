using System.Numerics;

namespace ActorMorpher.Preview;

public static class SoftwareModelPreviewProjector
{
    private static readonly Vector3 LightDirection = Vector3.Normalize(new Vector3(-0.35f, 0.55f, -0.75f));

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
            var normal = Vector3.TransformNormal(triangle.Normal, rotation);
            var brightness = 0.28f + 0.72f * MathF.Abs(Vector3.Dot(normal, LightDirection));
            var color = new Vector4(
                Math.Clamp(triangle.Color.X * brightness, 0.0f, 1.0f),
                Math.Clamp(triangle.Color.Y * brightness, 0.0f, 1.0f),
                Math.Clamp(triangle.Color.Z * brightness, 0.0f, 1.0f),
                1.0f);
            projected.Add(new(
                ToScreen(first, center, scale),
                ToScreen(second, center, scale),
                ToScreen(third, center, scale),
                (first.Z + second.Z + third.Z) / 3.0f,
                color));
        }
        projected.Sort(static (left, right) => left.Depth.CompareTo(right.Depth));
        return projected;
    }

    private static Vector2 ToScreen(Vector3 value, Vector2 center, float scale)
        => new(center.X + value.X * scale, center.Y - value.Y * scale);
}
