namespace ActorMorpher.BulkOutfit;

public sealed partial class PinnedOutfitStore
{
    private readonly Configuration configuration;
    private readonly Action save;
    private readonly Func<ulong> getOwner;

    public PinnedOutfitStore(Configuration configuration, Action save, Func<ulong> getOwner)
        : this(configuration, save, getOwner, null) { }

    internal PinnedOutfitStore(Configuration configuration, Action save, Func<ulong> getOwner,
        Func<Dalamud.Game.ClientState.Objects.Enums.ObjectKind, uint, NpcEquipmentGroup?>? getNpcEquipmentGroup)
    {
        this.configuration = configuration;
        this.save = save;
        this.getOwner = getOwner;
        this.getNpcEquipmentGroup = getNpcEquipmentGroup;
    }

    private List<PinnedOutfitConfiguration>? CurrentPins(bool create = false)
        => PinsForOwner(getOwner(), create);

    private List<PinnedOutfitConfiguration>? PinsForOwner(ulong owner, bool create = false)
    {
        if (owner == 0)
            return null;
        if (configuration.PinnedOutfitsByCharacter.TryGetValue(owner, out var pins))
            return pins;
        if (!create)
            return null;
        pins = [];
        configuration.PinnedOutfitsByCharacter.Add(owner, pins);
        return pins;
    }

    public bool IsPinned(ActorEntry actor)
        => CurrentPins()?.Any(entry => entry.Matches(actor)) == true;

    // Rebuilt from current pins for each UI draw; no result survives an identity or pin change.
    public HashSet<LogicalActorKey> GetPinnedActorKeysForDisplay(IReadOnlyList<ActorEntry> actors)
    {
        var pins = CurrentPins();
        if (pins is null)
            return [];
        var candidates = pins.ToLookup(static pin => pin.DisplayLookupId);
        var pinned = new HashSet<LogicalActorKey>();
        foreach (var actor in actors)
        {
            bool Matches(ulong? id) => candidates[id].Any(pin => pin.Matches(actor));
            if (Matches(null)
                || Matches(actor.Key.GameObjectId)
                || Matches(actor.Key.BaseId)
                || Matches(actor.BaseId)
                || (actor.Current.NpcAttachment is { } attachment && Matches(attachment.OwnerContentId))
                || (actor.Key.Continuity is { } identity && Matches(identity.Id))
                || actor.Representations.Any(snapshot => snapshot.ContinuityKey is { } continuity && Matches(continuity.Id)))
                pinned.Add(actor.Key);
        }
        return pinned;
    }

    public int Count => CurrentPins()?.Count ?? 0;

    public bool TryGetAppearance(ActorEntry actor, out AppearanceData appearance)
    {
        appearance = CurrentPins()?.LastOrDefault(entry => entry.Matches(actor))?.Appearance!;
        return appearance is not null;
    }

    public (int Pinned, int Unavailable) PinCurrent(
        IEnumerable<ActorEntry> actors, Func<ActorEntry, AppearanceData?> capture)
    {
        var owner = getOwner();
        var pins = PinsForOwner(owner, create: true);
        if (pins is null)
            return (0, actors.Count());
        var pinned = 0;
        var unavailable = 0;
        LoadSharedEquipment(owner, pins);
        foreach (var actor in actors)
        {
            var appearance = capture(actor);
            if (appearance is null)
            {
                unavailable++;
                continue;
            }
            pins.RemoveAll(entry => entry.Matches(actor));
            pins.Add(PinnedOutfitConfiguration.Create(actor, appearance));
            PinSharedEquipment(owner, pins, actor);
            pinned++;
        }
        if (pinned != 0)
            save();
        return (pinned, unavailable);
    }

    public int UnpinAll()
    {
        var owner = getOwner();
        var pins = PinsForOwner(owner);
        var count = pins?.Count ?? 0;
        sharedEquipment.Remove(owner);
        if (count != 0)
        {
            pins!.Clear();
            save();
        }
        return count;
    }

