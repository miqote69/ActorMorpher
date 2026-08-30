using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace ActorMorpher.Interop;

public sealed unsafe class NativeCutsceneActorTracker : IDisposable
{
    public const ushort CutsceneStartIndex = 200;
    public const ushort CutsceneEndIndex = 440;

    private readonly bool[] localPlayerCopies = new bool[CutsceneEndIndex - CutsceneStartIndex];
    private readonly IGameInteropProvider interop;
    private readonly IDiagnosticLog diagnostics;
    private Hook<CopyCharacterDelegate>? copyCharacterHook;
    private Hook<CharacterDestructorDelegate>? characterDestructorHook;
    private nint characterDestructorAddress;
    private bool enabled;
    private bool disposed;

    public NativeCutsceneActorTracker(IGameInteropProvider interop, IDiagnosticLog diagnostics)
    {
        this.interop = interop;
        this.diagnostics = diagnostics;
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

    public bool IsLocalPlayerCopy(GameObject* gameObject)
    {
        if (!enabled || gameObject is null)
            return false;

        var index = gameObject->ObjectIndex;
        return IsCutsceneIndex(index) && localPlayerCopies[index - CutsceneStartIndex];
    }

    public void Enable(Character* localPlayer)
    {
        if (disposed || enabled || copyCharacterHook is null || localPlayer is null)
            return;

        try
        {
            EnsureCharacterDestructorHook(localPlayer);
            ClearLinks();
            copyCharacterHook.Enable();
            characterDestructorHook!.Enable();
            enabled = true;
        }
        catch (Exception exception)
        {
            copyCharacterHook.Disable();
            characterDestructorHook?.Disable();
            ClearLinks();
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
        characterDestructorHook?.Disable();
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
        if (characterDestructorHook is not null && characterDestructorAddress == address)
            return;

        characterDestructorHook?.Dispose();
        characterDestructorHook = interop.HookFromAddress<CharacterDestructorDelegate>(address, CharacterDestructorDetour);
        characterDestructorAddress = address;
    }

    private ulong CopyCharacterDetour(CharacterSetupContainer* target, Character* source, uint unknown)
    {
        var targetCharacter = target is null ? null : target->OwnerObject;
        if (targetCharacter is not null)
        {
            var targetIndex = targetCharacter->GameObject.ObjectIndex;
            if (IsCutsceneIndex(targetIndex))
                SetLocalPlayerCopy(
                    targetIndex,
                    source is not null && source->GameObject.ObjectIndex == 0,
                    "CopyFromCharacter");
        }

        return copyCharacterHook!.Original(target, source, unknown);
    }

    private GameObject* CharacterDestructorDetour(Character* character, byte freeFlags)
    {
        if (character is not null)
        {
            var objectIndex = character->GameObject.ObjectIndex;
            if (objectIndex == 0)
                ClearLinks();
            else if (IsCutsceneIndex(objectIndex))
                SetLocalPlayerCopy(objectIndex, false, "CharacterDestructor");
        }

        return characterDestructorHook!.Original(character, freeFlags);
    }

    private void SetLocalPlayerCopy(
        ushort objectIndex,
        bool isLocalPlayerCopy,
        string source)
    {
        var slot = objectIndex - CutsceneStartIndex;
        var changed = localPlayerCopies[slot] != isLocalPlayerCopy;
        localPlayerCopies[slot] = isLocalPlayerCopy;
        if (!changed)
            return;

        diagnostics.Write(new DiagnosticLogEntry
        {
            EventId = isLocalPlayerCopy
                ? DiagnosticEventIds.CutsceneActorLinked
                : DiagnosticEventIds.CutsceneActorUnlinked,
            Category = DiagnosticCategory.Redraw,
            Message = isLocalPlayerCopy
                ? "Cutscene actor linked to the local player."
                : "Cutscene actor local-player link cleared.",
            Properties = new Dictionary<string, object?>
            {
                ["objectIndex"] = objectIndex,
                ["parentObjectIndex"] = isLocalPlayerCopy ? 0 : null,
                ["source"] = source,
            },
        });
    }

    private void ClearLinks()
        => Array.Clear(localPlayerCopies);

    private void DisposeHooks()
    {
        characterDestructorHook?.Dispose();
        copyCharacterHook?.Dispose();
        characterDestructorHook = null;
        copyCharacterHook = null;
        characterDestructorAddress = nint.Zero;
    }

    private static bool IsCutsceneIndex(ushort objectIndex)
        => objectIndex is >= CutsceneStartIndex and < CutsceneEndIndex;

    private delegate ulong CopyCharacterDelegate(CharacterSetupContainer* target, Character* source, uint unknown);

    private delegate GameObject* CharacterDestructorDelegate(Character* character, byte freeFlags);
}
