using Xunit;

namespace ActorMorpher.Tests;

public sealed class HumanTribeCatalogTests
{
    [Theory]
    [InlineData(1, 1, 2)]
    [InlineData(2, 3, 4)]
    [InlineData(7, 13, 14)]
    [InlineData(8, 15, 16)]
    public void ReturnsTwoTribesForEachRace(uint race, uint first, uint second)
        => Assert.Equal(new[] { first, second }, HumanTribeCatalog.GetTribes(race));

    [Fact]
    public void AnyRaceReturnsAllSixteenTribes()
    {
        var tribes = HumanTribeCatalog.GetTribes(0);

        Assert.Equal(16, tribes.Count);
        Assert.Equal((uint)1, tribes[0]);
        Assert.Equal((uint)16, tribes[^1]);
    }

    [Fact]
    public void RejectsTribeFromAnotherRace()
    {
        Assert.True(HumanTribeCatalog.IsValidForRace(1, 2));
        Assert.False(HumanTribeCatalog.IsValidForRace(1, 3));
    }
}
