namespace ActorMorpher.Preview;

public sealed class ModelPreviewSupportResolver
{
    private readonly HumanPreviewDataBuilder humanBuilder;
    private readonly ModelPreviewBackendCapabilities capabilities;

    public ModelPreviewSupportResolver(
        HumanPreviewDataBuilder humanBuilder,
        ModelPreviewBackendCapabilities? capabilities = null)
    {
        this.humanBuilder = humanBuilder;
        this.capabilities = capabilities ?? ModelPreviewBackendCapabilities.SafeDalamud15;
    }

    public ModelPreviewSupport Resolve(ModelSearchEntry model, ModelPreviewAssetReport assets)
        => model.Category == ModelCategory.Human
            ? ResolveHuman(model, assets)
            : ResolveAssetModel(assets);

    private ModelPreviewSupport ResolveHuman(ModelSearchEntry model, ModelPreviewAssetReport assets)
    {
        if (!humanBuilder.TryBuild(model, out _, out _))
            return Unsupported(
                ModelPreviewBackendKind.AssetRenderer,
                PreviewCompleteness.InvalidHumanData,
                ModelPreviewSupportReason.InvalidHumanData);

        if (capabilities.HasAssetRenderer)
            return ResolveAssetModel(assets);

        if (!capabilities.HasExclusiveCharaViewSlot && !capabilities.CanOwnCharaViewTexture)
            return Unsupported(
                ModelPreviewBackendKind.CharaView,
                PreviewCompleteness.StaticReady,
                ModelPreviewSupportReason.CharaViewSlotAndTextureOwnershipUnavailable);
        if (!capabilities.HasExclusiveCharaViewSlot)
            return Unsupported(
                ModelPreviewBackendKind.CharaView,
                PreviewCompleteness.StaticReady,
                ModelPreviewSupportReason.CharaViewSlotOwnershipUnavailable);
        if (!capabilities.CanOwnCharaViewTexture)
            return Unsupported(
                ModelPreviewBackendKind.CharaView,
                PreviewCompleteness.StaticReady,
                ModelPreviewSupportReason.CharaViewTextureOwnershipUnavailable);

        return new ModelPreviewSupport(
            true,
            false,
            ModelPreviewBackendKind.CharaView,
            PreviewCompleteness.StaticReady,
            ModelPreviewSupportReason.None);
    }

    private ModelPreviewSupport ResolveAssetModel(ModelPreviewAssetReport assets)
    {
        var hasModel = assets.Assets.Any(static asset => asset.Kind == ModelPreviewAssetKind.Model && asset.IsPresent);
        var hasSkeleton = assets.Assets.Any(static asset => asset.Kind == ModelPreviewAssetKind.Skeleton && asset.IsPresent);
        if (!hasModel && !hasSkeleton)
            return Unsupported(
                ModelPreviewBackendKind.AssetRenderer,
                PreviewCompleteness.MissingModelAndSkeleton,
                ModelPreviewSupportReason.MissingModelAndSkeleton);
        if (!hasModel)
            return Unsupported(
                ModelPreviewBackendKind.AssetRenderer,
                PreviewCompleteness.MissingModel,
                ModelPreviewSupportReason.MissingModel);
        if (capabilities.HasAssetRenderer)
            return new ModelPreviewSupport(
                true,
                false,
                ModelPreviewBackendKind.AssetRenderer,
                PreviewCompleteness.StaticReady,
                ModelPreviewSupportReason.None);
        if (!hasSkeleton)
            return Unsupported(
                ModelPreviewBackendKind.AssetRenderer,
                PreviewCompleteness.MissingSkeleton,
                ModelPreviewSupportReason.MissingSkeleton);
        if (!capabilities.HasAssetRenderer)
            return Unsupported(
                ModelPreviewBackendKind.AssetRenderer,
                PreviewCompleteness.StaticReady,
                ModelPreviewSupportReason.AssetRendererUnavailable);

        return new ModelPreviewSupport(
            true,
            false,
            ModelPreviewBackendKind.AssetRenderer,
            PreviewCompleteness.StaticReady,
            ModelPreviewSupportReason.None);
    }

    private static ModelPreviewSupport Unsupported(
        ModelPreviewBackendKind backend,
        PreviewCompleteness completeness,
        ModelPreviewSupportReason reason)
        => new(false, false, backend, completeness, reason);
}
