using System;
using System.Collections.Immutable;
using System.Linq;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using ActorMorpher.Interop;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class EquipmentCopyTests
{
    [Fact]
    public void OutfitListCopySnapshotsAllSlotsAndColorsWithoutCopyingWeapons()
    {
        var source = EquipmentDisplayFormatting.CreateHumanOutfit(Appearance(100))!;
        source = source with { Equipment = source.Equipment.SetItem(3, new(0, 0, 0, 0)) };
        var snapshot = source;
        var copied = EquipmentCopyData.FromOutfit(source);
        Assert.False(copied.IncludesWeapons);
        Assert.Null(copied.Mainhand);
        Assert.Null(copied.Offhand);
        Assert.Equal(snapshot, source);
        source = source with
        {
            Equipment = source.Equipment.SetItem(0, new(900, 4, 1, 2)),
            Facewear = new(true, 0), HatVisible = true, VisorToggled = false,
        };
        var destination = Appearance(700) with
        {
            Mainhand = 0x1011000200030004UL, Offhand = 0,
            MainhandDyes = new(new DyeColor(.1f, .2f, .3f) { Metallic = false }, null),
            OffhandDyes = new(null, new DyeColor(.4f, .5f, .6f) { Metallic = true }),
            ModelCharaId = 123, ModelScale = 1.7f,
            CustomizeColors = [new(8, .2f, .3f, .4f)],
        };
        var applied = copied.ApplyTo(destination);
        Assert.Equal<ulong>(snapshot.Equipment.Select(ActorRegistry.ToEquipmentModelValue), applied.Equipment);
        Assert.Equal<ArmorAppearance>(snapshot.Equipment, applied.ColoredEquipment);
        Assert.Equal(0UL, applied.Equipment[3]);
        Assert.Equal(snapshot.Facewear.ModelId, applied.FacewearModelId);
        Assert.Equal(snapshot.HatVisible, applied.HatVisible);
        Assert.Equal(snapshot.VisorToggled, applied.VisorToggled);
        Assert.Equal(snapshot.VieraEarsHidden, applied.VieraEarsHidden);
        Assert.Equal(destination.Mainhand, applied.Mainhand);
        Assert.Equal(destination.Offhand, applied.Offhand);
        Assert.Equal(destination.MainhandDyes, applied.MainhandDyes);
        Assert.Equal(destination.OffhandDyes, applied.OffhandDyes);
        Assert.Equal<byte>(destination.Customize, applied.Customize);
        Assert.Equal<CustomizeColorOverride>(destination.CustomizeColors, applied.CustomizeColors);
        Assert.Equal(destination.ModelCharaId, applied.ModelCharaId);
        Assert.Equal(destination.ModelScale, applied.ModelScale);
        Assert.Equal((ushort)900, source.Equipment[0].Set);
        Assert.Equal((ushort)100, copied.ColoredEquipment[0].Set);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutfitListCopyDistinguishesUnspecifiedMetadataFromExplicitRemoval(bool specified)
    {
        var source = EquipmentDisplayFormatting.CreateHumanOutfit(Appearance(100))! with
        {
            Facewear = specified ? new(true, 0) : FacewearAppearance.Unavailable,
            VieraEarsHidden = specified ? false : null,
        };
        var destination = Appearance(700) with { FacewearModelId = 22, VieraEarsHidden = true };
        var applied = EquipmentCopyData.FromOutfit(source).ApplyTo(destination);
        Assert.Equal(specified ? (ushort)0 : (ushort)22, applied.FacewearModelId);
        Assert.Equal(!specified, applied.VieraEarsHidden);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OutfitListPublicationAndPinsDoNotAcquireUnspecifiedFields(bool hasModel)
    {
        var actor = Actor();
        var persistence = new ActorAppearancePersistence();
        var retained = Appearance(700) with
        {
            FacewearModelId = 77, VieraEarsHidden = false,
            Mainhand = 0x1122000100020003UL, Offhand = 0x3344000400050006UL,
        };
        var original = EquipmentDisplayFormatting.CreateHumanOutfit(retained)!;
        persistence.Outfits.SetDesired(actor.Key, original, original);
        if (hasModel) persistence.RecordModel(actor.Current, retained);
        var source = EquipmentDisplayFormatting.CreateHumanOutfit(Appearance(100))! with
        {
            Facewear = FacewearAppearance.Unavailable, VieraEarsHidden = null,
        };
        var data = EquipmentCopyData.FromOutfit(source);
        var displayed = Appearance(900) with { FacewearModelId = 22, VieraEarsHidden = true };
        var desired = data.ApplyTo(displayed);
        // The redraw keeps what is currently displayed, but that readback must
        // not become a new weapon/facewear/ear override at publication.
        Assert.Equal(displayed.Mainhand, desired.Mainhand);
        Assert.Equal((ushort)22, desired.FacewearModelId);
        Assert.True(desired.VieraEarsHidden);
        Plugin.RecordEquipmentCopy(persistence, actor.Current, data, original);
        Assert.True(persistence.Outfits.TryGet(actor.Key, out var state));
        Assert.Same(original, state.Original);
        Assert.Equal<ArmorAppearance>(source.Equipment, state.Desired.Equipment);
        Assert.False(state.Desired.Facewear.IsAvailable);
        Assert.Null(state.Desired.VieraEarsHidden);

        if (hasModel)
        {
            var model = Assert.IsType<AppearanceData>(persistence.GetModel(actor.Key));
            Assert.Equal(retained.Mainhand, model.Mainhand);
            Assert.Equal(retained.Offhand, model.Offhand);
            Assert.Equal(retained.MainhandDyes, model.MainhandDyes);
            Assert.Equal(retained.OffhandDyes, model.OffhandDyes);
            Assert.Equal(retained.FacewearModelId, model.FacewearModelId);
            Assert.Equal(retained.VieraEarsHidden, model.VieraEarsHidden);
        }
        else
        {
            Assert.Null(persistence.GetModel(actor.Key));
            Assert.False(persistence.HasWeaponOverride(actor.Key));
            var create = Assert.IsType<AppearanceData>(persistence.GetCreateAppearance(actor.Key, 555, out var outfitOnly));
            Assert.True(outfitOnly);
            Assert.Null(create.Mainhand);
            Assert.Null(create.Offhand);
            Assert.Null(create.FacewearModelId);
            Assert.Null(create.VieraEarsHidden);
        }

        var pin = hasModel ? PinnedOutfitConfiguration.Create(actor, retained)
            : PinnedOutfitConfiguration.Create(actor, original);
        var configuration = new Configuration { PinnedOutfitsByCharacter = new() { [1] = [pin] } };
        var store = new PinnedOutfitStore(configuration, () => { }, () => 1);
        store.UpdateEquipment(actor, data);
        Assert.True(pin.TryCreateOutfit(out var pinnedOutfit));
        Assert.Equal<ArmorAppearance>(source.Equipment, pinnedOutfit.Equipment);
        Assert.Equal((ushort)77, pinnedOutfit.Facewear.ModelId);
        Assert.False(pinnedOutfit.VieraEarsHidden);
        if (hasModel)
        {
            Assert.Equal(retained.Mainhand, pin.Appearance!.Mainhand);
            Assert.Equal(retained.Offhand, pin.Appearance.Offhand);
            Assert.Equal(retained.MainhandDyes, pin.Appearance.MainhandDyes);
            Assert.Equal(retained.OffhandDyes, pin.Appearance.OffhandDyes);
        }
        else
            Assert.Null(pin.Appearance);
    }

    [Fact]
    public void CopyRequiresTheTenSlotHumanEquipmentContract()
    {
        Assert.True(EquipmentCopyData.CanCopy(Appearance(100)));
        Assert.False(EquipmentCopyData.CanCopy(null));
        Assert.False(EquipmentCopyData.TryCreate(Appearance(100) with { Category = ModelCategory.Monster }, out _));
        Assert.False(EquipmentCopyData.TryCreate(Appearance(100) with { Category = ModelCategory.Demihuman }, out _));
        Assert.False(EquipmentCopyData.TryCreate(Appearance(100) with { Equipment = [1UL] }, out _));
        Assert.False(EquipmentCopyData.TryCreate(Appearance(100) with { HatVisible = null }, out _));
        Assert.False(EquipmentCopyData.TryCreate(Appearance(100) with { VisorToggled = null }, out _));
    }

    [Fact]
    public void CopyRemainsAnImmutableSnapshotWhenTheSourceChanges()
    {
        var source = Appearance(100);
        Assert.True(EquipmentCopyData.TryCreate(source, out var copy));
        source = source with
        {
            Equipment = source.Equipment.SetItem(0, 0),
            ColoredEquipment = source.ColoredEquipment.SetItem(1, new(9, 1, 0, 0)),
            Mainhand = 0, HatVisible = true,
        };
        Assert.NotEqual(source.Equipment[0], copy.Equipment[0]);
        Assert.Equal((ushort)101, copy.ColoredEquipment[1].Set);
        Assert.Equal(0x2827000300020001UL, copy.Mainhand);
        Assert.False(copy.HatVisible);
    }

    [Fact]
    public unsafe void PasteTransfersAllEquipmentThroughCreateBuffersAndKeepsDestinationCustomization()
    {
        var source = Appearance(100);
        // A hidden hat still has its selected model, while an explicitly empty slot stays zero.
        source = source with { Equipment = source.Equipment.SetItem(3, 0),
            ColoredEquipment = source.ColoredEquipment.SetItem(3, new(0, 0, 0, 0)) };
        var destination = Appearance(700) with
        {
            ModelCharaId = 82, SourceRowId = 9001, ModelScale = 1.37f,
            Customize = Enumerable.Range(0, 26).Select(i => (byte)(200 + i)).ToImmutableArray(),
            CustomizeColors = [new(8, .7f, .8f, .9f)],
        };
        Assert.True(EquipmentCopyData.TryCreate(source, out var copy));
        var desired = copy.ApplyTo(destination);
        AssertEquipment(source, desired);
        Assert.Equal(destination.ModelCharaId, desired.ModelCharaId);
        Assert.Equal(destination.SourceRowId, desired.SourceRowId);
        Assert.Equal(destination.Category, desired.Category);
        Assert.Equal(destination.Completeness, desired.Completeness);
        Assert.Equal(destination.ModelScale, desired.ModelScale);
        Assert.Equal<byte>(destination.Customize, desired.Customize);
        Assert.Equal<CustomizeColorOverride>(destination.CustomizeColors, desired.CustomizeColors);

        var customizeInput = stackalloc byte[26];
        var equipmentInput = stackalloc ulong[10];
        new Span<byte>(customizeInput, 26).Fill(7);
        new Span<ulong>(equipmentInput, 10).Fill(99);
        NativeDrawObjectInjector.InvokeWithTemporaryCreateBuffers(desired, (nint)customizeInput, (nint)equipmentInput,
            (customize, equipment) =>
            {
                Assert.Equal(destination.Customize.ToArray(), new ReadOnlySpan<byte>((void*)customize, 26).ToArray());
                Assert.Equal(source.Equipment.ToArray(), new ReadOnlySpan<ulong>((void*)equipment, 10).ToArray());
                return (nint)1;
            });
        Assert.All(new ReadOnlySpan<byte>(customizeInput, 26).ToArray(), value => Assert.Equal((byte)7, value));
        Assert.All(new ReadOnlySpan<ulong>(equipmentInput, 10).ToArray(), value => Assert.Equal(99UL, value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicationKeepsTheExistingRetentionScopeAndOriginalOutfit(bool hasModel)
    {
        var actor = Actor();
        var persistence = new ActorAppearancePersistence();
        var before = Appearance(700) with { ModelCharaId = 99, ModelScale = 1.2f,
            CustomizeColors = [new(8, .3f, .5f, .7f)] };
        if (hasModel) persistence.RecordModel(actor.Current, before);
        var source = Appearance(100);
        Assert.True(EquipmentCopyData.TryCreate(source, out var copy));
        var desired = copy.ApplyTo(before);
        var original = hasModel ? null : EquipmentDisplayFormatting.CreateHumanOutfit(before);

        Plugin.RecordEquipmentCopy(persistence, actor.Current, copy, original);

        if (hasModel)
        {
            var retained = Assert.IsType<AppearanceData>(persistence.GetModel(actor.Key));
            AssertEquipment(source, retained);
            Assert.Equal<byte>(before.Customize, retained.Customize);
            Assert.Equal<CustomizeColorOverride>(before.CustomizeColors, retained.CustomizeColors);
            Assert.Equal(before.ModelCharaId, retained.ModelCharaId);
            Assert.Equal(before.ModelScale, retained.ModelScale);
            Assert.False(persistence.Outfits.TryGet(actor.Key, out _));
        }
        else
        {
            Assert.Null(persistence.GetModel(actor.Key));
            Assert.True(persistence.Outfits.TryGet(actor.Key, out var state));
            Assert.Same(original, state.Original);
            var create = Assert.IsType<AppearanceData>(persistence.GetCreateAppearance(actor.Key, 555, out var outfitOnly));
            Assert.True(outfitOnly);
            Assert.Empty(create.Customize);
            Assert.Empty(create.CustomizeColors);
            Assert.Null(create.ModelScale);
            Assert.Equal(555U, create.ModelCharaId);
            AssertEquipment(source, create);
            // A second explicit paste preserves the first restore baseline.
            Plugin.RecordEquipmentCopy(persistence, actor.Current, copy, EquipmentDisplayFormatting.CreateHumanOutfit(source));
            Assert.True(persistence.Outfits.TryGet(actor.Key, out state));
            Assert.Same(original, state.Original);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyExistingPinsAreUpdatedAndLegacyPinsKeepTheirScope(bool legacy)
    {
        var actor = Actor();
        var before = Appearance(700) with { ModelCharaId = 99, ModelScale = 1.2f };
        var source = Appearance(100);
        Assert.True(EquipmentCopyData.TryCreate(source, out var copy));
        var configuration = new Configuration { PinnedOutfitsByCharacter = new() { [1] = [] } };
        var saves = 0;
        var store = new PinnedOutfitStore(configuration, () => saves++, () => 1);
        store.UpdateEquipment(actor, copy);
        Assert.Empty(configuration.PinnedOutfitsByCharacter[1]);
        Assert.Equal(0, saves);

        var pin = legacy ? PinnedOutfitConfiguration.Create(actor, EquipmentDisplayFormatting.CreateHumanOutfit(before)!)
            : PinnedOutfitConfiguration.Create(actor, before);
        configuration.PinnedOutfitsByCharacter[1].Add(pin);
        var identity = pin.IdentityKey();
        store.UpdateEquipment(actor, copy);
        Assert.Single(configuration.PinnedOutfitsByCharacter[1]);
        Assert.Equal(1, saves);
        Assert.Equal(identity, pin.IdentityKey());
        if (legacy)
        {
            Assert.Null(pin.Appearance);
            Assert.True(pin.TryCreateOutfit(out var outfit));
            Assert.Equal<ArmorAppearance>(source.ColoredEquipment, outfit.Equipment);
            Assert.Equal(source.FacewearModelId, outfit.Facewear.ModelId);
            Assert.Equal(source.HatVisible, outfit.HatVisible);
            Assert.Equal(source.VisorToggled, outfit.VisorToggled);
            Assert.Equal(source.VieraEarsHidden, outfit.VieraEarsHidden);
        }
        else
        {
            var retained = Assert.IsType<AppearanceData>(pin.Appearance);
            AssertEquipment(source, retained);
            Assert.Equal<byte>(before.Customize, retained.Customize);
            Assert.Equal(before.ModelCharaId, retained.ModelCharaId);
            Assert.Equal(before.ModelScale, retained.ModelScale);
        }
    }

    private static void AssertEquipment(AppearanceData expected, AppearanceData actual)
    {
        Assert.Equal<ulong>(expected.Equipment, actual.Equipment);
        Assert.Equal<ArmorAppearance>(expected.ColoredEquipment, actual.ColoredEquipment);
        Assert.Equal(expected.Mainhand, actual.Mainhand);
        Assert.Equal(expected.Offhand, actual.Offhand);
        Assert.Equal(expected.MainhandDyes, actual.MainhandDyes);
        Assert.Equal(expected.OffhandDyes, actual.OffhandDyes);
        Assert.Equal(expected.FacewearModelId, actual.FacewearModelId);
        Assert.Equal(expected.HatVisible, actual.HatVisible);
        Assert.Equal(expected.VisorToggled, actual.VisorToggled);
        Assert.Equal(expected.VieraEarsHidden, actual.VieraEarsHidden);
    }

    private static AppearanceData Appearance(ushort firstSet)
    {
        var armor = Enumerable.Range(0, 10).Select(i => new ArmorAppearance((ushort)(firstSet + i),
            (byte)(i + 1), (byte)(i + 11), (byte)(i + 21))
        {
            Color1 = new DyeColor(.13f, .27f, .49f) { Metallic = true },
            Color2 = new DyeColor(.71f, .83f, .97f) { Metallic = false, UseOriginalColor = true },
        }).ToImmutableArray();
        return AppearanceData.Create(0, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
            Enumerable.Range(0, 26).Select(i => (byte)i), armor.Select(ActorRegistry.ToEquipmentModelValue),
            1, 0x2827000300020001UL, 0x3231000600050004UL, true, 15, false) with
        {
            ColoredEquipment = armor,
            MainhandDyes = new(armor[0].Color1, armor[0].Color2),
            OffhandDyes = new(armor[1].Color2, armor[1].Color1), VieraEarsHidden = true,
        };
    }

    private static ActorEntry Actor()
    {
        var key = new LogicalActorKey(2, 2, 2, 100, ObjectKind.Pc, 282);
        var snapshot = new ActorSnapshot(key, new(2, 2, 2, false, 282), "Target", ObjectKind.Pc,
            100, 0, 5, 1, 1, 24, 90, false);
        return new ActorEntry(key, snapshot.Name, ObjectKind.Pc, false, [snapshot]);
    }
}
