using System.Collections.Immutable;
using System.Text.Json;
using ActorMorpher.Appearance;
using Dalamud.Game.ClientState.Objects.Enums;

namespace ActorMorpher.BulkOutfit;

internal sealed record NpcEquipmentGroup(ObjectKind Kind, uint CanonicalBaseId, ImmutableArray<uint> BaseIds)
{
    public string Identity => $"{Kind}:{CanonicalBaseId}";
}

// Relations between original sheet definitions only; actor identity and saved edits stay separate.
internal sealed class NpcEquipmentGroups
{
    private readonly Dictionary<(ObjectKind Kind, uint BaseId), NpcEquipmentGroup> groups = [];

    internal NpcEquipmentGroups(IEnumerable<ModelSearchEntry> definitions)
    {
        var identities = definitions
            .Where(entry => entry.Source is ModelSource.EventNpc or ModelSource.BattleNpc
                && EquipmentCopyData.CanCopy(entry.ModelAppearance))
            .GroupBy(entry => (entry.Source, entry.SourceId))
            .Select(entries => new
            {
                entries.Key.Source,
                entries.Key.SourceId,
                // BNpc names are aliases of a base ID. Compare the entire name set, never
                // connect different IDs transitively through one overlapping alias.
                Names = JsonSerializer.Serialize(entries.Select(entry => entry.Name)
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()),
                Appearances = JsonSerializer.Serialize(entries
                    .Select(entry => Plugin.CreateAppearanceSignature(entry.ModelAppearance))
                    .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray()),
            });

        foreach (var equivalent in identities.GroupBy(entry => (entry.Source, entry.Names, entry.Appearances)))
        {
            var baseIds = equivalent.Select(entry => entry.SourceId).Order().ToImmutableArray();
            if (baseIds.Length < 2)
                continue;

            var kind = equivalent.Key.Source == ModelSource.EventNpc ? ObjectKind.EventNpc : ObjectKind.BattleNpc;
            var group = new NpcEquipmentGroup(kind, baseIds[0], baseIds);
            foreach (var baseId in baseIds)
                groups.Add((kind, baseId), group);
        }
    }

    internal bool TryGet(ObjectKind kind, uint baseId, out NpcEquipmentGroup group)
        => groups.TryGetValue((kind, baseId), out group!);
}
