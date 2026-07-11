namespace ActorMorpher.Preview;

public sealed class UnavailableModelPreviewBackend : IModelPreviewBackend
{
    private long generation;

    public ModelPreviewSnapshot Snapshot { get; private set; }
        = new(0, ModelPreviewState.Idle, null, "Select a model to inspect its preview status.");

    public void Select(ModelSearchEntry? model)
    {
        if (Snapshot.ModelId == model?.ModelId)
            return;
        generation++;
        Snapshot = model is null
            ? new(generation, ModelPreviewState.Idle, null, "Select a model to inspect its preview status.")
            : new(generation, ModelPreviewState.Unsupported, model.ModelId,
                "3D preview is unavailable: this Dalamud build does not expose verified CharaView slot ownership and texture lifetime APIs.");
    }

    public void ResetCamera()
    {
    }

    public void Dispose()
    {
    }
}