    internal static bool AppearanceEquals(AppearanceData left, AppearanceData right)
        => left.ModelCharaId == right.ModelCharaId && left.Category == right.Category
            && left.Customize.AsSpan().SequenceEqual(right.Customize.AsSpan())
            && left.CustomizeColors.AsSpan().SequenceEqual(right.CustomizeColors.AsSpan())
            && left.Equipment.AsSpan().SequenceEqual(right.Equipment.AsSpan())
            && left.ModelScale == right.ModelScale && left.Mainhand == right.Mainhand
            && left.Offhand == right.Offhand && left.VisorToggled == right.VisorToggled
            && left.MainhandDyes == right.MainhandDyes && left.OffhandDyes == right.OffhandDyes
            && left.FacewearModelId == right.FacewearModelId && left.HatVisible == right.HatVisible
            && left.VieraEarsHidden == right.VieraEarsHidden
            && OutfitDataValueComparer.AreEqual(EquipmentDisplayFormatting.CreateHumanOutfit(left),
                EquipmentDisplayFormatting.CreateHumanOutfit(right));

    internal static bool CanMaintainWithOutfit(AppearanceData current, AppearanceData pinned)
        => current.Category == ModelCategory.Human && pinned.Category == ModelCategory.Human
            && current.ModelCharaId == pinned.ModelCharaId
            && current.Customize.AsSpan().SequenceEqual(pinned.Customize.AsSpan())
            && current.CustomizeColors.AsSpan().SequenceEqual(pinned.CustomizeColors.AsSpan())
            && current.ModelScale == pinned.ModelScale
            && (current.Mainhand & WeaponSelection.ModelMask) == (pinned.Mainhand & WeaponSelection.ModelMask)
            && (current.Offhand & WeaponSelection.ModelMask) == (pinned.Offhand & WeaponSelection.ModelMask);

    public bool TryGet(ActorEntry actor, out OutfitData outfit)
    {
        var entry = CurrentPins()?.LastOrDefault(candidate => candidate.Matches(actor));
        if (entry is not null && entry.TryCreateOutfit(out outfit))
            return true;
        outfit = null!;
        return false;
    }

    public void Pin(ActorEntry actor, OutfitData outfit)
    {
        var owner = getOwner();
        var pins = PinsForOwner(owner, create: true);
        if (pins is null)
            return;
        LoadSharedEquipment(owner, pins);
        pins.RemoveAll(entry => entry.Matches(actor));
        pins.Add(PinnedOutfitConfiguration.Create(actor, outfit));
        PinSharedEquipment(owner, pins, actor);
        save();
    }

    public bool Unpin(ActorEntry actor)
    {
        var owner = getOwner();
        var pins = PinsForOwner(owner);
        var removed = pins?.RemoveAll(entry => entry.Matches(actor)) > 0;
        var sharedPinsChanged = ClearSharedNpcEquipment(owner, pins, actor);
        if (removed || sharedPinsChanged)
            save();
        return removed;
    }

