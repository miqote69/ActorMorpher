namespace ActorMorpher.Preview;

public sealed class SoftwareModelPreviewBackend : IModelPreviewBackend
{
    private readonly Func<ModelSearchEntry, ModelPreviewAssetReport> resolveAssets;
    private readonly Func<string, byte, ushort, byte, ModelPreviewCpuModel?> loadModel;
    private readonly SoftwareModelPreviewSceneBuilder sceneBuilder;
    private readonly object syncRoot = new();
    private ModelPreviewSelectionKey? selection;
    private SoftwareModelPreviewScene? scene;
    private float yaw = 0.55f;
    private float pitch = -0.12f;
    private float zoom = 1.0f;
    private long generation;

    public SoftwareModelPreviewBackend(
        Func<ModelSearchEntry, ModelPreviewAssetReport> resolveAssets,
        Func<string, byte, ushort, byte, ModelPreviewCpuModel?> loadModel,
        Func<string, bool>? showBackfaces = null,
        Func<string, bool>? isBodySkin = null)
    {
        this.resolveAssets = resolveAssets;
        this.loadModel = loadModel;
        sceneBuilder = new SoftwareModelPreviewSceneBuilder(showBackfaces, isBodySkin);
    }

    public ModelPreviewSnapshot Snapshot { get; private set; }
        = new(0, ModelPreviewState.Idle, null, "Select a model to inspect its preview status.");

    public void Select(ModelSearchEntry? model)
    {
        var next = ModelPreviewSelectionKey.From(model);
        if (selection == next)
            return;
        selection = next;
        generation++;
        lock (syncRoot)
            scene = null;
        if (model is null)
        {
            Snapshot = new(generation, ModelPreviewState.Idle, null, "Select a model to inspect its preview status.");
            return;
        }
        var assets = resolveAssets(model);
        var models = new List<ModelPreviewCpuModel>();
        var requiredModelFailed = false;
        var facialFeatures = model.Category == ModelCategory.Human
            && model.HumanAppearance is { Customize.Length: > 12 }
                ? model.HumanAppearance.Customize[12]
                : (byte)0;
        foreach (var asset in assets.Assets.Where(static asset =>
                     asset.Kind == ModelPreviewAssetKind.Model && asset.IsPresent && asset.Path is not null))
        {
            try
            {
                if (loadModel(asset.Path!, asset.MaterialVariant, assets.HumanTargetCode, facialFeatures) is { } loaded)
                    models.Add(loaded);
            }
            catch
            {
                requiredModelFailed |= asset.IsRequired;
                // A malformed optional part must not suppress other valid model parts.
            }
        }
        if (models.Count == 0 || requiredModelFailed)
        {
            Snapshot = new(generation, ModelPreviewState.Failed, model.ModelId,
                "No renderable static model geometry is available.");
            return;
        }

        var built = sceneBuilder.Build(models);
        lock (syncRoot)
        {
            scene = built;
            ResetCameraLocked();
        }
        Snapshot = new(generation, ModelPreviewState.Ready, model.ModelId, "Static 3D preview ready.");
    }

    public SoftwareModelPreviewView? GetView()
    {
        lock (syncRoot)
            return scene is null ? null : new SoftwareModelPreviewView(scene, yaw, pitch, zoom);
    }

    public void Orbit(float deltaX, float deltaY)
    {
        if (!float.IsFinite(deltaX) || !float.IsFinite(deltaY))
            return;
        lock (syncRoot)
        {
            yaw = MathF.IEEERemainder(yaw + deltaX * 0.012f, MathF.Tau);
            pitch = Math.Clamp(pitch + deltaY * 0.012f, -1.35f, 1.35f);
        }
    }

    public void AdjustZoom(float wheelDelta)
    {
        if (!float.IsFinite(wheelDelta))
            return;
        lock (syncRoot)
            zoom = Math.Clamp(zoom * MathF.Exp(wheelDelta * 0.12f), 0.35f, 3.0f);
    }

    public void ResetCamera()
    {
        lock (syncRoot)
            ResetCameraLocked();
    }

    public void Dispose()
    {
        lock (syncRoot)
            scene = null;
    }

    private void ResetCameraLocked()
    {
        yaw = 0.55f;
        pitch = -0.12f;
        zoom = 1.0f;
    }
}
