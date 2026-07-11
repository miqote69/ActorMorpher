namespace ActorMorpher.Appearance;

public sealed class AppearanceOverrideStore
{
    private readonly Dictionary<LogicalActorKey, AppearanceOverrideState> states = new();

    public int Count => states.Count;

    public AppearanceOverrideState SetDesired(
        LogicalActorKey actor,
        AppearanceData currentGameAppearance,
        AppearanceData desiredAppearance)
    {
        if (states.TryGetValue(actor, out var existing))
        {
            var updated = existing with
            {
                DesiredData = desiredAppearance,
                Revision = checked(existing.Revision + 1),
            };
            states[actor] = updated;
            return updated;
        }

        var created = new AppearanceOverrideState(currentGameAppearance, desiredAppearance, 1);
        states.Add(actor, created);
        return created;
    }

    public bool TryGet(LogicalActorKey actor, out AppearanceOverrideState state)
        => states.TryGetValue(actor, out state!);

    public bool CompleteRestore(LogicalActorKey actor)
        => states.Remove(actor);

    public void Clear()
        => states.Clear();
}
