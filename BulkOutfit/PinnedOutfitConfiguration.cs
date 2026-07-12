using Dalamud.Game.ClientState.Objects.Enums;

namespace ActorMorpher.BulkOutfit;

public sealed class PinnedOutfitConfiguration
{
    public string ActorName { get; set; } = string.Empty;
    public int ObjectKind { get; set; }
    public uint BaseId { get; set; }
    public bool IsLocalPlayer { get; set; }
    public List<PinnedArmorConfiguration> Equipment { get; set; } = [];
    public bool FacewearAvailable { get; set; }
    public ushort FacewearModelId { get; set; }
    public bool HatVisible { get; set; }
    public bool VisorToggled { get; set; }

    public bool TryCreateOutfit(out OutfitData outfit)
    {
        outfit = null!;
        if (Equipment is null || Equipment.Count != Enum.GetValues<OutfitSlot>().Length)
            return false;

        outfit = OutfitData.Create(
            Equipment.Select(static armor => new ArmorAppearance(
                armor.Set,
                armor.Variant,
                armor.Stain1,
                armor.Stain2)),
            new FacewearAppearance(FacewearAvailable, FacewearModelId),
            HatVisible,
            VisorToggled);
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
            }).ToList(),
            FacewearAvailable = outfit.Facewear.IsAvailable,
            FacewearModelId = outfit.Facewear.ModelId,
            HatVisible = outfit.HatVisible,
            VisorToggled = outfit.VisorToggled,
        };

    public bool Matches(ActorEntry actor)
    {
        if (ObjectKind != (int)actor.Kind || IsLocalPlayer != actor.IsLocalPlayer)
            return false;
        if (actor.IsLocalPlayer
            || actor.Kind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc
            || BaseId == 0)
            return string.Equals(ActorName, actor.Name, StringComparison.OrdinalIgnoreCase);
        return BaseId == actor.BaseId;
    }

    public string IdentityKey()
        => $"{ObjectKind}:{(IsLocalPlayer ? 1 : 0)}:{BaseId}:{ActorName.Trim().ToUpperInvariant()}";
}

public sealed class PinnedArmorConfiguration
{
    public ushort Set { get; set; }
    public byte Variant { get; set; }
    public byte Stain1 { get; set; }
    public byte Stain2 { get; set; }
}
