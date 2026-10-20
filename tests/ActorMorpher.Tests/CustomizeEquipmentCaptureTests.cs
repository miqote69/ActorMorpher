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

public sealed unsafe class CustomizeEquipmentCaptureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BodyEditsKeepTheSelectedWeaponsWhileTheirModelsAreStillLoading(bool oldModelLoaded)
    {
        Character character = default;
        Weapon main = default;
        Weapon off = default;
        Weapon.ChangedWeaponData pendingMain = new() { SecondaryId = 1, Variant = 1, Stain0 = 27, Stain1 = 34 };
        Weapon.ChangedWeaponData pendingOff = new() { SecondaryId = 53, Variant = 1, Stain0 = 41, Stain1 = 52 };
        main.ModelSetId = 2909;
        off.ModelSetId = 2951;
        if (oldModelLoaded)
        {
            main.SecondaryId = off.SecondaryId = 99;
            main.Variant = off.Variant = 9;
            main.Stain0 = off.Stain0 = 8;
            main.Stain1 = off.Stain1 = 7;
        }
        main.ChangedData = &pendingMain;
        off.ChangedData = &pendingOff;
        character.DrawData.Weapon(DrawDataContainer.WeaponSlot.MainHand).Weapon = &main;
        character.DrawData.Weapon(DrawDataContainer.WeaponSlot.OffHand).Weapon = &off;
        // Restored game backing is deliberately different from the displayed weapons.
        character.DrawData.Weapon(DrawDataContainer.WeaponSlot.MainHand).ModelId.Value = 123;
        character.DrawData.Weapon(DrawDataContainer.WeaponSlot.OffHand).ModelId.Value = 456;
        var expectedMain = new WeaponModelId { Id = 2909, Type = 1, Variant = 1, Stain0 = 27, Stain1 = 34 }.Value;
        var expectedOff = new WeaponModelId { Id = 2951, Type = 53, Variant = 1, Stain0 = 41, Stain1 = 52 }.Value;
        byte[] customize = [5, 1, 1, 50, 10, 2, 186, 128, 169, 81, 197, 198, 18, 0, 1, 81, 1, 5, 2, 131, 54, 100, 0, 100, 1, 135];
        var actor = new ActorSnapshot(new(2, 2, 2, 1, ObjectKind.Pc, 282), new(2, 2, 2, false, 282),
            "Actor", ObjectKind.Pc, 1, 0, 5, 1, 1, 0, 0, true);
        var draft = new CustomizeEditState(actor, customize.ToImmutableArray());

        foreach (var change in new[] { (Index: 3, Value: (byte)77), (Index: 23, Value: (byte)66) })
        {
            var current = AppearanceData.Create(0, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
                customize, Enumerable.Repeat(123UL, 10), 1,
                NativeAppearanceMemory.CaptureRenderedWeapon(&character, DrawDataContainer.WeaponSlot.MainHand),
                NativeAppearanceMemory.CaptureRenderedWeapon(&character, DrawDataContainer.WeaponSlot.OffHand));
            draft.SetValue(change.Index, change.Value);
            var request = new CustomizeLiveRequest(Guid.NewGuid(), draft.Actor, draft.Representation,
                draft.ToCustomize(), draft.ToColorOverrides());
            var desired = request.CreateAppearance(current);

            Assert.Equal(expectedMain, desired.Mainhand);
            Assert.Equal(expectedOff, desired.Offhand);
            NativeDrawObjectInjector.SubstituteBacking(&character, desired);
            Assert.Equal(expectedMain, character.DrawData.Weapon(DrawDataContainer.WeaponSlot.MainHand).ModelId.Value);
            Assert.Equal(expectedOff, character.DrawData.Weapon(DrawDataContainer.WeaponSlot.OffHand).ModelId.Value);
            Assert.Equal(change.Value, desired.Customize[change.Index]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WeaponCapturePreservesExplicitZeroFieldsInsteadOfUsingGameBacking(bool pending)
    {
        Character character = default;
        Weapon weapon = default;
        weapon.ModelSetId = 2909;
        Weapon.ChangedWeaponData selected = default;
        if (pending)
        {
            weapon.SecondaryId = 53;
            weapon.Variant = 3;
            weapon.Stain0 = 4;
            weapon.Stain1 = 5;
            weapon.ChangedData = &selected;
        }
        character.DrawData.Weapon(DrawDataContainer.WeaponSlot.MainHand).Weapon = &weapon;
        character.DrawData.Weapon(DrawDataContainer.WeaponSlot.MainHand).ModelId.Value = 0x01020300040005UL;

        Assert.Equal(2909UL, NativeAppearanceMemory.CaptureRenderedWeapon(&character, DrawDataContainer.WeaponSlot.MainHand));
    }

    [Fact]
    public void WeaponCaptureUsesLoadedFieldsWhenThereIsNoPendingChange()
    {
        Character character = default;
        Weapon weapon = default;
        var expected = new WeaponModelId { Id = 2951, Type = 53, Variant = 1, Stain0 = 41, Stain1 = 52 };
        weapon.ModelSetId = expected.Id;
        weapon.SecondaryId = expected.Type;
        weapon.Variant = expected.Variant;
        weapon.Stain0 = expected.Stain0;
        weapon.Stain1 = expected.Stain1;
        character.DrawData.Weapon(DrawDataContainer.WeaponSlot.OffHand).Weapon = &weapon;

        Assert.Equal(expected.Value, NativeAppearanceMemory.CaptureRenderedWeapon(&character, DrawDataContainer.WeaponSlot.OffHand));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsecutiveHeightRedrawsKeepSelectedEquipmentWhileLoadedSlotsAreEmpty(bool hiddenHead)
    {
        Character character = default;
        Human human = default;
        var pending = stackalloc byte[10 * 32];
        var table = stackalloc nint[72];
        new Span<nint>(table, 72).Clear();
        table[71] = (nint)(delegate* unmanaged<CharacterBase*, EquipmentModelId*, uint, void>)&ReadSelectedSlot;
        *(nint**)&human = table;
        human.ChangedEquipData = pending;
        // The escaped log had body input populated but only the head update bit set.
        human.SlotNeedsUpdateBitfield = 0x30001;
        // Managed hat visibility can differ from the game's restored backing flag.
        character.DrawData.IsHatHidden = false;
        var equipment = Enumerable.Range(0, 10).Select(slot => new EquipmentModelId
        {
            Id = (ushort)(100 + slot), Variant = (byte)(slot + 1),
            Stain0 = (byte)(20 + slot), Stain1 = (byte)(40 + slot),
        }.Value).ToArray();
        for (var slot = 0; slot < 10; slot++)
        {
            *(ulong*)(pending + slot * 32) = hiddenHead && slot == 0 ? 0 : equipment[slot];
            character.DrawData.EquipmentModelIds[slot].Value = (ulong)(900 + slot);
        }
        var ownedColors = OutfitData.Create(equipment.Select(value => new EquipmentModelId { Value = value })
            .Select(model => new ArmorAppearance(model.Id, model.Variant, model.Stain0, model.Stain1)
                { Color1 = new DyeColor(.123f, .456f, .789f) }), new(true, 12), !hiddenHead, true);
        byte[] customize = [5, 1, 1, 50, 10, 101, 5, 1, 1, 54, 181, 0, 0, 0, 2, 54, 2, 2, 1, 2, 72, 50, 0, 100, 4, 55];
        var actor = new ActorSnapshot(new(2, 2, 2, 1, ObjectKind.EventNpc, 282), new(2, 2, 2, false, 282),
            "Actor", ObjectKind.EventNpc, 1, 0, 5, 1, 1, 0, 0, false);
        var draft = new CustomizeEditState(actor, customize.ToImmutableArray());
        var customizeInput = stackalloc byte[26];
        var equipmentInput = stackalloc ulong[10];
        new Span<byte>(customizeInput, 26).Clear();
        new Span<ulong>(equipmentInput, 10).Clear();
        var originalBacking = character.DrawData.EquipmentModelIds.ToArray().Select(model => model.Value).ToArray();
        foreach (var height in new byte[] { 77, 97, 100 })
        {
            // This is the registry's existing rendered capture source: after native Create,
            // loaded slots are empty even though selected slot inputs still hold the outfit.
            var loaded = NativeOutfitMemory.CaptureEquipped(&character, &human, retained: ownedColors);
            Assert.All(loaded.Equipment.Skip(1), armor => Assert.Equal((ushort)0, armor.Set));
            var captured = AppearanceData.Create(0, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
                customize, loaded.Equipment.Select(ActorRegistry.ToEquipmentModelValue),
                1.25f, 0x03020100010002UL, 0x06050400020003UL, true, 12, !hiddenHead) with
            {
                MainhandDyes = new(new DyeColor(1, 0, 0), new DyeColor(0, 1, 0)),
                OffhandDyes = new(null, new DyeColor(0, 0, 1)), VieraEarsHidden = true,
            };
            var current = ActorRegistry.CaptureCustomizeEquipment(captured, &human, ownedColors);
            draft.SetValue(3, height);
            var request = new CustomizeLiveRequest(Guid.NewGuid(), draft.Actor, draft.Representation,
                draft.ToCustomize(), draft.ToColorOverrides());
            var desired = request.CreateAppearance(current);
            NativeDrawObjectInjector.SubstituteBacking(&character, desired);
            Assert.Equal(originalBacking, character.DrawData.EquipmentModelIds.ToArray().Select(model => model.Value));
            Assert.Equal(captured.Mainhand, character.DrawData.Weapon(DrawDataContainer.WeaponSlot.MainHand).ModelId.Value);
            Assert.Equal(captured.Offhand, character.DrawData.Weapon(DrawDataContainer.WeaponSlot.OffHand).ModelId.Value);
            Assert.Equal(hiddenHead, character.DrawData.IsHatHidden);
            Assert.True(character.DrawData.IsVisorToggled);
            Assert.Equal((ushort)12, character.DrawData.GlassesIds[0]);
            NativeDrawObjectInjector.InvokeWithTemporaryCreateBuffers(desired, (nint)customizeInput, (nint)equipmentInput,
                (bytes, slots) =>
                {
                    Assert.Equal(height, ((byte*)bytes)[3]);
                    Assert.Equal(equipment, new ReadOnlySpan<ulong>((void*)slots, 10).ToArray());
                    Assert.Equal<ArmorAppearance>(ownedColors.Equipment, desired.ColoredEquipment);
                    Assert.Equal(captured.MainhandDyes, desired.MainhandDyes);
                    Assert.Equal(captured.OffhandDyes, desired.OffhandDyes);
                    Assert.Equal(captured.ModelScale, desired.ModelScale);
                    Assert.Equal(captured.VieraEarsHidden, desired.VieraEarsHidden);
                    return (nint)1;
                });
        }
    }

    [Fact]
    public void AnExplicitlyEmptyNativeSlotIsNotReplacedWithLoadedOrRetainedEquipment()
    {
        Human human = default;
        var pending = stackalloc byte[10 * 32];
        new Span<byte>(pending, 10 * 32).Clear();
        var table = stackalloc nint[72];
        new Span<nint>(table, 72).Clear();
        table[71] = (nint)(delegate* unmanaged<CharacterBase*, EquipmentModelId*, uint, void>)&ReadSelectedSlot;
        *(nint**)&human = table;
        human.ChangedEquipData = pending;
        for (var slot = 0; slot < 10; slot++) human.EquipmentModels[slot].Id = 123;
        var oldOutfit = OutfitData.Create(Enumerable.Repeat(new ArmorAppearance(123, 0, 0, 0)
            { Color1 = new DyeColor(1, 0, 0) }, 10), new(true, 0), true, false);
        var rendered = AppearanceData.Create(0, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
            Enumerable.Repeat((byte)1, 26), Enumerable.Repeat(123UL, 10), hatVisible: true);
        var current = ActorRegistry.CaptureCustomizeEquipment(rendered, &human, oldOutfit);
        Assert.All(current.Equipment, value => Assert.Equal(0UL, value));
        Assert.All(current.ColoredEquipment, armor =>
        {
            Assert.Equal((ushort)0, armor.Set);
            Assert.Null(armor.Color1);
        });
    }

    [Fact]
    public void ClearingADyeUpdatesTheNativeInputCapturedForHeightRedraw()
    {
        Human human = default;
        var pending = stackalloc byte[10 * 32];
        new Span<byte>(pending, 10 * 32).Clear();
        var table = stackalloc nint[72];
        new Span<nint>(table, 72).Clear();
        table[71] = (nint)(delegate* unmanaged<CharacterBase*, EquipmentModelId*, uint, void>)&ReadSelectedSlot;
        *(nint**)&human = table;
        human.ChangedEquipData = pending;
        var before = new EquipmentModelId { Id = 123, Variant = 4, Stain0 = 20, Stain1 = 30 };
        human.EquipmentModels[1] = before;
        *(EquipmentModelId*)(pending + 32) = before;
        NativeOutfitMemory.ApplyRenderedStains(&human, 1, new(123, 4, 0, 30));
        Assert.Equal((byte)0, human.EquipmentModels[1].Stain0);
        var rendered = AppearanceData.Create(0, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
            Enumerable.Repeat((byte)1, 26), human.EquipmentModels.ToArray().Select(model => model.Value), hatVisible: true);
        var current = ActorRegistry.CaptureCustomizeEquipment(rendered, &human, null);
        var input = new EquipmentModelId { Value = current.Equipment[1] };
        Assert.Equal((ushort)123, input.Id);
        Assert.Equal((byte)4, input.Variant);
        Assert.Equal((byte)0, input.Stain0);
        Assert.Equal((byte)30, input.Stain1);
    }

    [Fact]
    public void DyeOnLoadedEquipmentDoesNotChangeADifferentPendingModel()
    {
        Human human = default;
        var pending = stackalloc byte[10 * 32];
        new Span<byte>(pending, 10 * 32).Clear();
        human.ChangedEquipData = pending;
        human.EquipmentModels[1] = new EquipmentModelId { Id = 123, Variant = 4, Stain0 = 20, Stain1 = 30 };
        var selected = new EquipmentModelId { Id = 456, Variant = 7, Stain0 = 50, Stain1 = 60 };
        *(EquipmentModelId*)(pending + 32) = selected;

        NativeOutfitMemory.ApplyRenderedStains(&human, 1, new(123, 4, 0, 30));

        Assert.Equal((byte)0, human.EquipmentModels[1].Stain0);
        Assert.Equal(selected.Value, ((EquipmentModelId*)(pending + 32))->Value);
    }

    [Fact]
    public void DyeOnLoadedEquipmentDoesNotRequirePendingStorage()
    {
        Human human = default;
        human.EquipmentModels[1] = new EquipmentModelId { Id = 123, Variant = 4, Stain0 = 20, Stain1 = 30 };

        NativeOutfitMemory.ApplyRenderedStains(&human, 1, new(123, 4, 0, 30));

        Assert.Equal((ushort)123, human.EquipmentModels[1].Id);
        Assert.Equal((byte)4, human.EquipmentModels[1].Variant);
        Assert.Equal((byte)0, human.EquipmentModels[1].Stain0);
        Assert.Equal((byte)30, human.EquipmentModels[1].Stain1);
        Assert.True(human.ChangedEquipData == null);
    }

    // TEST_ONLY native slot getter: the same vtable consumer the production capture calls.
    // It models the input/loaded split observed in the existing game log, not game rendering.
    [UnmanagedCallersOnly]
    private static void ReadSelectedSlot(CharacterBase* model, EquipmentModelId* value, uint slot)
        => value->Value = *(ulong*)(((Human*)model)->ChangedEquipData + slot * 32);
}
