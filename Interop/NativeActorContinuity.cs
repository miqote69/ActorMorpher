using FFXIVClientStructs.FFXIV.Client.Game.Character;
using GameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;
using ObjectKind = Dalamud.Game.ClientState.Objects.Enums.ObjectKind;

namespace ActorMorpher.Interop;

public sealed unsafe class NativeActorContinuity
{
    private readonly Dictionary<nint, LivingActor> living = new();
    private readonly NpcFollowerIpc? npcFollower;
    private readonly IReadOnlyDictionary<uint, uint>? minionModels;
    private ulong nextLifetime;

    public NativeActorContinuity() { }

    internal NativeActorContinuity(NpcFollowerIpc? npcFollower, IReadOnlyDictionary<uint, uint>? minionModels = null)
    {
        this.npcFollower = npcFollower;
        this.minionModels = minionModels;
    }

    internal readonly record struct MinionObservation(ushort ObjectIndex, uint Territory, int Model, CompanionSpawnState State);
    private readonly record struct DepartingMinion(uint RowId, int Model, bool SpawnStarted = false);
    private readonly record struct LivingActor(ulong Lifetime, ActorContinuityKey Identity, Guid? FollowerSpawnId)
    {
        internal MinionObservation? Minion { get; init; }
        internal DepartingMinion? DepartingMinion { get; init; }
    }

    internal NpcFollowerIdentity? ReadNpcFollower() => npcFollower?.ReadCurrent();

    internal (IReadOnlyList<ActorRoleAssignment> Roles, NpcFollowerIdentity? Follower) ReadActorRoles()
        => npcFollower?.ReadForRegistry() ?? ([], null);

    public (ActorContinuityKey Key, ulong Lifetime) Read(GameObject* obj, uint territory)
        => Read(obj, territory, out _);

    internal (ActorContinuityKey Key, ulong Lifetime) Read(GameObject* obj, uint territory, out bool minionSwitchPending)
    {
        var address = (nint)obj;
        var kind = (ObjectKind)obj->ObjectKind;
        var gameObjectId = obj->GetGameObjectId().Id;
        minionSwitchPending = false;
        if (kind == ObjectKind.Companion && npcFollower is not null)
        {
            if (npcFollower.IsFrameworkThread)
            {
                if (ReadNpcFollower() is { } follower && follower.Matches(kind, obj->ObjectIndex, gameObjectId, address))
                    return Observe(address, new(kind, 5, gameObjectId, obj->BaseId, territory), follower.SpawnId);
            }
            else if (living.TryGetValue(address, out var previous) && previous.FollowerSpawnId is not null)
            {
                // A native callback outside the framework thread cannot confirm this generation.
                return (new(kind, 5, gameObjectId, obj->BaseId, territory), 0);
            }
        }
        return Read(obj, territory, kind, gameObjectId, null, out minionSwitchPending);
    }

    internal (ActorContinuityKey Key, ulong Lifetime) Read(GameObject* obj, uint territory, Guid? followerSpawnId)
        => Read(obj, territory, followerSpawnId, out _);

    internal (ActorContinuityKey Key, ulong Lifetime) Read(GameObject* obj, uint territory, Guid? followerSpawnId,
        out bool minionSwitchPending)
        => Read(obj, territory, (ObjectKind)obj->ObjectKind, obj->GetGameObjectId().Id, followerSpawnId,
            out minionSwitchPending);

    private (ActorContinuityKey Key, ulong Lifetime) Read(GameObject* obj, uint territory, ObjectKind kind,
        ulong gameObjectId, Guid? followerSpawnId, out bool minionSwitchPending)
    {
        var identity = followerSpawnId is not null
            ? new ActorContinuityKey(kind, 5, gameObjectId, obj->BaseId, territory)
            : Describe(obj, territory, kind, gameObjectId);
        MinionObservation? minion = null;
        if (identity is { Kind: ObjectKind.Companion, Source: 2 }
            && minionModels?.ContainsKey(identity.BaseId) == true)
        {
            var table = Companion.StaticVirtualTablePointer;
            if (table != null && obj->VirtualTable == (GameObject.GameObjectVirtualTable*)table)
            {
                var companion = (Companion*)obj;
                if (companion->CompanionType != CompanionType.FollowMount)
                    minion = new(obj->ObjectIndex, territory, companion->ModelContainer.ModelCharaId, companion->SpawnState);
            }
        }
        return Observe((nint)obj, identity, followerSpawnId, minion, out minionSwitchPending);
    }

