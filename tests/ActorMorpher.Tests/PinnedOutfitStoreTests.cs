using System;
using System.Linq;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class PinnedOutfitStoreTests
{
    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 10)]
    [InlineData(true, 10)]
    public void SelectedEquipmentUpdatesOnlyChosenSlotInExistingPin(bool fullAppearance, int slot)
    {
        var configuration = new Configuration();
        var saves = 0;
        var store = new PinnedOutfitStore(configuration, () => saves++);
        var actor = Actor("Selected", false, 1, 100);
        var other = Actor("Other", false, 2, 100);
        var original = Outfit(20);
        original = original with { Equipment = original.Equipment.SetItem(1,
            original.Equipment[1] with { Color1 = new(0.3f, 0.4f, 0.5f) }) };
        var model = Appearance(ModelCategory.Human).WithOutfit(original.Equipment.Select(ActorRegistry.ToEquipmentModelValue),
            original.VisorToggled, original.Facewear.ModelId, original.HatVisible) with { ColoredEquipment = original.Equipment };
        if (fullAppearance) store.PinCurrent([actor], _ => model);
        else store.Pin(actor, original);
        store.Pin(other, Outfit(30));
        var choice = new EquipmentChoiceKey(slot, 9005, 2, 17);
        var applied = EquipmentChoice.Replace(original, choice);
        store.UpdateSelectedEquipment(actor, choice, applied);
        Assert.Equal(3, saves);
        Assert.True(store.TryGet(actor, out var pinned));
        Assert.True(OutfitDataValueComparer.AreEqual(applied, pinned));
        Assert.True(store.TryGet(other, out var untouched));
        Assert.True(OutfitDataValueComparer.AreEqual(Outfit(30), untouched));
        if (fullAppearance)
        {
            Assert.True(store.TryGetAppearance(actor, out var updated));
            Assert.Equal(model.Customize, updated.Customize);
            Assert.Equal(model.ModelCharaId, updated.ModelCharaId);
            Assert.Equal(model.Mainhand, updated.Mainhand);
            var expected = model.WithOutfit(applied.Equipment.Select(ActorRegistry.ToEquipmentModelValue),
                applied.VisorToggled, applied.Facewear.ModelId, applied.HatVisible) with { ColoredEquipment = applied.Equipment };
            Assert.True(PinnedOutfitStore.AppearanceEquals(expected, updated));
        }
    }

    [Theory]
    [InlineData(ModelCategory.Human)]
    [InlineData(ModelCategory.Demihuman)]
    [InlineData(ModelCategory.Monster)]
    public void CurrentFullPinRoundTripsActualConfigurationJson(ModelCategory category)
    {
        var configuration = new Configuration();
        var saves = 0;
        var store = new PinnedOutfitStore(configuration, () => saves++);
        var actor = Actor("Current", false, 1, 100);
        var displayed = Appearance(category);
        var result = store.PinCurrent([actor], _ => displayed);
        Assert.Equal((1, 0), result);
        Assert.Equal(1, saves);
        // Same serializer as Dalamud config persistence; these are TEST_ONLY actor values.
        var json = Newtonsoft.Json.JsonConvert.SerializeObject(configuration);
        var loaded = Newtonsoft.Json.JsonConvert.DeserializeObject<Configuration>(json)!;
        PinnedOutfitStore.Normalize(loaded);
        Assert.True(new PinnedOutfitStore(loaded, () => { }).TryGetAppearance(actor, out var restored));
        Assert.True(PinnedOutfitStore.AppearanceEquals(displayed, restored));
        Assert.Equal(displayed.SourceRowId, restored.SourceRowId);
        Assert.Equal(displayed.Completeness, restored.Completeness);
    }

    [Fact]
    public void BatchKeepsSameNameActorsAndMoreThan256PinsAndClearsAbsentActorsOnce()
    {
        var configuration = new Configuration();
        var saves = 0;
        var store = new PinnedOutfitStore(configuration, () => saves++);
        var actors = Enumerable.Range(1, 270).Select(i => Actor("Same name", false, (ushort)i, 100)).ToArray();
        Assert.Equal((270, 0), store.PinCurrent(actors, actor => Appearance(ModelCategory.Human) with { ModelCharaId = actor.Key.OriginalObjectIndex }));
        Assert.Equal(270, store.Count);
        Assert.Equal(1, saves);
        PinnedOutfitStore.Normalize(configuration);
        Assert.Equal(270, store.Count);
        foreach (var actor in actors)
        {
            Assert.True(store.TryGetAppearance(actor, out var appearance));
            Assert.Equal((uint)actor.Key.OriginalObjectIndex, appearance.ModelCharaId);
        }
        Assert.Equal((1, 1), store.PinCurrent(actors.Take(2), actor => actor == actors[0] ? Appearance(ModelCategory.Monster) : null));
        Assert.Equal(270, store.Count);
        Assert.Equal(2, saves);
        // No visible actor list is needed to clear stored/absent actors.
        Assert.Equal(270, store.UnpinAll());
        Assert.Equal(0, store.Count);
        Assert.Equal(3, saves);
        Assert.Equal(0, store.UnpinAll());
        Assert.Equal(3, saves);
    }

    [Fact]
    public void FullPinIdentityUsesContinuityOrExactLifetimeAndSession()
    {
        var first = Actor("Same", true, 1, 100);
        var identity = new ActorContinuityKey(ObjectKind.Pc, 1, 1234, 0, 0);
        first = first with { Key = first.Key with { Continuity = identity } };
        var pin = PinnedOutfitConfiguration.Create(first, Appearance(ModelCategory.Human));
        var recreated = Actor("Renamed", true, 99, 200);
        recreated = recreated with { Key = recreated.Key with { Continuity = identity } };
        Assert.True(pin.Matches(recreated));
        Assert.False(pin.Matches(recreated with { Key = recreated.Key with { Continuity = identity with { Id = 999 } } }));
        var provisional = Actor("Same", false, 5, 100);
        pin = PinnedOutfitConfiguration.Create(provisional, Appearance(ModelCategory.Monster));
        Assert.True(pin.Matches(provisional));
        Assert.False(pin.Matches(provisional with { Key = provisional.Key with { Lifetime = 2 } }));
        pin.Session = Guid.NewGuid();
        Assert.False(pin.Matches(provisional));
    }

    [Fact]
    public void FreshRenderedFieldsWinExceptManagedModelAndHatDescriptors()
    {
        var displayed = Appearance(ModelCategory.Human);
        var desired = displayed with { ModelCharaId = 901, Equipment = System.Collections.Immutable.ImmutableArray.CreateRange(Enumerable.Repeat(999UL, 10)), HatVisible = true };
        var snapshot = Actor("Current", true, 1, 100).Current with { CurrentAppearance = desired, IsAppearanceManaged = true };
        var captured = ActorRegistry.DescribeRenderedAppearance(snapshot, snapshot with { CurrentAppearance = displayed })!;
        Assert.Equal(901U, captured.ModelCharaId);
        Assert.Equal(displayed.Equipment, captured.Equipment);
        Assert.Equal(desired.HatVisible, captured.HatVisible);
        Assert.True(PinnedOutfitStore.AppearanceEquals(displayed with { ModelCharaId = 901, HatVisible = desired.HatVisible }, captured));
        Assert.Null(ActorRegistry.DescribeRenderedAppearance(snapshot, null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoredBackingHatDoesNotKeepFullPinComparisonUnequal(bool visible)
    {
        var pinned = Appearance(ModelCategory.Human) with { HatVisible = visible };
        var managed = Actor("Pinned", true, 1, 100).Current with
        {
            CurrentAppearance = pinned, IsAppearanceManaged = true,
        };
        var raw = pinned with { HatVisible = !visible };
        // TEST_ONLY post-Apply observations. These are the actual capture/comparison
        // helpers used by the scan, not native execution or a shutdown reproduction.
        for (var i = 0; i < 77; ++i)
        {
            var captured = ActorRegistry.DescribeRenderedAppearance(managed,
                managed with { CurrentAppearance = raw })!;
            Assert.True(PinnedOutfitStore.AppearanceEquals(captured, pinned));
        }
        var changedValues = new[]
        {
            raw with { Equipment = raw.Equipment.SetItem(0, 123) },
            raw with { Equipment = raw.Equipment.SetItem(1, 456) },
            raw with { Mainhand = 10 }, raw with { Offhand = 20 },
            raw with { Customize = raw.Customize.SetItem(0, 8) },
            raw with { VisorToggled = !raw.VisorToggled },
        };
        foreach (var changed in changedValues)
            Assert.False(PinnedOutfitStore.AppearanceEquals(pinned,
                ActorRegistry.DescribeRenderedAppearance(managed,
                    managed with { CurrentAppearance = changed })!));

        var unmanaged = managed with { IsAppearanceManaged = false };
        Assert.Equal(raw, ActorRegistry.DescribeRenderedAppearance(unmanaged,
            unmanaged with { CurrentAppearance = raw }));
        var monster = Appearance(ModelCategory.Monster) with { HatVisible = null };
        Assert.Null(ActorRegistry.DescribeRenderedAppearance(
            managed with { CurrentAppearance = monster },
            managed with { CurrentAppearance = monster })!.HatVisible);
    }

    [Fact]
    public void ReusedRepresentationWithNewLifetimeDoesNotCaptureOrSavePin()
    {
        var configuration = new Configuration();
        var saves = 0;
        var store = new PinnedOutfitStore(configuration, () => saves++);
        var actor = Actor("Old", false, 1, 100);
        var expected = actor.Current with { CurrentAppearance = Appearance(ModelCategory.Human) };
        var fresh = expected with { LogicalKey = expected.LogicalKey with { Lifetime = 99 } };
        Assert.Equal(expected.RepresentationKey, fresh.RepresentationKey);
        Assert.Equal((0, 1), store.PinCurrent([actor], _ => ActorRegistry.DescribeRenderedAppearance(expected, fresh)));
        Assert.Equal(0, saves);
        Assert.Equal(0, store.Count);
        Assert.False(store.TryGetAppearance(actor, out _));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void PersistentIdentityUsesOnlyDurableSourcesAcrossSessions(byte source, bool durable)
    {
        var actor = Actor("Same", false, 1, 100);
        var identity = new ActorContinuityKey(ObjectKind.EventNpc, source, 1234, 100, 100);
        actor = actor with { Key = actor.Key with { Continuity = identity } };
        var pin = PinnedOutfitConfiguration.Create(actor, Appearance(ModelCategory.Human));
        var recreated = actor with { Key = actor.Key with { OriginalObjectIndex = 99, Lifetime = 2 } };
        Assert.True(pin.Matches(recreated));
        var originalGroup = pin.IdentityKey();
        pin.Session = Guid.NewGuid();
        Assert.Equal(durable, pin.Matches(recreated));
        Assert.Equal(durable, originalGroup == pin.IdentityKey());
    }

    [Fact]
    public void VisibleComparisonIgnoresProvenanceButIncludesEveryVisibleField()
    {
        var appearance = Appearance(ModelCategory.Human);
        Assert.True(PinnedOutfitStore.AppearanceEquals(appearance, appearance with { SourceRowId = 99, Completeness = AppearanceCompleteness.Unsupported }));
        var differences = new[] {
            appearance with { ModelCharaId = 9 }, appearance with { Category = ModelCategory.Demihuman },
            appearance with { Customize = appearance.Customize.SetItem(0, 8) },
            appearance with { Equipment = appearance.Equipment.SetItem(0, 123) },
            appearance with { ModelScale = 2 }, appearance with { Mainhand = 10 },
            appearance with { Offhand = 20 }, appearance with { HatVisible = true },
            appearance with { VisorToggled = true }, appearance with { FacewearModelId = 3 },
        };
        Assert.All(differences, changed => Assert.False(PinnedOutfitStore.AppearanceEquals(appearance, changed)));
    }

    [Fact]
    public void LegacyOutfitPinRemainsOutfitOnly()
    {
        var configuration = new Configuration();
        var store = new PinnedOutfitStore(configuration, () => { });
        var actor = Actor("Legacy", true, 1, 100);
        store.Pin(actor, Outfit(12));
        PinnedOutfitStore.Normalize(configuration);
        Assert.False(store.TryGetAppearance(actor, out _));
        Assert.True(store.TryGet(actor, out var outfit));
        Assert.True(OutfitDataValueComparer.AreEqual(Outfit(12), outfit));
        Assert.True(store.Unpin(actor));
        Assert.Equal(0, store.Count);
    }

    private static AppearanceData Appearance(ModelCategory category)
        => AppearanceData.Create(0, category, 17,
            category == ModelCategory.Monster ? AppearanceCompleteness.ModelOnly : AppearanceCompleteness.Complete,
            category == ModelCategory.Human ? new byte[26] : Array.Empty<byte>(),
            category == ModelCategory.Monster ? Array.Empty<ulong>() : Enumerable.Range(0, 10).Select(i => (ulong)i << 24),
            0.84f, category == ModelCategory.Human ? 15UL : null,
            category == ModelCategory.Human ? 16UL : null,
            category == ModelCategory.Human ? false : null,
            category == ModelCategory.Human ? (ushort)2 : null,
            category == ModelCategory.Human ? false : null);

    [Fact]
    public void PinRoundTripsOutfitAndSurvivesLogicalKeyChanges()
    {
        var configuration = new Configuration();
        var saveCount = 0;
        var store = new PinnedOutfitStore(configuration, () => saveCount++);
        var first = Actor("Test Player", true, 1, 10);
        var recreated = Actor("Test Player", true, 99, 20);
        var outfit = Outfit(42);

        store.Pin(first, outfit);

        Assert.True(store.IsPinned(recreated));
        Assert.True(store.TryGet(recreated, out var restored));
        Assert.True(OutfitDataValueComparer.AreEqual(outfit, restored));
        Assert.Equal(1, saveCount);
    }

    [Fact]
    public void UnpinRemovesOnlyMatchingActor()
    {
        var configuration = new Configuration();
        var store = new PinnedOutfitStore(configuration, static () => { });
        var first = Actor("First", false, 1, 100);
        var second = Actor("Second", false, 2, 200);
        store.Pin(first, Outfit(10));
        store.Pin(second, Outfit(20));

        Assert.True(store.Unpin(first));

        Assert.False(store.IsPinned(first));
        Assert.True(store.IsPinned(second));
    }

    [Fact]
    public void ValueComparerComparesEquipmentContents()
    {
        Assert.True(OutfitDataValueComparer.AreEqual(Outfit(7), Outfit(7)));
        Assert.False(OutfitDataValueComparer.AreEqual(Outfit(7), Outfit(8)));
    }

    private static ActorEntry Actor(string name, bool local, ushort index, uint territory)
    {
        var key = new LogicalActorKey(index, index, index, local ? 0U : 100U, ObjectKind.Pc, territory);
        var snapshot = new ActorSnapshot(
            key,
            new ActorRepresentationKey(index, index, index, false),
            name,
            ObjectKind.Pc,
            key.BaseId,
            1,
            1,
            0,
            1,
            0,
            90,
            local);
        return new ActorEntry(key, name, ObjectKind.Pc, local, [snapshot]);
    }

    private static OutfitData Outfit(ushort set)
        => OutfitData.Create(
            Enum.GetValues<OutfitSlot>().Select((_, index) => new ArmorAppearance(
                checked((ushort)(set + index)),
                1,
                2,
                3)),
            new FacewearAppearance(true, 12),
            true,
            false);
}
