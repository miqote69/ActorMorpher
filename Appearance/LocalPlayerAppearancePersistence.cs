namespace ActorMorpher.Appearance;

public sealed class LocalPlayerAppearancePersistence
{
    private uint territoryId;
    private bool initialized;
    private bool wasLoggedIn;

    public AppearanceData? Desired { get; private set; }
    public bool ReapplyPending { get; private set; }
    public LogicalActorKey? ActiveReapplyActor { get; private set; }

    public bool UpdateContext(uint territory, bool loggedIn)
    {
        if (!initialized)
        {
            initialized = true;
            territoryId = territory;
            wasLoggedIn = loggedIn;
            return false;
        }

        var territoryChanged = loggedIn && wasLoggedIn && territory != territoryId;
        var loggedOut = wasLoggedIn && !loggedIn;
        if (loggedOut)
            Clear();
        else if (territoryChanged && Desired is not null)
        {
            ReapplyPending = true;
            ActiveReapplyActor = null;
        }

        territoryId = territory;
        wasLoggedIn = loggedIn;
        return territoryChanged || loggedOut;
    }

    public void RecordApplied(AppearanceData desired)
    {
        Desired = desired;
        ReapplyPending = false;
        ActiveReapplyActor = null;
    }

    public void RecordRestored()
        => Clear();

    public bool TryGetPending(out AppearanceData desired)
    {
        desired = Desired!;
        return ReapplyPending && ActiveReapplyActor is null && Desired is not null;
    }

    public bool MarkReapplyStarted(LogicalActorKey actor)
    {
        if (!ReapplyPending || ActiveReapplyActor is not null || Desired is null)
            return false;
        ReapplyPending = false;
        ActiveReapplyActor = actor;
        return true;
    }

    public bool IsReapplyActor(LogicalActorKey actor)
        => ActiveReapplyActor == actor;

    public void CompleteReapply(LogicalActorKey actor, bool succeeded)
    {
        if (ActiveReapplyActor != actor)
            return;
        ActiveReapplyActor = null;
        ReapplyPending = !succeeded && Desired is not null;
    }

    private void Clear()
    {
        Desired = null;
        ReapplyPending = false;
        ActiveReapplyActor = null;
    }
}
