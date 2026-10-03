using System.Numerics;

namespace ActorMorpher;

// Display-only resolution and projection. Appearance actions keep their existing resolver.
internal static class ActorMarkerProjection
{
    internal delegate bool ProjectRepresentation(ActorSnapshot representation, out Vector2 screen);

    internal static bool TryGetScreenPosition(ActorEntry actor, bool isGPosing,
        ProjectRepresentation project, out Vector2 screen, ActorSnapshot? directGPose = null)
    {
        foreach (var representation in actor.Representations)
            if (representation.RepresentationKey.IsGPoseRepresentation == isGPosing
                && project(representation, out screen))
                return true;
        if (isGPosing && directGPose is not null && project(directGPose, out screen))
            return true;
        screen = default;
        return false;
    }

    internal static bool TryProject(Vector3 world, Matrix4x4 viewProjection,
        Vector2 viewportOrigin, Vector2 viewportSize, out Vector2 screen)
    {
        screen = default;
        var clip = Vector4.Transform(new Vector4(world, 1), viewProjection);
        if (!(clip.W > 0))
            return false;
        var x = clip.X / clip.W;
        var y = clip.Y / clip.W;
        if (!(x > -1 && x < 1 && y > -1 && y < 1))
            return false;
        screen = viewportOrigin + new Vector2((x + 1) * viewportSize.X * 0.5f,
            (1 - y) * viewportSize.Y * 0.5f);
        return true;
    }

    internal static bool TryProjectModelTop(Vector3 min, Vector3 max, Matrix4x4 viewProjection,
        Vector2 viewportOrigin, Vector2 viewportSize, out Vector2 screen)
    {
        var center = (min + max) * 0.5f;
        if (!TryProject(new Vector3(center.X, max.Y, center.Z), viewProjection,
            viewportOrigin, viewportSize, out screen))
            return false;

        // Perspective can project a corner of the model above its world-space upper center.
        var upperY = screen.Y;
        for (var corner = 0; corner < 8; corner++)
        {
            var world = new Vector3((corner & 1) == 0 ? min.X : max.X,
                (corner & 2) == 0 ? min.Y : max.Y, (corner & 4) == 0 ? min.Z : max.Z);
            var clip = Vector4.Transform(new Vector4(world, 1), viewProjection);
            if (clip.W > 0)
                upperY = MathF.Min(upperY,
                    viewportOrigin.Y + (1 - clip.Y / clip.W) * viewportSize.Y * 0.5f);
        }
        if (!(upperY > viewportOrigin.Y && upperY < viewportOrigin.Y + viewportSize.Y))
        {
            screen = default;
            return false;
        }
        screen.Y = upperY;
        return true;
    }
}
