namespace ActorMorpher.Preview;

internal readonly record struct ModelPreviewSelectionKey(
    uint RowId,
    ModelCategory Category,
    ModelSource Source,
    uint SourceId)
{
    public static ModelPreviewSelectionKey? From(ModelSearchEntry? model)
        => model is null
            ? null
            : new(model.RowId, model.Category, model.Source, model.SourceId);
}
