using Dalamud.Game.ClientState.Objects.Enums;

namespace ActorMorpher.BulkOutfit;

public sealed class PinnedOutfitConfiguration
{
    private static readonly Guid CurrentSession = Guid.NewGuid();
    public LogicalActorKey? ActorKey { get; set; }
    public NpcAttachmentKey? NpcAttachment { get; set; }
    public Guid? Session { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public int ObjectKind { get; set; }
    public uint BaseId { get; set; }
    public bool IsLocalPlayer { get; set; }
    public AppearanceData? Appearance { get; set; }
    public List<PinnedArmorConfiguration> Equipment { get; set; } = [];
    public bool FacewearAvailable { get; set; }
    public ushort FacewearModelId { get; set; }
    public bool HatVisible { get; set; }
    public bool VisorToggled { get; set; }
    public bool? VieraEarsHidden { get; set; }

    public bool TryCreateOutfit(out OutfitData outfit)
    {
        outfit = null!;
        if (Appearance is { } appearance)
        {
            outfit = EquipmentDisplayFormatting.CreateHumanOutfit(appearance)!;
            return outfit is not null;
        }
        if (Equipment is null || Equipment.Count != Enum.GetValues<OutfitSlot>().Length)
            return false;

        outfit = OutfitData.Create(
            Equipment.Select(static armor => new ArmorAppearance(
                armor.Set,
                armor.Variant,
                armor.Stain1,
                armor.Stain2) { Color1 = armor.Color1, Color2 = armor.Color2 }),
            new FacewearAppearance(FacewearAvailable, FacewearModelId),
            HatVisible,
            VisorToggled) with { VieraEarsHidden = VieraEarsHidden };
        return true;
    }

    public static PinnedOutfitConfiguration Create(ActorEntry actor, OutfitData outfit)
        => new()
        {
            ActorName = actor.Name.Trim(),
            ObjectKind = (int)actor.Kind,
            BaseId = actor.BaseId,
            IsLocalPlayer = actor.IsLocalPlayer,
            Equipment = outfit.Equipment.Select(static armor => new PinnedArmorConfiguration
            {
                Set = armor.Set,
                Variant = armor.Variant,
                Stain1 = armor.Stain1,
                Stain2 = armor.Stain2,
                Color1 = armor.Color1,
                Color2 = armor.Color2,
            }).ToList(),
            FacewearAvailable = outfit.Facewear.IsAvailable,
            FacewearModelId = outfit.Facewear.ModelId,
            HatVisible = outfit.HatVisible,
            VisorToggled = outfit.VisorToggled,
            VieraEarsHidden = outfit.VieraEarsHidden,
        };

    public static PinnedOutfitConfiguration Create(ActorEntry actor, AppearanceData appearance)
        => new()
        {
            ActorKey = actor.Current.ContinuityKey is { Source: not 5 } identity
                ? actor.Key with { Continuity = identity }
                : actor.Key,
            Session = CurrentSession,
            NpcAttachment = UsesNpcSlotIdentity(actor.Key)
                && actor.Current.ContinuityKey is null or { Source: 5 }
                    ? actor.Current.NpcAttachment : null,
            ActorName = actor.Name.Trim(),
            ObjectKind = (int)actor.Kind,
            BaseId = actor.BaseId,
            IsLocalPlayer = actor.IsLocalPlayer,
            Appearance = appearance,
        };

    public bool Matches(ActorEntry actor)
    {
        if (UsesAttachmentIdentity && NpcAttachment is { } attachment)
            return Session == CurrentSession && actor.Current.NpcAttachment == attachment;
        if (ActorKey is { } key)
        {
            if (UsesNpcSlotIdentity(key))
                return Session == CurrentSession
                    && actor.Key.ObjectKind == key.ObjectKind
                    && actor.Key.BaseId == key.BaseId
                    && actor.Key.OriginalObjectIndex == key.OriginalObjectIndex
                    && actor.Key.TerritoryId == key.TerritoryId;
            if (key.Continuity is { Source: not 5 } identity)
                return (identity.Source is 1 or 3 || Session == CurrentSession)
                    && (actor.Key.Continuity == identity
                        || actor.Representations.Any(snapshot => snapshot.ContinuityKey == identity));
            return Session == CurrentSession && actor.Key == key;
        }
        if (ObjectKind != (int)actor.Kind || IsLocalPlayer != actor.IsLocalPlayer)
            return false;
        if (actor.IsLocalPlayer
            || actor.Kind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc
            || BaseId == 0)
            return string.Equals(ActorName, actor.Name, StringComparison.OrdinalIgnoreCase);
        return BaseId == actor.BaseId;
    }

    public string IdentityKey()
        => UsesAttachmentIdentity && NpcAttachment is { } attachment
            ? $"Appearance:Attachment:{Session}:{attachment}"
            : ActorKey is { } key
            ? UsesNpcSlotIdentity(key)
                ? $"Appearance:NpcSlot:{Session}:{key.ObjectKind}:{key.BaseId}:{key.OriginalObjectIndex}:{key.TerritoryId}"
                : key.Continuity is { Source: not 5 } identity
                ? identity.Source is 1 or 3
                    ? $"Appearance:{identity}"
                    : $"Appearance:{Session}:{identity}"
                : $"Appearance:{Session}:{key}"
            : $"{ObjectKind}:{(IsLocalPlayer ? 1 : 0)}:{BaseId}:{ActorName.Trim().ToUpperInvariant()}";

    private bool UsesAttachmentIdentity
        => ActorKey is { } key && UsesNpcSlotIdentity(key) && NpcAttachment is not null;

    // A pin targets the NPC number in this slot across recreation. This does not
    // merge live Actor keys or weaken the lifetime check used by queued operations.
    private static bool UsesNpcSlotIdentity(LogicalActorKey key)
        => key.ObjectKind is Dalamud.Game.ClientState.Objects.Enums.ObjectKind.EventNpc
                or Dalamud.Game.ClientState.Objects.Enums.ObjectKind.BattleNpc
            && key.Continuity is null or { Source: 5 };
}

public sealed class PinnedArmorConfiguration
{
    public DyeColor? Color1 { get; set; }
    public DyeColor? Color2 { get; set; }
    public ushort Set { get; set; }
    public byte Variant { get; set; }
    public byte Stain1 { get; set; }
    public byte Stain2 { get; set; }
}
