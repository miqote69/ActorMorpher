using ActorMorpher;
using ActorMorpher.Appearance;
using ActorMorpher.Preview;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class ModelPreviewAssetResolverTests
{
    [Fact]
    public void ResolvesMonsterPathsAndRequiresModelAndSkeletonForCompleteReadiness()
    {
        var present = new HashSet<string>
        {
            "chara/monster/m0123/obj/body/b0045/model/m0123b0045.mdl",
            "chara/monster/m0123/skeleton/base/b0001/skl_m0123b0001.sklb",
        };
        var report = new ModelPreviewAssetResolver(present.Contains).Resolve(Entry(ModelCategory.Monster, 3, 123, 45));

        Assert.Equal(ModelPreviewReadiness.AssetsComplete, report.Readiness);
        Assert.Equal(2, report.PresentCount);
        Assert.Contains(report.Assets, asset => asset.Path == "chara/monster/m0123/obj/body/b0045/b0045.imc");
    }

    [Fact]
    public void DemihumanWithOnlySomePartsIsPartial()
    {
        var present = new HashSet<string>
        {
            "chara/demihuman/d0006/obj/equipment/e0002/model/d0006e0002_top.mdl",
        };
        var report = new ModelPreviewAssetResolver(present.Contains).Resolve(Entry(ModelCategory.Demihuman, 2, 6, 2));

        Assert.Equal(ModelPreviewReadiness.AssetsPartial, report.Readiness);
        Assert.Equal(7, report.Assets.Count);
    }

    [Fact]
    public void DemihumanWithOnePartAndSkeletonIsReadyAndOtherPartsAreOptional()
    {
        var present = new HashSet<string>
        {
            "chara/demihuman/d0006/obj/equipment/e0002/model/d0006e0002_top.mdl",
            "chara/demihuman/d0006/skeleton/base/b0001/skl_d0006b0001.sklb",
        };
        var report = new ModelPreviewAssetResolver(present.Contains).Resolve(Entry(ModelCategory.Demihuman, 2, 6, 2));

        Assert.Equal(ModelPreviewReadiness.AssetsComplete, report.Readiness);
        Assert.Equal(0, report.MissingCount);
        Assert.Equal(5, report.OptionalMissingCount);
    }

    [Fact]
    public void RejectsIdsThatCannotUseVerifiedFourDigitPaths()
    {
        var report = new ModelPreviewAssetResolver(_ => true).Resolve(Entry(ModelCategory.Monster, 3, 10000, 1));

        Assert.Equal(ModelPreviewReadiness.InvalidModelData, report.Readiness);
        Assert.Empty(report.Assets);
    }

    [Fact]
    public void HumanReportsPreparedInMemoryAppearanceData()
    {
        var customize = new byte[26];
        customize[0] = 1;
        customize[1] = 0;
        customize[2] = (byte)NpcAge.Normal;
        var appearance = new HumanAppearance(customize, new ulong[10], 0, 0, false);
        var modelAppearance = AppearanceData.Create(100, ModelCategory.Human, 100, AppearanceCompleteness.Complete, customize, new ulong[10]);
        var entry = Entry(ModelCategory.Human, 1, 1, 1) with { HumanAppearance = appearance, ModelAppearance = modelAppearance };

        var report = new ModelPreviewAssetResolver(_ => false).Resolve(entry);

        Assert.Equal(ModelPreviewReadiness.HumanDataReady, report.Readiness);
        Assert.Equal(1, report.PresentCount);
    }

    [Fact]
    public void FileLookupFailureIsContainedToTheAffectedAssets()
    {
        var report = new ModelPreviewAssetResolver(_ => throw new IOException("Unavailable"))
            .Resolve(Entry(ModelCategory.Monster, 3, 123, 45));

        Assert.Equal(ModelPreviewReadiness.AssetsMissing, report.Readiness);
        Assert.Equal(2, report.MissingCount);
        Assert.Equal(1, report.OptionalMissingCount);
    }

    private static ModelSearchEntry Entry(ModelCategory category, byte type, ushort model, ushort @base)
        => new(100, category, ModelSource.ModelChara, 100, "Model", type, model, @base, 1, 0, 0, 0, null,
            AppearanceCompleteness.Unsupported, null);
}
