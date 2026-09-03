using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace ActorMorpher.Interop;

public sealed unsafe class NativeCutsceneActorTracker : IDisposable
{
    public const ushort CutsceneStartIndex = 200;
    public const ushort CutsceneEndIndex = 440;

    private readonly ActorCopyLinks copies = new();
    private readonly Func<nint, LogicalActorKey> resolveActor;
    private readonly Action<nint> forgetLifetime;
    private readonly IGameInteropProvider interop;
    private readonly IDiagnosticLog diagnostics;
    private Hook<CopyCharacterDelegate>? copyCharacterHook;
    private readonly Dictionary<nint, Hook<CharacterDestructorDelegate>> destructorHooks = new();
    private bool enabled;
    private bool disposed;

    public NativeCutsceneActorTracker(IGameInteropProvider interop, IDiagnosticLog diagnostics,
        Func<nint, LogicalActorKey> resolveActor, Action<nint> forgetLifetime)
    {
        this.interop = interop;
        this.diagnostics = diagnostics;
        this.resolveActor = resolveActor;
        this.forgetLifetime = forgetLifetime;
        try
        {
            copyCharacterHook = interop.HookFromAddress<CopyCharacterDelegate>(
                (nint)CharacterSetupContainer.MemberFunctionPointers.CopyFromCharacter,
                CopyCharacterDetour);
        }
        catch (Exception exception)
        {
            DisposeHooks();
            diagnostics.Write(new DiagnosticLogEntry
            {
                Level = DiagnosticLogLevel.Error,
                EventId = DiagnosticEventIds.HandledException,
                Category = DiagnosticCategory.Redraw,
                Message = "Cutscene actor tracking could not be initialized.",
                Exception = DiagnosticExceptionInfo.FromException(exception),
            });
        }
    }

    public LogicalActorKey? GetSource(GameObject* gameObject)
    {
        if (!enabled || gameObject is null)
            return null;

        return copies.Get((nint)gameObject);
    }

    public void Enable(Character* localPlayer)
    {
        if (disposed || copyCharacterHook is null || localPlayer is null)
            return;

        try
        {
            EnsureCharacterDestructorHook(localPlayer);
            if (enabled)
                return;
            ClearLinks();
            copyCharacterHook.Enable();
            enabled = true;
        }
        catch (Exception exception)
        {
            diagnostics.Write(new DiagnosticLogEntry
            {
                Level = DiagnosticLogLevel.Error,
                EventId = DiagnosticEventIds.HandledException,
                Category = DiagnosticCategory.Redraw,
                Message = "Cutscene actor tracking could not be enabled.",
                Exception = DiagnosticExceptionInfo.FromException(exception),
            });
        }
    }

    public void Disable()
    {
        if (!enabled)
            return;

        copyCharacterHook?.Disable();
        foreach (var hook in destructorHooks.Values)
            hook.Disable();
        enabled = false;
        ClearLinks();
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        Disable();
        DisposeHooks();
    }

    private void EnsureCharacterDestructorHook(Character* localPlayer)
    {
        if (localPlayer->VirtualTable is null)
            throw new InvalidOperationException("The local player's Character virtual table is unavailable.");

        var address = (nint)localPlayer->VirtualTable->Dtor;
        if (address == nint.Zero)
            throw new InvalidOperationException("The local player's Character destructor is unavailable.");
        if (destructorHooks.ContainsKey(address))
            return;

        Hook<CharacterDestructorDelegate>? hook = null;
        hook = interop.HookFromAddress<CharacterDestructorDelegate>(address,
            (actor, flags) => CharacterDestructorDetour(actor, flags, hook!));
        try
        {
            hook.Enable();
            destructorHooks.Add(address, hook);
        }
        catch
        {
            hook.Dispose();
            throw;
        }
    }

    private ulong CopyCharacterDetour(CharacterSetupContainer* target, Character* source, uint unknown)
    {
        var targetCharacter = target is null ? null : target->OwnerObject;
        if (targetCharacter is not null)
        {
            var targetIndex = targetCharacter->GameObject.ObjectIndex;
            if (IsCutsceneIndex(targetIndex))
            {
                copies.Remove((nint)targetCharacter);
                try
                {
                    EnsureCharacterDestructorHook(targetCharacter);
                    if (source is not null)
                    {
                        EnsureCharacterDestructorHook(source);
                        copies.Link((nint)targetCharacter, (nint)source, resolveActor);
                    }
                }
                catch (Exception exception)
                {
                    // Hook installation failure must not suppress the game's CopyFromCharacter.
                    try
                    {
                        diagnostics.Write(new DiagnosticLogEntry
                        {
                            Level = DiagnosticLogLevel.Error,
                            EventId = DiagnosticEventIds.HandledException,
                            Category = DiagnosticCategory.Redraw,
                            Message = "Actor copy lifetime tracking could not be initialized.",
                            Exception = DiagnosticExceptionInfo.FromException(exception),
                        });
                    }
                    catch { }
                }
            }
        }

        return copyCharacterHook!.Original(target, source, unknown);
    }

    private GameObject* CharacterDestructorDetour(Character* character, byte freeFlags,
        Hook<CharacterDestructorDelegate> hook)
    {
        if (character is not null)
        {
            // A live copy still owns its source identity after the field actor disappears.
            copies.Remove((nint)character);
            forgetLifetime((nint)character);
        }

        return hook.Original(character, freeFlags);
    }

    private void ClearLinks()
        => copies.Clear();

    private void DisposeHooks()
    {
        foreach (var hook in destructorHooks.Values)
            hook.Dispose();
        destructorHooks.Clear();
        copyCharacterHook?.Dispose();
        copyCharacterHook = null;
    }

    private static bool IsCutsceneIndex(ushort objectIndex)
        => objectIndex is >= CutsceneStartIndex and < CutsceneEndIndex;

    private delegate ulong CopyCharacterDelegate(CharacterSetupContainer* target, Character* source, uint unknown);

    private delegate GameObject* CharacterDestructorDelegate(Character* character, byte freeFlags);
}

internal sealed class ActorCopyLinks
{
    private readonly Dictionary<nint, LogicalActorKey> sources = new();
    public LogicalActorKey? Get(nint address)
        => sources.TryGetValue(address, out var source) ? source : null;
    public void Link(nint target, nint source, Func<nint, LogicalActorKey> resolve)
        => sources[target] = Get(source) ?? resolve(source);
    public void Remove(nint address) => sources.Remove(address);
    public void Clear() => sources.Clear();
}
