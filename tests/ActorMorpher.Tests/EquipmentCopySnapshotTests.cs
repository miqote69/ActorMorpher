using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using ActorMorpher.Interop;
using Dalamud.Game.ClientState.Objects.Enums;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Xunit;

namespace ActorMorpher.Tests;

public sealed unsafe class EquipmentCopySnapshotTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CopyPublicationSurvivesOppositeBackingHatFlagsAndTheNextCustomizeCapture(bool visible)
    {
        var actor = Actor();
        var persistence = new ActorAppearancePersistence();
        var armor = Enumerable.Range(0, 10).Select(i => new ArmorAppearance((ushort)(100 + i),
            (byte)(i + 1), (byte)(i + 20), (byte)(i + 40))
        {
            Color1 = new DyeColor(.12f, .34f, .56f) { Metallic = true },
        }).ToImmutableArray();
        var outfit = OutfitData.Create(armor, new(true, 0), visible, false);
        var copy = EquipmentCopyData.FromOutfit(outfit);
        var original = outfit with { HatVisible = !visible };
        Plugin.RecordEquipmentCopy(persistence, actor, copy, original);
        Assert.Null(persistence.GetModel(actor.LogicalKey));
        Assert.True(persistence.Outfits.TryGet(actor.LogicalKey, out var retained));

        Character character = default;
        Human human = default;
        var pending = stackalloc byte[10 * 32];
        new Span<byte>(pending, 10 * 32).Clear();
        var table = stackalloc nint[72];
        new Span<nint>(table, 72).Clear();
        table[71] = (nint)(delegate* unmanaged<CharacterBase*, EquipmentModelId*, uint, void>)&ReadSelectedSlot;
        *(nint**)&human = table;
        human.ChangedEquipData = pending;
        // Temporary redraw backing has been restored, opposite to the copied outfit.
        character.DrawData.IsHatHidden = visible;
        for (var slot = 0; slot < 10; slot++)
        {
            var value = slot == 0 && !visible ? 0 : ActorRegistry.ToEquipmentModelValue(armor[slot]);
            human.EquipmentModels[slot].Value = value;
            *(ulong*)(pending + slot * 32) = value;
        }
        var colors = persistence.GetColorOutfit(actor.LogicalKey)!;
        var rawOutfit = NativeOutfitMemory.CaptureEquipped(&character, &human, retained: colors);
        Assert.Equal(!visible, rawOutfit.HatVisible);
        if (!visible) Assert.Equal((ushort)0, rawOutfit.Equipment[0].Set);
        var fresh = AppearanceData.Create(555, ModelCategory.Human, 901, AppearanceCompleteness.Complete,
            Enumerable.Range(0, 26).Select(i => (byte)(200 + i)),
            rawOutfit.Equipment.Select(ActorRegistry.ToEquipmentModelValue), 1.7f, 12, 34,
            rawOutfit.VisorToggled, rawOutfit.Facewear.ModelId, rawOutfit.HatVisible) with
        {
            ColoredEquipment = NativeOutfitMemory.WithColors(rawOutfit, colors).Equipment,
            CustomizeColors = [new(8, .8f, .7f, .6f)],
        };

        // The same descriptor merge is used after Refresh/GPose mapping and direct capture.
        var next = Assert.IsType<AppearanceData>(ActorRegistry.ApplyRetainedOutfit(fresh,
            persistence.GetRetainedOutfit(actor.LogicalKey)));
        Assert.True(EquipmentCopyData.TryCreate(next, out var copiedAgain));
        Assert.Equal(visible, copiedAgain.HatVisible);
        Assert.Equal(ActorRegistry.ToEquipmentModelValue(armor[0]), copiedAgain.Equipment[0]);
        Assert.Equal(armor[0], copiedAgain.ColoredEquipment[0]);

        // Direct height capture first reads native inputs, then restores only the owned descriptor.
        var captured = ActorRegistry.CaptureCustomizeEquipment(fresh, &human, colors);
        captured = ActorRegistry.ApplyRetainedOutfit(captured, persistence.GetRetainedOutfit(actor.LogicalKey))!;
        var desired = new CustomizeLiveRequest(Guid.NewGuid(), actor.LogicalKey, actor.RepresentationKey,
            captured.Customize.SetItem(3, 77), captured.CustomizeColors).CreateAppearance(captured);
        Assert.Equal(visible, desired.HatVisible);
        Assert.Equal(ActorRegistry.ToEquipmentModelValue(armor[0]), desired.Equipment[0]);
        Assert.Equal(armor[0], desired.ColoredEquipment[0]);
        Assert.Equal(fresh.ModelCharaId, next.ModelCharaId);
        Assert.Equal(fresh.SourceRowId, next.SourceRowId);
        Assert.Equal(fresh.ModelScale, next.ModelScale);
        Assert.Equal<byte>(fresh.Customize, next.Customize);
        Assert.Equal<CustomizeColorOverride>(fresh.CustomizeColors, next.CustomizeColors);
        Assert.Equal(fresh.Mainhand, next.Mainhand);
        Assert.Equal(fresh.Offhand, next.Offhand);
        Assert.Null(persistence.GetModel(actor.LogicalKey));
        var partial = Assert.IsType<AppearanceData>(persistence.GetCreateAppearance(actor.LogicalKey, 777, out var outfitOnly));
        Assert.True(outfitOnly);
        Assert.Empty(partial.Customize);
        Assert.Null(partial.ModelScale);
    }

    [Fact]
    public void ColorOwnershipAloneDoesNotRetainHatVisibilityOrSelectedHead()
    {
        var actor = Actor();
        var persistence = new ActorAppearancePersistence();
        var colors = OutfitData.Create(Enumerable.Repeat(new ArmorAppearance(999, 1, 20, 30), 10),
            new(true, 0), false, false);
        persistence.SetColorOutfit(actor.LogicalKey, colors);
        var fresh = AppearanceData.Create(12, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
            Enumerable.Repeat((byte)1, 26), Enumerable.Repeat(0UL, 10), 1.4f, hatVisible: true);
        var retained = persistence.GetRetainedOutfit(actor.LogicalKey);
        var result = ActorRegistry.ApplyRetainedOutfit(fresh, retained);
        Assert.Same(fresh, result);
        Assert.True(result!.HatVisible);
        Assert.Equal(0UL, result.Equipment[0]);
        Assert.Null(persistence.GetModel(actor.LogicalKey));
    }

    [Fact]
    public void FullModelRetentionTakesPriorityOverAnOlderPartialHatDescriptor()
    {
        var actor = Actor();
        var persistence = new ActorAppearancePersistence();
        var oldOutfit = OutfitData.Create(Enumerable.Repeat(new ArmorAppearance(999, 4, 20, 30), 10),
            new(true, 0), false, false);
        persistence.Outfits.SetDesired(actor.LogicalKey, oldOutfit, oldOutfit);
        var fullModel = AppearanceData.Create(555, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
            Enumerable.Repeat((byte)1, 26), Enumerable.Repeat(111UL, 10), 1.4f, 0, 0, false, 0, true);
        persistence.RecordModel(actor, fullModel);
        var nativeFresh = fullModel with
        {
            HatVisible = false,
            Equipment = Enumerable.Range(0, 10).Select(i => (ulong)(222 + i)).ToImmutableArray(),
            Customize = Enumerable.Repeat((byte)2, 26).ToImmutableArray(),
            ModelScale = 1.6f,
        };
        var fresh = actor with { CurrentAppearance = nativeFresh };
        var managed = actor with { CurrentAppearance = fullModel, IsAppearanceManaged = true };
        Assert.Null(persistence.GetRetainedOutfit(actor.LogicalKey));
        Assert.Same(fullModel, persistence.GetCreateAppearance(actor.LogicalKey, 0, out var outfitOnly));
        Assert.False(outfitOnly);

        var refreshed = ActorRegistry.MergeManagedAppearance(fresh, fullModel).CurrentAppearance!;
        var direct = ActorRegistry.DescribeRenderedAppearance(managed, fresh)!;
        foreach (var appearance in new[] { refreshed, direct })
        {
            var result = ActorRegistry.ApplyRetainedOutfit(appearance, persistence.GetRetainedOutfit(actor.LogicalKey));
            Assert.Same(appearance, result);
            Assert.True(result!.HatVisible);
            Assert.Equal<ulong>(nativeFresh.Equipment, result.Equipment);
            Assert.Equal<byte>(nativeFresh.Customize, result.Customize);
            Assert.Equal(nativeFresh.ModelScale, result.ModelScale);
        }
        // Source priority does not remove or rewrite either retained record.
        Assert.True(persistence.Outfits.TryGet(actor.LogicalKey, out var partial));
        Assert.Same(oldOutfit, partial.Desired);
        Assert.Same(fullModel, persistence.GetModel(actor.LogicalKey));
    }

    private static ActorSnapshot Actor()
        => new(new(2, 2, 2, 100, ObjectKind.Pc, 282), new(2, 2, 2, false, 282),
            "Target", ObjectKind.Pc, 100, 0, 5, 1, 1, 24, 90, false);

    // TEST_ONLY virtual slot getter for the existing native input capture consumer.
    [UnmanagedCallersOnly]
    private static void ReadSelectedSlot(CharacterBase* model, EquipmentModelId* value, uint slot)
        => value->Value = *(ulong*)(((Human*)model)->ChangedEquipData + slot * 32);
}
