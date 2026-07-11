namespace ActorMorpher.BulkOutfit;

public sealed class OutfitOverrideStore
{
    private readonly Dictionary<LogicalActorKey, OutfitOverrideState> states = new();
    public IReadOnlyDictionary<LogicalActorKey, OutfitOverrideState> States
        => new Dictionary<LogicalActorKey, OutfitOverrideState>(states);

    public OutfitOverrideState SetDesired(LogicalActorKey actor, OutfitData currentGameOutfit, OutfitData desiredOutfit)
    {
        if (states.TryGetValue(actor, out var existing))
        {
            var updated = existing with
            {
                Desired = desiredOutfit,
                Revision = checked(existing.Revision + 1),
            };
            states[actor] = updated;
            return updated;
        }

        var created = new OutfitOverrideState(currentGameOutfit, desiredOutfit, 1);
        states.Add(actor, created);
        return created;
    }

    public bool TryGet(LogicalActorKey actor, out OutfitOverrideState state)
        => states.TryGetValue(actor, out state!);

    public bool CompleteRestore(LogicalActorKey actor)
        => states.Remove(actor);

    public void RestoreState(LogicalActorKey actor, OutfitOverrideState? state)
    {
        if (state is null)
            states.Remove(actor);
        else
            states[actor] = state;
    }

    public void Clear()
        => states.Clear();
}
