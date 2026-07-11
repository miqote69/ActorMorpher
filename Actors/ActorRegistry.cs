using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace ActorMorpher.Actors;

public sealed unsafe class ActorRegistry : IDisposable
{
    private readonly IObjectTable objectTable;
    private readonly IClientState clientState;
    private readonly IFramework framework;
    private readonly object syncRoot = new();
    private IReadOnlyList<ActorEntry> entries = Array.Empty<ActorEntry>();

    public ActorRegistry(IObjectTable objectTable, IClientState clientState, IFramework framework)
    {
        this.objectTable = objectTable;
        this.clientState = clientState;
        this.framework = framework;
        framework.Update += OnFrameworkUpdate;
        Refresh();
    }

    public IReadOnlyList<ActorEntry> Entries
    {
        get
        {
            lock (syncRoot)
                return entries;
        }
    }

    public void Dispose()
        => framework.Update -= OnFrameworkUpdate;

    public bool TryGet(LogicalActorKey key, out ActorEntry actor)
    {
        lock (syncRoot)
        {
            actor = entries.FirstOrDefault(candidate => candidate.Key == key)!;
            return actor is not null;
        }
    }

    private void OnFrameworkUpdate(IFramework _)
        => Refresh();

    private void Refresh()
    {
        var territoryId = clientState.TerritoryType;
        var localPlayerId = objectTable.LocalPlayer?.GameObjectId;
        var snapshots = objectTable
            .Where(static obj => obj is not null
                && obj.Address != nint.Zero
                && obj.IsValid()
                && obj.ObjectKind is ObjectKind.Pc or ObjectKind.EventNpc or ObjectKind.BattleNpc)
            .Select(obj => CreateSnapshot(obj, territoryId, localPlayerId))
            .Where(static snapshot => snapshot is not null)
            .Select(static snapshot => snapshot!)
            .ToArray();

        var next = snapshots
            .GroupBy(static snapshot => snapshot.LogicalKey)
            .Select(static group => new ActorEntry(
                group.Key,
                group.First().Name,
                group.First().ObjectKind,
                group.Any(static representation => representation.IsLocalPlayer),
                group.OrderBy(static representation => representation.RepresentationKey.IsGPoseRepresentation).ToArray()))
            .OrderByDescending(static actor => actor.IsLocalPlayer)
            .ThenBy(static actor => actor.Kind)
            .ThenBy(static actor => actor.Name)
            .ToArray();

        lock (syncRoot)
            entries = next;
    }

    private static ActorSnapshot? CreateSnapshot(IGameObject obj, uint territoryId, ulong? localPlayerId)
    {
        if (obj.Address == nint.Zero)
            return null;

        var native = (Character*)obj.Address;
        var objectIndex = checked((ushort)obj.ObjectIndex);
        var modelCharaId = checked((uint)native->ModelContainer.ModelCharaId);
        var isHuman = modelCharaId == 0;
        var name = obj.Name.ToString();
        if (string.IsNullOrWhiteSpace(name))
            name = $"{obj.ObjectKind} {obj.BaseId}";

        var representation = new ActorRepresentationKey(
            objectIndex,
            obj.GameObjectId,
            obj.EntityId,
            false);
        var logical = new LogicalActorKey(
            objectIndex,
            obj.GameObjectId,
            obj.EntityId,
            obj.BaseId,
            obj.ObjectKind,
            territoryId);

        return new ActorSnapshot(
            logical,
            representation,
            name,
            obj.ObjectKind,
            obj.BaseId,
            modelCharaId,
            isHuman ? native->DrawData.CustomizeData.Race : null,
            isHuman ? native->DrawData.CustomizeData.Sex : null,
            isHuman ? native->DrawData.CustomizeData.BodyType : null,
            native->CharacterData.ClassJob,
            native->CharacterData.Level,
            obj.GameObjectId == localPlayerId);
    }
}
