using System;
using System.Collections.Generic;
using System.Linq;
using ActorMorpher.BulkOutfit;
using FFXIVClientStructs.FFXIV.Client.Game;
using Newtonsoft.Json;
using Xunit;

namespace ActorMorpher.Tests;

public class GlamourPlateCacheTests
{
    private static SavedGlamourPlates Plates(uint item) => new()
    {
        AcquiredAt = DateTimeOffset.Parse("2026-09-15T12:00:00Z"),
        Plates = Enumerable.Range(0, 20).Select(_ => new SavedGlamourPlate
        {
            Items = Enumerable.Repeat(item, 12).ToArray(),
            Stain1 = Enumerable.Repeat((byte)7, 12).ToArray(),
            Stain2 = Enumerable.Repeat((byte)9, 12).ToArray(),
        }).ToArray(),
    };

    [Fact]
    public void AcquiresOnceAndUsesOnlyCurrentCharactersSavedPlatesAfterAreaChangeAndRestart()
    {
        var configuration = new Configuration();
        var requests = 0;
        string? json = null;
        var cache = new GlamourPlateCache(configuration.GlamourPlates,
            () => json = JsonConvert.SerializeObject(configuration));
        var received = Plates(123);
        bool Request() { requests++; return true; }
        cache.Update(42, 819, true, false, false, Request, _ => received);
        Assert.True(cache.IsWaiting);
        for (var frame = 0; frame < 1000; frame++)
            cache.Update(42, 819, true, false, true, Request, _ => throw new Exception());
        Assert.Equal(1, requests);
        cache.Update(42, 819, true, true, true, Request, _ => received);
        Assert.False(cache.IsWaiting);
        cache.Update(42, 813, false, false, false, Request, _ => throw new Exception());
        Assert.Same(received, cache.Get(42));
        Assert.Null(cache.Get(43));
        Assert.Null(cache.Get(0));
        Assert.Equal(1, requests);
        var restored = JsonConvert.DeserializeObject<Configuration>(json!)!;
        var restarted = new GlamourPlateCache(restored.GlamourPlates, () => { });
        var plate = restarted.Get(42)!.Plates[19];
        Assert.Equal(received.AcquiredAt, restarted.Get(42)!.AcquiredAt);
        Assert.True(GlamourPlateSource.TryConvert(plate.Items, plate.Stain1, plate.Stain2,
            _ => 1UL, out var equipment, out _));
        Assert.All(equipment, item => Assert.Equal(new ArmorAppearance(1, 0, 7, 9), item));
        Assert.Null(restarted.Get(43));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CharacterSwitchOrLogoutDoesNotAdoptLatePreviousCharactersData(bool logout)
    {
        var entries = new Dictionary<ulong, SavedGlamourPlates> { [42] = Plates(111), [43] = Plates(222) };
        var cache = new GlamourPlateCache(entries, () => { });
        cache.Update(42, 819, true, false, false, () => true, _ => throw new Exception());
        if (logout) cache.EndSession();
        // A's completion is already loaded when B first appears: never save it for B.
        cache.Update(43, 819, true, true, true, () => throw new Exception(), _ => throw new Exception());
        Assert.Equal(222u, cache.Get(43)!.Plates[0].Items[0]);
        Assert.Equal(111u, cache.Get(42)!.Plates[0].Items[0]);
        Assert.False(cache.IsWaiting);
        // Normal native invalidation followed by B's own acquisition.
        cache.Update(43, 819, true, false, false, () => true, _ => throw new Exception());
        Assert.True(cache.IsWaiting);
        var b = Plates(333);
        cache.Update(43, 819, true, true, true, () => throw new Exception(), _ => b);
        Assert.Same(b, cache.Get(43));
        Assert.Equal(111u, cache.Get(42)!.Plates[0].Items[0]);
    }

    [Fact]
    public void FailedAcquisitionKeepsOldDataWithoutRepeatedRequestsOrAnImportWait()
    {
        var old = Plates(111);
        var entries = new Dictionary<ulong, SavedGlamourPlates> { [42] = old };
        var requests = 0;
        var cache = new GlamourPlateCache(entries, () => throw new Exception());
        bool Request() { requests++; return true; }
        cache.Update(42, 819, true, false, false, Request, _ => throw new Exception());
        cache.Update(42, 819, true, false, true, Request, _ => throw new Exception());
        for (var i = 0; i < 1000; i++)
            cache.Update(42, 819, true, false, false, Request, _ => throw new Exception());
        Assert.False(cache.IsWaiting);
        Assert.Same(old, cache.Get(42));
        Assert.Equal(1, requests);
    }

    [Fact]
    public void PreviousSessionsInFlightRequestCannotBecomeNewCharactersAcquisition()
    {
        var entries = new Dictionary<ulong, SavedGlamourPlates>();
        var cache = new GlamourPlateCache(entries, () => { });
        cache.Update(42, 819, true, false, false, () => true, _ => throw new Exception());
        cache.EndSession();
        cache.Update(43, 819, true, false, true, () => throw new Exception(), _ => throw new Exception());
        cache.Update(43, 819, true, true, true, () => throw new Exception(), _ => throw new Exception());
        Assert.Null(cache.Get(43));
        Assert.False(cache.IsWaiting);
        // Only after the old request/buffer are cleared can B issue its own request.
        cache.Update(43, 819, true, false, false, () => true, _ => throw new Exception());
        Assert.True(cache.IsWaiting);
        var b = Plates(333);
        cache.Update(43, 819, true, true, true, () => throw new Exception(), _ => b);
        Assert.Same(b, cache.Get(43));
        Assert.Null(cache.Get(42));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedSaveKeepsPreviousPersistedValueAndTimeWithoutPerFrameRetry(bool existing)
    {
        var old = Plates(111);
        var entries = new Dictionary<ulong, SavedGlamourPlates>();
        if (existing) entries[42] = old;
        var saves = 0;
        var cache = new GlamourPlateCache(entries, () => { saves++; throw new Exception("Disk write failed"); });
        var fresh = Plates(222);
        fresh.AcquiredAt = old.AcquiredAt.AddDays(1);
        Assert.Throws<Exception>(() => cache.Update(42, 819, true, true, true,
            () => throw new Exception(), _ => fresh));
        for (var i = 0; i < 100; i++)
            cache.Update(42, 819, true, true, true, () => throw new Exception(), observed =>
            {
                Assert.Same(fresh, observed);
                return observed!;
            });
        Assert.Equal(1, saves);
        if (existing) Assert.Same(old, cache.Get(42));
        else Assert.Null(cache.Get(42));
    }

    [Fact]
    public void LogoutDuringRequestCannotPublishOwnershipIntoNextSession()
    {
        var cache = new GlamourPlateCache(new(), () => { });
        cache.Update(42, 819, true, false, false,
            () => { cache.EndSession(); return true; }, _ => throw new Exception());
        Assert.False(cache.IsWaiting);
        cache.Update(42, 819, true, true, true, () => throw new Exception(), _ => throw new Exception());
        Assert.Null(cache.Get(42));
    }

    [Fact]
    public unsafe void CapturePreservesAllNativeDataDetectsDyeChangesAndAcceptsEmptyPlates()
    {
        var manager = new MirageManager();
        manager.GlamourPlatesLoaded = true;
        manager.GlamourPlates[19].ItemIds[11] = 12345;
        manager.GlamourPlates[19].Stain0Ids[11] = 4;
        manager.GlamourPlates[19].Stain1Ids[11] = 6;
        var bytes = new ReadOnlySpan<byte>(&manager, sizeof(MirageManager)).ToArray();
        var first = GlamourPlateSource.Capture(&manager, null);
        Assert.Equal(20, first.Plates.Length);
        Assert.Equal(12345u, first.Plates[19].Items[11]);
        Assert.Equal(4, first.Plates[19].Stain1[11]);
        Assert.Equal(6, first.Plates[19].Stain2[11]);
        Assert.Same(first, GlamourPlateSource.Capture(&manager, first));
        Assert.Equal(bytes, new ReadOnlySpan<byte>(&manager, sizeof(MirageManager)).ToArray());
        manager.GlamourPlates[19].Stain1Ids[11] = 8;
        var second = GlamourPlateSource.Capture(&manager, first);
        Assert.NotSame(first, second);
        Assert.Equal(6, first.Plates[19].Stain2[11]);
        Assert.Equal(8, second.Plates[19].Stain2[11]);
        manager = new MirageManager();
        var empty = GlamourPlateSource.Capture(&manager, second);
        Assert.All(empty.Plates, plate => Assert.All(plate.Items, item => Assert.Equal(0u, item)));
    }
}
