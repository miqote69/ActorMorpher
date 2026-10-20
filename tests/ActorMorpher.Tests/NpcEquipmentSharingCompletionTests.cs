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

public sealed class NpcEquipmentSharingCompletionTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void SharedWeaponRedrawCompletionKeepsOnlyEquipmentAndDoesNotPublishAnotherEdit(bool hasModel, bool succeeded)
    {
        var appearance = AppearanceData.Create(42, ModelCategory.Human, 901, AppearanceCompleteness.Complete,
            Enumerable.Repeat((byte)1, 26), Enumerable.Repeat(123UL, 10), 1.7f, 12, 34, false, 0, true);
        var actor = new ActorSnapshot(new(1, 1, 1, 100, ObjectKind.EventNpc, 282),
            new(1, 1, 1, false, 282), "NPC", ObjectKind.EventNpc, 100, 42, 1, 1, 1, 0, 0, false,
            CurrentAppearance: appearance);
        var desired = appearance with { Mainhand = 99, Offhand = 88 };
        Assert.True(EquipmentCopyData.TryCreate(desired, out var data));
        var registry = Blank<ActorRegistry>();
        Set(registry, "syncRoot", new object());
        Set(registry, "entries", new ActorEntry[] { new(actor.LogicalKey, actor.Name, actor.ObjectKind, false, [actor]) });
        var persistence = new ActorAppearancePersistence(() => 1);
        if (hasModel) persistence.RecordModel(actor, appearance);
        var plugin = Blank<Plugin>();
        var operation = Guid.NewGuid();
        var original = EquipmentDisplayFormatting.CreateHumanOutfit(appearance)!;
        Set(plugin, "actorRegistry", registry);
        Set(plugin, "actorResolver", new Resolver(registry));
        Set(plugin, "appearancePersistence", persistence);
        Set(plugin, "equipmentCopyOperations", new Dictionary<Guid, (EquipmentCopyData, OutfitData?)>
            { [operation] = (data, original) });
        Set(plugin, "pinnedAppearanceOperationActor", actor.LogicalKey);
        Set(plugin, "pinnedAppearanceOperationObservedState", appearance);
        Set(plugin, "failedPinnedAppearanceStates", new Dictionary<LogicalActorKey, AppearanceData>());
        var injector = Blank<NativeDrawObjectInjector>();
        Set(injector, "disposed", true); // No native hooks or game process in this test.
        Set(plugin, "drawObjectInjector", injector);
        typeof(Plugin).GetMethod("OnAppearanceOperationCompleted", Flags)!
            .Invoke(plugin, [operation, actor.LogicalKey, actor.RepresentationKey, desired, succeeded]);

        Assert.Null(Get(plugin, "pinnedAppearanceOperationActor"));
        Assert.Empty((Dictionary<Guid, (EquipmentCopyData, OutfitData?)>)Get(plugin, "equipmentCopyOperations")!);
        Assert.Null(Get(plugin, "npcEquipmentGroups"));
        var retained = persistence.GetModel(actor.LogicalKey);
        if (hasModel)
        {
            Assert.NotNull(retained);
            Assert.Equal<byte>(appearance.Customize, retained.Customize);
            Assert.Equal(appearance.ModelCharaId, retained.ModelCharaId);
            Assert.Equal(appearance.ModelScale, retained.ModelScale);
            Assert.Equal(appearance.SourceRowId, retained.SourceRowId);
            Assert.Equal(desired.Mainhand, retained.Mainhand);
        }
        else Assert.Null(retained);
        var creation = persistence.GetCreateAppearance(actor.LogicalKey, 42, out var outfitOnly);
        if (succeeded && !hasModel)
        {
            Assert.True(outfitOnly);
            Assert.NotNull(creation);
            Assert.Empty(creation.Customize);
            Assert.Null(creation.ModelScale);
            Assert.Equal(desired.Mainhand, creation.Mainhand);
        }
        if (!succeeded) Assert.Null(creation);
    }

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