    private static ActorContinuityKey Describe(GameObject* obj, uint territory, ObjectKind kind, ulong gameObjectId)
        => Describe(kind, ((Character*)obj)->ContentId, obj->EntityId, obj->LayoutId,
            gameObjectId, obj->BaseId, obj->ObjectIndex, territory);

    internal (ActorContinuityKey Key, ulong Lifetime) Observe(nint address, ActorContinuityKey identity,
        Guid? followerSpawnId = null)
        => Observe(address, identity, followerSpawnId, null, out _);

    internal (ActorContinuityKey Key, ulong Lifetime) Observe(nint address, ActorContinuityKey identity,
        Guid? followerSpawnId, MinionObservation? minion, out bool minionSwitchPending)
    {
        var observed = living.TryGetValue(address, out var previous);
        var departing = GetDepartingMinion(previous, identity, minion);
        minionSwitchPending = departing is not null;
        if (!observed
            || previous.FollowerSpawnId != followerSpawnId
            || previous.Identity.Kind != identity.Kind
            || previous.Identity.BaseId != identity.BaseId
            || (previous.Identity.Source == identity.Source && identity.Source != 5
                && previous.Identity != identity))
        {
            previous = new(++nextLifetime, identity, followerSpawnId);
        }
        else if (identity.Source < previous.Identity.Source)
        {
            previous = previous with { Identity = identity };
        }
        living[address] = previous with { Minion = minion, DepartingMinion = departing };
        return (identity, previous.Lifetime);
    }

    private DepartingMinion? GetDepartingMinion(LivingActor previous, ActorContinuityKey identity,
        MinionObservation? observation)
    {
        if (observation is not { } minion || previous.Minion is not { } old
            || old.ObjectIndex != minion.ObjectIndex || old.Territory != minion.Territory
            || previous.Identity.Id != identity.Id)
            return null;

        var departing = previous.DepartingMinion;
        if (departing is null && previous.Identity.BaseId != identity.BaseId)
            departing = new(previous.Identity.BaseId, old.Model);
        if (departing is not { } outgoing
            || minionModels?.TryGetValue(identity.BaseId, out var model) != true)
            return null;

        // The game's row can advance while the departing minion is still on screen.
        // Shared-model rows also need evidence that a new native spawn has started.
        var spawnStarted = (previous.Identity.BaseId == identity.BaseId && outgoing.SpawnStarted)
            || (minion.State == CompanionSpawnState.Spawning && old.State != minion.State)
            || (minion.State == CompanionSpawnState.Active && old.State is CompanionSpawnState.None or CompanionSpawnState.Despawning);
        var distinctModel = minion.Model != outgoing.Model
            && (!minionModels.TryGetValue(outgoing.RowId, out var outgoingModel) || outgoingModel != model);
        return (minion.State != CompanionSpawnState.Despawning && minion.Model == model && (distinctModel || spawnStarted))
            || (identity.BaseId == outgoing.RowId && minion.State == CompanionSpawnState.Active)
            ? null : outgoing with { SpawnStarted = spawnStarted };
    }

    public void Forget(nint address) => living.Remove(address);

    internal static ActorContinuityKey Describe(ObjectKind kind, ulong contentId, uint entityId,
        uint layoutId, ulong gameObjectId, uint baseId, ushort index, uint territory)
    {
        if (kind == ObjectKind.Pc && contentId != 0)
            return new(kind, 1, contentId, 0, 0);
        if (kind == ObjectKind.Companion && (gameObjectId >> 32) == 4)
            return new(kind, 2, gameObjectId, baseId, 0); // game identity contains its owner
        if (kind is ObjectKind.EventNpc or ObjectKind.BattleNpc && layoutId is not (0 or 0xE0000000))
            return new(kind, 3, layoutId, baseId, territory);
        if (entityId is not (0 or 0xE0000000))
            return new(kind, 4, entityId, baseId, kind == ObjectKind.Pc ? 0 : territory);
        // No cross-slot identity is supplied by the game for this object.
        return new(kind, 5, gameObjectId, baseId, territory);
    }
}
