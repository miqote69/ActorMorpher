using Dalamud.Game.ClientState.Objects.Enums;
using System.Collections.Immutable;

namespace ActorMorpher.BulkOutfit;

// New arrivals and their equipment initialization use the existing pin comparison promptly.
internal sealed class PinnedActorDiscovery
{
    private const long ScanIntervalMilliseconds = 500;
    private Dictionary<Availability, Observation> previous = [];
    private long regularScan;

    internal IReadOnlyList<ActorEntry> GetCandidates(IReadOnlyList<ActorEntry> actors,
        Func<ActorEntry, bool> isPinned, long now, ref long nextScanTick)
    {
        var discovered = FindNext(actors, isPinned);
        if (discovered is not null)
            return [discovered];
        if (now < nextScanTick)
            return [];

        nextScanTick = now + ScanIntervalMilliseconds;
        regularScan++;
        return actors.Where(static actor => !actor.Current.MinionSwitchPending).ToArray();
    }

    internal ActorEntry? FindNext(IReadOnlyList<ActorEntry> actors, Func<ActorEntry, bool> isPinned)
    {
        var current = new Dictionary<Availability, Observation>();
        ActorEntry? discovered = null;
        foreach (var actor in actors)
        {
            var snapshot = actor.Current;
            if (snapshot.MinionSwitchPending)
                continue;
            var minion = snapshot.ObjectKind == ObjectKind.Companion;
            var npc = snapshot.ObjectKind is ObjectKind.EventNpc or ObjectKind.BattleNpc;
            var availability = new Availability(actor.Key, snapshot.RepresentationKey,
                snapshot.NpcAttachment, snapshot.MinionOwnerContentId, snapshot.CurrentAppearance is not null,
                minion ? snapshot.EquipmentLoadRevision : 0);
            var known = previous.TryGetValue(availability, out var last);
            // Follow initialization until the ordinary scan takes over. A pin apply
            // can move that scan back while additional equipment is still loading.
            var arrival = last.NpcArrivalScan == regularScan ? last.NpcArrivalScan : null;
            // LoadEquipment may queue a later render update. Detect that consumption too.
            var equipment = minion || npc ? snapshot.CurrentAppearance?.Equipment ?? [] : [];
            var changed = !known
                || ((minion || arrival is not null) && !equipment.AsSpan().SequenceEqual(last.Equipment.AsSpan()))
                || (arrival is not null && snapshot.EquipmentLoadRevision != last.EquipmentLoadRevision);
            if (availability.Drawable && changed && isPinned(actor))
            {
                // Leave other arrivals unseen until the next free operation slot.
                if (discovered is not null)
                {
                    if (arrival is not null)
                        current[availability] = last;
                    continue;
                }
                discovered = actor;
                if (npc && !known)
                    arrival = regularScan;
            }
            current[availability] = new(equipment, snapshot.EquipmentLoadRevision, arrival);
        }
        previous = current;
        return discovered;
    }

    private readonly record struct Availability(LogicalActorKey Actor, ActorRepresentationKey Representation,
        NpcAttachmentKey? Attachment, ulong MinionOwnerContentId, bool Drawable, long EquipmentLoadRevision);

    private readonly record struct Observation(ImmutableArray<ulong> Equipment, long EquipmentLoadRevision,
        long? NpcArrivalScan);
}
