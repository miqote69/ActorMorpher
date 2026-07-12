using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace ActorMorpher.Interop;

public sealed unsafe class NativeDrawObjectInjector : IDisposable
{
    private readonly Hook<CreateCharacterBaseDelegate> hook;
    private readonly IDiagnosticLog diagnostics;
    private InjectionContext? active;
    private bool disposed;

    public NativeDrawObjectInjector(IGameInteropProvider interop, IDiagnosticLog diagnostics)
    {
        this.diagnostics = diagnostics;
        hook = interop.HookFromAddress<CreateCharacterBaseDelegate>(
            (nint)CharacterBase.MemberFunctionPointers.Create,
            CreateCharacterBaseDetour);
    }

    public void Invoke(ActorSnapshot actor, AppearanceData appearance, GameObject* gameObject)
    {
        if (disposed || active is not null)
            throw new InvalidOperationException("Draw object injection is unavailable or already active.");

        hook.Enable();
        active = new InjectionContext(actor.LogicalKey, appearance);
        try
        {
            gameObject->EnableDraw();
        }
        finally
        {
            active = null;
            hook.Disable();
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        active = null;
        hook.Dispose();
    }

    private CharacterBase* CreateCharacterBaseDetour(
        uint modelId,
        CustomizeData* customize,
        EquipmentModelId* equipment,
        byte unknown)
    {
        var context = active;
        if (context is null)
            return hook.Original(modelId, customize, equipment, unknown);

        var appearance = context.Appearance;
        var injectedCustomize = default(CustomizeData);
        var customizeArgument = customize;
        if (!appearance.Customize.IsDefaultOrEmpty
            && appearance.Customize.Length == injectedCustomize.Data.Length)
        {
            appearance.Customize.AsSpan().CopyTo(injectedCustomize.Data);
            customizeArgument = &injectedCustomize;
        }

        var equipmentArgument = equipment;
        const int equipmentSlotCount = 10;
        EquipmentModelId* injectedEquipment = stackalloc EquipmentModelId[equipmentSlotCount];
        if (!appearance.Equipment.IsDefaultOrEmpty)
        {
            if (appearance.Equipment.Length == equipmentSlotCount)
            {
                for (var index = 0; index < equipmentSlotCount; ++index)
                    injectedEquipment[index].Value = appearance.Equipment[index];
                equipmentArgument = injectedEquipment;
            }
        }

        diagnostics.Write(new DiagnosticLogEntry
        {
            EventId = DiagnosticEventIds.DrawObjectCreateInjected,
            Category = DiagnosticCategory.Redraw,
            Message = "Desired appearance injected into CharacterBase creation.",
            ActorKey = DiagnosticActorKeys.Format(diagnostics, context.Actor),
            Properties = new Dictionary<string, object?>
            {
                ["originalModelCharaId"] = modelId,
                ["injectedModelCharaId"] = appearance.ModelCharaId,
                ["category"] = appearance.Category,
                ["bodyType"] = appearance.Customize.Length > 2 ? appearance.Customize[2] : null,
                ["customizeInjected"] = customizeArgument != customize,
                ["equipmentInjected"] = equipmentArgument != equipment,
                ["customizeSignature"] = Signature(appearance.Customize),
                ["equipmentSignature"] = Signature(appearance.Equipment),
            },
        });

        return hook.Original(
            appearance.ModelCharaId,
            customizeArgument,
            equipmentArgument,
            unknown);
    }

    private static string Signature(IEnumerable<byte> values)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        foreach (var value in values)
        {
            hash ^= value;
            hash *= prime;
        }
        return hash.ToString("X16");
    }

    private static string Signature(IEnumerable<ulong> values)
    {
        const ulong offset = 14695981039346656037;
        const ulong prime = 1099511628211;
        var hash = offset;
        foreach (var value in values)
        {
            var remaining = value;
            for (var index = 0; index < sizeof(ulong); ++index)
            {
                hash ^= (byte)remaining;
                hash *= prime;
                remaining >>= 8;
            }
        }
        return hash.ToString("X16");
    }

    private delegate CharacterBase* CreateCharacterBaseDelegate(
        uint modelId,
        CustomizeData* customize,
        EquipmentModelId* equipment,
        byte unknown);

    private sealed record InjectionContext(LogicalActorKey Actor, AppearanceData Appearance);
}
