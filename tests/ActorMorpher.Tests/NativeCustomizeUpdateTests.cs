using System;
using System.Linq;
using System.Runtime.InteropServices;
using ActorMorpher.Appearance;
using ActorMorpher.Interop;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Xunit;

namespace ActorMorpher.Tests;

// The SDK member-function address is process-wide, so these isolated tests must not
// overlap other tests while temporarily replacing it. No game process is involved.
[CollectionDefinition(nameof(NativeCustomizeUpdateCollection), DisableParallelization = true)]
public sealed class NativeCustomizeUpdateCollection { }

[Collection(nameof(NativeCustomizeUpdateCollection))]
public sealed unsafe class NativeCustomizeUpdateTests
{
    private static readonly byte[] ReceivedCustomize = new byte[26];
    private static int calls;
    private static nint receivedHuman;
    private static byte receivedSkipEquipment;
    private static byte nativeResult;
    private static ConstantBuffer* replacementBuffer;

    [Theory]
    [InlineData(3, 85)] // Height
    [InlineData(21, 67)] // Muscle tone
    [InlineData(23, 92)] // Bust size
    public void SliderUpdatesCurrentHumanOnceAndAppliesRgbToNewNativeBuffer(int index, byte value)
    {
        Human human = default;
        var vtable = stackalloc nint[51];
        new Span<nint>(vtable, 51).Clear();
        vtable[50] = (nint)(delegate* unmanaged<CharacterBase*, CharacterBase.ModelType>)&HumanModelType;
        *(nint**)&human = vtable;
        human.Customize.Data.Fill(1);
        for (var slot = 0; slot < 10; slot++) human.EquipmentModels[slot].Value = (ulong)(0x102030 + slot);
        var pendingEquipment = stackalloc byte[320];
        new Span<byte>(pendingEquipment, 320).Fill(0xA5);
        human.ChangedEquipData = pendingEquipment;

        var oldRgb = stackalloc float[28];
        var newRgb = stackalloc float[28];
        new Span<float>(oldRgb, 28).Fill(0.25f);
        new Span<float>(newRgb, 28).Fill(0.36f);
        ConstantBuffer oldBuffer = new() { ByteSize = 28 * sizeof(float), UnsafeSourcePointer = oldRgb };
        ConstantBuffer newBuffer = new() { ByteSize = 28 * sizeof(float), UnsafeSourcePointer = newRgb };
        human.CustomizeParameterCBuffer = &oldBuffer;
        var originalHuman = new ReadOnlySpan<byte>(&human, sizeof(Human)).ToArray();
        var desiredBytes = Enumerable.Repeat((byte)1, 26).ToArray();
        desiredBytes[index] = value;
        var desired = Desired(desiredBytes) with { CustomizeColors = [new(8, 0.2f, 0.4f, 0.6f)] };

        var originalAddress = Human.Addresses.UpdateDrawData.Value;
        try
        {
            ResetCapture(1, &newBuffer);
            Human.Addresses.UpdateDrawData.Value = (nint)(delegate* unmanaged<Human*, Human.DrawData*, byte, byte>)&UpdateDrawData;

            Assert.True(NativeCustomizeMemory.Apply(&human, desired));

            Assert.Equal(1, calls);
            Assert.Equal((nint)(&human), receivedHuman);
            Assert.Equal((byte)1, receivedSkipEquipment);
            Assert.Equal(desiredBytes, ReceivedCustomize);
            Assert.Equal((nint)(&newBuffer), (nint)human.CustomizeParameterCBuffer);
            Assert.Equal(0.2f * 0.2f, newRgb[0]);
            Assert.Equal(0.4f * 0.4f, newRgb[1]);
            Assert.Equal(0.6f * 0.6f, newRgb[2]);
            Assert.All(new ReadOnlySpan<float>(newRgb + 3, 25).ToArray(), channel => Assert.Equal(0.36f, channel));
            Assert.All(new ReadOnlySpan<float>(oldRgb, 28).ToArray(), channel => Assert.Equal(0.25f, channel));

            // Only the native callback's CBuffer replacement changed this detached
            // Human. Equipment, pending equipment, and all other bytes remain intact.
            // There is no GameObject backing supplied to this direct-update path.
            human.CustomizeParameterCBuffer = &oldBuffer;
            Assert.Equal(originalHuman, new ReadOnlySpan<byte>(&human, sizeof(Human)).ToArray());
            Assert.All(new ReadOnlySpan<byte>(pendingEquipment, 320).ToArray(), item => Assert.Equal((byte)0xA5, item));
        }
        finally
        {
            Human.Addresses.UpdateDrawData.Value = originalAddress;
            replacementBuffer = null;
        }
    }