    internal void UpdateSelectedEquipment(ActorEntry actor, EquipmentChoiceKey choice, OutfitData applied)
    {
        var pin = CurrentPins()?.LastOrDefault(entry => entry.Matches(actor));
        if (pin is null || !pin.TryCreateOutfit(out var pinned))
            return;
        var updated = choice.Slot == 10 ? pinned with { Facewear = applied.Facewear }
            : pinned with { Equipment = pinned.Equipment.SetItem(choice.Slot, applied.Equipment[choice.Slot]) };
        if (pin.Appearance is { } appearance)
            pin.Appearance = appearance.WithOutfit(updated.Equipment.Select(ActorRegistry.ToEquipmentModelValue),
                updated.VisorToggled, updated.Facewear.IsAvailable ? updated.Facewear.ModelId : null,
                updated.HatVisible) with { ColoredEquipment = updated.Equipment, VieraEarsHidden = updated.VieraEarsHidden };
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

    internal void UpdateEquipment(ActorEntry actor, EquipmentCopyData equipment)
    {
        var pin = CurrentPins()?.LastOrDefault(entry => entry.Matches(actor));
        if (pin is null) return;
        ApplyEquipmentToPin(pin, equipment);
        save();
    }

    private static void ApplyEquipmentToPin(PinnedOutfitConfiguration pin, EquipmentCopyData equipment)
    {
        if (pin.Appearance is { } appearance)
            pin.Appearance = equipment.ApplyTo(appearance);
        else
        {
            var outfit = equipment.Outfit;
            // Older outfit-only pins keep their original scope; copying equipment
            // does not turn them into pins for customization or model/scale.
            pin.Equipment = outfit.Equipment.Select(armor => new PinnedArmorConfiguration
            {
                Set = armor.Set, Variant = armor.Variant, Stain1 = armor.Stain1, Stain2 = armor.Stain2,
                Color1 = armor.Color1, Color2 = armor.Color2,
            }).ToList();
            if (equipment.IncludesHeadSettings || outfit.Facewear.IsAvailable)
            {
                pin.FacewearAvailable = outfit.Facewear.IsAvailable;
                pin.FacewearModelId = outfit.Facewear.ModelId;
            }
            pin.HatVisible = outfit.HatVisible;
            pin.VisorToggled = outfit.VisorToggled;
            if (equipment.IncludesHeadSettings || outfit.VieraEarsHidden is not null)
                pin.VieraEarsHidden = outfit.VieraEarsHidden;
        }
    }

    internal void UpdateCustomize(ActorEntry actor, System.Collections.Immutable.ImmutableArray<byte> customize,
        System.Collections.Immutable.ImmutableArray<CustomizeColorOverride> colors = default)
    {
        var pin = CurrentPins()?.LastOrDefault(entry => entry.Matches(actor));
        if (pin?.Appearance is not { } appearance)
            return;
        pin.Appearance = appearance with { Customize = customize,
            CustomizeColors = colors.IsDefault ? appearance.CustomizeColors : colors };
        save();
    }

    internal void UpdateSelectedWeapon(ActorEntry actor, bool offhand, ulong weapon, WeaponDyes? dyes = null)
    {
        var pin = CurrentPins()?.LastOrDefault(entry => entry.Matches(actor));
        if (pin?.Appearance is not { } appearance)
            return;
        pin.Appearance = offhand
            ? appearance with { Offhand = weapon, OffhandDyes = dyes ?? appearance.OffhandDyes }
            : appearance with { Mainhand = weapon, MainhandDyes = dyes ?? appearance.MainhandDyes };
        save();
    }

    internal void UpdateHeadgearSettings(ActorEntry actor, OutfitData applied)
    {
        var pin = CurrentPins()?.LastOrDefault(entry => entry.Matches(actor));
        if (pin is null) return;
        if (pin.Appearance is { } appearance)
            pin.Appearance = appearance with { HatVisible = applied.HatVisible,
                VisorToggled = applied.VisorToggled, VieraEarsHidden = applied.VieraEarsHidden };
        else
        {
            pin.HatVisible = applied.HatVisible;
            pin.VisorToggled = applied.VisorToggled;
            pin.VieraEarsHidden = applied.VieraEarsHidden;
        }
        save();
    }

    public static void Normalize(Configuration configuration)
    {
        configuration.PinnedOutfits = NormalizePins(configuration.PinnedOutfits);
        configuration.PinnedOutfitsByCharacter ??= [];
        foreach (var owner in configuration.PinnedOutfitsByCharacter.Keys.ToArray())
            configuration.PinnedOutfitsByCharacter[owner] = NormalizePins(configuration.PinnedOutfitsByCharacter[owner]);
    }

    internal static List<PinnedOutfitConfiguration> NormalizePins(List<PinnedOutfitConfiguration>? pins)
        => (pins ?? [])
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
