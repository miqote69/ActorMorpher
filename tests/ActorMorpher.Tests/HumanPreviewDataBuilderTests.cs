using ActorMorpher.Appearance;
using ActorMorpher.Preview;
using System.Linq;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class HumanPreviewDataBuilderTests
{
    [Fact]
    public void BuildsImmutableCharaViewInputFromCompleteHumanEntry()
    {
        var customize = Enumerable.Range(0, 26).Select(static value => (byte)value).ToArray();
        customize[0] = 1;
        customize[1] = 1;
        customize[2] = (byte)NpcAge.Young;
        customize[4] = 2;
        var equipment = Enumerable.Range(1, 10).Select(static value => (ulong)value).ToArray();
        var entry = Entry(customize, equipment, 0x1234, 0x5678, true);

        var result = new HumanPreviewDataBuilder().TryBuild(entry, out var data, out var failure);

        Assert.True(result);
        Assert.Equal(HumanPreviewDataFailure.None, failure);
        Assert.Equal((byte)NpcAge.Young, data.BodyType);
        Assert.Equal(new ulong[] { 0x1234, 0x5678, 0 }, data.Weapons);
        Assert.Equal(new ushort[] { 0, 0 }, data.Glasses);
        Assert.True(data.VisorClosed);

        customize[0] = 8;
        equipment[0] = 999;
        Assert.Equal((byte)1, data.Race);
        Assert.Equal((ulong)1, data.Equipment[0]);
    }

    [Theory]
    [InlineData(25, 10, HumanPreviewDataFailure.InvalidCustomizeLength)]
    [InlineData(26, 9, HumanPreviewDataFailure.InvalidEquipmentLength)]
    public void RejectsMalformedPayloadLengths(int customizeLength, int equipmentLength, HumanPreviewDataFailure expected)
    {
        var customize = new byte[customizeLength];
        if (customizeLength >= 2)
        {
            customize[0] = 1;
            customize[1] = 0;
        }
        var equipment = new ulong[equipmentLength];
        var entry = Entry(customize, equipment);

        var result = new HumanPreviewDataBuilder().TryBuild(entry, out _, out var failure);

        Assert.False(result);
        Assert.Equal(expected, failure);
    }

    [Fact]
    public void RejectsAppearanceThatDoesNotMatchModelBackingData()
    {
        var customize = ValidCustomize();
        var equipment = new ulong[10];
        var entry = Entry(customize, equipment) with
        {
            ModelAppearance = AppearanceData.Create(
                100,
                ModelCategory.Human,
                10,
                AppearanceCompleteness.Complete,
                customize,
                Enumerable.Repeat(7UL, 10)),
        };

        var result = new HumanPreviewDataBuilder().TryBuild(entry, out _, out var failure);

        Assert.False(result);
        Assert.Equal(HumanPreviewDataFailure.InconsistentAppearance, failure);
    }

    private static ModelSearchEntry Entry(
        byte[] customize,
        ulong[] equipment,
        ulong mainhand = 0,
        ulong offhand = 0,
        bool visor = false)
    {
        var human = new HumanAppearance(customize, equipment, mainhand, offhand, visor);
        var appearance = AppearanceData.Create(
            100,
            ModelCategory.Human,
            10,
            AppearanceCompleteness.Complete,
            customize,
            equipment);
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
            customize.Length > 2 ? customize[2] : (byte)0,
            human,
            AppearanceCompleteness.Complete,
            appearance);
    }

    private static byte[] ValidCustomize()
    {
        var customize = new byte[26];
        customize[0] = 1;
        customize[1] = 0;
        customize[2] = (byte)NpcAge.Normal;
        customize[4] = 1;
        return customize;
    }
}
