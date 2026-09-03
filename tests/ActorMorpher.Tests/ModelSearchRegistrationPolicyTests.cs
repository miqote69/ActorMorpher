using ActorMorpher.Appearance;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class ModelSearchRegistrationPolicyTests
{
    [Fact]
    public void InvalidAlkyoneusIdentityIsExcludedButValidSameNameIdentityRemains()
    {
        // Identity values read from the installed BNpcBase/BNpcCustomize sheets.
        var invalid = Human(0, 0, 0) with { SourceRowId = 18950 };
        var valid = Human(1, 0, 2) with { SourceRowId = 18981, ModelCharaId = 4696 };

        Assert.False(Plugin.ShouldRegisterModelSearchAppearance(invalid));
        Assert.True(Plugin.ShouldRegisterModelSearchAppearance(valid));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 0, 1)]
    [InlineData(9, 0, 1)]
    [InlineData(1, 2, 1)]
    [InlineData(1, 0, 0)]
    [InlineData(1, 0, 3)]
    [InlineData(8, 1, 17)]
    public void InvalidHumanIdentityIsNotRegistered(byte race, byte sex, byte tribe)
        => Assert.False(Plugin.ShouldRegisterModelSearchAppearance(Human(race, sex, tribe)));

    [Fact]
    public void AllSupportedHumanIdentitiesRemainWithZeroModelAndEquipment()
    {
        for (byte race = 1; race <= 8; ++race)
        for (byte sex = 0; sex <= 1; ++sex)
        foreach (var tribe in HumanTribeCatalog.GetTribes(race))
        {
            var appearance = Human(race, sex, (byte)tribe);
            Assert.True(Plugin.ShouldRegisterModelSearchAppearance(appearance));
            Assert.Equal(0u, appearance.ModelCharaId);
            Assert.All(appearance.Equipment, value => Assert.Equal(0UL, value));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(255)]
    public void BodyTypeAndEquipmentSentinelsDoNotDetermineRegistration(byte bodyType)
    {
        var appearance = Human(2, 1, 3);
        appearance = appearance with
        {
            Customize = appearance.Customize.SetItem(2, bodyType),
            Equipment = appearance.Equipment.SetItem(0, 0xFFFFFFFFUL),
        };
        Assert.True(Plugin.ShouldRegisterModelSearchAppearance(appearance));
        Assert.Equal(bodyType, appearance.Customize[2]);
        Assert.Equal(0xFFFFFFFFUL, appearance.Equipment[0]);
    }

    [Fact]
    public void MissingHumanCustomizeIsNotRegistered()
    {
        var appearance = Human(1, 0, 1) with { Customize = [] };
        Assert.False(Plugin.ShouldRegisterModelSearchAppearance(appearance));
    }

    private static AppearanceData Human(byte race, byte sex, byte tribe)
    {
        var customize = new byte[26];
        customize[0] = race;
        customize[1] = sex;
        customize[4] = tribe;
        return AppearanceData.Create(0, ModelCategory.Human, 1,
            AppearanceCompleteness.Complete, customize, new ulong[10]);
    }

    [Fact]
    public void RegistrationRequiresATypedAppearancePayload()
    {
        Assert.False(Plugin.ShouldRegisterModelSearchAppearance(null));
        Assert.True(Plugin.ShouldRegisterModelSearchAppearance(AppearanceData.Create(
            1,
            ModelCategory.Monster,
            1,
            AppearanceCompleteness.ModelOnly,
            [],
            [])));
    }

    [Fact]
    public void OnlyMonsterModelCharaCanBeRegisteredWithoutASourceRow()
    {
        Assert.False(Plugin.ShouldRegisterUnreferencedModelChara(2));
        Assert.True(Plugin.ShouldRegisterUnreferencedModelChara(3));
    }

    [Fact]
    public void DemihumanRegistrationPreservesCustomizeWithoutEquipment()
    {
        var appearance = Plugin.CreateDemihumanAppearance(1, 2, new byte[26], null, 1f);

        Assert.Equal(26, appearance.Customize.Length);
        Assert.Empty(appearance.Equipment);
        Assert.True(Plugin.ShouldRegisterModelSearchAppearance(appearance));
    }

    [Fact]
    public void DemihumanRegistrationPreservesEquipmentWithoutCustomize()
    {
        var appearance = Plugin.CreateDemihumanAppearance(1, 2, null, new ulong[10], 1f);

        Assert.Empty(appearance.Customize);
        Assert.Equal(10, appearance.Equipment.Length);
        Assert.True(Plugin.ShouldRegisterModelSearchAppearance(appearance));
    }

}
