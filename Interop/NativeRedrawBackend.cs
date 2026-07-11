using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace ActorMorpher.Interop;

public sealed unsafe class NativeRedrawBackend(IObjectTable objectTable) : IRedrawBackend
{
    public bool TryDisable(ActorSnapshot actor)
        => TryInvoke(actor, true);

    public bool TryEnable(ActorSnapshot actor)
        => TryInvoke(actor, false);

    private bool TryInvoke(ActorSnapshot expected, bool disable)
    {
        var key = expected.RepresentationKey;
        var current = objectTable.FirstOrDefault(obj => obj is not null && obj.ObjectIndex == key.ObjectIndex);
        if (current is null
            || current.Address == nint.Zero
            || current.GameObjectId != key.GameObjectId
            || current.EntityId != key.EntityId)
            return false;

        if (disable)
            ((GameObject*)current.Address)->DisableDraw();
        else
            ((GameObject*)current.Address)->EnableDraw();
        return true;
    }
}
