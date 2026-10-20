using System.Collections.Immutable;

namespace ActorMorpher.Appearance;

public sealed class EquipmentCopyData
{
    private EquipmentCopyData(AppearanceData appearance)
    {
        IncludesWeapons = true;
        IncludesHeadSettings = true;
        Outfit = EquipmentDisplayFormatting.CreateHumanOutfit(appearance)!;
        Equipment = appearance.Equipment;
        ColoredEquipment = appearance.ColoredEquipment;
        Mainhand = appearance.Mainhand;
        Offhand = appearance.Offhand;
        MainhandDyes = appearance.MainhandDyes;
        OffhandDyes = appearance.OffhandDyes;
        FacewearModelId = appearance.FacewearModelId;
        HatVisible = appearance.HatVisible;
        VisorToggled = appearance.VisorToggled;
        VieraEarsHidden = appearance.VieraEarsHidden;
    }

    private EquipmentCopyData(OutfitData outfit)
    {
        Outfit = outfit;
        Equipment = outfit.Equipment.Select(ActorRegistry.ToEquipmentModelValue).ToImmutableArray();
        ColoredEquipment = outfit.Equipment;
        FacewearModelId = outfit.Facewear.IsAvailable ? outfit.Facewear.ModelId : null;
        HatVisible = outfit.HatVisible;
        VisorToggled = outfit.VisorToggled;
        VieraEarsHidden = outfit.VieraEarsHidden;
    }

    private EquipmentCopyData(OutfitData outfit, ulong? mainhand, ulong? offhand,
        WeaponDyes mainhandDyes, WeaponDyes offhandDyes) : this(outfit)
    {
        IncludesWeapons = true;
        Mainhand = mainhand;
        Offhand = offhand;
        MainhandDyes = mainhandDyes;
        OffhandDyes = offhandDyes;
    }

    public ImmutableArray<ulong> Equipment { get; }
    public ImmutableArray<ArmorAppearance> ColoredEquipment { get; }
    public ulong? Mainhand { get; }
    public ulong? Offhand { get; }
    public WeaponDyes MainhandDyes { get; }
    public WeaponDyes OffhandDyes { get; }
    public ushort? FacewearModelId { get; }
    public bool? HatVisible { get; }
    public bool? VisorToggled { get; }
    public bool? VieraEarsHidden { get; }
    internal bool IncludesWeapons { get; }
    internal bool IncludesHeadSettings { get; }
    internal OutfitData Outfit { get; }

    internal static EquipmentCopyData FromOutfit(OutfitData outfit) => new(outfit);

    internal static EquipmentCopyData FromOutfit(OutfitData outfit, ulong? mainhand, ulong? offhand,
        WeaponDyes mainhandDyes, WeaponDyes offhandDyes)
        => new(outfit, mainhand, offhand, mainhandDyes, offhandDyes);

    // Human capture and injection expose the same ten armor/accessory slots,
    // both weapon draw objects, and the head/facewear settings.
    public static bool CanCopy(AppearanceData? appearance)
        => EquipmentDisplayFormatting.CreateHumanOutfit(appearance) is not null;

    internal static bool TryCreate(AppearanceData? appearance, out EquipmentCopyData data)
    {
        data = null!;
        if (!CanCopy(appearance)) return false;
        data = new EquipmentCopyData(appearance!);
        return true;
    }

    internal AppearanceData ApplyTo(AppearanceData destination)
        => destination with
        {
            Equipment = Equipment,
            ColoredEquipment = ColoredEquipment,
            Mainhand = IncludesWeapons ? Mainhand : destination.Mainhand,
            Offhand = IncludesWeapons ? Offhand : destination.Offhand,
            MainhandDyes = IncludesWeapons ? MainhandDyes : destination.MainhandDyes,
            OffhandDyes = IncludesWeapons ? OffhandDyes : destination.OffhandDyes,
            // Outfit-only data uses unavailable facewear/ear values as unspecified.
            FacewearModelId = IncludesHeadSettings ? FacewearModelId : FacewearModelId ?? destination.FacewearModelId,
            HatVisible = HatVisible,
            VisorToggled = VisorToggled,
            VieraEarsHidden = IncludesHeadSettings ? VieraEarsHidden : VieraEarsHidden ?? destination.VieraEarsHidden,
        };
}
