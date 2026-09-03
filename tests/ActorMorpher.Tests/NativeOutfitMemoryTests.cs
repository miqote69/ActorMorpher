using System;
using System.Linq;
using System.Runtime.InteropServices;
using ActorMorpher.BulkOutfit;
using ActorMorpher.Interop;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Xunit;

namespace ActorMorpher.Tests;

public sealed unsafe class NativeOutfitMemoryTests
{
    [Theory]
    [InlineData(900, 3, true, 17)]
    [InlineData(900, 4, true, 18)]
    [InlineData(0, 0, true, 0)]
    [InlineData(901, 3, false, 0)]
    [InlineData(900, 5, false, 0)]
    public void RenderedFacewearResolvesModelAndVariantToSheetRow(
        ushort model, byte variant, bool available, ushort rowId)
    {
        // TEST_ONLY native buffers. Backing row deliberately differs from rendered appearance.
        Character character = default;
        Human human = default;
        character.DrawData.GlassesIds[0] = 23;
        human.Glasses0 = new EquipmentModelId { Id = model, Variant = variant };
        var lookup = new FacewearModelLookup(new (ushort, ushort, byte)[] { (17, 900, 3), (18, 900, 4) });
        var outfit = NativeOutfitMemory.CaptureRendered(&character, &human, lookup.Resolve);
        Assert.Equal(new FacewearAppearance(available, rowId), outfit.Facewear);
        Assert.Equal((ushort)23, character.DrawData.GlassesIds[0]);
        Assert.Equal(model, human.Glasses0.Id);
        Assert.Equal(variant, human.Glasses0.Variant);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ArmorApplyAndRestoreQueueRenderedSlotsWithoutChangingBacking(bool nativeReturnsFalse)
    {
        Character character = default;
        Human human = default;
        var table = stackalloc nint[72];
        new Span<nint>(table, 72).Clear();
        table[69] = (nint)(delegate* unmanaged<CharacterBase*, uint, EquipmentModelId*, byte>)&QueueEquipment;
        *(nint**)&human = table;
        // TEST_ONLY queue: value, call count, return flag. No FF14 functions execute.
        var pending = stackalloc byte[10 * 32];
        new Span<byte>(pending, 10 * 32).Clear();
        human.ChangedEquipData = pending;
        var original = OutfitData.Create(Enumerable.Range(0, 10)
            .Select(i => new ArmorAppearance((ushort)(9160 + i), 1, 0, 0)),
            new FacewearAppearance(true, 0), true, false);
        for (var i = 0; i < 10; ++i)
        {
            var armor = original.Equipment[i];
            character.DrawData.EquipmentModelIds[i] = Model(armor);
            human.EquipmentModels[i] = Model(armor);
            pending[i * 32 + 16] = nativeReturnsFalse ? (byte)1 : (byte)0;
        }
        var backing = character.DrawData.EquipmentModelIds.ToArray();
        var desired = OutfitData.Create(Enumerable.Range(0, 10)
            .Select(i => i % 2 == 0 ? default : new ArmorAppearance(51, 2, (byte)i, 7)),
            new FacewearAppearance(true, 0), true, false);
        var second = desired with { Equipment = original.Equipment };

        foreach (var outfit in new[] { desired, second, desired, original })
        {
            var before = human.EquipmentModels.ToArray();
            NativeOutfitMemory.ApplyRenderedEquipment(&human, outfit);
            Assert.Equal(backing, character.DrawData.EquipmentModelIds.ToArray());
            for (var i = 0; i < 10; ++i)
            {
                var model = Model(outfit.Equipment[i]);
                var expectedCalls = before[i].Value == model.Value ? 0 : 1;
                Assert.Equal(expectedCalls, *(int*)(pending + i * 32 + 8));
                if (expectedCalls != 0)
                    Assert.Equal(model.Value, *(ulong*)(pending + i * 32));
                // TEST_ONLY consume the queue; runtime loading remains separately unverified.
                human.EquipmentModels[i] = model;
                *(int*)(pending + i * 32 + 8) = 0;
            }
        }
        NativeOutfitMemory.ApplyRenderedEquipment(&human, original);
        for (var i = 0; i < 10; ++i)
            Assert.Equal(0, *(int*)(pending + i * 32 + 8));
        Assert.Equal(backing, character.DrawData.EquipmentModelIds.ToArray());
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 7)]
    [InlineData(true, 7)]
    public void HeadSelectionKeepsRequestedPendingValueAfterSetterSubstitution(bool nativeReturnsFalse, byte stain)
    {
        // TEST_ONLY native buffers and setter; no FF14 execution.
        Character character = default;
        Human human = default;
        var table = stackalloc nint[72];
        new Span<nint>(table, 72).Clear();
        table[69] = (nint)(delegate* unmanaged<CharacterBase*, uint, EquipmentModelId*, byte>)&SubstituteHead;
        *(nint**)&human = table;
        var pending = stackalloc byte[10 * 32];
        new Span<byte>(pending, 10 * 32).Clear();
        human.ChangedEquipData = pending;
        pending[16] = nativeReturnsFalse ? (byte)1 : (byte)0;
        human.SlotNeedsUpdateBitfield = 1u << 4;
        var original = OutfitData.Create(Enumerable.Range(0, 10)
            .Select(i => new ArmorAppearance((ushort)(891 + i), 1, stain, stain)),
            new FacewearAppearance(true, 17), true, false);
        for (var i = 0; i < 10; ++i)
        {
            character.DrawData.EquipmentModelIds[i] = Model(original.Equipment[i]);
            human.EquipmentModels[i] = Model(original.Equipment[i]);
            *(ulong*)(pending + i * 32) = Model(original.Equipment[i]).Value;
        }
        var backing = character.DrawData.EquipmentModelIds.ToArray();
        foreach (var head in new[] { new EquipmentChoiceKey(0, 0, 0), new(0, 6023, 1), new(0, 891, 1) })
        {
            var desired = EquipmentChoice.Replace(original, head);
            NativeOutfitMemory.ApplyRenderedEquipment(&human, desired);
            Assert.Equal(Model(desired.Equipment[0]).Value, *(ulong*)pending);
            Assert.Equal(1, *(int*)(pending + 8));
            Assert.Equal((1u << 4) | 1u, human.SlotNeedsUpdateBitfield);
            Assert.Equal(backing, character.DrawData.EquipmentModelIds.ToArray());
            for (var i = 1; i < 10; ++i)
            {
                Assert.Equal(Model(original.Equipment[i]).Value, *(ulong*)(pending + i * 32));
                Assert.Equal(0, *(int*)(pending + i * 32 + 8));
            }
            human.EquipmentModels[0] = Model(desired.Equipment[0]);
            *(int*)(pending + 8) = 0;
            human.SlotNeedsUpdateBitfield = 1u << 4;
            NativeOutfitMemory.ApplyRenderedEquipment(&human, desired);
            Assert.Equal(0, *(int*)(pending + 8));
            Assert.Equal(1u << 4, human.SlotNeedsUpdateBitfield);
        }
    }

    [UnmanagedCallersOnly]
    private static byte SubstituteHead(CharacterBase* draw, uint slot, EquipmentModelId* model)
    {
        var entry = ((Human*)draw)->ChangedEquipData + slot * 32;
        model->Value = 0xFFFF;
        *(ulong*)entry = model->Value;
        ++*(int*)(entry + 8);
        return entry[16] != 0 ? (byte)0 : (byte)1;
    }

    private static EquipmentModelId Model(ArmorAppearance armor)
        => new() { Id = armor.Set, Variant = armor.Variant, Stain0 = armor.Stain1, Stain1 = armor.Stain2 };

    [UnmanagedCallersOnly]
    private static byte QueueEquipment(CharacterBase* draw, uint slot, EquipmentModelId* model)
    {
        var entry = ((Human*)draw)->ChangedEquipData + slot * 32;
        *(ulong*)entry = model->Value;
        ++*(int*)(entry + 8);
        return entry[16] != 0 ? (byte)0 : (byte)1;
    }
}
