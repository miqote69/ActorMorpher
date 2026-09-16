namespace ActorMorpher.BulkOutfit;

public enum HeadgearSetting { Visible, VisorToggled, VieraEarsVisible }

internal static class HeadgearSettings
{
    internal static OutfitData Change(OutfitData outfit, HeadgearSetting setting, bool value)
        => setting switch
        {
            HeadgearSetting.Visible => outfit with { HatVisible = value },
            HeadgearSetting.VisorToggled => outfit with { VisorToggled = value },
            HeadgearSetting.VieraEarsVisible => outfit with { VieraEarsHidden = !value },
            _ => throw new ArgumentOutOfRangeException(nameof(setting)),
        };
}
