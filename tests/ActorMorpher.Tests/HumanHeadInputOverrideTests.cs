using System;
using System.Linq;
using System.Runtime.InteropServices;
using ActorMorpher;
using ActorMorpher.Appearance;
using ActorMorpher.Interop;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Xunit;

namespace ActorMorpher.Tests;

public sealed unsafe class HumanHeadInputOverrideTests
{
    [Theory]
    [InlineData(0UL, 1)]
    [InlineData(123456UL, 1)]
    [InlineData(0UL, 0)]
    public void NestedSetterHatReplacementIsOverwrittenInPendingHeadBeforeCreateReturns(ulong head, int result)
    {
        using var f = new NativeInputs();
        var pendingBefore = f.PendingBytes();
        var humanBefore = f.HumanBytes();
        var dataBefore = f.DataBytes();
        var calls = 0;
        Assert.Equal((nint)987, HumanHeadInputOverride.Invoke(Payload(head), () =>
        {
            var actual = HumanHeadInputOverride.Dispatch(f.Human, f.Data, (human, data) =>
            {
                Assert.Equal(f.Human, human);
                Assert.Equal(f.Data, data);
                calls++;
                // The escaped path: a setter inside setup replaces the zero head.
                f.PendingHead = 66202;
                return (byte)result;
            });
            Assert.Equal((byte)result, actual);
            Assert.Equal(head, f.PendingHead);
            Assert.Equal(0xA1u, f.Flags);
            return 987;
        }));
        BitConverter.GetBytes(head).CopyTo(pendingBefore, 0);
        BitConverter.GetBytes(0xA1u).CopyTo(humanBefore, 0xA3C);
        Assert.Equal(pendingBefore, f.PendingBytes());
        Assert.Equal(humanBefore, f.HumanBytes());
        Assert.Equal(dataBefore, f.DataBytes());
        Assert.Equal(1, calls);
    }

    [Fact]
    public void NestedUnrelatedCreateAndRepeatedSetupDoNotAcquireTheHeadOverride()
    {
        using var f = new NativeInputs();
        var calls = 0;
        void Dispatch() => HumanHeadInputOverride.Dispatch(f.Human, f.Data, (_, _) => { calls++; return 1; });
        f.PendingHead = 66202;
        Dispatch();
        Assert.Equal(66202UL, f.PendingHead);
        HumanHeadInputOverride.Invoke(Payload(0), () =>
        {
            HumanHeadInputOverride.Invoke(null, () => { Dispatch(); return 0; });
            Assert.Equal(66202UL, f.PendingHead);
            Dispatch();
            Assert.Equal(0UL, f.PendingHead);
            f.PendingHead = 55;
            Dispatch();
            Assert.Equal(55UL, f.PendingHead);
            return 0;
        });
        Dispatch();
        Assert.Equal(55UL, f.PendingHead);
        Assert.Equal(5, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingHumanEquipmentOrNonHumanWithEquipmentPassesThrough(bool nonHuman)
    {
        using var f = new NativeInputs();
        f.PendingHead = 66202;
        var request = AppearanceData.Create(1, nonHuman ? ModelCategory.Monster : ModelCategory.Human,
            1, AppearanceCompleteness.ModelOnly, Array.Empty<byte>(),
            nonHuman ? new ulong[10] : Array.Empty<ulong>());
        HumanHeadInputOverride.Invoke(request, () =>
        {
            return HumanHeadInputOverride.Dispatch(f.Human, f.Data, (_, _) => 1);
        });
        Assert.Equal(66202UL, f.PendingHead);
        Assert.Equal(0xA0u, f.Flags);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExceptionsClearScopeAndDoNotWriteUnfinishedModel(bool duringSetup)
    {
        using var f = new NativeInputs();
        f.PendingHead = 66202;
        var calls = 0;
        var failure = new InvalidOperationException("native test failure");
        Assert.Same(failure, Assert.Throws<InvalidOperationException>(() =>
            HumanHeadInputOverride.Invoke(Payload(0), () =>
            {
                if (duringSetup)
                    HumanHeadInputOverride.Dispatch(0, f.Data, (_, _) => { calls++; throw failure; });
                throw failure;
            })));
        Assert.Equal(duringSetup ? 1 : 0, calls);
        HumanHeadInputOverride.Dispatch(f.Human, f.Data, (_, _) => 1);
        Assert.Equal(66202UL, f.PendingHead);
        Assert.Equal(0xA0u, f.Flags);
    }

    private static AppearanceData Payload(ulong head)
        => AppearanceData.Create(0, ModelCategory.Human, 1030098, AppearanceCompleteness.Complete,
            new byte[26], Enumerable.Repeat(1UL, 10).Select((value, index) => index == 0 ? head : value));

    private sealed class NativeInputs : IDisposable
    {
        internal nint Human { get; } = (nint)NativeMemory.AllocZeroed((nuint)sizeof(Human));
        internal nint Data { get; } = (nint)NativeMemory.AllocZeroed((nuint)sizeof(Human.DrawData));
        private readonly nint pending = (nint)NativeMemory.AllocZeroed(0x180);
        internal NativeInputs()
        {
            new Span<byte>((void*)pending, 0x180).Fill(42);
            ((Human*)Human)->ChangedEquipData = (byte*)pending;
            Flags = 0xA0;
        }
        internal ulong PendingHead { get => *(ulong*)pending; set => *(ulong*)pending = value; }
        internal uint Flags { get => ((Human*)Human)->SlotNeedsUpdateBitfield; set => ((Human*)Human)->SlotNeedsUpdateBitfield = value; }
        internal byte[] PendingBytes() => new ReadOnlySpan<byte>((void*)pending, 0x180).ToArray();
        internal byte[] HumanBytes() => new ReadOnlySpan<byte>((void*)Human, sizeof(Human)).ToArray();
        internal byte[] DataBytes() => new ReadOnlySpan<byte>((void*)Data, sizeof(Human.DrawData)).ToArray();
        public void Dispose() { NativeMemory.Free((void*)pending); NativeMemory.Free((void*)Data); NativeMemory.Free((void*)Human); }
    }
}
