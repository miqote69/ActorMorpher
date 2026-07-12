using ActorMorpher.Appearance;
using ActorMorpher.Preview;
using System;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class ModelPreviewSupportResolverTests
{
    private readonly HumanPreviewDataBuilder humanBuilder = new();

    [Fact]
    public void ValidHumanIsBlockedByMissingSafeCharaViewOwnership()
    {
        var model = HumanEntry();
        var assets = new ModelPreviewAssetResolver(_ => false, humanBuilder).Resolve(model);

        var support = new ModelPreviewSupportResolver(humanBuilder).Resolve(model, assets);

        Assert.False(support.CanPreview);
        Assert.Equal(ModelPreviewBackendKind.CharaView, support.PreferredBackend);
        Assert.Equal(PreviewCompleteness.StaticReady, support.Completeness);
        Assert.Equal(ModelPreviewSupportReason.CharaViewSlotAndTextureOwnershipUnavailable, support.Reason);
    }

    [Fact]
    public void HumanCanPreviewOnlyWhenBothCharaViewOwnershipContractsExist()
    {
        var model = HumanEntry();
        var assets = new ModelPreviewAssetResolver(_ => false, humanBuilder).Resolve(model);
        var capabilities = new ModelPreviewBackendCapabilities(true, true, false);

        var support = new ModelPreviewSupportResolver(humanBuilder, capabilities).Resolve(model, assets);

        Assert.True(support.CanPreview);
        Assert.Equal(ModelPreviewSupportReason.None, support.Reason);
    }

    [Fact]
    public void CompleteMonsterAssetsReportRendererAsOnlyBlocker()
    {
        var model = AssetEntry(ModelCategory.Monster);
        var assets = new ModelPreviewAssetReport(
            model.ModelId,
            model.Category,
            ModelPreviewReadiness.AssetsComplete,
            [
                new(ModelPreviewAssetKind.Model, "Body", "model.mdl", true),
                new(ModelPreviewAssetKind.Skeleton, "Skeleton", "skeleton.sklb", true),
            ]);

        var support = new ModelPreviewSupportResolver(humanBuilder).Resolve(model, assets);

        Assert.False(support.CanPreview);
        Assert.Equal(PreviewCompleteness.StaticReady, support.Completeness);
        Assert.Equal(ModelPreviewSupportReason.AssetRendererUnavailable, support.Reason);
    }

    [Theory]
    [InlineData(false, false, ModelPreviewSupportReason.MissingModelAndSkeleton)]
    [InlineData(false, true, ModelPreviewSupportReason.MissingModel)]
    [InlineData(true, false, ModelPreviewSupportReason.MissingSkeleton)]
    public void ReportsMissingStaticAssetsPrecisely(bool hasModel, bool hasSkeleton, ModelPreviewSupportReason expected)
    {
        var model = AssetEntry(ModelCategory.Demihuman);
        var assets = new ModelPreviewAssetReport(
            model.ModelId,
            model.Category,
            ModelPreviewReadiness.AssetsMissing,
            [
                new(ModelPreviewAssetKind.Model, "Body", "model.mdl", hasModel),
                new(ModelPreviewAssetKind.Skeleton, "Skeleton", "skeleton.sklb", hasSkeleton),
            ]);

        var support = new ModelPreviewSupportResolver(humanBuilder).Resolve(model, assets);

        Assert.False(support.CanPreview);
        Assert.Equal(expected, support.Reason);
    }

    private static ModelSearchEntry HumanEntry()
    {
        var customize = new byte[26];
        customize[0] = 1;
        customize[1] = 0;
        customize[2] = (byte)NpcAge.Normal;
        customize[4] = 1;
        var equipment = new ulong[10];
        var human = new HumanAppearance(customize, equipment, 0, 0, false);
        return new ModelSearchEntry(
            100,
            ModelCategory.Human,
            ModelSource.EventNpc,
            10,
            "Human",
            1,
            1,
            1,
            1,
            1,
            0,
            (byte)NpcAge.Normal,
            human,
            AppearanceCompleteness.Complete,
            AppearanceData.Create(100, ModelCategory.Human, 10, AppearanceCompleteness.Complete, customize, equipment));
    }

    private static ModelSearchEntry AssetEntry(ModelCategory category)
        => new(
            100,
            category,
            ModelSource.ModelChara,
            100,
            category.ToString(),
            category == ModelCategory.Demihuman ? (byte)2 : (byte)3,
            1,
            1,
            1,
            0,
            0,
            0,
            null,
            AppearanceCompleteness.ModelOnly,
            null);
}
