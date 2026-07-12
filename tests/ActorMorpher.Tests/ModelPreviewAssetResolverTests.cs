using ActorMorpher;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
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
    public void HumanResolvesFaceAndBodyModelsForSoftwarePreview()
    {
        var customize = new byte[26];
        customize[0] = 1;
        customize[1] = 0;
        customize[2] = (byte)NpcAge.Normal;
        customize[4] = 1;
        customize[5] = 1;
        var appearance = new HumanAppearance(customize, new ulong[10], 0, 0, false);
        var modelAppearance = AppearanceData.Create(100, ModelCategory.Human, 100, AppearanceCompleteness.Complete, customize, new ulong[10]);
        var entry = Entry(ModelCategory.Human, 1, 1, 1) with { HumanAppearance = appearance, ModelAppearance = modelAppearance };
        var present = new HashSet<string>
        {
            "chara/human/c0101/obj/face/f0001/model/c0101f0001_fac.mdl",
            "chara/equipment/e0000/model/c0101e0000_top.mdl",
            "chara/equipment/e0000/model/c0101e0000_glv.mdl",
            "chara/equipment/e0000/model/c0101e0000_sho.mdl",
        };

        var report = new ModelPreviewAssetResolver(present.Contains).Resolve(entry);

        Assert.Equal(ModelPreviewReadiness.AssetsComplete, report.Readiness);
        Assert.Equal((ushort)101, report.HumanTargetCode);
        Assert.Contains(report.Assets, asset => asset.Label == "Face" && asset.IsPresent);
        Assert.Contains(report.Assets, asset => asset.Label == "Body" && asset.IsPresent);
        Assert.Contains(report.Assets, asset => asset.Label == "Hands" && asset.IsPresent);
        Assert.Contains(report.Assets, asset => asset.Label == "Feet" && asset.IsPresent);
    }

    [Fact]
    public void YoungHumanUsesYoungFaceAndAdultBodyFallback()
    {
        var customize = new byte[26];
        customize[0] = 1;
        customize[1] = 0;
        customize[2] = (byte)NpcAge.Young;
        customize[4] = 1;
        customize[5] = 1;
        customize[6] = 1;
        var equipment = new ulong[10];
        var appearance = new HumanAppearance(customize, equipment, 0, 0, false);
        var modelAppearance = AppearanceData.Create(100, ModelCategory.Human, 100, AppearanceCompleteness.Complete, customize, equipment);
        var entry = Entry(ModelCategory.Human, 1, 1, 1) with { HumanAppearance = appearance, ModelAppearance = modelAppearance };
        var present = new HashSet<string>
        {
            "chara/human/c0104/obj/face/f0001/model/c0104f0001_fac.mdl",
            "chara/human/c0104/obj/hair/h0001/model/c0104h0001_hir.mdl",
            "chara/equipment/e0000/model/c0101e0000_top.mdl",
        };

        var report = new ModelPreviewAssetResolver(present.Contains).Resolve(entry);

        Assert.Contains(report.Assets, asset => asset.Path == "chara/human/c0104/obj/face/f0001/model/c0104f0001_fac.mdl");
        Assert.Contains(report.Assets, asset => asset.Path == "chara/human/c0104/obj/hair/h0001/model/c0104h0001_hir.mdl");
        Assert.Equal((ushort)104, report.HumanTargetCode);
        Assert.Contains(report.Assets, asset => asset.Path == "chara/equipment/e0000/model/c0101e0000_top.mdl");
    }

    [Fact]
    public void YoungElezenUsesYoungAndSharedAncestorModelsBeforeAdultFemaleModels()
    {
        var customize = new byte[26];
        customize[0] = 2;
        customize[1] = 1;
        customize[2] = (byte)NpcAge.Young;
        customize[4] = 3;
        customize[5] = 201;
        customize[6] = 201;
        var equipment = new ulong[10];
        equipment[(int)OutfitSlot.Body] = 0x000123CE;
        equipment[(int)OutfitSlot.Hands] = 0x000101FF;
        equipment[(int)OutfitSlot.Legs] = 0x0002239A;
        equipment[(int)OutfitSlot.Feet] = 0x0002178C;
        var appearance = new HumanAppearance(customize, equipment, 0, 0, false);
        var modelAppearance = AppearanceData.Create(
            100, ModelCategory.Human, 100, AppearanceCompleteness.Complete, customize, equipment);
        var entry = Entry(ModelCategory.Human, 1, 1, 1) with
        {
            HumanAppearance = appearance,
            ModelAppearance = modelAppearance,
        };
        var present = new HashSet<string>
        {
            "chara/human/c0604/obj/face/f0201/model/c0604f0201_fac.mdl",
            "chara/human/c0604/obj/hair/h0201/model/c0604h0201_hir.mdl",
            "chara/equipment/e9166/model/c0604e9166_top.mdl",
            "chara/equipment/e9166/model/c0201e9166_top.mdl",
            "chara/equipment/e0511/model/c0101e0511_glv.mdl",
            "chara/equipment/e9114/model/c0604e9114_dwn.mdl",
            "chara/equipment/e6028/model/c0101e6028_sho.mdl",
        };

        var report = new ModelPreviewAssetResolver(present.Contains).Resolve(entry);

        Assert.Equal(ModelPreviewReadiness.AssetsComplete, report.Readiness);
        Assert.Equal((ushort)604, report.HumanTargetCode);
        Assert.Contains(report.Assets, asset => asset.Label == "Body"
            && asset.Path == "chara/equipment/e9166/model/c0604e9166_top.mdl");
        Assert.Contains(report.Assets, asset => asset.Label == "Hands"
            && asset.Path == "chara/equipment/e0511/model/c0101e0511_glv.mdl");
        Assert.Contains(report.Assets, asset => asset.Label == "Legs"
            && asset.Path == "chara/equipment/e9114/model/c0604e9114_dwn.mdl");
        Assert.Contains(report.Assets, asset => asset.Label == "Feet"
            && asset.Path == "chara/equipment/e6028/model/c0101e6028_sho.mdl");
        Assert.DoesNotContain(report.Assets, asset => asset.Path == "chara/equipment/e9166/model/c0201e9166_top.mdl");
    }

    [Fact]
    public void YoungAuraNpcNormalizesNpcFaceValueToExistingFaceModelId()
    {
        var customize = new byte[26];
        customize[0] = 6;
        customize[1] = 1;
        customize[2] = (byte)NpcAge.Young;
        customize[4] = 12;
        customize[5] = 102;
        customize[6] = 1;
        var equipment = new ulong[10];
        equipment[(int)OutfitSlot.Body] = 0x00010170;
        var appearance = new HumanAppearance(customize, equipment, 0, 0, false);
        var modelAppearance = AppearanceData.Create(
            0, ModelCategory.Human, 1031878, AppearanceCompleteness.Complete, customize, equipment);
        var entry = Entry(ModelCategory.Human, 1, 0, 1) with
        {
            RowId = 0,
            Source = ModelSource.EventNpc,
            SourceId = 1031878,
            HumanAppearance = appearance,
            ModelAppearance = modelAppearance,
        };
        var present = new HashSet<string>
        {
            "chara/human/c1404/obj/face/f0002/model/c1404f0002_fac.mdl",
            "chara/human/c1404/obj/hair/h0001/model/c1404h0001_hir.mdl",
            "chara/equipment/e0368/model/c0101e0368_top.mdl",
        };

        var report = new ModelPreviewAssetResolver(present.Contains).Resolve(entry);

        Assert.Equal(ModelPreviewReadiness.AssetsComplete, report.Readiness);
        Assert.Equal((ushort)1404, report.HumanTargetCode);
        Assert.Contains(report.Assets, asset => asset.Label == "Face"
            && asset.Path == "chara/human/c1404/obj/face/f0002/model/c1404f0002_fac.mdl"
            && asset.IsPresent);
    }

    [Fact]
    public void MissingEquippedHandsAndFeetFallBackToSharedBareModels()
    {
        var customize = new byte[26];
        customize[0] = 1;
        customize[1] = 1;
        customize[2] = (byte)NpcAge.Normal;
        customize[4] = 2;
        customize[5] = 1;
        var equipment = new ulong[10];
        equipment[(int)OutfitSlot.Hands] = 0x000302D5;
        equipment[(int)OutfitSlot.Feet] = 0x00020323;
        var appearance = new HumanAppearance(customize, equipment, 0, 0, false);
        var modelAppearance = AppearanceData.Create(
            100, ModelCategory.Human, 100, AppearanceCompleteness.Complete, customize, equipment);
        var entry = Entry(ModelCategory.Human, 1, 1, 1) with
        {
            HumanAppearance = appearance,
            ModelAppearance = modelAppearance,
        };
        var present = new HashSet<string>
        {
            "chara/human/c0401/obj/face/f0001/model/c0401f0001_fac.mdl",
            "chara/equipment/e0000/model/c0201e0000_top.mdl",
            "chara/equipment/e0000/model/c0201e0000_glv.mdl",
            "chara/equipment/e0000/model/c0201e0000_dwn.mdl",
            "chara/equipment/e0000/model/c0201e0000_sho.mdl",
        };

        var report = new ModelPreviewAssetResolver(present.Contains).Resolve(entry);

        Assert.Equal((ushort)401, report.HumanTargetCode);
        Assert.Contains(report.Assets, asset => asset.Label == "Hands"
            && asset.Path == "chara/equipment/e0000/model/c0201e0000_glv.mdl"
            && asset.IsPresent);
        Assert.Contains(report.Assets, asset => asset.Label == "Feet"
            && asset.Path == "chara/equipment/e0000/model/c0201e0000_sho.mdl"
            && asset.IsPresent);
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
