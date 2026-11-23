using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using ActorMorpher.Diagnostics;
using ActorMorpher.Interop;
using ActorMorpher.Redraw;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class CustomizeModelApplyTests
{
    [Theory]
    [InlineData(3)]
    [InlineData(23)]
    public void ModelSearchCompletionRebasesDraftBeforeHeightOrBustEdit(int index)
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Plugin.TryApplyModel(fixture.Actor.LogicalKey, fixture.Model, out var operation, out _));
        Assert.Contains(operation, Get<HashSet<Guid>>(fixture.Plugin, "modelSearchOperations"));
        fixture.Finish();
        Assert.NotNull(Get<CustomizeEditState>(fixture.Window, "customizeDraft"));
        Assert.DoesNotContain(operation, Get<HashSet<Guid>>(fixture.Plugin, "modelSearchOperations"));
        var published = fixture.Registry.Entries.Single().Current;
        Assert.Equal(fixture.Desired, published.CurrentAppearance);
        // Native begin-edit capture is outside this isolated test. Exercise the same rendered
        // descriptor and draft/request types using the backend's applied bytes.
        var fresh = ActorRegistry.DescribeRenderedAppearance(published,
            fixture.Actor with { CurrentAppearance = fixture.Backend.Rendered })!;
        var draft = Get<CustomizeEditState>(fixture.Window, "customizeDraft");
        draft.SetValue(index, 77);
        var request = new CustomizeLiveRequest(Guid.NewGuid(), draft.Actor, draft.Representation,
            draft.ToCustomize(), draft.ToColorOverrides());
        var next = request.CreateAppearance(fresh);
        Assert.Equal<byte>(fixture.Desired.Customize.SetItem(index, 77), next.Customize);
        Assert.Equal<CustomizeColorOverride>(fixture.Desired.CustomizeColors, next.CustomizeColors);
        Assert.Equal(fixture.Desired, next with
        {
            Customize = fixture.Desired.Customize,
            CustomizeColors = fixture.Desired.CustomizeColors,
        });
        draft.Reset();
        Assert.Equal<byte>(fixture.Desired.Customize, draft.ToCustomize());
        Assert.Equal<CustomizeColorOverride>(fixture.Desired.CustomizeColors, draft.ToColorOverrides());
        Assert.Equal(1, fixture.Backend.EnableCount);
    }

    [Theory]
    [InlineData("failure")]
    [InlineData("other-actor")]
    [InlineData("other-representation")]
    [InlineData("customize-only")]
    public void CompletionOutsideSuccessfulModelApplyToDraftDoesNotInvalidate(string scenario)
    {
        using var fixture = new Fixture();
        var draftActor = scenario switch
        {
            "other-actor" => fixture.Actor with { LogicalKey = fixture.Actor.LogicalKey with { BaseId = 999 } },
            "other-representation" => fixture.Actor with { RepresentationKey = fixture.Actor.RepresentationKey with { ObjectIndex = 201 } },
            _ => fixture.Actor,
        };
        var draft = new CustomizeEditState(draftActor, fixture.Actor.CurrentAppearance!.Customize);
        Set(fixture.Window, "customizeDraft", draft);
        if (scenario == "customize-only")
        {
            Assert.True(fixture.Service.TryApply(fixture.Actor, fixture.Desired, out var operation, out _));
            Get<Dictionary<Guid, Guid>>(fixture.Plugin, "customizeOperations").Add(operation, Guid.NewGuid());
        }
        else
        {
            fixture.Backend.Succeed = scenario != "failure";
            Assert.True(fixture.Plugin.TryApplyModel(fixture.Actor.LogicalKey, fixture.Model, out _, out _));
        }
        fixture.Finish();
        Assert.Same(draft, Get<CustomizeEditState>(fixture.Window, "customizeDraft"));
        Assert.Equal("previous status", Get<string>(fixture.Window, "customizeStatus"));
    }

    [Fact]
    public void EditsQueuedDuringModelApplyKeepNewNpcAndBothExplicitChanges()
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Plugin.TryApplyModel(fixture.Actor.LogicalKey, fixture.Model, out _, out _));
        var oldDraft = Get<CustomizeEditState>(fixture.Window, "customizeDraft");
        oldDraft.BeginFieldChanges();
        oldDraft.SetValue(3, 60);
        Assert.True(fixture.Plugin.TryApplyCustomize(fixture.Actor, oldDraft, Guid.NewGuid(), out _, oldDraft.GetFieldChangeMasks(), oldDraft.ColorChangeMask));
        oldDraft.BeginFieldChanges();
        oldDraft.SetValue(23, 70);
        Assert.True(fixture.Plugin.TryApplyCustomize(fixture.Actor, oldDraft, Guid.NewGuid(), out _, oldDraft.GetFieldChangeMasks(), oldDraft.ColorChangeMask));
        var previousOperation = Get<object?>(fixture.Window, "customizeOperation");
        fixture.Finish();
        var published = fixture.Registry.Entries.Single().Current.CurrentAppearance!;
        var queued = Get<CustomizeLiveApplyQueue>(fixture.Plugin, "pendingCustomizeEdits")
            .GetLatest(fixture.Actor.LogicalKey, fixture.Actor.RepresentationKey)!;
        var expected = fixture.Desired.Customize.SetItem(3, 60).SetItem(23, 70);
        var desired = queued.CreateAppearance(published);
        Assert.Equal<byte>(expected, desired.Customize);
        Assert.Equal<CustomizeColorOverride>(fixture.Desired.CustomizeColors, desired.CustomizeColors);
        Assert.Equal(fixture.Desired, desired with
        { Customize = fixture.Desired.Customize, CustomizeColors = fixture.Desired.CustomizeColors });
        var rebased = Get<CustomizeEditState>(fixture.Window, "customizeDraft");
        Assert.NotSame(oldDraft, rebased);
        Assert.Equal<byte>(fixture.Desired.Customize, rebased.Original);
        Assert.Equal<byte>(expected, rebased.ToCustomize());
        Assert.Equal<CustomizeColorOverride>(fixture.Desired.CustomizeColors, rebased.ToColorOverrides());
        var persistence = Get<ActorAppearancePersistence>(fixture.Plugin, "appearancePersistence");
        var reopened = persistence.CreateCustomizeDraft(fixture.Actor, desired);
        reopened.Reset();
        Assert.Equal<byte>(fixture.Desired.Customize, reopened.ToCustomize());
        Assert.Equal<CustomizeColorOverride>(fixture.Desired.CustomizeColors, reopened.ToColorOverrides());
        Assert.Equal(previousOperation, Get<object?>(fixture.Window, "customizeOperation"));
        Assert.Equal("previous status", Get<string>(fixture.Window, "customizeStatus"));
        Assert.Equal(1, fixture.Backend.EnableCount);
    }
    [Fact]
    public void QueuedPackedBitEditKeepsAppliedNpcUneditedBits()
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Plugin.TryApplyModel(fixture.Actor.LogicalKey, fixture.Model, out _, out _));
        var draft = Get<CustomizeEditState>(fixture.Window, "customizeDraft");
        draft.BeginFieldChanges();
        draft.SetValue(16, 0x80, 0x80);
        Assert.True(fixture.Plugin.TryApplyCustomize(fixture.Actor, draft, Guid.NewGuid(), out _, draft.GetFieldChangeMasks(), draft.ColorChangeMask));
        fixture.Finish();
        var pending = Get<CustomizeLiveApplyQueue>(fixture.Plugin, "pendingCustomizeEdits")
            .GetLatest(fixture.Actor.LogicalKey, fixture.Actor.RepresentationKey)!;
        var expected = fixture.Desired.Customize.SetItem(16, (byte)(fixture.Desired.Customize[16] | 0x80));
        Assert.Equal<byte>(expected, pending.CreateAppearance(fixture.Desired).Customize);
        Assert.Equal<byte>(expected, Get<CustomizeEditState>(fixture.Window, "customizeDraft").ToCustomize());
    }

    [Fact]
    public void PaletteClickDuringModelApplyRemovesOnlyItsRgbAndKeepsClickedPalette()
    {
        using var fixture = new Fixture();
        var original = fixture.Actor.CurrentAppearance!;
        var draft = new CustomizeEditState(fixture.Actor, original.Customize,
            [new CustomizeColorOverride(8, .9f, .8f, .7f)]);
        draft.BeginFieldChanges();
        draft.SetValue(8, original.Customize[8]); // Same palette byte, but an effective RGB removal.
        Assert.Equal((byte)0xff, draft.GetFieldChangeMasks()[8]);
        Assert.Equal(1u << 8, draft.ColorChangeMask);
        var applied = fixture.Desired with
        { CustomizeColors = [new CustomizeColorOverride(8, .4f, .5f, .6f), fixture.Desired.CustomizeColors[0]] };
        var request = new CustomizeLiveRequest(Guid.NewGuid(), draft.Actor, draft.Representation,
            draft.ToCustomize(), draft.ToColorOverrides(), draft.GetFieldChangeMasks(), draft.ColorChangeMask);
        var desired = request.CreateAppearance(applied);
        Assert.Equal<byte>(applied.Customize.SetItem(8, original.Customize[8]), desired.Customize);
        Assert.Equal<CustomizeColorOverride>(fixture.Desired.CustomizeColors, desired.CustomizeColors);
        Assert.Equal(applied, desired with { Customize = applied.Customize, CustomizeColors = applied.CustomizeColors });
    }
    [Fact]
    public void FreshDraftDuringPendingModelKeepsPreviousEditsAndLatestExplicitEditsWin()
    {
        using var fixture = new Fixture();
        Assert.True(fixture.Plugin.TryApplyModel(fixture.Actor.LogicalKey, fixture.Model, out _, out _));
        var original = fixture.Actor.CurrentAppearance!;
        var first = new CustomizeEditState(fixture.Actor, original.Customize);
        first.BeginFieldChanges();
        first.SetValue(3, 60);
        first.SetValue(16, 0x80, 0x80);
        first.SetColor(8, new(.2f, .3f, .4f));
        first.SetColor(10, new(.3f, .4f, .5f));
        Assert.True(fixture.Plugin.TryApplyCustomize(fixture.Actor, first, Guid.NewGuid(), out _, first.GetFieldChangeMasks(), first.ColorChangeMask));
        // Returning to this actor while Apply is pending creates a different old baseline.
        var second = new CustomizeEditState(fixture.Actor, original.Customize);
        second.BeginFieldChanges();
        second.SetValue(23, 70);
        second.SetColor(10, new(.6f, .7f, .8f));
        Assert.True(fixture.Plugin.TryApplyCustomize(fixture.Actor, second, Guid.NewGuid(), out _, second.GetFieldChangeMasks(), second.ColorChangeMask));
        var third = new CustomizeEditState(fixture.Actor, original.Customize.SetItem(16, 0x81));
        third.BeginFieldChanges();
        third.SetValue(16, 0, 0x80);
        Assert.True(fixture.Plugin.TryApplyCustomize(fixture.Actor, third, Guid.NewGuid(), out _, third.GetFieldChangeMasks(), third.ColorChangeMask));
        fixture.Finish();
        var queued = Get<CustomizeLiveApplyQueue>(fixture.Plugin, "pendingCustomizeEdits")
            .GetLatest(fixture.Actor.LogicalKey, fixture.Actor.RepresentationKey)!;
        var desired = queued.CreateAppearance(fixture.Desired);
        Assert.Equal<byte>(fixture.Desired.Customize.SetItem(3, 60).SetItem(23, 70), desired.Customize);
        Assert.Equal<CustomizeColorOverride>([new(8, .2f, .3f, .4f), new(10, .6f, .7f, .8f)], desired.CustomizeColors);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullRequestAndPartialRequestKeepExplicitReplacementMeaning(bool fullLast)
    {
        using var fixture = new Fixture();
        var actor = fixture.Actor;
        var full = new CustomizeLiveRequest(Guid.NewGuid(), actor.LogicalKey, actor.RepresentationKey,
            fixture.Desired.Customize.SetItem(3, 60), [new(8, .2f, .3f, .4f)]);
        var draft = new CustomizeEditState(actor, actor.CurrentAppearance!.Customize);
        draft.BeginFieldChanges();
        draft.SetValue(23, 70);
        var partial = new CustomizeLiveRequest(Guid.NewGuid(), actor.LogicalKey, actor.RepresentationKey,
            draft.ToCustomize(), draft.ToColorOverrides(), draft.GetFieldChangeMasks(), draft.ColorChangeMask);
        var queue = new CustomizeLiveApplyQueue();
        Assert.True(queue.Submit(fullLast ? partial : full, _ => true, _ => throw new Exception("Unexpected dispatch")));
        Assert.True(queue.Submit(fullLast ? full : partial, _ => true, _ => throw new Exception("Unexpected dispatch")));
        var result = queue.GetLatest(actor.LogicalKey, actor.RepresentationKey)!.CreateAppearance(fixture.Desired);
        Assert.Equal<byte>(fullLast ? full.Customize : full.Customize.SetItem(23, 70), result.Customize);
        Assert.Equal<CustomizeColorOverride>(full.Colors, result.CustomizeColors);
    }
    private sealed class Fixture : IDisposable
    {
        public readonly Plugin Plugin = Blank<Plugin>();
        public readonly MainWindow Window = Blank<MainWindow>();
        public readonly ActorRegistry Registry = Blank<ActorRegistry>();
        public readonly ActorSnapshot Actor;
        public readonly AppearanceData Desired;
        public readonly ModelSearchEntry Model;
        public readonly Backend Backend = new();
        public readonly RedrawCoordinator Redraw;
        public readonly AppearanceApplyService Service;
        public Fixture()
        {
            var original = AppearanceData.Create(0, ModelCategory.Human, 1, AppearanceCompleteness.Complete,
                Enumerable.Repeat((byte)1, 26), Enumerable.Repeat(0UL, 10));
            Desired = original with
            {
                ModelCharaId = 42,
                Customize = original.Customize.Select(x => (byte)(x + 20)).ToImmutableArray(),
                CustomizeColors = [new CustomizeColorOverride(10, .1f, .2f, .3f)],
            };
            Actor = new(new(1, 1, 1, 10, ObjectKind.EventNpc, 30), new(1, 1, 1, false, 30),
                "Original NPC", ObjectKind.EventNpc, 10, 0, 1, 0, 1, 0, 0, false, CurrentAppearance: original);
            Model = new(42, ModelCategory.Human, default, 42, "Applied NPC", 1, 1, 1, 1, 1, 0, 1,
                null, AppearanceCompleteness.Complete, Desired);
            Set(Registry, "syncRoot", new object());
            Set(Registry, "entries", new ActorEntry[] { new(Actor.LogicalKey, Actor.Name, Actor.ObjectKind, false, [Actor]) });
            var resolver = new Resolver(Registry);
            Redraw = new(resolver, Backend, new Context());
            Service = new(resolver, new Context(), Redraw, NullDiagnosticLog.Instance);
            Set(Plugin, "appearanceApplyService", Service);
            Set(Plugin, "actorResolver", resolver);
            Set(Plugin, "actorRegistry", Registry);
            Set(Plugin, "npcEquipmentGroups", new NpcEquipmentGroups([]));
            Set(Plugin, "mainWindow", Window);
            Set(Plugin, "modelSearchOperations", new HashSet<Guid>());
            Set(Plugin, "customizeOperations", new Dictionary<Guid, Guid>());
            Set(Plugin, "pendingCustomizeEdits", new CustomizeLiveApplyQueue());
            Set(Plugin, "equipmentCopyOperations", new Dictionary<Guid, (EquipmentCopyData, OutfitData?)>());
            Set(Plugin, "appearancePersistence", new ActorAppearancePersistence());
            Set(Plugin, "pinnedOutfitStore", new PinnedOutfitStore(new Configuration(), () => { }, () => 0));
            var injector = Blank<NativeDrawObjectInjector>();
            Set(injector, "disposed", true); // Never create or invoke native hooks in this fixture.
            Set(Plugin, "drawObjectInjector", injector);
            Set(Window, "customizeDraft", new CustomizeEditState(Actor, original.Customize));
            Set(Window, "customizeOperation", Guid.NewGuid());
            Set(Window, "customizeSucceeded", true);
            Set(Window, "customizeStatus", "previous status");
            Service.OperationCompleted += Complete;
        }
        private void Complete(Guid id, LogicalActorKey actor, ActorRepresentationKey representation, AppearanceData data, bool success)
            => typeof(Plugin).GetMethod("OnAppearanceOperationCompleted", Flags)!.Invoke(Plugin, [id, actor, representation, data, success]);
        public void Finish() { Redraw.ProcessNextFrame(); Redraw.ProcessNextFrame(); Redraw.ProcessNextFrame(); }
        public void Dispose() { Service.OperationCompleted -= Complete; Service.Dispose(); Redraw.Dispose(); }
    }
    private sealed class Resolver(ActorRegistry registry) : IActorResolver
    {
        public bool TryResolve(LogicalActorKey key, out ActorSnapshot snapshot)
        {
            var found = registry.TryGet(key, out var entry);
            snapshot = found ? entry.Current : null!;
            return found;
        }
    }
    private sealed class Backend : IRedrawBackend
    {
        public bool Succeed = true;
        public int EnableCount;
        public AppearanceData? Rendered;
        public bool TryDisable(ActorSnapshot actor) => true;
        public bool TryEnable(ActorSnapshot actor, AppearanceData? appearance, Guid operationId)
        { EnableCount++; if (Succeed) Rendered = appearance; return Succeed; }
    }
    private sealed class Context : IClientContext
    { public uint TerritoryId => 30; public bool IsLoggedIn => true; public bool IsGPosing => false; }
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Set(object instance, string name, object? value) => instance.GetType().GetField(name, Flags)!.SetValue(instance, value);
    private static T Get<T>(object instance, string name) => (T)instance.GetType().GetField(name, Flags)!.GetValue(instance)!;
}
