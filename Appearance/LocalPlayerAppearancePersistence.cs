namespace ActorMorpher.Appearance;

public sealed class LocalPlayerAppearancePersistence
{
    private readonly HashSet<ActorRepresentationKey> observedRepresentations = [];
    private uint territoryId;
    private bool initialized;
    private bool wasLoggedIn;
    private bool isArmed;
    private long lastEvaluatedPublicationVersion;

    public LogicalActorKey? Actor { get; private set; }
    public ActorRepresentationKey? CurrentRepresentation { get; private set; }
    public AppearanceData? CurrentAppearance { get; private set; }
    public bool IsActive => Actor is not null;
    public bool IsArmed => isArmed;

    public bool UpdateContext(uint territory, bool loggedIn)
    {
        if (!initialized)
        {
            initialized = true;
            territoryId = territory;
            wasLoggedIn = loggedIn;
            return false;
        }

        var loggedOut = wasLoggedIn && !loggedIn;
        if (loggedOut)
            Clear();

        var territoryChanged = loggedIn && wasLoggedIn && territory != territoryId;
        territoryId = territory;
        wasLoggedIn = loggedIn;
        return territoryChanged || loggedOut;
    }

    public void RecordApplied(
        LogicalActorKey actor,
        ActorRepresentationKey representation,
        long publicationVersion)
        => RecordApplied(actor, representation, publicationVersion, null);

    public void RecordApplied(
        LogicalActorKey actor,
        ActorRepresentationKey representation,
        long publicationVersion,
        AppearanceData? appearance)
    {
        Actor = actor;
        CurrentRepresentation = representation;
        CurrentAppearance = appearance;
        observedRepresentations.Clear();
        observedRepresentations.Add(representation);
        isArmed = false;
        lastEvaluatedPublicationVersion = publicationVersion;
    }

    public AppearanceData? GetRetainedAppearance(ActorRepresentationKey representation)
        => CurrentRepresentation == representation ? CurrentAppearance : null;

    public void UpdateRetainedAppearance(
        ActorRepresentationKey representation,
        AppearanceData appearance,
        long publicationVersion)
    {
        if (Actor is null || CurrentRepresentation != representation)
            return;

        CurrentAppearance = appearance;
        isArmed = ActorRegistry.IsCompleteCurrentAppearance(appearance);
        lastEvaluatedPublicationVersion = publicationVersion;
    }

    public void UpdatePublishedAppearance(
        ActorRepresentationKey source,
        bool isComplete,
        long publicationVersion)
    {
        if (Actor is null
            || CurrentRepresentation != source
            || publicationVersion <= lastEvaluatedPublicationVersion)
            return;

        isArmed = isComplete;
        if (!isComplete)
            CurrentAppearance = null;
        lastEvaluatedPublicationVersion = publicationVersion;
    }

    public void UpdatePublishedAppearance(
        ActorRepresentationKey source,
        AppearanceData? appearance,
        long publicationVersion)
    {
        if (Actor is null
            || CurrentRepresentation != source
            || publicationVersion <= lastEvaluatedPublicationVersion)
            return;

        var isComplete = ActorRegistry.IsCompleteCurrentAppearance(appearance);
        CurrentAppearance = isComplete ? appearance : null;
        isArmed = isComplete;
        lastEvaluatedPublicationVersion = publicationVersion;
    }

    public bool TryGetArmedSource(
        out LogicalActorKey actor,
        out ActorRepresentationKey source)
    {
        actor = default;
        source = default;
        if (Actor is not { } currentActor
            || CurrentRepresentation is not { } currentRepresentation
            || !isArmed)
            return false;

        actor = currentActor;
        source = currentRepresentation;
        return true;
    }

    public void ObserveRepresentation(ActorRepresentationKey representation)
    {
        if (Actor is null
            || CurrentRepresentation is not { } currentRepresentation
            || currentRepresentation == representation
            || !observedRepresentations.Add(representation))
            return;

    }

    public bool CanBeginTransfer(ActorRepresentationKey representation)
        => TryGetArmedSource(out _, out var source)
        && source != representation
        && !observedRepresentations.Contains(representation);

    public bool TryBeginTransfer(
        ActorRepresentationKey source,
        ActorRepresentationKey representation,
        long publicationVersion,
        out LogicalActorKey actor)
    {
        actor = default;
        if (!TryGetArmedSource(out var currentActor, out var currentSource)
            || currentSource != source
            || source == representation
            || publicationVersion < lastEvaluatedPublicationVersion
            || !observedRepresentations.Add(representation))
            return false;

        actor = currentActor;
        CurrentRepresentation = representation;
        isArmed = false;
        lastEvaluatedPublicationVersion = publicationVersion;
        return true;
    }

    public void RecordRestored()
        => Clear();

    private void Clear()
    {
        Actor = null;
        CurrentRepresentation = null;
        CurrentAppearance = null;
        observedRepresentations.Clear();
        isArmed = false;
        lastEvaluatedPublicationVersion = 0;
    }
}
