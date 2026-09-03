using ActorMorpher.Appearance;
using System.Linq;
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

    [Fact]
    public void HumanPayloadAndOutfitReplacementPreserveNonOutfitValues()
    {
        var customize = Enumerable.Range(1, 26).Select(static value => (byte)value).ToArray();
        var sourceEquipment = Enumerable.Range(1, 10).Select(static value => (ulong)value).ToArray();
        var replacementEquipment = Enumerable.Range(101, 10).Select(static value => (ulong)value).ToArray();
        var appearance = AppearanceData.Create(
            2720,
            ModelCategory.Human,
            1033894,
            AppearanceCompleteness.Complete,
            customize,
            sourceEquipment,
            0.84f,
            0x0102030405060708,
            0x1112131415161718,
            true,
            27,
            false);

        var replaced = appearance.WithOutfit(replacementEquipment, false);

        Assert.Equal(2720u, replaced.ModelCharaId);
        Assert.Equal(1033894u, replaced.SourceRowId);
        Assert.Equal(customize, replaced.Customize);
        Assert.Equal(replacementEquipment, replaced.Equipment);
        Assert.Equal(0.84f, replaced.ModelScale);
        Assert.Equal(0x0102030405060708UL, replaced.Mainhand);
        Assert.Equal(0x1112131415161718UL, replaced.Offhand);
        Assert.False(replaced.VisorToggled);
        Assert.Equal((ushort)27, replaced.FacewearModelId);
        Assert.False(replaced.HatVisible);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(-1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void CreatePreservesModelScaleWithoutNormalization(float value)
    {
        var appearance = AppearanceData.Create(
            2720,
            ModelCategory.Human,
            10072,
            AppearanceCompleteness.Complete,
            new byte[26],
            new ulong[10],
            value);

        Assert.Equal(value, appearance.ModelScale);
    }

    [Fact]
    public void CompleteOutfitReplacementUpdatesNpcOwnedFacewearAndHatState()
    {
        var appearance = AppearanceData.Create(
            2720,
            ModelCategory.Human,
            10072,
            AppearanceCompleteness.Complete,
            new byte[26],
            new ulong[10],
            0.84f,
            1,
            2,
            false,
            0,
            false);

        var replaced = appearance.WithOutfit(Enumerable.Repeat(3UL, 10), true, 27, true);

        Assert.Equal((ushort)27, replaced.FacewearModelId);
        Assert.True(replaced.HatVisible);
        Assert.True(replaced.VisorToggled);
        Assert.Equal(0.84f, replaced.ModelScale);
    }
}
