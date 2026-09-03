using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace ActorMorpher.Interop;

public sealed unsafe class NativeRedrawBackend(
    IObjectTable objectTable,
    NativeDrawObjectInjector drawObjectInjector) : IRedrawBackend
{
    public bool TryDisable(ActorSnapshot actor)
        => TryInvoke(actor, true);

    public bool TryEnable(ActorSnapshot actor, AppearanceData? appearance, Guid operationId)
        => TryInvoke(actor, false, appearance, operationId);

    private bool TryInvoke(
        ActorSnapshot expected,
        bool disable,
        AppearanceData? appearance = null,
        Guid operationId = default)
    {
        var key = expected.RepresentationKey;
        var current = objectTable[key.ObjectIndex];
        if (current is null
            || current.Address == nint.Zero
            || current.GameObjectId != key.GameObjectId
            || current.EntityId != key.EntityId)
            return false;

        var gameObject = (GameObject*)current.Address;
        if (disable)
        {
            gameObject->RenderFlags |= VisibilityFlags.Model;
            gameObject->DisableDraw();
        }
        else
        {
            gameObject->RenderFlags &= ~VisibilityFlags.Model;
            if (appearance is null)
            {
                gameObject->EnableDraw();
                return true;
            }
            else
                return drawObjectInjector.Invoke(operationId, expected, appearance, gameObject);
        }
        return true;
    }
}
