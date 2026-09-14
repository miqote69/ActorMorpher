using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using System.Collections.Immutable;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace ActorMorpher.Interop;

/// <summary>Selected Human setup inputs, scoped to one requested native Create.</summary>
internal sealed unsafe class HumanHeadInputOverride : IDisposable
{
    private readonly Hook<SetupDelegate> hook;

    [ThreadStatic]
    private static SetupInput? current;

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
            && (!appearance.Customize.IsDefaultOrEmpty || !appearance.Equipment.IsDefaultOrEmpty)
                ? new SetupInput(appearance.Customize,
                    appearance.Equipment.IsDefaultOrEmpty ? null : appearance.Equipment[0])
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
        // Create listeners can replace Customize before this native consumer reads it.
        // Supply the selected bytes here without teaching them to the game-owned base.
        var customizeLength = input.Customize.IsDefaultOrEmpty ? 0 : input.Customize.Length;
        var customize = new Span<byte>((void*)data, customizeLength);
        Span<byte> previousCustomize = stackalloc byte[customizeLength];
        customize.CopyTo(previousCustomize);
        input.Customize.AsSpan().CopyTo(customize);
        byte result;
        try
        {
            result = original(human, data);
        }
        finally
        {
            previousCustomize.CopyTo(customize);
        }
        // Setup's nested equipment setters may replace an explicit zero with a
        // retained hat. Feed the generated Human's pending head after those calls,
        // before CharacterBase.Create returns. No extra native setter is invoked.
        if (input.Head is { } head)
        {
            var target = (Human*)human;
            ((EquipmentModelId*)target->ChangedEquipData)->Value = head;
            target->SlotNeedsUpdateBitfield |= 1u;
        }
        return result;
    }

    private sealed class SetupInput(ImmutableArray<byte> customize, ulong? head)
    {
        internal ImmutableArray<byte> Customize { get; } = customize;
        internal ulong? Head { get; } = head;
        internal bool Consumed { get; set; }
    }

    private delegate byte SetupDelegate(nint human, nint data);
}
