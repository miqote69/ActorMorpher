using System.Numerics;
using ActorMorpher.Appearance;
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
}
