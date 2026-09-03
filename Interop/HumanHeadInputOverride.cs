using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace ActorMorpher.Interop;

/// <summary>Removable head-input experiment, scoped to one requested native Create.</summary>
internal sealed unsafe class HumanHeadInputOverride : IDisposable
{
    private readonly Hook<SetupDelegate> hook;

    [ThreadStatic]
    private static HeadInput? current;

    internal HumanHeadInputOverride(IGameInteropProvider interop)
    {
        hook = interop.HookFromAddress<SetupDelegate>(
            (nint)Human.MemberFunctionPointers.SetupFromCharacterData, SetupDetour);
        try
        {
            hook.Enable();
        }
        catch
        {
            hook.Dispose();
            throw;
        }
    }

    public void Dispose() => hook.Dispose();

    // A null appearance suspends the outer scope during an unrelated nested Create.
    internal static nint Invoke(AppearanceData? appearance, Func<nint> original)
    {
        var previous = current;
        current = appearance is { Category: ModelCategory.Human }
            && !appearance.Equipment.IsDefaultOrEmpty
                ? new HeadInput(appearance.Equipment[0])
                : null;
        try
        {
            return original();
        }
        finally
        {
            current = previous;
        }
    }

    private byte SetupDetour(nint human, nint data)
        => Dispatch(human, data, (target, input) => hook.Original(target, input));

    internal static byte Dispatch(nint human, nint data, Func<nint, nint, byte> original)
    {
        var input = current;
        if (input is null || input.Consumed)
            return original(human, data);

        // SetupFromCharacterData belongs to this synchronous Create. Claim it once;
        // neither input comparisons nor diagnostic completeness choose the payload.
        input.Consumed = true;
        var result = original(human, data);
        // Setup's nested equipment setters may replace an explicit zero with a
        // retained hat. Feed the generated Human's pending head after those calls,
        // before CharacterBase.Create returns. No extra native setter is invoked.
        var target = (Human*)human;
        ((EquipmentModelId*)target->ChangedEquipData)->Value = input.Head;
        target->SlotNeedsUpdateBitfield |= 1u;
        return result;
    }

    private sealed class HeadInput(ulong head)
    {
        internal ulong Head { get; } = head;
        internal bool Consumed { get; set; }
    }

    private delegate byte SetupDelegate(nint human, nint data);
}
