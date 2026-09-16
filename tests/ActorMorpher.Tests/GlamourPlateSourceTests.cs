using System.Linq;
using System;
using FFXIVClientStructs.FFXIV.Client.Game;
using ActorMorpher.BulkOutfit;
using Xunit;

namespace ActorMorpher.Tests;

public class GlamourPlateSourceTests
{
    [Fact]
    public void ObservationFailureDoesNotChangeRequestPendingOrCompletion()
    {
        var operation = new GlamourPlateImportOperation();
        var requests = 0;
        void FailedObservation() => Plugin.ObserveGlamourPlateImport(
            () => throw new InvalidOperationException("Observation-only failure."));
        bool Request()
        {
            requests++;
            FailedObservation();
            return true;
        }
        Assert.True(operation.Start(2, 42, 956, Request));
        Assert.True(operation.IsPending);
        Assert.False(operation.Start(2, 42, 956, Request));
        Assert.Equal(1, requests);
        FailedObservation();
        Assert.Equal(PlateImportProgress.Waiting, operation.Poll(42, 956, false, out _));
        FailedObservation();
        Assert.Equal(PlateImportProgress.Ready, operation.Poll(42, 956, true, out var plate));
        Assert.Equal(2, plate);
        Assert.False(operation.IsPending);
        Assert.True(operation.Start(3, 42, 956, Request));
        operation.Cancel();
        FailedObservation();
        Assert.Equal(PlateImportProgress.Idle, operation.Poll(42, 956, true, out _));
        Assert.True(operation.Start(3, 42, 956, Request));
        FailedObservation();
        Assert.Equal(PlateImportProgress.ContextChanged, operation.Poll(42, 957, false, out _));
        Assert.False(operation.IsPending);
        Assert.Equal(3, requests);
    }

    [Fact]
    public unsafe void NativeObservationReadsFlagsAndAllPlatesWithoutChangingThem()
    {
        var manager = new MirageManager();
        Assert.Equal(default, GlamourPlateSource.Observe(null));
        var empty = GlamourPlateSource.Observe(&manager);
        Assert.False(empty.Requested);
        Assert.False(empty.Loaded);
        Assert.Equal(0, empty.NonEmptyItems);
        manager.GlamourPlatesRequested = true;
        manager.GlamourPlates[19].ItemIds[11] = 54321;
        var before = new ReadOnlySpan<byte>(&manager, sizeof(MirageManager)).ToArray();
        var requested = GlamourPlateSource.Observe(&manager);
        Assert.True(requested.Requested);
        Assert.False(requested.Loaded);
        Assert.Equal(1, requested.NonEmptyItems);
        Assert.Equal(1, requested.NonEmptyPlates);
        Assert.NotEqual(empty.DataHash, requested.DataHash);
        Assert.Equal(before, new ReadOnlySpan<byte>(&manager, sizeof(MirageManager)).ToArray());
        manager.GlamourPlates[19].Stain1Ids[11] = 7;
        var recolored = GlamourPlateSource.Observe(&manager);
        Assert.NotEqual(requested.DataHash, recolored.DataHash);
        manager.GlamourPlatesLoaded = true;
        var received = GlamourPlateSource.Observe(&manager);
        Assert.True(received.Loaded);
        Assert.Equal(recolored.DataHash, received.DataHash);
        Assert.Equal((nint)(&manager), received.ManagerAddress);
    }

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
