using Dalamud.Game.ClientState.Objects.Enums;

namespace ActorMorpher.BulkOutfit;

public sealed partial class PinnedOutfitStore
{
    private readonly Func<ObjectKind, uint, NpcEquipmentGroup?>? getNpcEquipmentGroup;
    private readonly Dictionary<ulong, Dictionary<string, SharedEquipment>> sharedEquipment = [];
    private readonly HashSet<ulong> loadedSharedOwners = [];

    private sealed class SharedEquipment(NpcEquipmentGroup group, EquipmentCopyData? equipment,
        CustomizeCopyData? customize = null)
    {
        internal readonly NpcEquipmentGroup Group = group;
        internal EquipmentCopyData? Equipment = equipment;
        internal CustomizeCopyData? Customize = customize;
        internal readonly HashSet<(LogicalActorKey, ActorRepresentationKey)> Attempted = [];
    }

    private void LoadSharedEquipment(ulong owner, List<PinnedOutfitConfiguration>? pins)
    {
        if (owner == 0 || getNpcEquipmentGroup is null || !loadedSharedOwners.Add(owner)) return;
        foreach (var pin in pins ?? [])
        {
            if (pin.NpcGroupSharingStopped
                || getNpcEquipmentGroup((ObjectKind)pin.ObjectKind, pin.BaseId) is not { } group)
                continue;
            var (equipment, customize) = ReadSharedPin(pin);
            if (equipment is null && customize is null) continue;
            if (!sharedEquipment.TryGetValue(owner, out var groups)) sharedEquipment[owner] = groups = [];
            // Existing pins supply their stored components without an opt-in flag.
            if (!groups.TryGetValue(group.Identity, out var shared))
                groups[group.Identity] = shared = new(group, equipment, customize);
            shared.Equipment = PreserveStoredWeapons(shared.Equipment, equipment);
            shared.Customize ??= customize;
        }
        if (sharedEquipment.TryGetValue(owner, out var loaded))
        {
            var changed = false;
            foreach (var shared in loaded.Values)
                changed |= UpdateSharedPins(pins, shared);
            if (changed) save();
        }
    }

    private static (EquipmentCopyData? Equipment, CustomizeCopyData? Customize) ReadSharedPin(
        PinnedOutfitConfiguration pin)
    {
        var equipment = pin.Appearance is { } appearance && EquipmentCopyData.TryCreate(appearance, out var data)
            ? data : pin.TryCreateOutfit(out var outfit)
                ? pin.SharedWeapons is { } weapons
                    ? EquipmentCopyData.FromOutfit(outfit, weapons.Mainhand, weapons.Offhand,
                        weapons.MainhandDyes, weapons.OffhandDyes)
                    : EquipmentCopyData.FromOutfit(outfit) : null;
        var customize = pin.Appearance is { } full && CustomizeCopyData.TryCreate(full, out var copied)
            ? copied : pin.SharedCustomize;
        return (equipment, customize);
    }

    private static EquipmentCopyData? PreserveStoredWeapons(EquipmentCopyData? equipment,
        EquipmentCopyData? stored)
        => equipment is { IncludesWeapons: false } && stored is { IncludesWeapons: true }
            ? EquipmentCopyData.FromOutfit(equipment.Outfit, stored.Mainhand, stored.Offhand,
                stored.MainhandDyes, stored.OffhandDyes)
            : equipment ?? stored;

    private static SharedEquipment? ReadRetainedGroupComponents(List<PinnedOutfitConfiguration>? pins,
        NpcEquipmentGroup group)
    {
        SharedEquipment? retained = null;
        foreach (var pin in pins ?? [])
        {
            if (!BelongsTo(pin, group)) continue;
            var (equipment, customize) = ReadSharedPin(pin);
            retained ??= new(group, null);
            retained.Equipment = PreserveStoredWeapons(retained.Equipment, equipment);
            retained.Customize ??= customize;
        }
        return retained;
    }

    private SharedEquipment? FindSharedEquipment(ActorEntry actor, out ulong owner)
    {
        owner = getOwner();
        LoadSharedEquipment(owner, PinsForOwner(owner));
        return owner != 0 && sharedEquipment.TryGetValue(owner, out var groups)
            && getNpcEquipmentGroup?.Invoke(actor.Kind, actor.BaseId) is { } group
                ? groups.GetValueOrDefault(group.Identity) : null;
    }

