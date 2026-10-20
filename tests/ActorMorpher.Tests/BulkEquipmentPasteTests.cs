using System;
using System.Linq;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using ActorMorpher.Diagnostics;
using ActorMorpher.Interop;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class BulkEquipmentPasteTests
{
    [Fact]
    public void PasteOnlyEditsSourceThenExplicitBulkApplyUsesTheCopiedOutfit()
    {
        var actor = new ActorSnapshot(new(2, 2, 2, 100, ObjectKind.Pc, 30), new(2, 2, 2, false, 30),
            "Target", ObjectKind.Pc, 100, 0, 5, 1, 1, 24, 90, false);
        var original = OutfitData.Create(new ArmorAppearance[10], new(true, 0), true, false);
        var memory = new Memory(original);
        var resolver = new Resolver(actor);
        var store = new OutfitOverrideStore();
        using var service = new BulkOutfitService(resolver, memory, new Context(), store, NullDiagnosticLog.Instance);
        service.ProcessNextFrame();
        var source = OutfitData.Create(Enumerable.Range(0, 10).Select(i => new ArmorAppearance(
            (ushort)(100 + i), (byte)(i + 1), (byte)(i + 20), (byte)(i + 40))
        {
            Color1 = new DyeColor(.13f, .27f, .49f) { Metallic = true },
            Color2 = new DyeColor(.71f, .83f, .97f) { Metallic = false, UseOriginalColor = true },
        }), new(true, 12), false, true) with { VieraEarsHidden = false };
        source = source with { Equipment = source.Equipment.SetItem(3, new(0, 0, 0, 0)) };
        var clipboard = EquipmentCopyData.FromOutfit(source);
        var oldStatus = service.LastStatus;
        var completions = 0;
        service.ActorOperationCompleted += (_, _, _, _) => completions++;

        service.SetSourceOutfit(clipboard.Outfit);

        Assert.Same(source, service.SourceOutfit);
        Assert.Same(original, memory.Current);
        Assert.Equal(0, memory.Captures);
        Assert.Equal(0, memory.Applies);
        Assert.Equal(0, resolver.Calls);
        Assert.Equal(0, completions);
        Assert.Empty(store.States);
        Assert.Null(service.CurrentOperation);
        Assert.Equal(oldStatus, service.LastStatus);

        Assert.True(service.StartApply([actor.LogicalKey], out _));
        Assert.Equal(0, memory.Applies);
        service.ProcessNextFrame();

        Assert.Equal(1, memory.Applies);
        Assert.Same(source, memory.Current);
        Assert.True(store.TryGet(actor.LogicalKey, out var applied));
        Assert.Same(original, applied.Original);
        Assert.Same(source, applied.Desired);
        Assert.Equal(1, completions);
    }

    private sealed class Resolver(ActorSnapshot actor) : IActorResolver
    {
        public int Calls { get; private set; }
        public bool TryResolve(LogicalActorKey key, out ActorSnapshot snapshot)
        {
            Calls++;
            snapshot = actor;
            return key == actor.LogicalKey;
        }
    }

    private sealed class Memory(OutfitData original) : IOutfitMemory
    {
        public OutfitData Current { get; private set; } = original;
        public int Captures { get; private set; }
        public int Applies { get; private set; }
        public bool TryCapture(ActorSnapshot actor, out OutfitData outfit)
        {
            Captures++;
            outfit = Current;
            return true;
        }
        public bool TryCaptureRendered(ActorSnapshot actor, out OutfitData outfit) => TryCapture(actor, out outfit);
        public bool TryApply(ActorSnapshot actor, OutfitData outfit)
        {
            Applies++;
            Current = outfit;
            return true;
        }
        public bool TryApplyDye(ActorSnapshot actor, OutfitSlot slot, int channel, OutfitData outfit, out bool confirmed)
            => throw new InvalidOperationException("This operation must not invoke a separate dye edit.");
    }

    private sealed class Context : IClientContext
    {
        public uint TerritoryId => 30;
        public bool IsLoggedIn => true;
        public bool IsGPosing => false;
    }
}
