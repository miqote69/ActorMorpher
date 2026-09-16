namespace ActorMorpher.BulkOutfit;

[Serializable]
public sealed class SavedGlamourPlate
{
    public uint[] Items { get; set; } = new uint[12];
    public byte[] Stain1 { get; set; } = new byte[12];
    public byte[] Stain2 { get; set; } = new byte[12];
}

[Serializable]
public sealed class SavedGlamourPlates
{
    public DateTimeOffset AcquiredAt { get; set; }
    public SavedGlamourPlate[] Plates { get; set; } = [];
}

// Native buffers have no Content ID. Bind their lifetime to the login context,
// and do not adopt a still-loaded previous character's buffer after a switch.
internal sealed class GlamourPlateCache(Dictionary<ulong, SavedGlamourPlates> entries, Action save)
{
    private ulong owner;
    private uint territory;
    private bool initialized;
    private bool nativeBelongsToOwner;
    private bool eligible;
    private bool wasLoaded;
    private long session;
    private SavedGlamourPlates? lastObserved;
    public bool IsWaiting { get; private set; }

    public SavedGlamourPlates? Get(ulong contentId)
        => contentId != 0 && entries.TryGetValue(contentId, out var data) ? data : null;

    public void EndSession()
    {
        initialized = true;
        session++;
        owner = 0;
        eligible = false;
        nativeBelongsToOwner = false;
        wasLoaded = false;
        lastObserved = null;
        IsWaiting = false;
    }

    public void Update(ulong contentId, uint area, bool canRequest, bool loaded, bool requested,
        Func<bool> request, Func<SavedGlamourPlates?, SavedGlamourPlates> capture)
    {
        if (contentId == 0)
        {
            EndSession();
            return;
        }
        if (!initialized || owner != contentId)
        {
            // On plugin startup an already-loaded buffer belongs to the current
            // login. Across logins, wait for native invalidation before binding.
            nativeBelongsToOwner = !initialized && loaded;
            session++;
            owner = contentId;
            initialized = true;
            eligible = false;
            wasLoaded = false;
            lastObserved = null;
            IsWaiting = false;
        }
        if (territory != area)
        {
            territory = area;
            eligible = false;
            IsWaiting = false;
        }
        if (loaded && nativeBelongsToOwner)
        {
            var previous = Get(owner);
            var observedOwner = owner;
            var observedSession = session;
            var current = capture(wasLoaded ? lastObserved : null);
            if (owner != observedOwner || session != observedSession)
                return;
            var changed = !ReferenceEquals(lastObserved, current);
            lastObserved = current;
            wasLoaded = true;
            IsWaiting = false;
            eligible = canRequest;
            if (changed)
            {
                entries[owner] = current;
                try
                {
                    save();
                }
                catch
                {
                    // The dictionary is the persisted source used by imports and
                    // timestamps. A failed write must not publish a saved result.
                    if (previous == null) entries.Remove(observedOwner);
                    else entries[observedOwner] = previous;
                    throw;
                }
            }
        }
        else
        {
            wasLoaded = false;
            if (IsWaiting && !requested)
                IsWaiting = false;
        }

        // One request when entering an eligible context; no repeated sends while
        // waiting or after a failed response. Loaded data needs no new request.
        if (!canRequest)
            eligible = false;
        if (canRequest && !eligible && !loaded && !requested)
        {
            var requestedOwner = owner;
            var requestedSession = session;
            eligible = true;
            var invoked = request();
            if (owner == requestedOwner && session == requestedSession && invoked)
            {
                nativeBelongsToOwner = true;
                IsWaiting = true;
            }
        }
    }
}
