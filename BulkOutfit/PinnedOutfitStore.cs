namespace ActorMorpher.BulkOutfit;

public sealed class PinnedOutfitStore
{
    public const int MaximumPins = 256;

    private readonly Configuration configuration;
    private readonly Action save;

    public PinnedOutfitStore(Configuration configuration, Action save)
    {
        this.configuration = configuration;
        this.save = save;
    }

    public bool IsPinned(ActorEntry actor)
        => configuration.PinnedOutfits.Any(entry => entry.Matches(actor));

    public bool TryGet(ActorEntry actor, out OutfitData outfit)
    {
        var entry = configuration.PinnedOutfits.LastOrDefault(candidate => candidate.Matches(actor));
        if (entry is not null && entry.TryCreateOutfit(out outfit))
            return true;
        outfit = null!;
        return false;
    }

    public void Pin(ActorEntry actor, OutfitData outfit)
    {
        configuration.PinnedOutfits.RemoveAll(entry => entry.Matches(actor));
        configuration.PinnedOutfits.Add(PinnedOutfitConfiguration.Create(actor, outfit));
        if (configuration.PinnedOutfits.Count > MaximumPins)
            configuration.PinnedOutfits.RemoveRange(0, configuration.PinnedOutfits.Count - MaximumPins);
        save();
    }

    public bool Unpin(ActorEntry actor)
    {
        var removed = configuration.PinnedOutfits.RemoveAll(entry => entry.Matches(actor)) > 0;
        if (removed)
            save();
        return removed;
    }

    public static void Normalize(Configuration configuration)
    {
        configuration.PinnedOutfits ??= [];
        configuration.PinnedOutfits = configuration.PinnedOutfits
            .Where(static entry => entry is not null
                && !string.IsNullOrWhiteSpace(entry.ActorName)
                && entry.ActorName.Length <= 128
                && Enum.IsDefined(
                    typeof(Dalamud.Game.ClientState.Objects.Enums.ObjectKind),
                    (Dalamud.Game.ClientState.Objects.Enums.ObjectKind)entry.ObjectKind)
                && entry.TryCreateOutfit(out _))
            .GroupBy(static entry => entry.IdentityKey(), StringComparer.Ordinal)
            .Select(static group => group.Last())
            .TakeLast(MaximumPins)
            .ToList();
    }
}
