using System.Numerics;
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
}
