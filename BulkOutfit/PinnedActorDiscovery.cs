using Dalamud.Game.ClientState.Objects.Enums;
using System.Collections.Immutable;

namespace ActorMorpher.BulkOutfit;

// New arrivals and minion equipment initialization use the existing pin comparison promptly.
internal sealed class PinnedActorDiscovery
{
    private Dictionary<Availability, ImmutableArray<ulong>> previous = [];

    internal IReadOnlyList<ActorEntry> GetCandidates(IReadOnlyList<ActorEntry> actors,
        Func<ActorEntry, bool> isPinned, long now, ref long nextScanTick)
    {
        var discovered = FindNext(actors, isPinned);
        if (discovered is not null)
            return [discovered];
        if (now < nextScanTick)
            return [];

        nextScanTick = now + 500;
        return actors;
    }

    internal ActorEntry? FindNext(IReadOnlyList<ActorEntry> actors, Func<ActorEntry, bool> isPinned)
    {
        var current = new Dictionary<Availability, ImmutableArray<ulong>>();
        ActorEntry? discovered = null;
        foreach (var actor in actors)
        {
            var snapshot = actor.Current;
            var minion = snapshot.ObjectKind == ObjectKind.Companion;
            var availability = new Availability(actor.Key, snapshot.RepresentationKey,
                snapshot.NpcAttachment, snapshot.MinionOwnerContentId, snapshot.CurrentAppearance is not null,
                minion ? snapshot.EquipmentLoadRevision : 0);
            // LoadEquipment may queue a later render update. Detect that consumption too.
            var equipment = minion ? snapshot.CurrentAppearance?.Equipment ?? [] : [];
            var changed = !previous.TryGetValue(availability, out var lastEquipment)
                || !equipment.AsSpan().SequenceEqual(lastEquipment.AsSpan());
            if (availability.Drawable && changed && isPinned(actor))
            {
                // Leave other arrivals unseen until the next free operation slot.
                if (discovered is not null)
                    continue;
                discovered = actor;
            }
            current[availability] = equipment;
        }
        previous = current;
        return discovered;
    }

    private readonly record struct Availability(LogicalActorKey Actor, ActorRepresentationKey Representation,
        NpcAttachmentKey? Attachment, ulong MinionOwnerContentId, bool Drawable, long EquipmentLoadRevision);
}
