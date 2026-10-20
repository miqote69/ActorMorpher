using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using ActorMorpher.Interop;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class NpcCustomizeSharingCompletionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitCustomizeCompletionSharesOnlyItsSuccessfulCustomization(bool succeeded)
    {
        var fixture = new Fixture();
        var operation = Guid.NewGuid();
        fixture.CustomizeOperations[operation] = Guid.NewGuid();
        var edited = fixture.Original with
        {
            Customize = fixture.Original.Customize.SetItem(0, 2).SetItem(1, 1).SetItem(3, 77).SetItem(23, 66),
            CustomizeColors = [new(8, .2f, .3f, .4f)],
        };
        fixture.Complete(operation, fixture.Source, edited, succeeded);
        Assert.True(fixture.Store.TryGetSharedNpcCustomize(fixture.TargetEntry, out var shared));
        Assert.True(fixture.Store.TryGetSharedNpcEquipment(fixture.TargetEntry, out _));
        Assert.Empty(fixture.CustomizeOperations);
        var expected = succeeded ? edited : fixture.Original;
        Assert.Equal<byte>(expected.Customize, shared.Customize);
        Assert.Equal<CustomizeColorOverride>(expected.CustomizeColors, shared.CustomizeColors);
        Assert.True(fixture.Store.TryGetAppearance(fixture.TargetEntry, out var targetPin));
        Assert.Equal<byte>(expected.Customize, targetPin.Customize);
        Assert.Equal(fixture.Original.ModelCharaId, targetPin.ModelCharaId);
        Assert.Equal(fixture.Original.ModelScale, targetPin.ModelScale);
        Assert.Equal<ulong>(fixture.Original.Equipment, targetPin.Equipment);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OldMaintenanceCompletionDoesNotRepublishOrConsumeANewerSharedEdit(bool succeeded)
    {
        var fixture = new Fixture();
        var old = fixture.Original with { Customize = fixture.Original.Customize.SetItem(1, 1) };
        Assert.True(CustomizeCopyData.TryCreate(old, out var previous));
        fixture.Store.ShareNpcCustomize(fixture.SourceEntry, previous);
        fixture.Store.MarkSharedNpcEquipmentAttempted(fixture.TargetEntry);
        Set(fixture.Plugin, "pinnedAppearanceOperationActor", fixture.Target.LogicalKey);
        Set(fixture.Plugin, "pinnedAppearanceOperationObservedState", fixture.Original);
        var latest = old with { Customize = old.Customize.SetItem(3, 80) };
        Assert.True(CustomizeCopyData.TryCreate(latest, out var next));
        fixture.Store.ShareNpcCustomize(fixture.SourceEntry, next);

        fixture.Complete(Guid.NewGuid(), fixture.Target, old, succeeded);
        Assert.Null(Get(fixture.Plugin, "pinnedAppearanceOperationActor"));
        Assert.False(fixture.Store.WasSharedNpcChangeAttempted(fixture.TargetEntry));
        Assert.True(fixture.Store.TryGetSharedNpcCustomize(fixture.TargetEntry, out var shared));
        Assert.Equal<byte>(latest.Customize, shared.Customize);
        Assert.True(fixture.Store.TryGetAppearance(fixture.TargetEntry, out var pin));
        Assert.Equal<byte>(latest.Customize, pin.Customize);
        Assert.True(fixture.Store.TryGetSharedNpcEquipment(fixture.TargetEntry, out _));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    [InlineData(5, false)]
    [InlineData(6, true)]
    [InlineData(23, true)]
    public void SharedHeightBustAndHairUseTheExistingDirectPathWhileModelFamilyChangesRedraw(int index, bool direct)
    {
        var original = OriginalAppearance();
        var desired = original with { Customize = original.Customize.SetItem(index, 2) };
        Assert.Equal(direct, Plugin.CanApplySharedCustomizeDirectly(original, desired));
    }

    [Theory]
    [InlineData(3, true)]
    [InlineData(1, false)]
    [InlineData(-1, false)]
    public void CustomizeOnlySharingStillComposesTheLegacyOutfitPinForEveryMaintenanceRoute(int index, bool direct)
    {
        var rendered = OriginalAppearance();
        var customizeAppearance = index < 0 ? rendered
            : rendered with { Customize = rendered.Customize.SetItem(index, 2) };
        Assert.True(CustomizeCopyData.TryCreate(customizeAppearance, out var customize));
        var legacy = EquipmentDisplayFormatting.CreateHumanOutfit(rendered)!;
        legacy = legacy with { Equipment = legacy.Equipment.SetItem(1, new(999, 3, 4, 5)) };
        var desired = Plugin.ComposeSharedNpcAppearance(rendered, null, legacy, null, customize);
        Assert.Equal(direct, Plugin.CanApplySharedCustomizeDirectly(rendered, desired));
        Assert.False(PinnedOutfitStore.AppearanceEquals(rendered, desired));
        Assert.Equal((ushort)999, EquipmentDisplayFormatting.CreateHumanOutfit(desired)!.Equipment[1].Set);
        Assert.Equal<byte>(customizeAppearance.Customize, desired.Customize);
        Assert.Equal(rendered.ModelCharaId, desired.ModelCharaId);
        Assert.Equal(rendered.ModelScale, desired.ModelScale);
    }

    private sealed class Fixture
    {
        public readonly Plugin Plugin = Blank<Plugin>();
        public readonly AppearanceData Original = OriginalAppearance();
        public readonly ActorSnapshot Source;
        public readonly ActorSnapshot Target;
        public readonly ActorEntry SourceEntry;
        public readonly ActorEntry TargetEntry;
        public readonly PinnedOutfitStore Store;
        public readonly Dictionary<Guid, Guid> CustomizeOperations = [];
        public Fixture()
        {
            Source = Actor(100, 1, Original);
            Target = Actor(101, 2, Original);
            SourceEntry = new(Source.LogicalKey, Source.Name, Source.ObjectKind, false, [Source]);
            TargetEntry = new(Target.LogicalKey, Target.Name, Target.ObjectKind, false, [Target]);
            var registry = Blank<ActorRegistry>();
            Set(registry, "syncRoot", new object());
            Set(registry, "entries", new[] { SourceEntry, TargetEntry });
            var groups = new NpcEquipmentGroups(new[] { Source, Target }.Select(actor =>
                new ModelSearchEntry(0, ModelCategory.Human, ModelSource.EventNpc, actor.BaseId,
                    actor.Name, 1, 0, 0, 0, 1, 0, 0, null, AppearanceCompleteness.Complete, Original)));
            Store = new(new Configuration(), () => { }, () => 1,
                (kind, id) => groups.TryGet(kind, id, out var group) ? group : null);
            Store.PinCurrent([SourceEntry, TargetEntry], _ => Original);
            Set(Plugin, "actorRegistry", registry);
            Set(Plugin, "actorResolver", new Resolver(registry));
            Set(Plugin, "npcEquipmentGroups", groups);
            Set(Plugin, "pinnedOutfitStore", Store);
            Set(Plugin, "appearancePersistence", new ActorAppearancePersistence(() => 1));
            Set(Plugin, "equipmentCopyOperations", new Dictionary<Guid, (EquipmentCopyData, OutfitData?)>());
            Set(Plugin, "modelSearchOperations", new HashSet<Guid>());
            Set(Plugin, "customizeOperations", CustomizeOperations);
            Set(Plugin, "failedPinnedAppearanceStates", new Dictionary<LogicalActorKey, AppearanceData>());
            Set(Plugin, "failedPinnedOutfitReapplyStates", new Dictionary<LogicalActorKey, OutfitData>());
            var injector = Blank<NativeDrawObjectInjector>();
            Set(injector, "disposed", true);
            Set(Plugin, "drawObjectInjector", injector);
        }
        public void Complete(Guid operation, ActorSnapshot actor, AppearanceData applied, bool succeeded)
            => typeof(Plugin).GetMethod("OnAppearanceOperationCompleted", Flags)!
                .Invoke(Plugin, [operation, actor.LogicalKey, actor.RepresentationKey, applied, succeeded]);
    }

    private static AppearanceData OriginalAppearance()
        => AppearanceData.Create(0, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
            Enumerable.Repeat((byte)1, 26), Enumerable.Repeat(123UL, 10), 1.3f, 12, 34, false, 0, true);
    private static ActorSnapshot Actor(uint id, ushort index, AppearanceData appearance)
        => new(new(index, index, index, id, ObjectKind.EventNpc, 282),
            new(index, index, index, false, 282), "NPC", ObjectKind.EventNpc, id, 0, 1, 1, 1, 0, 0, false,
            CurrentAppearance: appearance);
    private sealed class Resolver(ActorRegistry registry) : IActorResolver
    {
        public bool TryResolve(LogicalActorKey key, out ActorSnapshot actor)
        {
            var found = registry.TryGet(key, out var entry);
            actor = found ? entry.Current : null!;
            return found;
        }
    }
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static T Blank<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    private static void Set(object instance, string name, object? value)
        => instance.GetType().GetField(name, Flags)!.SetValue(instance, value);
    private static object? Get(object instance, string name)
        => instance.GetType().GetField(name, Flags)!.GetValue(instance);
}