    internal bool HasSharedNpcEquipment
    {
        get
        {
            var owner = getOwner();
            LoadSharedEquipment(owner, PinsForOwner(owner));
            return owner != 0 && sharedEquipment.TryGetValue(owner, out var groups) && groups.Count != 0;
        }
    }

    private static bool BelongsTo(PinnedOutfitConfiguration pin, NpcEquipmentGroup group)
        => pin.ObjectKind == (int)group.Kind && group.BaseIds.Contains(pin.BaseId);

    private bool HasSharedPin(ulong owner, SharedEquipment shared)
        => PinsForOwner(owner)?.Any(pin => !pin.NpcGroupSharingStopped
            && BelongsTo(pin, shared.Group)) == true;

    internal bool IsNpcEquipmentSharingCandidate(ActorEntry actor)
        => FindSharedEquipment(actor, out var owner) is { } shared
            && (HasSharedPin(owner, shared) || !shared.Attempted.Contains((actor.Key, actor.Current.RepresentationKey)));

    internal bool TryGetSharedNpcEquipment(ActorEntry actor, out EquipmentCopyData equipment)
    {
        var shared = FindSharedEquipment(actor, out var owner);
        equipment = shared?.Equipment!;
        return equipment is not null
            && (HasSharedPin(owner, shared!) || !shared!.Attempted.Contains((actor.Key, actor.Current.RepresentationKey)));
    }

    internal bool TryGetSharedNpcCustomize(ActorEntry actor, out CustomizeCopyData customize)
    {
        var shared = FindSharedEquipment(actor, out var owner);
        customize = shared?.Customize!;
        return customize is not null
            && (HasSharedPin(owner, shared!) || !shared!.Attempted.Contains((actor.Key, actor.Current.RepresentationKey)));
    }

    // A bulk pin of an absent Actor must use the current group components even
    // if the game already applied them, or the Actor is no longer available.
    internal AppearanceData? ApplyCurrentNpcSharingToRetainedPin(ActorEntry actor, AppearanceData appearance)
    {
        if (!sharedEquipment.TryGetValue(getOwner(), out var groups)
            || getNpcEquipmentGroup?.Invoke(actor.Kind, actor.BaseId) is not { } group
            || !groups.TryGetValue(group.Identity, out var shared))
            return null;
        if (shared.Equipment is { } equipment) appearance = equipment.ApplyTo(appearance);
        if (shared.Customize is { } customize) appearance = customize.ApplyTo(appearance);
        return appearance;
    }

    internal bool WasSharedNpcChangeAttempted(ActorEntry actor)
        => FindSharedEquipment(actor, out _)?.Attempted.Contains((actor.Key, actor.Current.RepresentationKey)) == true;

    internal void MarkSharedNpcEquipmentAttempted(ActorEntry actor)
        => FindSharedEquipment(actor, out _)?.Attempted.Add((actor.Key, actor.Current.RepresentationKey));

    internal bool ShareNpcEquipment(ActorEntry actor, EquipmentCopyData equipment)
        => ShareNpcChanges(actor, equipment, null);

    internal bool ShareNpcCustomize(ActorEntry actor, CustomizeCopyData customize)
        => ShareNpcChanges(actor, null, customize);

    // Model/scale and actor identity remain individual. Each explicit edit replaces
    // only its own component, preserving any previously shared other component.
    private bool ShareNpcChanges(ActorEntry actor, EquipmentCopyData? equipment, CustomizeCopyData? customize)
    {
        var owner = getOwner();
        if (owner == 0 || getNpcEquipmentGroup?.Invoke(actor.Kind, actor.BaseId) is not { } group) return false;
        var pins = PinsForOwner(owner);
        LoadSharedEquipment(owner, pins);
        if (!sharedEquipment.TryGetValue(owner, out var groups)) sharedEquipment[owner] = groups = [];
        var previous = groups.GetValueOrDefault(group.Identity) ?? ReadRetainedGroupComponents(pins, group);
        var shared = new SharedEquipment(group, PreserveStoredWeapons(equipment, previous?.Equipment),
            customize ?? previous?.Customize);
        // The edit has applied its own component on the source. A retained other
        // component still needs the normal comparison on that source as well.
        if ((equipment is not null || previous?.Equipment is null)
            && (customize is not null || previous?.Customize is null))
            shared.Attempted.Add((actor.Key, actor.Current.RepresentationKey));
        groups[group.Identity] = shared;
        var saved = UpdateSharedPins(pins, shared);
        if (saved) save();
        return true;
    }

