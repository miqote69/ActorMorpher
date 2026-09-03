using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace ActorMorpher.Interop;

internal static unsafe class NativeModelScale
{
    private const int CharacterBaseModelScaleOffset = 0x2A4;
    private const int SetScaleVfuncIndex = 25;
    public static float? Capture(Character* character)
        => character == null ? null : character->ModelScale;

    public static float? CaptureRendered(Character* character)
        => character == null ? null : ((GameObject*)character)->Scale;

    public static void ApplyRendered(Character* character, float? modelScale)
    {
        if (modelScale is not { } requested)
            return;
        if (character == null)
            throw new InvalidOperationException("The target character is unavailable for model-scale application.");

        var gameObject = (GameObject*)character;
        var vtable = *(nint**)gameObject;
        if (vtable == null || vtable[SetScaleVfuncIndex] == 0)
            throw new InvalidOperationException("The target character's scale function is unavailable.");

        var backingScale = character->ModelScale;
        var setScale = (delegate* unmanaged<GameObject*, float, void>)vtable[SetScaleVfuncIndex];
        try
        {
            setScale(gameObject, requested);
        }
        finally
        {
            character->ModelScale = backingScale;
        }
    }

    public static float? ReadCharacterBaseOptional(CharacterBase* characterBase)
        => characterBase == null ? null : Read(characterBase);

    private static float Read(CharacterBase* characterBase)
        => *(float*)((byte*)characterBase + CharacterBaseModelScaleOffset);
}
