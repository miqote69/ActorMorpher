using ActorMorpher.Appearance;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class AppearanceDataTests
{
    [Fact]
    public void CreatePreservesFinitePositiveModelScale()
    {
        var appearance = AppearanceData.Create(
            2720,
            ModelCategory.Human,
            10072,
            AppearanceCompleteness.Complete,
            new byte[26],
            new ulong[10],
            0.84f);

        Assert.Equal(0.84f, appearance.ModelScale);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void CreateRejectsInvalidModelScale(float value)
    {
        var appearance = AppearanceData.Create(
            2720,
            ModelCategory.Human,
            10072,
            AppearanceCompleteness.Complete,
            new byte[26],
            new ulong[10],
            value);

        Assert.Null(appearance.ModelScale);
    }
}
