namespace ActorMorpher.BulkOutfit;

// Detect newly available pin targets cheaply; unchanged targets keep the normal scan interval.
internal sealed class PinnedActorDiscovery
{
    private HashSet<Availability> previous = [];

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
        var current = new HashSet<Availability>();
        ActorEntry? discovered = null;
        foreach (var actor in actors)
        {
            var snapshot = actor.Current;
            var availability = new Availability(actor.Key, snapshot.RepresentationKey,
                snapshot.NpcAttachment, snapshot.CurrentAppearance is not null);
            if (availability.Drawable && !previous.Contains(availability) && isPinned(actor))
            {
                // Leave other arrivals unseen until the next free operation slot.
                if (discovered is not null)
                    continue;
                discovered = actor;
            }
            current.Add(availability);
        }
        previous = current;
        return discovered;
    }

    private readonly record struct Availability(LogicalActorKey Actor, ActorRepresentationKey Representation,
        NpcAttachmentKey? Attachment, bool Drawable);
}
