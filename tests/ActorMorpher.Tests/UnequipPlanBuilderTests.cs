using System;
using System.Linq;
using ActorMorpher.BulkOutfit;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class UnequipPlanBuilderTests
{
    [Fact]
    public void MissingSlotDataRejectsWholePlanWithoutChangingCurrentOutfit()
    {
        var current = CurrentOutfit();
        var provider = new TestProvider(OutfitSlot.LeftRing);

        var success = new UnequipPlanBuilder().TryCreate(current, provider, out var desired, out _);

        Assert.False(success);
        Assert.Equal(current, desired);
    }

    [Fact]
    public void CompleteProviderBuildsSlotSpecificPlanAndPreservesMetaState()
    {
        var current = CurrentOutfit();
        var provider = new TestProvider(null);

        var success = new UnequipPlanBuilder().TryCreate(current, provider, out var desired, out _);

        Assert.True(success);
        Assert.Equal(Enum.GetValues<OutfitSlot>().Length, desired.Equipment.Distinct().Count());
        Assert.Equal(current.HatVisible, desired.HatVisible);
        Assert.Equal(current.VisorToggled, desired.VisorToggled);
    }

    private static OutfitData CurrentOutfit()
        => OutfitData.Create(
            Enumerable.Repeat(new ArmorAppearance(500, 1, 2, 3), Enum.GetValues<OutfitSlot>().Length),
            new FacewearAppearance(true, 10),
            false,
            true);

    private sealed class TestProvider(OutfitSlot? missingSlot) : IUnequipAppearanceProvider
    {
        public bool TryGetNothing(OutfitSlot slot, out ArmorAppearance appearance)
        {
            appearance = new ArmorAppearance((ushort)(100 + (int)slot), (byte)slot, 0, 0);
            return slot != missingSlot;
        }

        public bool TryGetNoFacewear(out FacewearAppearance appearance)
        {
            appearance = new FacewearAppearance(true, 0);
            return true;
        }
    }
}
