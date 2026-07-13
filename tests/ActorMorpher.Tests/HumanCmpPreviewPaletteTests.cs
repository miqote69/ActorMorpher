using System.Numerics;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using ActorMorpher.Preview;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class HumanCmpPreviewPaletteTests
{
    [Fact]
    public void ReadsZeroBasedGenderTribeHairAndHighlightColors()
    {
        var data = new byte[18432 + 32 * 5120];
        var mainOffset = 18432 + ((3 - 1) * 2 + 1) * 5120 + 1024 + 10 * 8;
        data[mainOffset] = 12;
        data[mainOffset + 1] = 34;
        data[mainOffset + 2] = 56;
        data[mainOffset + 3] = 255;
        var highlightOffset = 1024 + 11 * 4;
        data[highlightOffset] = 78;
        data[highlightOffset + 1] = 90;
        data[highlightOffset + 2] = 123;
        data[highlightOffset + 3] = 255;

        var result = HumanCmpPreviewPalette.TryGetHairColors(
            data, 3, 1, 10, 11, out var main, out var highlight);

        Assert.True(result);
        Assert.Equal(new Vector4(12, 34, 56, 255) / 255.0f, main);
        Assert.Equal(new Vector4(78, 90, 123, 255) / 255.0f, highlight);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(17, 0)]
    [InlineData(1, 2)]
    public void RejectsInvalidGenderTribeIndex(byte tribe, byte sex)
    {
        Assert.False(HumanCmpPreviewPalette.TryGetHairColors(
            new byte[200000], tribe, sex, 0, 0, out _, out _));
    }

    [Fact]
    public void DemihumanPreviewUsesNpcCustomizeHairColors()
    {
        var data = new byte[18432 + 32 * 5120];
        var hairOffset = 18432 + 5120 + 1024 + 6 * 8;
        data[hairOffset] = 5;
        data[hairOffset + 1] = 10;
        data[hairOffset + 2] = 30;
        data[hairOffset + 3] = 255;
        var highlightOffset = 1024;
        data[highlightOffset] = 20;
        data[highlightOffset + 1] = 30;
        data[highlightOffset + 2] = 50;
        data[highlightOffset + 3] = 255;
        var customize = new byte[26];
        customize[1] = 1;
        customize[4] = 1;
        customize[7] = 1;
        customize[10] = 6;
        var appearance = AppearanceData.Create(
            2853,
            ModelCategory.Demihuman,
            11558,
            AppearanceCompleteness.Complete,
            customize,
            new ulong[10]);
        var model = new ModelSearchEntry(
            2853, ModelCategory.Demihuman, ModelSource.BattleNpc, 11558, "Gaia", 2, 1041, 1, 1,
            1, 1, 1, null, AppearanceCompleteness.Complete, appearance);

        var context = ModelPreviewTextureContext.FromModel(model, data);

        Assert.Equal(new Vector4(5, 10, 30, 255) / 255.0f, context.HairColor);
        Assert.Equal(new Vector4(20, 30, 50, 255) / 255.0f, context.HairHighlightColor);
        Assert.True(context.UseMaterialHairColor);
    }

    [Fact]
    public void ReadsAuraFemaleSkinColorForSkinShaderComposition()
    {
        var data = new byte[18432 + 32 * 5120];
        var skinOffset = 18432 + 23 * 5120 + 124 * 4;
        data[skinOffset] = 142;
        data[skinOffset + 1] = 165;
        data[skinOffset + 2] = 179;
        data[skinOffset + 3] = 255;

        var result = HumanCmpPreviewPalette.TryGetSkinColor(data, 12, 1, 124, out var skin);

        Assert.True(result);
        Assert.Equal(new Vector4(142, 165, 179, 255) / 255.0f, skin);

        var customize = new byte[26];
        customize[0] = 6;
        customize[1] = 1;
        customize[4] = 12;
        customize[8] = 124;
        var human = new HumanAppearance(customize, new ulong[10], 0, 0, false);
        var appearance = AppearanceData.Create(
            0, ModelCategory.Human, 1031878, AppearanceCompleteness.Complete, customize, new ulong[10]);
        var model = new ModelSearchEntry(
            0, ModelCategory.Human, ModelSource.EventNpc, 1031878, "Aura Young", 1, 0, 1, 1,
            6, 1, (byte)NpcAge.Young, human, AppearanceCompleteness.Complete, appearance);

        var context = ModelPreviewTextureContext.FromModel(model, data);

        Assert.Equal(skin, context.SkinColor);
        Assert.False(context.UseMaterialHairColor);
    }

    [Fact]
    public void HumanTextureContextMapsPackedBodyStainsToBodyMaterials()
    {
        var customize = new byte[26];
        customize[0] = 1;
        customize[1] = 1;
        customize[4] = 1;
        var equipment = new ulong[10];
        equipment[(int)OutfitSlot.Body] = 428UL | (2UL << 16) | (51UL << 24);
        var human = new HumanAppearance(customize, equipment, 0, 0, false);
        var appearance = AppearanceData.Create(
            0, ModelCategory.Human, 1037096, AppearanceCompleteness.Complete, customize, equipment);
        var model = new ModelSearchEntry(
            0, ModelCategory.Human, ModelSource.EventNpc, 1037096, "Alphinaud", 1, 0, 1, 1,
            1, 1, (byte)NpcAge.Normal, human, AppearanceCompleteness.Complete, appearance);

        var context = ModelPreviewTextureContext.FromModel(model, new byte[18432 + 32 * 5120]);

        Assert.Equal(
            new ModelPreviewStains(51, 0),
            context.StainsForMaterial("chara/equipment/e0428/material/v0002/mt_c0201e0428_top_a.mtrl"));
        Assert.Equal(
            default(ModelPreviewStains),
            context.StainsForMaterial("chara/equipment/e0559/material/v0001/mt_c0101e0559_sho_a.mtrl"));
    }

    [Fact]
    public void ReadsEyeFacialFeatureAndFacePaintColorsFromSharedCmpPalettes()
    {
        var data = new byte[18432 + 32 * 5120];
        WriteRgba(data, 130 * 4, 10, 20, 30, 255);
        WriteRgba(data, 3072 + 42 * 4, 40, 50, 60, 255);
        WriteRgba(data, 4608 + 7 * 4, 70, 80, 90, 255);

        Assert.True(HumanCmpPreviewPalette.TryGetEyeColor(data, 130, out var eye));
        Assert.True(HumanCmpPreviewPalette.TryGetFacialFeatureColor(data, 42, out var feature));
        Assert.True(HumanCmpPreviewPalette.TryGetFacePaintColor(data, 0x87, out var facePaint));
        Assert.Equal(new Vector4(10, 20, 30, 255) / 255.0f, eye);
        Assert.Equal(new Vector4(40, 50, 60, 255) / 255.0f, feature);
        Assert.Equal(new Vector4(70, 80, 90, 255) / 255.0f, facePaint);

        var customize = new byte[26];
        customize[0] = 1;
        customize[1] = 1;
        customize[4] = 1;
        customize[9] = 130;
        customize[13] = 42;
        customize[15] = 130;
        customize[24] = 5;
        customize[25] = 0x87;
        var equipment = new ulong[10];
        var human = new HumanAppearance(customize, equipment, 0, 0, false);
        var appearance = AppearanceData.Create(
            0, ModelCategory.Human, 1, AppearanceCompleteness.Complete, customize, equipment);
        var model = new ModelSearchEntry(
            0, ModelCategory.Human, ModelSource.EventNpc, 1, "Face", 1, 0, 1, 1,
            1, 1, (byte)NpcAge.Normal, human, AppearanceCompleteness.Complete, appearance);

        var context = ModelPreviewTextureContext.FromModel(model, data);

        Assert.Equal(eye, context.EyeColor);
        Assert.Equal(eye, context.HeterochromiaColor);
        Assert.Equal(feature, context.FacialFeatureColor);
        Assert.Equal(facePaint, context.FacePaintColor);
        Assert.Equal((byte)5, context.FacePaint);
    }

    private static void WriteRgba(byte[] data, int offset, byte red, byte green, byte blue, byte alpha)
    {
        data[offset] = red;
        data[offset + 1] = green;
        data[offset + 2] = blue;
        data[offset + 3] = alpha;
    }
}
