using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace ActorMorpher.Interop;

internal static unsafe class NativeModelScale
{
    private const int CharacterBaseModelScaleOffset = 0x2A4;
    private const float Tolerance = 0.0001f;

    public static float? Capture(GameObject* gameObject)
    {
        var characterBase = gameObject == null ? null : gameObject->GetCharacterBase();
        if (characterBase == null)
            return null;

        return AppearanceData.NormalizeModelScale(Read(characterBase));
    }

    public static bool TryWrite(CharacterBase* characterBase, float? modelScale)
    {
        if (modelScale is not { } requested)
            return true;
        if (characterBase == null)
            return false;

        *(float*)((byte*)characterBase + CharacterBaseModelScaleOffset) = requested;
        return true;
    }

    public static bool IsApplied(CharacterBase* characterBase, float? modelScale)
        => modelScale is not { } expected
            || characterBase != null && MathF.Abs(Read(characterBase) - expected) < Tolerance;

    public static float? ReadOptional(CharacterBase* characterBase)
        => characterBase == null ? null : Read(characterBase);

    private static float Read(CharacterBase* characterBase)
        => *(float*)((byte*)characterBase + CharacterBaseModelScaleOffset);
}
