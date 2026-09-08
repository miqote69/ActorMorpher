using System.Linq;
using ActorMorpher.BulkOutfit;
using Xunit;

namespace ActorMorpher.Tests;

public class GlamourPlateSourceTests
{
    [Fact]
    public void MapsTenPlateSlotsAndBothDyesWithoutWeapons()
    {
        var items = Enumerable.Range(100, 12).Select(x => (uint)x).ToArray();
        var stains1 = Enumerable.Range(1, 12).Select(x => (byte)x).ToArray();
        var stains2 = Enumerable.Range(21, 12).Select(x => (byte)x).ToArray();
        Assert.True(GlamourPlateSource.TryConvert(items, stains1, stains2,
            id => { Assert.True(id >= 102); return id | ((ulong)(id - 100) << 16); },
            out var outfit, out var missing));
        Assert.Equal(0u, missing);
        Assert.Equal(10, outfit.Length);
        for (var slot = 0; slot < 10; ++slot)
        {
            Assert.Equal(new ArmorAppearance((ushort)(102 + slot), (byte)(2 + slot),
                (byte)(3 + slot), (byte)(23 + slot)), outfit[slot]);
            Assert.Null(outfit[slot].Color1);
            Assert.Null(outfit[slot].Color2);
        }
    }

    [Fact]
    public void EmptySlotsRemainEmptyAndMissingItemDoesNotReturnPartialEquipment()
    {
        var items = new uint[12];
        var dyes = Enumerable.Repeat((byte)9, 12).ToArray();
        Assert.True(GlamourPlateSource.TryConvert(items, dyes, dyes,
            _ => throw new System.InvalidOperationException("Empty slots require no Item row."),
            out var equipment, out _));
        Assert.All(equipment, slot => Assert.Equal(default(ArmorAppearance), slot));
        items[2] = 111;
        items[11] = 222;
        Assert.False(GlamourPlateSource.TryConvert(items, dyes, dyes,
            id => id == 111 ? 123UL : null, out equipment, out var missing));
        Assert.Equal(222u, missing);
        Assert.Empty(equipment);
    }
}
