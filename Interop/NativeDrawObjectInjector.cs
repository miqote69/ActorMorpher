using System.Collections.Immutable;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace ActorMorpher.Interop;

public sealed unsafe class NativeDrawObjectInjector : IDisposable
{
    private readonly IGameInteropProvider interop;
    private readonly Hook<CreateCharacterBaseDelegate> createHook;
    private readonly NativeCutsceneActorTracker cutsceneActors;
    private readonly IObjectTable objectTable;
    private readonly IDiagnosticLog diagnostics;
    private Hook<EnableDrawDelegate>? localEnableDrawHook;
    private nint localEnableDrawAddress;
    private InjectionContext? persistentLocal;
    private bool disposed;

    [ThreadStatic]
    private static InjectionContext? active;

    public NativeDrawObjectInjector(
        IGameInteropProvider interop,
        IObjectTable objectTable,
        IDiagnosticLog diagnostics)
    {
        this.interop = interop;
        this.objectTable = objectTable;
        this.diagnostics = diagnostics;
        cutsceneActors = new NativeCutsceneActorTracker(interop, diagnostics);
        createHook = interop.HookFromAddress<CreateCharacterBaseDelegate>(
            (nint)CharacterBase.MemberFunctionPointers.Create,
            CreateCharacterBaseDetour);
        createHook.Enable();
    }

    public void Invoke(ActorSnapshot actor, AppearanceData appearance, GameObject* gameObject)
    {
        if (disposed || active is not null)
            throw new InvalidOperationException("Draw object injection is unavailable or already active.");

        active = new InjectionContext(actor.LogicalKey, appearance);
        try
        {
            gameObject->EnableDraw();
        }
        finally
        {
            active = null;
        }
    }

    public void SetPersistentLocalAppearance(LogicalActorKey actor, AppearanceData appearance)
    {
        if (disposed)
            return;

        var localPlayer = objectTable.LocalPlayer;
        if (localPlayer is null || localPlayer.Address == nint.Zero)
            return;

        EnsureLocalEnableDrawHook((GameObject*)localPlayer.Address);
        persistentLocal = new InjectionContext(actor, appearance);
        cutsceneActors.Enable((Character*)localPlayer.Address);
    }

    public void ClearPersistentLocalAppearance()
    {
        persistentLocal = null;
        cutsceneActors.Disable();
        localEnableDrawHook?.Dispose();
        localEnableDrawHook = null;
        localEnableDrawAddress = nint.Zero;
    }

    public void UpdatePersistentLocalOutfit(OutfitData outfit)
    {
        var context = persistentLocal;
        if (disposed || context is null || outfit.Equipment.Length != 10)
            return;

        var equipment = ImmutableArray.CreateBuilder<ulong>(outfit.Equipment.Length);
        foreach (var source in outfit.Equipment)
        {
            var model = new EquipmentModelId
            {
                Id = source.Set,
                Variant = source.Variant,
                Stain0 = source.Stain1,
                Stain1 = source.Stain2,
            };
            equipment.Add(model.Value);
        }

        persistentLocal = context with
        {
            Appearance = context.Appearance with { Equipment = equipment.MoveToImmutable() },
        };
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        active = null;
        ClearPersistentLocalAppearance();
        createHook.Dispose();
        cutsceneActors.Dispose();
    }

    private void EnsureLocalEnableDrawHook(GameObject* gameObject)
    {
        var virtualTable = *(nint**)gameObject;
        if (virtualTable is null)
            throw new InvalidOperationException("The local player's virtual table is unavailable.");

        const int enableDrawVtableIndex = 12;
        var address = virtualTable[enableDrawVtableIndex];
        if (address == nint.Zero)
            throw new InvalidOperationException("The local player's EnableDraw function is unavailable.");
        if (localEnableDrawHook is not null && localEnableDrawAddress == address)
            return;

        localEnableDrawHook?.Dispose();
        localEnableDrawHook = interop.HookFromAddress<EnableDrawDelegate>(address, LocalEnableDrawDetour);
        localEnableDrawAddress = address;
        localEnableDrawHook.Enable();
    }

    private void LocalEnableDrawDetour(GameObject* gameObject)
    {
        var drawHook = localEnableDrawHook;
        if (drawHook is null)
            return;

        if (disposed || active is not null || gameObject is null)
        {
            drawHook.Original(gameObject);
            return;
        }

        var context = persistentLocal;
        var localPlayer = objectTable.LocalPlayer;
        var isDirectLocalPlayer = localPlayer is not null && localPlayer.Address == (nint)gameObject;
        var isCutsceneCopy = cutsceneActors.IsLocalPlayerCopy(gameObject);
        if (context is null || !isDirectLocalPlayer && !isCutsceneCopy)
        {
            drawHook.Original(gameObject);
            return;
        }

        active = context with
        {
            RuntimeObjectIndex = gameObject->ObjectIndex,
            IsCutsceneCopy = isCutsceneCopy,
        };
        try
        {
            drawHook.Original(gameObject);
        }
        finally
        {
            active = null;
        }
    }

    private CharacterBase* CreateCharacterBaseDetour(
        uint modelId,
        CustomizeData* customize,
        EquipmentModelId* equipment,
        byte unknown)
    {
        var context = active;
        if (context is null)
            return createHook.Original(modelId, customize, equipment, unknown);

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
                ["runtimeObjectIndex"] = context.RuntimeObjectIndex,
                ["isCutsceneCopy"] = context.IsCutsceneCopy,
            },
        });

        var created = createHook.Original(
            appearance.ModelCharaId,
            customizeArgument,
            equipmentArgument,
            unknown);
        NativeModelScale.TryWrite(created, appearance.ModelScale);
        return created;
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

    private delegate void EnableDrawDelegate(GameObject* gameObject);

    private sealed record InjectionContext(
        LogicalActorKey Actor,
        AppearanceData Appearance,
        ushort? RuntimeObjectIndex = null,
        bool IsCutsceneCopy = false);
}