    [Fact]
    public void NativeFailureReturnsFalseAfterOneCallWithoutRetryOrRgbWrite()
    {
        Human human = default;
        var vtable = stackalloc nint[51];
        new Span<nint>(vtable, 51).Clear();
        vtable[50] = (nint)(delegate* unmanaged<CharacterBase*, CharacterBase.ModelType>)&HumanModelType;
        *(nint**)&human = vtable;
        human.Customize.Data.Fill(1);
        var rgb = stackalloc float[28];
        new Span<float>(rgb, 28).Fill(0.25f);
        ConstantBuffer buffer = new() { ByteSize = 28 * sizeof(float), UnsafeSourcePointer = rgb };
        human.CustomizeParameterCBuffer = &buffer;
        var originalHuman = new ReadOnlySpan<byte>(&human, sizeof(Human)).ToArray();
        var desiredBytes = Enumerable.Repeat((byte)1, 26).ToArray();
        desiredBytes[3] = 85;
        var desired = Desired(desiredBytes) with { CustomizeColors = [new(8, 0.2f, 0.4f, 0.6f)] };
        var originalAddress = Human.Addresses.UpdateDrawData.Value;
        try
        {
            ResetCapture(0, null);
            Human.Addresses.UpdateDrawData.Value = (nint)(delegate* unmanaged<Human*, Human.DrawData*, byte, byte>)&UpdateDrawData;

            Assert.False(NativeCustomizeMemory.Apply(&human, desired));

            Assert.Equal(1, calls);
            Assert.Equal((nint)(&human), receivedHuman);
            Assert.Equal((byte)1, receivedSkipEquipment);
            Assert.Equal(desiredBytes, ReceivedCustomize);
            Assert.Equal(originalHuman, new ReadOnlySpan<byte>(&human, sizeof(Human)).ToArray());
            Assert.All(new ReadOnlySpan<float>(rgb, 28).ToArray(), channel => Assert.Equal(0.25f, channel));
        }
        finally
        {
            Human.Addresses.UpdateDrawData.Value = originalAddress;
            replacementBuffer = null;
        }
    }

    private static AppearanceData Desired(byte[] customize)
        => AppearanceData.Create(0, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
            customize, Enumerable.Repeat(0xFFEEDDUL, 10), mainhand: 123, offhand: 456);

    private static void ResetCapture(byte result, ConstantBuffer* newBuffer)
    {
        calls = 0;
        receivedHuman = 0;
        receivedSkipEquipment = 0;
        Array.Clear(ReceivedCustomize);
        nativeResult = result;
        replacementBuffer = newBuffer;
    }

    // Exercise the SDK's actual generated dispatch, replacing only its native target.
    // byte is the blittable ABI equivalent of the one-byte native bool arguments.
    // Assertions stay outside the unmanaged callback.
    [UnmanagedCallersOnly]
    private static byte UpdateDrawData(Human* human, Human.DrawData* data, byte skipEquipment)
    {
        calls++;
        receivedHuman = (nint)human;
        receivedSkipEquipment = skipEquipment;
        for (var i = 0; i < 26; i++) ReceivedCustomize[i] = data->CustomizeData.Data[i];
        if (nativeResult != 0 && replacementBuffer != null)
            human->CustomizeParameterCBuffer = replacementBuffer;
        return nativeResult;
    }

    [UnmanagedCallersOnly]
    private static CharacterBase.ModelType HumanModelType(CharacterBase* model)
        => CharacterBase.ModelType.Human;
}