    private void PinSharedEquipment(ulong owner, List<PinnedOutfitConfiguration> pins, ActorEntry actor)
    {
        if (getNpcEquipmentGroup?.Invoke(actor.Kind, actor.BaseId) is not { } group
            || pins.LastOrDefault(pin => pin.Matches(actor)) is not { } pinned) return;
        var saved = ReadSharedPin(pinned);
        if (saved.Equipment is null && saved.Customize is null) return;
        if (!sharedEquipment.TryGetValue(owner, out var groups)) sharedEquipment[owner] = groups = [];
        var previous = groups.GetValueOrDefault(group.Identity) ?? ReadRetainedGroupComponents(pins, group);
        var shared = new SharedEquipment(group, PreserveStoredWeapons(saved.Equipment, previous?.Equipment),
            saved.Customize ?? previous?.Customize);
        groups[group.Identity] = shared;
        UpdateSharedPins(pins, shared);
    }

    private bool ClearSharedNpcEquipment(ulong owner, List<PinnedOutfitConfiguration>? pins, ActorEntry actor)
    {
        if (owner == 0
            || getNpcEquipmentGroup?.Invoke(actor.Kind, actor.BaseId) is not { } group)
            return false;
        if (sharedEquipment.TryGetValue(owner, out var groups)) groups.Remove(group.Identity);
        var changed = false;
        foreach (var pin in pins ?? [])
        {
            if (!BelongsTo(pin, group)) continue;
            changed |= !pin.NpcGroupSharingStopped || pin.ShareNpcEquipment || pin.ShareNpcCustomize
                || pin.SharedWeapons is not null || pin.SharedCustomize is not null;
            pin.NpcGroupSharingStopped = true;
            pin.ShareNpcEquipment = false;
            pin.SharedWeapons = null;
            pin.ShareNpcCustomize = false;
            pin.SharedCustomize = null;
        }
        return changed;
    }

    private static bool UpdateSharedPins(List<PinnedOutfitConfiguration>? pins, SharedEquipment shared)
    {
        var changed = false;
        foreach (var pin in pins ?? [])
        {
            if (!BelongsTo(pin, shared.Group)) continue;
            var beforeAppearance = pin.Appearance;
            pin.TryCreateOutfit(out var beforeOutfit);
            var beforeWeapons = pin.SharedWeapons;
            var beforeCustomize = pin.SharedCustomize;
            var beforeEquipmentFlag = pin.ShareNpcEquipment;
            var beforeCustomizeFlag = pin.ShareNpcCustomize;
            var wasStopped = pin.NpcGroupSharingStopped;
            if (shared.Equipment is { } equipment)
            {
                ApplyEquipmentToPin(pin, equipment);
                pin.SharedWeapons = pin.Appearance is null && equipment.IncludesWeapons
                    ? new(equipment.Mainhand, equipment.Offhand, equipment.MainhandDyes, equipment.OffhandDyes) : null;
                pin.ShareNpcEquipment = true;
            }
            if (shared.Customize is { } customize)
            {
                if (pin.Appearance is { } appearance)
                    pin.Appearance = customize.ApplyTo(appearance);
                pin.SharedCustomize = pin.Appearance is null ? customize : null;
                pin.ShareNpcCustomize = true;
            }
            pin.NpcGroupSharingStopped = false;
            pin.TryCreateOutfit(out var afterOutfit);
            changed |= wasStopped || beforeEquipmentFlag != pin.ShareNpcEquipment
                || beforeCustomizeFlag != pin.ShareNpcCustomize || beforeWeapons != pin.SharedWeapons
                || !CustomizeEquals(beforeCustomize, pin.SharedCustomize)
                || !OutfitDataValueComparer.AreEqual(beforeOutfit, afterOutfit)
                || (beforeAppearance is not null && !AppearanceEquals(beforeAppearance, pin.Appearance!));
        }
        return changed;
    }

    private static bool CustomizeEquals(CustomizeCopyData? left, CustomizeCopyData? right)
        => ReferenceEquals(left, right) || (left is not null && right is not null
            && left.Customize.AsSpan().SequenceEqual(right.Customize.AsSpan())
            && left.CustomizeColors.AsSpan().SequenceEqual(right.CustomizeColors.AsSpan()));

}
