namespace ActorMorpher.Preview;

public enum ModelPreviewReadiness
{
    HumanDataReady,
    AssetsComplete,
    AssetsPartial,
    AssetsMissing,
    InvalidModelData,
}

public enum ModelPreviewAssetKind
{
    HumanAppearance,
    Imc,
    Model,
    Skeleton,
}

public sealed record ModelPreviewAsset(
    ModelPreviewAssetKind Kind,
    string Label,
    string? Path,
    bool IsPresent);

public sealed record ModelPreviewAssetReport(
    uint ModelCharaId,
    ModelCategory Category,
    ModelPreviewReadiness Readiness,
    IReadOnlyList<ModelPreviewAsset> Assets)
{
    public int PresentCount => Assets.Count(static asset => asset.IsPresent);
    public int MissingCount => Assets.Count(static asset => !asset.IsPresent);
}
