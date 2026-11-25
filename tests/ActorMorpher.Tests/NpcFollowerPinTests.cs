using System;
using System.Collections.Immutable;
using System.Reflection;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.BulkOutfit;
using ActorMorpher.Interop;
using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;
using Xunit;
using Stub = ActorMorpher.Tests.NativeOutfitMemoryTests.PasteStub;
using RoleRow = (byte Role, ushort ObjectIndex, ulong GameObjectId, ulong Address, System.Guid SpawnId,
    System.Guid AppearanceId, string? DisplayName);

namespace ActorMorpher.Tests;

public sealed class NpcFollowerPinTests
{
    [Fact]
    public void RepinnedFollowerSurvivesReloadAndFieldChangeOnlyForTheSameProfileAndPlayer()
    {
        var identity = new NpcFollowerIdentity(Guid.NewGuid(), Guid.NewGuid(), 201, 0x6000000C9, 0x1234);
        var appearance = Appearance();
        var first = Entry(identity, 1190, 1);
        var legacy = PinnedOutfitConfiguration.Create(Entry(identity, 1190, 1, identified: false), appearance);
        var configuration = new Configuration { PinnedOutfitsByCharacter = new() { [42] = [legacy] } };
        var owner = 42UL;
        var store = new PinnedOutfitStore(configuration, () => { }, () => owner);
        Assert.False(store.IsPinned(first));
        Assert.Equal((1, 0), store.PinCurrent([first], _ => appearance));

        var reloaded = JsonConvert.DeserializeObject<Configuration>(JsonConvert.SerializeObject(configuration))!;
        PinnedOutfitStore.Normalize(reloaded);
        var saved = Assert.Single(reloaded.PinnedOutfitsByCharacter[owner], pin => pin.NpcFollowerAppearanceId != null);
        Assert.Equal(identity.AppearanceId, saved.NpcFollowerAppearanceId);
        Assert.Null(saved.Session);
        Assert.Null(saved.ActorKey);
        Assert.Contains(reloaded.PinnedOutfitsByCharacter[owner], pin => pin.NpcFollowerAppearanceId == null);
        store = new PinnedOutfitStore(reloaded, () => { }, () => owner);

        var nextIdentity = identity with { SpawnId = Guid.NewGuid() };
        var next = Entry(nextIdentity, 962, 2);
        var sameName = Entry(nextIdentity with { AppearanceId = Guid.NewGuid() }, 962, 3);
        var ordinaryMinion = Entry(nextIdentity, 962, 4, identified: false);
        Assert.Equal(next.Key, Assert.Single(store.GetPinnedActorKeysForDisplay([sameName, ordinaryMinion, next])));
        Assert.True(store.TryGetAppearance(next, out var pinned));
        Assert.True(PinnedOutfitStore.AppearanceEquals(appearance, pinned));
        Assert.False(store.TryGetAppearance(sameName, out _));
        Assert.False(store.TryGetAppearance(ordinaryMinion, out _));

        var discovery = new PinnedActorDiscovery();
        long nextScan = 500;
        Assert.Equal(next.Key, Assert.Single(discovery.GetCandidates([next], store.IsPinned, 0, ref nextScan)).Key);
        owner = 43;
        Assert.Empty(store.GetPinnedActorKeysForDisplay([next]));
        Assert.False(store.TryGetAppearance(next, out _));
        owner = 42;
        Assert.True(store.Unpin(next));
        Assert.False(store.IsPinned(next));
    }

