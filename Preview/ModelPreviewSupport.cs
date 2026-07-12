namespace ActorMorpher.Preview;

public enum ModelPreviewBackendKind
{
    None,
    CharaView,
    AssetRenderer,
}

public enum PreviewCompleteness
{
    Unknown,
    MetadataOnly,
    StaticReady,
    AnimatedReady,
    MissingModel,
    MissingSkeleton,
    MissingModelAndSkeleton,
    InvalidHumanData,
}

public enum ModelPreviewSupportReason
{
    None,
    InvalidHumanData,
    MissingModel,
    MissingSkeleton,
    MissingModelAndSkeleton,
    CharaViewSlotOwnershipUnavailable,
    CharaViewTextureOwnershipUnavailable,
    CharaViewSlotAndTextureOwnershipUnavailable,
    AssetRendererUnavailable,
}

public sealed record ModelPreviewSupport(
    bool CanPreview,
    bool CanAnimate,
    ModelPreviewBackendKind PreferredBackend,
    PreviewCompleteness Completeness,
    ModelPreviewSupportReason Reason);

public sealed record ModelPreviewBackendCapabilities(
    bool HasExclusiveCharaViewSlot,
    bool CanOwnCharaViewTexture,
    bool HasAssetRenderer)
{
    public static ModelPreviewBackendCapabilities SafeDalamud15 { get; } = new(false, false, false);
    public static ModelPreviewBackendCapabilities SoftwarePreview { get; } = new(false, false, true);
}
