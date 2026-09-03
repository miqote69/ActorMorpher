namespace ActorMorpher.BulkOutfit;

public sealed class PinnedOutfitStore
{
    private readonly Configuration configuration;
    private readonly Action save;

    public PinnedOutfitStore(Configuration configuration, Action save)
    {
        this.configuration = configuration;
        this.save = save;
    }

    public bool IsPinned(ActorEntry actor)
        => configuration.PinnedOutfits.Any(entry => entry.Matches(actor));

    public int Count => configuration.PinnedOutfits.Count;

    public bool TryGetAppearance(ActorEntry actor, out AppearanceData appearance)
    {
        appearance = configuration.PinnedOutfits.LastOrDefault(entry => entry.Matches(actor))?.Appearance!;
        return appearance is not null;
    }

    public (int Pinned, int Unavailable) PinCurrent(
        IEnumerable<ActorEntry> actors, Func<ActorEntry, AppearanceData?> capture)
    {
        var pinned = 0;
        var unavailable = 0;
        foreach (var actor in actors)
        {
            var appearance = capture(actor);
            if (appearance is null)
            {
                unavailable++;
                continue;
            }
            configuration.PinnedOutfits.RemoveAll(entry => entry.Matches(actor));
            configuration.PinnedOutfits.Add(PinnedOutfitConfiguration.Create(actor, appearance));
            pinned++;
        }
        if (pinned != 0)
            save();
        return (pinned, unavailable);
    }

    public int UnpinAll()
    {
        var count = Count;
        if (count != 0)
        {
            configuration.PinnedOutfits.Clear();
            save();
        }
        return count;
    }

    internal static bool AppearanceEquals(AppearanceData left, AppearanceData right)
        => left.ModelCharaId == right.ModelCharaId && left.Category == right.Category
            && left.Customize.AsSpan().SequenceEqual(right.Customize.AsSpan())
            && left.Equipment.AsSpan().SequenceEqual(right.Equipment.AsSpan())
            && left.ModelScale == right.ModelScale && left.Mainhand == right.Mainhand
            && left.Offhand == right.Offhand && left.VisorToggled == right.VisorToggled
            && left.FacewearModelId == right.FacewearModelId && left.HatVisible == right.HatVisible
            && OutfitDataValueComparer.AreEqual(EquipmentDisplayFormatting.CreateHumanOutfit(left),
                EquipmentDisplayFormatting.CreateHumanOutfit(right));

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
        save();
    }

    public bool Unpin(ActorEntry actor)
    {
        var removed = configuration.PinnedOutfits.RemoveAll(entry => entry.Matches(actor)) > 0;
        if (removed)
            save();
        return removed;
    }

    internal void UpdateSelectedEquipment(ActorEntry actor, EquipmentChoiceKey choice, OutfitData applied)
    {
        var pin = configuration.PinnedOutfits.LastOrDefault(entry => entry.Matches(actor));
        if (pin is null || !pin.TryCreateOutfit(out var pinned))
            return;
        var updated = choice.Slot == 10 ? pinned with { Facewear = applied.Facewear }
            : pinned with { Equipment = pinned.Equipment.SetItem(choice.Slot, applied.Equipment[choice.Slot]) };
        if (pin.Appearance is { } appearance)
            pin.Appearance = appearance.WithOutfit(updated.Equipment.Select(ActorRegistry.ToEquipmentModelValue),
                updated.VisorToggled, updated.Facewear.IsAvailable ? updated.Facewear.ModelId : null,
                updated.HatVisible) with { ColoredEquipment = updated.Equipment };
        else if (choice.Slot == 10)
        {
            pin.FacewearAvailable = updated.Facewear.IsAvailable;
            pin.FacewearModelId = updated.Facewear.ModelId;
        }
        else
        {
            var armor = updated.Equipment[choice.Slot];
            pin.Equipment[choice.Slot] = new PinnedArmorConfiguration
            {
                Set = armor.Set, Variant = armor.Variant, Stain1 = armor.Stain1, Stain2 = armor.Stain2,
                Color1 = armor.Color1, Color2 = armor.Color2,
            };
        }
        save();
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
                && (entry.Appearance is not null || entry.TryCreateOutfit(out _)))
            .GroupBy(static entry => entry.IdentityKey(), StringComparer.Ordinal)
            .Select(static group => group.Last())
            .ToList();
    }
}
