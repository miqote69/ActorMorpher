namespace ActorMorpher.Preview;

public interface IModelPreviewBackend : IDisposable
{
    ModelPreviewSnapshot Snapshot { get; }
    void Select(ModelSearchEntry? model);
    void ResetCamera();
}
