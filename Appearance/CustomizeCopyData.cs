using System.Collections.Immutable;

namespace ActorMorpher.Appearance;

public sealed class CustomizeCopyData
{
    [Newtonsoft.Json.JsonConstructor]
    internal CustomizeCopyData(ImmutableArray<byte> customize, ImmutableArray<CustomizeColorOverride> customizeColors)
    {
        Customize = customize;
        CustomizeColors = customizeColors;
    }

    public ImmutableArray<byte> Customize { get; }
    public ImmutableArray<CustomizeColorOverride> CustomizeColors { get; }

    internal static bool TryCreate(AppearanceData? appearance, out CustomizeCopyData data)
    {
        data = null!;
        if (appearance is not { Category: ModelCategory.Human, Customize.Length: 26 }) return false;
        data = new CustomizeCopyData(appearance.Customize, appearance.CustomizeColors);
        return true;
    }

    internal AppearanceData ApplyTo(AppearanceData appearance)
        => appearance with { Customize = Customize, CustomizeColors = CustomizeColors };
}
