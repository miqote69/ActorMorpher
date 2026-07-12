namespace ActorMorpher.Preview;

public static class ModelPreviewCameraFraming
{
    public static bool TryCalculate(
        ModelPreviewBounds bounds,
        float aspectRatio,
        out ModelPreviewCameraFrame frame,
        float verticalFieldOfViewDegrees = 35.0f,
        float padding = 1.15f)
    {
        frame = default;
        if (!bounds.IsValid
            || !float.IsFinite(aspectRatio)
            || aspectRatio <= 0
            || verticalFieldOfViewDegrees is <= 1 or >= 179
            || !float.IsFinite(padding)
            || padding < 1)
            return false;

        var half = bounds.Size / 2.0f;
        var tangent = MathF.Tan(verticalFieldOfViewDegrees * MathF.PI / 360.0f);
        var verticalDistance = half.Y / tangent;
        var horizontalDistance = half.X / (tangent * aspectRatio);
        var distance = (MathF.Max(verticalDistance, horizontalDistance) + half.Z) * padding;
        var radius = bounds.Size.Length() / 2.0f;
        var nearPlane = MathF.Max(0.01f, distance - radius * 1.25f);
        var farPlane = MathF.Max(nearPlane + 1.0f, distance + radius * 2.0f);
        if (!float.IsFinite(distance) || distance <= 0 || !float.IsFinite(farPlane))
            return false;

        frame = new ModelPreviewCameraFrame(bounds.Center, distance, nearPlane, farPlane);
        return true;
    }
}
