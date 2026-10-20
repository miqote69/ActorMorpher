using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace ActorMorpher.Interop;

internal static unsafe class NativeCustomizeMemory
{
    internal static bool TryApply(IObjectTable objects, ActorSnapshot actor, AppearanceData desired)
        => Apply((Human*)NativeCustomizeColors.Resolve(objects, actor), desired);

    internal static bool Apply(Human* human, AppearanceData desired)
    {
        if (human is null) return false;
        Human.DrawData data = default;
        desired.Customize.AsSpan().CopyTo(data.CustomizeData.Data);
        // Preserve equipment and glasses on the current draw object. Native updates
        // height/shape and rebuilds palette buffers, so apply owned RGB values afterward.
        return human->UpdateDrawData(&data, true)
            && NativeCustomizeColors.Apply((CharacterBase*)human, desired.CustomizeColors);
    }
}
