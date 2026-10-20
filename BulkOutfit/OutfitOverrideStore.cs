namespace ActorMorpher.BulkOutfit;

public sealed class OutfitOverrideStore
{
    private readonly Func<ulong> getOwner;
    private readonly Dictionary<ulong, Dictionary<LogicalActorKey, OutfitOverrideState>> characters = new();

    public OutfitOverrideStore(Func<ulong>? getOwner = null)
        => this.getOwner = getOwner ?? (() => 0);

    private Dictionary<LogicalActorKey, OutfitOverrideState> GetStates(ulong owner)
    {
        if (!characters.TryGetValue(owner, out var states))
            characters.Add(owner, states = new());
        return states;
    }

    public IReadOnlyDictionary<LogicalActorKey, OutfitOverrideState> States
        => new Dictionary<LogicalActorKey, OutfitOverrideState>(GetStates(getOwner()));

    public OutfitOverrideState SetDesired(LogicalActorKey actor, OutfitData currentGameOutfit, OutfitData desiredOutfit)
    {
        var states = GetStates(getOwner());
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
        => TryGet(getOwner(), actor, out state);

    internal bool TryGet(ulong owner, LogicalActorKey actor, out OutfitOverrideState state)
        => GetStates(owner).TryGetValue(actor, out state!);

    public bool CompleteRestore(LogicalActorKey actor)
        => CompleteRestore(getOwner(), actor);

    internal bool CompleteRestore(ulong owner, LogicalActorKey actor)
        => GetStates(owner).Remove(actor);

    public void RestoreState(LogicalActorKey actor, OutfitOverrideState? state)
    {
        var states = GetStates(getOwner());
        if (state is null)
            states.Remove(actor);
        else
            states[actor] = state;
    }

    public void Clear()
        => GetStates(getOwner()).Clear();
}
