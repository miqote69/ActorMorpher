using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Ipc.Exceptions;
using Dalamud.Plugin.Services;

namespace ActorMorpher.Interop;

internal readonly record struct NpcFollowerIdentity(
    Guid AppearanceId, Guid SpawnId, ushort ObjectIndex, ulong GameObjectId, ulong Address)
{
    internal bool Matches(ObjectKind kind, ushort objectIndex, ulong gameObjectId, nint address)
        => kind == ObjectKind.Companion && ObjectIndex == objectIndex && GameObjectId == gameObjectId
            && Address == unchecked((ulong)address);
}

internal sealed class NpcFollowerIpc(IDalamudPluginInterface pluginInterface, IFramework framework)
{
    private readonly ICallGateSubscriber<
        (Guid AppearanceId, Guid SpawnId, ushort ObjectIndex, ulong GameObjectId, ulong Address)?> currentFollower =
        pluginInterface.GetIpcSubscriber<(Guid, Guid, ushort, ulong, ulong)?>("NpcFollower.GetCurrentFollower.v1");
    private readonly ICallGateSubscriber<
        (byte Role, ushort ObjectIndex, ulong GameObjectId, ulong Address, Guid SpawnId, Guid AppearanceId, string? DisplayName)[]> actorRoles =
        pluginInterface.GetIpcSubscriber<(byte, ushort, ulong, ulong, Guid, Guid, string?)[]>("NpcFollower.GetActorRoles.v1");

    internal bool IsFrameworkThread => framework.IsInFrameworkUpdateThread;

    internal (IReadOnlyList<ActorRoleAssignment> Roles, NpcFollowerIdentity? Follower) ReadForRegistry()
    {
        if (!IsFrameworkThread)
            return ([], null);
        if (actorRoles.HasFunction)
        {
            try
            {
                var actors = actorRoles.InvokeFunc();
                if (actors.Length == 0)
                    return ([], null);

                var roles = new List<ActorRoleAssignment>(actors.Length);
                NpcFollowerIdentity? follower = null;
                foreach (var actor in actors)
                {
                    if (actor.Address == 0 || actor.SpawnId == Guid.Empty)
                        continue;

                    var instance = new ActorObjectReference(actor.ObjectIndex, actor.GameObjectId, actor.Address);
                    switch ((ActorRole)actor.Role)
                    {
                        case ActorRole.Editable when actor.AppearanceId != Guid.Empty:
                            roles.Add(new(ActorRole.Editable, instance, actor.SpawnId, actor.DisplayName));
                            follower = new(actor.AppearanceId, actor.SpawnId, actor.ObjectIndex, actor.GameObjectId, actor.Address);
                            break;
                        case ActorRole.Support:
                        case ActorRole.Inactive:
                            roles.Add(new((ActorRole)actor.Role, instance, actor.SpawnId, null));
                            break;
                    }
                }
                return (roles, follower);
            }
            catch (IpcNotReadyError)
            {
                // Follow the existing IPC if the roles endpoint is no longer registered.
            }
        }
        return ([], ReadCurrent());
    }

    internal NpcFollowerIdentity? ReadCurrent()
    {
        if (!IsFrameworkThread || !currentFollower.HasFunction)
            return null;
        try
        {
            return currentFollower.InvokeFunc() is { } follower
                && follower.AppearanceId != Guid.Empty && follower.SpawnId != Guid.Empty && follower.Address != 0
                ? new(follower.AppearanceId, follower.SpawnId, follower.ObjectIndex, follower.GameObjectId, follower.Address)
                : null;
        }
        catch (IpcNotReadyError)
        {
            // The optional provider can unload after HasFunction was read.
            return null;
        }
    }
}
