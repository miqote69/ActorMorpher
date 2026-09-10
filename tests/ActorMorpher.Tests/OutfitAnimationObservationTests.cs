using System;
using System.Collections.Generic;
using ActorMorpher.Diagnostics;
using ActorMorpher.Interop;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.Havok.Animation.Playback;
using Xunit;

namespace ActorMorpher.Tests;

public sealed unsafe class OutfitAnimationObservationTests
{
    [Fact]
    public void RenderBindingObservationDistinguishesOwnerAndSlotSkeletonWithoutRebinding()
    {
        CharacterBase owner = default;
        Skeleton ownerSkeleton = default;
        Skeleton renderSkeleton = default;
        Model render = default;
        void* bone = null;
        render.BoneList = &bone;
        render.BoneCount = 17;
        render.Skeleton = &renderSkeleton;
        render.RenderModelCallback = &owner.RenderModelCallback;
        Model** slots = stackalloc Model*[2];
        slots[0] = null;
        slots[1] = &render;
        owner.Models = slots;
        owner.SlotCount = 2;
        owner.Skeleton = &ownerSkeleton;
        var before = new ReadOnlySpan<byte>(&render, sizeof(Model)).ToArray();
        var log = new ObservationLog(FileDiagnosticMode.Full, false);
        NativeOutfitMemory.ObserveAnimation(log, &owner, "BeforeOutfitQueue");
        var records = log.Entry!.Properties;
        Assert.Null(records["renderSlot0.modelId"]);
        Assert.NotEqual(records["skeletonId"], records["renderSlot1.skeletonId"]);
        Assert.Equal(records["renderModelCallbackId"], records["renderSlot1.callbackId"]);
        Assert.Equal(17, records["renderSlot1.boneCount"]);
        Assert.Equal(before, new ReadOnlySpan<byte>(&render, sizeof(Model)).ToArray());
        Assert.True(owner.Skeleton == &ownerSkeleton && render.Skeleton == &renderSkeleton);

        NativeOutfitMemory.ObserveAnimation(log, &owner, "BeforeSlotSetup", 1);
        records = log.Entry!.Properties;
        Assert.False(records.ContainsKey("renderSlot0.modelId"));
        Assert.Equal(1L, records["renderSlot1.index"]);
        NativeOutfitMemory.ObserveAnimation(log, &owner, "BeforeSlotSetup", 2);
        Assert.False(log.Entry!.Properties.ContainsKey("renderSlot2.modelId"));
    }

    [Theory]
    [InlineData(FileDiagnosticMode.Off, false, true)]
    [InlineData(FileDiagnosticMode.Full, false, false)]
    [InlineData(FileDiagnosticMode.Full, false, true)]
    [InlineData(FileDiagnosticMode.Full, true, true)]
    public void ObservationNeverChangesNativeAnimationOrPropagatesLoggingFailure(
        FileDiagnosticMode mode, bool throws, bool hasSkeleton)
    {
        // TEST_ONLY allocated structs; no game calls. A nonzero animation-control count
        // deliberately has no backing array: the observer must not enumerate controls.
        hkaAnimatedSkeleton animation = default;
        animation.AnimationControls.Length = 3;
        PartialSkeleton partial = default;
        partial.HavokAnimatedSkeletons[0] = (ulong)&animation;
        Skeleton skeleton = default;
        skeleton.PartialSkeletonCount = 1;
        skeleton.PartialSkeletons = &partial;
        CharacterBase model = default;
        model.Skeleton = hasSkeleton ? &skeleton : null;
        model.AnimationVariant = 7;
        model.StateFlags = CharacterBase.StateFlag.VisorToggled;
        var beforeModel = new ReadOnlySpan<byte>(&model, sizeof(CharacterBase)).ToArray();
        var beforeSkeleton = new ReadOnlySpan<byte>(&skeleton, sizeof(Skeleton)).ToArray();
        var beforePartial = new ReadOnlySpan<byte>(&partial, sizeof(PartialSkeleton)).ToArray();
        var beforeAnimation = new ReadOnlySpan<byte>(&animation, sizeof(hkaAnimatedSkeleton)).ToArray();
        var log = new ObservationLog(mode, throws);

        NativeOutfitMemory.ObserveAnimation(log, &model, "BeforeOutfitQueue");

        Assert.Equal(beforeModel, new ReadOnlySpan<byte>(&model, sizeof(CharacterBase)).ToArray());
        Assert.Equal(beforeSkeleton, new ReadOnlySpan<byte>(&skeleton, sizeof(Skeleton)).ToArray());
        Assert.Equal(beforePartial, new ReadOnlySpan<byte>(&partial, sizeof(PartialSkeleton)).ToArray());
        Assert.Equal(beforeAnimation, new ReadOnlySpan<byte>(&animation, sizeof(hkaAnimatedSkeleton)).ToArray());
        Assert.Equal(mode == FileDiagnosticMode.Full ? 1 : 0, log.Writes);
        if (mode == FileDiagnosticMode.Full && !throws)
            Assert.Equal(hasSkeleton ? 3 : (object?)null, log.Entry!.Properties["baseControlCount0"]);
    }

    private sealed class ObservationLog(FileDiagnosticMode mode, bool throws) : IDiagnosticLog
    {
        public bool IsEnabled => mode != FileDiagnosticMode.Off;
        public FileDiagnosticMode Mode => mode;
        public string SessionId => "TEST_ONLY";
        public DiagnosticStatus Status => new(null, null, null, 0, 0, 0, null);
        public int Writes;
        public DiagnosticLogEntry? Entry;
        public void Write(DiagnosticLogEntry entry)
        {
            Writes++;
            if (throws) throw new InvalidOperationException("TEST_ONLY logging failure");
            Entry = entry;
        }
        public DiagnosticOperation BeginOperation(DiagnosticCategory category, string eventId,
            string operationName, string? actorKey = null, IReadOnlyDictionary<string, object?>? properties = null,
            string? parentOperationId = null) => throw new NotSupportedException();
        public void Error(string eventId, DiagnosticCategory category, string message,
            Exception? exception = null, IReadOnlyDictionary<string, object?>? properties = null)
            => throw new NotSupportedException();
    }
}