    [Fact]
    public void ReusingTheSlotAndAddressDoesNotReuseTheRetainedAppearanceLifetime()
    {
        var identity = new NpcFollowerIdentity(Guid.NewGuid(), Guid.NewGuid(), 201, 0x6000000C9, 0x1234);
        var actorContinuity = new NativeActorContinuity();
        var gameIdentity = new ActorContinuityKey(ObjectKind.Companion, 5, identity.GameObjectId, 98, 962);
        var original = actorContinuity.Observe((nint)identity.Address, gameIdentity, identity.SpawnId);
        Assert.Equal(original, actorContinuity.Observe((nint)identity.Address, gameIdentity, identity.SpawnId));
        var newSpawn = actorContinuity.Observe((nint)identity.Address, gameIdentity, Guid.NewGuid());
        Assert.NotEqual(original.Lifetime, newSpawn.Lifetime);
        Assert.NotEqual(newSpawn.Lifetime, actorContinuity.Observe((nint)identity.Address, gameIdentity).Lifetime);
    }

    [Fact]
    public void RecheckingThePublishedActorDetectsAReplacedOrUnpublishedSpawnBeforeAnApply()
    {
        var identity = new NpcFollowerIdentity(Guid.NewGuid(), Guid.NewGuid(), 201, 0x6000000C9, 0x1234);
        Assert.True(identity.Matches(ObjectKind.Companion, 201, identity.GameObjectId, (nint)identity.Address));
        Assert.False(identity.Matches(ObjectKind.Companion, 201, identity.GameObjectId, (nint)0x4321));
        Assert.False(identity.Matches(ObjectKind.EventNpc, 201, identity.GameObjectId, (nint)identity.Address));
        (Guid, Guid, ushort, ulong, ulong)? value = (identity.AppearanceId, identity.SpawnId,
            identity.ObjectIndex, identity.GameObjectId, identity.Address);
        var framework = Stub.Create<IFramework>(member => member == "get_IsInFrameworkUpdateThread" ? true : null);
        var continuity = new NativeActorContinuity(CreateIpc(framework, () => value));
        using var registry = new ActorRegistry(null!, null!, framework, null!, continuity: continuity);
        var published = Entry(identity, 962, 1);
        typeof(ActorRegistry).GetField("entries", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(registry, new[] { published });
        var resolver = new RegistryActorResolver(registry);
        Assert.True(resolver.TryResolve(published.Key, out _));
        Assert.True(resolver.TryResolve(published.Key, published.Current.RepresentationKey, out _));

        value = (identity.AppearanceId, Guid.NewGuid(), identity.ObjectIndex, identity.GameObjectId, identity.Address);
        Assert.False(resolver.TryResolve(published.Key, out _));
        Assert.False(resolver.TryResolve(published.Key, published.Current.RepresentationKey, out _));
        value = null;
        Assert.False(resolver.TryResolve(published.Key, out _));
    }

    [Fact]
    public void RolesCarryTheSavedNameAndRequireTheExactObjectBeforeExcludingIt()
    {
        var spawnId = Guid.NewGuid();
        var appearanceId = Guid.NewGuid();
        var parent = new ActorObjectReference(200, 0x6000000C8, 0x1230);
        var child = new ActorObjectReference(201, 0x6000000C9, 0x4560);
        RoleRow[] rows =
        [
            (2, parent.ObjectIndex, parent.GameObjectId, parent.Address, spawnId, Guid.Empty, null),
            (1, child.ObjectIndex, child.GameObjectId, child.Address, spawnId, appearanceId, "ミンフィリア"),
        ];
        var framework = Stub.Create<IFramework>(member => member == "get_IsInFrameworkUpdateThread" ? true : null);
        var ipc = CreateIpc(framework, () => throw new InvalidOperationException("Unexpected legacy read"), () => rows);

        var (roles, follower) = ipc.ReadForRegistry();
        Assert.Equal(new NpcFollowerIdentity(appearanceId, spawnId, child.ObjectIndex, child.GameObjectId, child.Address), follower);
        Assert.True(ActorRoleAssignment.FindMatch(roles, Object(parent))!.Value.BlocksEditing);
        var editable = ActorRoleAssignment.FindMatch(roles, Object(child))!.Value;
        Assert.False(editable.BlocksEditing);
        Assert.Equal("ミンフィリア", editable.DisplayName);
        Assert.Null(ActorRoleAssignment.FindMatch(roles, Object(parent with { ObjectIndex = 206 })));
        Assert.Null(ActorRoleAssignment.FindMatch(roles, Object(parent with { GameObjectId = 12 })));
        Assert.Null(ActorRoleAssignment.FindMatch(roles, Object(parent with { Address = 0x6780 })));

        rows[1] = (3, child.ObjectIndex, child.GameObjectId, child.Address, spawnId, Guid.Empty, null);
        (roles, follower) = ipc.ReadForRegistry();
        Assert.Null(follower);
        Assert.Equal(ActorRole.Inactive, ActorRoleAssignment.FindMatch(roles, Object(child))!.Value.Role);
        Assert.True(ActorRoleAssignment.FindMatch(roles, Object(child))!.Value.BlocksEditing);
    }

    [Fact]
    public void EmptyRolesDisableTheCurrentBindingAndAnUnavailableRoleApiUsesLegacy()
    {
        var appearanceId = Guid.NewGuid();
        var spawnId = Guid.NewGuid();
        var framework = Stub.Create<IFramework>(member => member == "get_IsInFrameworkUpdateThread" ? true : null);
        var legacyReads = 0;
        (Guid, Guid, ushort, ulong, ulong)? ReadLegacy()
        {
            legacyReads++;
            return (appearanceId, spawnId, 201, 0x6000000C9, 0x1234);
        }

        var (roles, follower) = CreateIpc(framework, ReadLegacy, () => []).ReadForRegistry();
        Assert.Empty(roles);
        Assert.Null(follower);
        Assert.Equal(0, legacyReads);

        (roles, follower) = CreateIpc(framework, ReadLegacy).ReadForRegistry();
        Assert.Empty(roles);
        Assert.Equal(new NpcFollowerIdentity(appearanceId, spawnId, 201, 0x6000000C9, 0x1234), follower);
        Assert.Equal(1, legacyReads);
    }

    [Fact]
    public void BulkPinSkipsTheConfirmedSupportAcrossTerritoriesAndKeepsOtherHistory()
    {
        var identity = new NpcFollowerIdentity(Guid.NewGuid(), Guid.NewGuid(), 201, 0x6000000C9, 0x1234);
        var continuity = new NativeActorContinuity();
        var original = continuity.Observe((nint)0x1110, new(ObjectKind.BattleNpc, 5, 0x6000000C8, 1, 962));
        var support = EditedActor(200, 0x6000000C8, 0x1110, original.Lifetime);
        var other = EditedActor(207, 0x6000000CF, 0x2220, 12);
        var follower = Entry(identity, 962, 13);
        var appearance = Appearance();
        var persistence = new ActorAppearancePersistence(() => 42);
        var store = new PinnedOutfitStore(new Configuration(), () => { }, () => 42);
        persistence.RecordModel(support.Current, appearance);
        persistence.RecordModel(other.Current, appearance);
        persistence.RecordModel(follower.Current, appearance);
        Assert.Equal((1, 0), store.PinCurrent([follower], _ => appearance));

        var instance = support.Current.Instance!.Value;
        persistence.ConfirmSupportActor(instance with { Lifetime = instance.Lifetime + 40 });
        persistence.ConfirmSupportActor(instance with { Object = instance.Object with { Address = 0x9990 } });
        Assert.Contains(persistence.GetAbsentModifiedActors([], []), actor => actor.Key == support.Key);

        var observedAfterMove = continuity.Observe((nint)0x1110, original.Key with { Territory = 1190 });
        Assert.Equal(original.Lifetime, observedAfterMove.Lifetime);
        persistence.ConfirmSupportActor(new(instance.Object, observedAfterMove.Lifetime, instance.EntityId));
        Assert.Equal((2, 0), Plugin.PinModifiedActors(store, persistence, [], _ => false, _ => null));
        Assert.False(store.IsPinned(support));
        Assert.True(store.IsPinned(other));
        Assert.True(store.IsPinned(Entry(identity with { SpawnId = Guid.NewGuid() }, 1190, 14)));
    }

    private static ActorEntry EditedActor(ushort slot, ulong objectId, ulong address, ulong lifetime)
    {
        var continuity = new ActorContinuityKey(ObjectKind.BattleNpc, 5, objectId, 1, 962);
        var key = new LogicalActorKey(slot, objectId, 0xE0000000, 1, ObjectKind.BattleNpc, 962)
        { Lifetime = lifetime, Continuity = continuity };
        var snapshot = new ActorSnapshot(key, new(slot, objectId, key.EntityId, false, 962),
            "NPC", ObjectKind.BattleNpc, 1, 0, 1, 1, 1, 0, 1, false, CurrentAppearance: Appearance())
        { ContinuityKey = continuity, Instance = new(new(slot, objectId, address), lifetime, key.EntityId) };
        return new(key, snapshot.Name, snapshot.ObjectKind, false, [snapshot]);
    }

    private static IGameObject Object(ActorObjectReference actor)
        => Stub.Create<IGameObject>(member => member switch
        {
            "get_ObjectIndex" => Convert.ChangeType(actor.ObjectIndex, typeof(IGameObject).GetProperty(nameof(IGameObject.ObjectIndex))!.PropertyType),
            "get_GameObjectId" => actor.GameObjectId,
            "get_Address" => unchecked((nint)actor.Address),
            _ => throw new InvalidOperationException(member),
        });

    private static NpcFollowerIpc CreateIpc(IFramework framework, Func<(Guid, Guid, ushort, ulong, ulong)?> legacy,
        Func<RoleRow[]>? roles = null)
    {
        var pluginInterface = DispatchProxy.Create<IDalamudPluginInterface, IpcStub>();
        var stub = (IpcStub)(object)pluginInterface;
        stub.Legacy = Stub.Create<ICallGateSubscriber<(Guid, Guid, ushort, ulong, ulong)?>>(member => member switch
        {
            "get_HasFunction" => true,
            "InvokeFunc" => legacy(),
            _ => throw new InvalidOperationException(member),
        });
        stub.Roles = Stub.Create<ICallGateSubscriber<RoleRow[]>>(member => member switch
        {
            "get_HasFunction" => roles is not null,
            "InvokeFunc" => roles!(),
            _ => throw new InvalidOperationException(member),
        });
        return new(pluginInterface, framework);
    }

    public class IpcStub : DispatchProxy
    {
        internal object Legacy { get; set; } = null!;
        internal object Roles { get; set; } = null!;

        protected override object Invoke(MethodInfo? targetMethod, object?[]? args)
            => args?[0] switch
            {
                "NpcFollower.GetCurrentFollower.v1" => Legacy,
                "NpcFollower.GetActorRoles.v1" => Roles,
                _ => throw new InvalidOperationException(targetMethod?.Name),
            };
    }

    private static ActorEntry Entry(NpcFollowerIdentity identity, uint territory, ulong lifetime, bool identified = true)
    {
        var key = new LogicalActorKey(identity.ObjectIndex, identity.GameObjectId, 0xE0000000,
            98, ObjectKind.Companion, territory) { Lifetime = lifetime };
        var snapshot = new ActorSnapshot(key, new(identity.ObjectIndex, identity.GameObjectId, key.EntityId, false, territory),
            "同行NPC", ObjectKind.Companion, 98, 0, 1, 1, 1, 0, 1, false, CurrentAppearance: Appearance())
        { NpcFollower = identified ? identity : null };
        return new(key, snapshot.Name, snapshot.ObjectKind, false, [snapshot]);
    }

    private static AppearanceData Appearance()
        => new(0, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
            ImmutableArray.CreateRange(new byte[26]), ImmutableArray.CreateRange(new ulong[] { 0, 123, 0, 0, 0, 0, 0, 0, 0, 0 }),
            1, 0, 0, false, 0, true);
}
