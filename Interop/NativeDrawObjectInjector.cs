using System.Diagnostics.CodeAnalysis;
using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace ActorMorpher.Interop;

public sealed unsafe class NativeDrawObjectInjector : IDisposable
{
    private static readonly string[] EquipmentSlotNames =
    [
        "Head", "Body", "Hands", "Legs", "Feet",
        "Ears", "Neck", "Wrists", "RightRing", "LeftRing",
    ];

    private readonly IGameInteropProvider interop;
    private readonly Hook<CreateCharacterBaseDelegate> createHook;
    private readonly OneShotAppearanceConsumerTransaction consumerTransaction;
    private readonly NativeCutsceneActorTracker cutsceneActors;
    private readonly IObjectTable objectTable;
    private readonly IClientState clientState;
    private readonly IDiagnosticLog diagnostics;
    private readonly LocalPlayerAppearancePersistence transitionState;
    private readonly Func<ActorRepresentationKey, (long PublicationVersion, AppearanceData? Appearance)> getPublishedAppearance;
    private Hook<EnableDrawDelegate>? localEnableDrawHook;
    private nint localEnableDrawAddress;
    private bool disposed;

    [ThreadStatic]
    private static InjectionContext? active;

    public NativeDrawObjectInjector(
        IGameInteropProvider interop,
        IObjectTable objectTable,
        IClientState clientState,
        IDiagnosticLog diagnostics,
        LocalPlayerAppearancePersistence transitionState,
        Func<ActorRepresentationKey, (long PublicationVersion, AppearanceData? Appearance)> getPublishedAppearance)
    {
        this.interop = interop;
        this.objectTable = objectTable;
        this.clientState = clientState;
        this.diagnostics = diagnostics;
        this.transitionState = transitionState;
        this.getPublishedAppearance = getPublishedAppearance;
        cutsceneActors = new NativeCutsceneActorTracker(interop, diagnostics);
        consumerTransaction = new OneShotAppearanceConsumerTransaction(interop);
        createHook = interop.HookFromAddress<CreateCharacterBaseDelegate>(
            (nint)CharacterBase.MemberFunctionPointers.Create,
            CreateCharacterBaseDetour);
        createHook.Enable();
    }

    public bool Invoke(
        Guid operationId,
        ActorSnapshot actor,
        AppearanceData appearance,
        GameObject* gameObject)
    {
        if (disposed || active is not null)
            throw new InvalidOperationException("Draw object injection is unavailable or already active.");

        var context = new InjectionContext(actor.LogicalKey, actor.RepresentationKey, appearance, operationId)
        {
            RuntimeObjectIndex = gameObject->ObjectIndex,
        };
        return InvokeWithTemporaryBacking(context, gameObject, static target => target->EnableDraw());
    }

    public void EnablePersistentLocalAppearance()
    {
        if (disposed)
            return;

        var localPlayer = objectTable.LocalPlayer;
        if (localPlayer is null || localPlayer.Address == nint.Zero)
            return;

        EnsureLocalEnableDrawHook((GameObject*)localPlayer.Address);
        cutsceneActors.Enable((Character*)localPlayer.Address);
    }

    public void ClearPersistentLocalAppearance()
    {
        cutsceneActors.Disable();
        localEnableDrawHook?.Dispose();
        localEnableDrawHook = null;
        localEnableDrawAddress = nint.Zero;
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        active = null;
        ClearPersistentLocalAppearance();
        createHook.Dispose();
        consumerTransaction.Dispose();
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

        if (disposed || gameObject is null)
        {
            drawHook.Original(gameObject);
            return;
        }

        if (!TryDescribeLocalRepresentation(
                gameObject,
                out var representation,
                out var representationKind,
                out var isCutsceneCopy))
        {
            drawHook.Original(gameObject);
            return;
        }

        if (active is not null)
        {
            drawHook.Original(gameObject);
            return;
        }

        if (!TryPreparePersistentTransfer(
                transitionState,
                representation,
                getPublishedAppearance,
                out var actor,
                out var appearance))
        {
            drawHook.Original(gameObject);
            return;
        }

        var runtimeContext = new InjectionContext(actor, representation, appearance)
        {
            RuntimeObjectIndex = gameObject->ObjectIndex,
            IsCutsceneCopy = isCutsceneCopy,
        };
        InvokeWithTemporaryBacking(
            runtimeContext,
            gameObject,
            target => drawHook.Original(target));
    }

    internal static bool TryPreparePersistentTransfer(
        LocalPlayerAppearancePersistence transitionState,
        ActorRepresentationKey representation,
        Func<ActorRepresentationKey, (long PublicationVersion, AppearanceData? Appearance)> getPublishedAppearance,
        out LogicalActorKey actor,
        [NotNullWhen(true)] out AppearanceData? appearance)
    {
        actor = default;
        appearance = null;
        if (!transitionState.TryGetArmedSource(out _, out var source))
        {
            transitionState.ObserveRepresentation(representation);
            return false;
        }
        if (!transitionState.CanBeginTransfer(representation))
            return false;

        var latest = getPublishedAppearance(source);
        if (!ActorRegistry.IsCompleteCurrentAppearance(latest.Appearance))
        {
            transitionState.UpdatePublishedAppearance(source, false, latest.PublicationVersion);
            transitionState.ObserveRepresentation(representation);
            return false;
        }

        transitionState.UpdateRetainedAppearance(
            source,
            latest.Appearance!,
            latest.PublicationVersion);

        if (!transitionState.TryBeginTransfer(
                source,
                representation,
                latest.PublicationVersion,
                out actor))
            return false;

        appearance = latest.Appearance!;
        return true;
    }

    private bool TryDescribeLocalRepresentation(
        GameObject* gameObject,
        out ActorRepresentationKey representation,
        out string representationKind,
        out bool isCutsceneCopy)
    {
        representation = default;
        representationKind = string.Empty;
        isCutsceneCopy = false;

        var current = objectTable[gameObject->ObjectIndex];
        if (current is null || current.Address != (nint)gameObject)
            return false;

        isCutsceneCopy = cutsceneActors.IsLocalPlayerCopy(gameObject);
        var isDirectLocalPlayer = objectTable.LocalPlayer?.Address == (nint)gameObject;
        var isGPoseLocalPlayer = clientState.IsGPosing
            && gameObject->ObjectIndex == ActorRegistry.GPoseLocalPlayerSlot
            && current.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc;
        if (!isCutsceneCopy && !isDirectLocalPlayer && !isGPoseLocalPlayer)
            return false;

        representation = new ActorRepresentationKey(
            gameObject->ObjectIndex,
            current.GameObjectId,
            current.EntityId,
            isGPoseLocalPlayer,
            clientState.TerritoryType);
        representationKind = isCutsceneCopy ? "Cutscene" : isGPoseLocalPlayer ? "GPose" : "Field";
        return true;
    }

    private bool InvokeWithTemporaryBacking(
        InjectionContext context,
        GameObject* gameObject,
        EnableDrawAction enableDraw)
    {
        var character = (Character*)gameObject;
        var appearance = context.Appearance;
        var originalModelCharaId = character->ModelContainer.ModelCharaId;
        var originalCustomize = character->DrawData.CustomizeData.Data.ToArray();
        var originalEquipment = character->DrawData.EquipmentModelIds
            .ToArray()
            .Select(static model => model.Value)
            .ToArray();
        var originalMainhand = character->DrawData.Weapon(DrawDataContainer.WeaponSlot.MainHand).ModelId.Value;
        var originalOffhand = character->DrawData.Weapon(DrawDataContainer.WeaponSlot.OffHand).ModelId.Value;
        var originalFacewear = character->DrawData.GlassesIds[0];
        var originalHatHidden = character->DrawData.IsHatHidden;
        var originalVisor = character->DrawData.IsVisorToggled;
        var originalScale = character->ModelScale;
        var transaction = context.OperationId is { } operationId
            ? consumerTransaction.Begin(
                operationId,
                context.Representation,
                context.RuntimeObjectIndex,
                (nint)gameObject,
                appearance)
            : null;
        context.ConsumerTransaction = transaction;

        try
        {
            SubstituteBacking(character, appearance);
            active = context;
            enableDraw(gameObject);
            var consumerCompletionObserved = transaction is null
                || consumerTransaction.End(
                    transaction,
                    context.OperationId!.Value,
                    context.Representation,
                    context.RuntimeObjectIndex);
            if (transaction is not null)
                TryWriteConsumerCompletionObservation(context, consumerCompletionObserved);
            var createSucceeded = IsRequestedApplySuccessful(
                context.CreateCallObserved,
                context.CreateCallSucceeded,
                consumerCompletionObserved);
            if (createSucceeded)
                NativeModelScale.ApplyRendered(character, appearance.ModelScale);
            return createSucceeded;
        }
        finally
        {
            consumerTransaction.Abort(transaction);
            context.ConsumerTransaction = null;
            active = null;
            character->ModelContainer.ModelCharaId = originalModelCharaId;
            originalCustomize.AsSpan().CopyTo(character->DrawData.CustomizeData.Data);
            for (var index = 0; index < originalEquipment.Length; ++index)
                character->DrawData.EquipmentModelIds[index].Value = originalEquipment[index];
            character->DrawData.Weapon(DrawDataContainer.WeaponSlot.MainHand).ModelId.Value = originalMainhand;
            character->DrawData.Weapon(DrawDataContainer.WeaponSlot.OffHand).ModelId.Value = originalOffhand;
            character->DrawData.GlassesIds[0] = originalFacewear;
            character->DrawData.IsHatHidden = originalHatHidden;
            character->DrawData.IsVisorToggled = originalVisor;
            character->ModelScale = originalScale;
            TryWritePostRestoreObservation(context, character);
        }
    }

    internal static bool IsRequestedCreateSuccessful(bool createCallObserved, bool createCallSucceeded)
        => createCallObserved && createCallSucceeded;

    internal static bool IsRequestedApplySuccessful(
        bool createCallObserved,
        bool createCallSucceeded,
        bool consumerCompletionObserved)
    {
        // Consumer completion is diagnostic-only and must never become an Apply gate.
        _ = consumerCompletionObserved;
        return IsRequestedCreateSuccessful(createCallObserved, createCallSucceeded);
    }

    internal static void SubstituteBacking(Character* character, AppearanceData appearance)
    {
        character->ModelContainer.ModelCharaId = checked((int)appearance.ModelCharaId);
        // Selected Customize and equipment belong to the Create arguments, not the actor's
        // game-owned base. Stateful Create/slot listeners must not learn temporary B as that base.

        if (appearance.Category == ModelCategory.Human)
        {
            if (appearance.Mainhand is { } mainhand)
                character->DrawData.Weapon(DrawDataContainer.WeaponSlot.MainHand).ModelId.Value = mainhand;
            if (appearance.Offhand is { } offhand)
                character->DrawData.Weapon(DrawDataContainer.WeaponSlot.OffHand).ModelId.Value = offhand;
            if (appearance.FacewearModelId is { } facewear)
                character->DrawData.GlassesIds[0] = facewear;
            if (appearance.HatVisible is { } hatVisible)
                character->DrawData.IsHatHidden = !hatVisible;
            if (appearance.VisorToggled is { } visor)
                character->DrawData.IsVisorToggled = visor;
        }

        if (appearance.ModelScale is { } scale)
            character->ModelScale = scale;
    }

    private CharacterBase* CreateCharacterBaseDetour(
        uint modelId,
        CustomizeData* customize,
        EquipmentModelId* equipment,
        byte unknown)
    {
        var context = active;
        if (context is null || context.CreateCallObserved)
            return createHook.Original(modelId, customize, equipment, unknown);
        context.CreateCallObserved = true;
        if (context.ConsumerTransaction is { } transaction)
            transaction.TryBeginCreate();
        else
            active = null;

        var appearance = context.Appearance;
        var customizeInjected = !appearance.Customize.IsDefaultOrEmpty;
        var equipmentInjected = !appearance.Equipment.IsDefaultOrEmpty;

        var created = (CharacterBase*)InvokeWithTemporaryCreateBuffers(
            appearance,
            (nint)customize,
            (nint)equipment,
            (customizeAddress, equipmentAddress) =>
            {
                TryWriteCreateArguments(context, customizeAddress, equipmentAddress, "BeforeOriginalCreate");
                var result = createHook.Original(
                    appearance.ModelCharaId,
                    (CustomizeData*)customizeAddress,
                    (EquipmentModelId*)equipmentAddress,
                    unknown);
                TryWriteCreateArguments(context, customizeAddress, equipmentAddress, "OriginalCreateReturnedArguments",
                    result is not null ? "NonNull" : "Null");
                TryWriteCreateObservation(
                    context, result, modelId, customizeInjected, equipmentInjected,
                    "OriginalCreateReturnedBeforeBufferRestore");
                return (nint)result;
            });
        context.CreateCallSucceeded = created is not null;
        context.CreatedCharacterBaseAddress = (nint)created;
        context.ConsumerTransaction?.CompleteCreate((nint)created);
        TryWriteCreateObservation(
            context,
            created,
            modelId,
            customizeInjected,
            equipmentInjected);

        return created;
    }

    internal static nint InvokeWithTemporaryCreateBuffers(
        AppearanceData appearance,
        nint customizeAddress,
        nint equipmentAddress,
        CreateOriginalCall original)
    {
        var hasCustomize = !appearance.Customize.IsDefaultOrEmpty;
        var hasEquipment = !appearance.Equipment.IsDefaultOrEmpty;
        const int customizeLength = 26;
        const int equipmentSlotCount = 10;
        Span<byte> originalCustomize = stackalloc byte[customizeLength];
        Span<ulong> originalEquipment = stackalloc ulong[equipmentSlotCount];
        var customize = hasCustomize
            ? new Span<byte>((void*)customizeAddress, customizeLength)
            : default;
        var equipment = hasEquipment
            ? new Span<ulong>((void*)equipmentAddress, equipmentSlotCount)
            : default;

        if (hasCustomize)
        {
            customize.CopyTo(originalCustomize);
            appearance.Customize.AsSpan().CopyTo(customize);
        }

        if (hasEquipment)
        {
            for (var index = 0; index < equipmentSlotCount; ++index)
            {
                originalEquipment[index] = equipment[index];
                equipment[index] = appearance.Equipment[index];
            }
        }

        try
        {
            return original(customizeAddress, equipmentAddress);
        }
        finally
        {
            if (hasCustomize)
                originalCustomize.CopyTo(customize);
            if (hasEquipment)
            {
                for (var index = 0; index < equipmentSlotCount; ++index)
                    equipment[index] = originalEquipment[index];
            }
        }
    }

    internal static IReadOnlyDictionary<string, object?> CaptureCreateArguments(
        AppearanceData appearance,
        nint customizeAddress,
        nint equipmentAddress,
        string callResult = "NotCalled")
    {
        var customize = appearance.Customize.IsDefaultOrEmpty
            ? null
            : new ReadOnlySpan<byte>((void*)customizeAddress, 26).ToArray();
        var equipment = appearance.Equipment.IsDefaultOrEmpty
            ? null
            : new ReadOnlySpan<ulong>((void*)equipmentAddress, 10).ToArray();
        return new Dictionary<string, object?>
        {
            ["callResult"] = callResult,
            ["requestedModelCharaId"] = appearance.ModelCharaId,
            ["argumentCustomize"] = customize,
            ["argumentCustomizeSignature"] = customize is null ? null : Signature(customize),
            ["argumentEquipment"] = equipment,
            ["argumentEquipmentSignature"] = equipment is null ? null : Signature(equipment),
        };
    }

    private void TryWriteCreateArguments(
        InjectionContext context,
        nint customizeAddress,
        nint equipmentAddress,
        string phase,
        string callResult = "NotCalled")
    {
        try
        {
            WriteHumanObservation(context, phase, "Create hook arguments observed without modifying them.",
                CaptureCreateArguments(context.Appearance, customizeAddress, equipmentAddress, callResult));
        }
        catch (Exception exception)
        {
            WriteObservationFailure(context, phase, exception);
        }
    }

    private void TryWriteCreateObservation(
        InjectionContext context,
        CharacterBase* created,
        uint originalModelCharaId,
        bool customizeInjected,
        bool equipmentInjected,
        string phase = "CreateReturnedBeforeBackingRestore")
    {
        try
        {
            var character = ResolveCurrentCharacter(context.Representation);
            var observation = CaptureHumanObservation(
                character,
                created,
                created is null ? null : context.Appearance.ModelCharaId,
                created is null ? "Unavailable" : "CreateArgument",
                created is null ? "Unavailable" : "Created",
                created is null ? "Unavailable" : null,
                created is not null ? "NonNull" : "Null");
            var properties = new Dictionary<string, object?>(BuildHumanDiagnosticProperties(context.Appearance, observation))
            {
                ["originalModelCharaId"] = originalModelCharaId,
                ["injectedModelCharaId"] = context.Appearance.ModelCharaId,
                ["category"] = context.Appearance.Category,
                ["bodyType"] = context.Appearance.Customize.Length > 2 ? context.Appearance.Customize[2] : null,
                ["customizeInjected"] = customizeInjected,
                ["equipmentInjected"] = equipmentInjected,
                ["customizeSignature"] = Signature(context.Appearance.Customize),
                ["equipmentSignature"] = Signature(context.Appearance.Equipment),
                ["runtimeObjectIndex"] = context.RuntimeObjectIndex,
                ["isCutsceneCopy"] = context.IsCutsceneCopy,
            };
            WriteHumanObservation(context, phase, "Returned CharacterBase observed at the named Create boundary.", properties);
        }
        catch (Exception exception)
        {
            WriteObservationFailure(context, phase, exception);
        }
    }

    private void TryWritePostRestoreObservation(InjectionContext context, Character* character)
    {
        try
        {
            var current = character is null
                ? null
                : ((GameObject*)character)->GetCharacterBase();
            var selectedAddress = SelectPostRestoreCharacterBaseAddress(
                context.CreatedCharacterBaseAddress,
                (nint)current,
                current is not null && current->GetModelType() == CharacterBase.ModelType.Human,
                out var continuity,
                out var unavailableReason);
            var selected = (CharacterBase*)selectedAddress;
            uint? modelCharaId = selected is null
                ? null
                : continuity == "Changed"
                    ? null
                    : context.Appearance.ModelCharaId;
            var modelCharaIdSource = selected is null
                ? "Unavailable"
                : continuity == "Changed"
                    ? "UnavailableAfterCharacterBaseChange"
                    : "CreateArgument";
            var observedCharacterBase = context.CreatedCharacterBaseAddress == nint.Zero
                ? null
                : selected is not null ? selected : current;
            var observation = CaptureHumanObservation(
                character,
                observedCharacterBase,
                modelCharaId,
                modelCharaIdSource,
                continuity,
                unavailableReason,
                context.CreateCallSucceeded ? "NonNull" : "Null");
            var properties = new Dictionary<string, object?>(BuildHumanDiagnosticProperties(context.Appearance, observation))
            {
                ["runtimeObjectIndex"] = context.RuntimeObjectIndex,
                ["isCutsceneCopy"] = context.IsCutsceneCopy,
            };
            WriteHumanObservation(context, "BackingRestored", "Current CharacterBase observed after backing restoration.", properties);
        }
        catch (Exception exception)
        {
            WriteObservationFailure(context, "BackingRestored", exception);
        }
    }

    private void TryWriteConsumerCompletionObservation(
        InjectionContext context,
        bool consumerCompletionObserved)
    {
        try
        {
            diagnostics.Write(new DiagnosticLogEntry
            {
                EventId = DiagnosticEventIds.DrawObjectConsumerObserved,
                Category = DiagnosticCategory.Redraw,
                OperationId = context.OperationId is { } operationId ? $"redraw-{operationId:N}" : null,
                Message = "Consumer transaction completion observed without changing the Apply result.",
                ActorKey = DiagnosticActorKeys.Format(diagnostics, context.Actor),
                RepresentationKey = context.Representation.ToString(),
                Phase = "ConsumerTransaction",
                Outcome = consumerCompletionObserved ? "Complete" : "Incomplete",
                Properties = new Dictionary<string, object?>(
                    context.ConsumerTransaction!.CaptureObservation())
                {
                    ["runtimeObjectIndex"] = context.RuntimeObjectIndex,
                    ["consumerCompletionObserved"] = consumerCompletionObserved,
                },
            });
        }
        catch (Exception exception)
        {
            WriteObservationFailure(context, "ConsumerTransaction", exception);
        }
    }

    private Character* ResolveCurrentCharacter(ActorRepresentationKey representation)
    {
        var current = objectTable[representation.ObjectIndex];
        return current is null
            || current.Address == nint.Zero
            || current.GameObjectId != representation.GameObjectId
            || current.EntityId != representation.EntityId
                ? null
                : (Character*)current.Address;
    }

    private void WriteHumanObservation(
        InjectionContext context,
        string phase,
        string message,
        IReadOnlyDictionary<string, object?> properties)
        => diagnostics.Write(CreateHumanObservationEntry(phase, message, properties) with
        {
            ActorKey = DiagnosticActorKeys.Format(diagnostics, context.Actor),
            RepresentationKey = context.Representation.ToString(),
            OperationId = context.OperationId is { } id ? $"redraw-{id:N}" : null,
        });

    internal static DiagnosticLogEntry CreateHumanObservationEntry(
        string phase,
        string message,
        IReadOnlyDictionary<string, object?> properties)
        => new()
        {
            EventId = DiagnosticEventIds.DrawObjectCreateInjected,
            Category = DiagnosticCategory.Redraw,
            Message = message,
            Phase = phase,
            Outcome = (string)properties["callResult"]!,
            Properties = properties,
        };

    private void WriteObservationFailure(InjectionContext context, string phase, Exception exception)
    {
        try
        {
            diagnostics.Error(
                DiagnosticEventIds.HandledException,
                DiagnosticCategory.Redraw,
                $"Apply diagnostic observation failed during {phase} without changing the Apply result.",
                exception,
                new Dictionary<string, object?>
                {
                    ["phase"] = phase,
                    ["runtimeObjectIndex"] = context.RuntimeObjectIndex,
                });
        }
        catch
        {
            // Diagnostics must not change the requested Apply result.
        }
    }

    private static HumanDiagnosticObservation CaptureHumanObservation(
        Character* character,
        CharacterBase* characterBase,
        uint? modelCharaId,
        string modelCharaIdSource,
        string continuity,
        string? unavailableReason,
        string callResult)
    {
        var hatVisibleBacking = character is null
            ? null
            : (bool?)!character->DrawData.IsHatHidden;
        if (characterBase is null)
            return new HumanDiagnosticObservation(
                modelCharaId,
                modelCharaIdSource,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                hatVisibleBacking,
                null,
                null,
                null,
                continuity,
                "Unavailable",
                unavailableReason ?? "CharacterBaseNull",
                callResult);

        var modelType = ToModelCategory(characterBase->GetModelType());
        if (modelType != ModelCategory.Human || character is null)
            return new HumanDiagnosticObservation(
                modelCharaId,
                modelCharaIdSource,
                modelType,
                null,
                null,
                NativeModelScale.CaptureRendered(character),
                null,
                null,
                null,
                hatVisibleBacking,
                null,
                null,
                null,
                continuity,
                "Unavailable",
                unavailableReason ?? "CurrentCharacterBaseNonHuman",
                callResult);

        var human = (Human*)characterBase;
        var customize = human->Customize.Data.ToArray();
        var outfit = NativeOutfitMemory.CaptureRendered(character, human);
        return new HumanDiagnosticObservation(
            modelCharaId,
            modelCharaIdSource,
            modelType,
            customize,
            outfit.Equipment.Select(ActorRegistry.ToEquipmentModelValue).ToArray(),
            NativeModelScale.CaptureRendered(character),
            NativeAppearanceMemory.CaptureRenderedWeapon(character, DrawDataContainer.WeaponSlot.MainHand),
            NativeAppearanceMemory.CaptureRenderedWeapon(character, DrawDataContainer.WeaponSlot.OffHand),
            outfit.Facewear.ModelId,
            hatVisibleBacking,
            null,
            null,
            outfit.VisorToggled,
            continuity,
            "Available",
            unavailableReason,
            callResult)
        {
            EquipmentInput = CaptureNativeEquipmentInput(characterBase),
            SlotNeedsUpdateBitfield = human->SlotNeedsUpdateBitfield,
        };
    }

    internal static ulong[] CaptureNativeEquipmentInput(CharacterBase* characterBase)
    {
        var equipment = new ulong[10];
        for (uint slot = 0; slot < equipment.Length; ++slot)
        {
            EquipmentModelId model = default;
            characterBase->GetEquipmentSlotModel(&model, slot);
            equipment[slot] = model.Value;
        }
        return equipment;
    }

    private static ModelCategory ToModelCategory(CharacterBase.ModelType modelType)
        => modelType switch
        {
            CharacterBase.ModelType.Human => ModelCategory.Human,
            CharacterBase.ModelType.DemiHuman => ModelCategory.Demihuman,
            CharacterBase.ModelType.Monster => ModelCategory.Monster,
            _ => ModelCategory.Other,
        };

    internal static nint SelectPostRestoreCharacterBaseAddress(
        nint createdAddress,
        nint currentAddress,
        bool currentIsHuman,
        out string continuity,
        out string? unavailableReason)
    {
        if (createdAddress == nint.Zero)
        {
            continuity = "Unavailable";
            unavailableReason = "CreateReturnedNull";
            return nint.Zero;
        }
        if (currentAddress == nint.Zero)
        {
            continuity = "Unavailable";
            unavailableReason = "CurrentCharacterBaseNull";
            return nint.Zero;
        }

        continuity = createdAddress == currentAddress ? "Same" : "Changed";
        if (!currentIsHuman)
        {
            unavailableReason = "CurrentCharacterBaseNonHuman";
            return nint.Zero;
        }

        unavailableReason = null;
        return currentAddress;
    }

    internal static IReadOnlyDictionary<string, object?> BuildHumanDiagnosticProperties(
        AppearanceData desired,
        HumanDiagnosticObservation observed)
    {
        var mismatches = new List<string>();
        var unavailable = new List<string>();

        CompareRequired("ModelCharaId", desired.ModelCharaId, observed.ModelCharaId, mismatches, unavailable);
        CompareRequired("ModelType", desired.Category, observed.ModelType, mismatches, unavailable);
        CompareSequence("CustomizeSignature", desired.Customize, observed.Customize, mismatches, unavailable);
        CompareRequired("Race", At(desired.Customize, 0), At(observed.Customize, 0), mismatches, unavailable);
        CompareRequired("Gender", At(desired.Customize, 1), At(observed.Customize, 1), mismatches, unavailable);
        CompareRequired("BodyType", At(desired.Customize, 2), At(observed.Customize, 2), mismatches, unavailable);
        CompareSequence("EquipmentSignature", desired.Equipment, observed.Equipment, mismatches, unavailable);
        for (var index = 0; index < EquipmentSlotNames.Length; ++index)
            CompareRequired(
                EquipmentSlotNames[index],
                At(desired.Equipment, index),
                At(observed.Equipment, index),
                mismatches,
                unavailable);
        CompareRequired("ModelScale", desired.ModelScale, observed.ModelScale, mismatches, unavailable);
        CompareRequired("Mainhand", desired.Mainhand, observed.Mainhand, mismatches, unavailable);
        CompareRequired("Offhand", desired.Offhand, observed.Offhand, mismatches, unavailable);
        CompareRequired("FacewearModelId", desired.FacewearModelId, observed.FacewearModelId, mismatches, unavailable);
        CompareRequired("HatVisible", desired.HatVisible, observed.HatVisibleObserved, mismatches, unavailable);
        CompareRequired("VisorToggled", desired.VisorToggled, observed.VisorToggled, mismatches, unavailable);

        var properties = new Dictionary<string, object?>
        {
            ["callResult"] = observed.CallResult,
            ["payloadComparison"] = mismatches.Count > 0 ? "Mismatch" : unavailable.Count > 0 ? "Unavailable" : "Match",
            ["mismatchedFields"] = mismatches,
            ["unavailableFields"] = unavailable,
            ["humanSnapshotStatus"] = observed.HumanSnapshotStatus,
            ["unavailableReason"] = observed.UnavailableReason,
            ["characterBaseContinuity"] = observed.CharacterBaseContinuity,
            ["requestedModelCharaId"] = desired.ModelCharaId,
            ["observedModelCharaId"] = observed.ModelCharaId,
            ["modelCharaIdSource"] = observed.ModelCharaIdSource,
            ["requestedModelType"] = desired.Category,
            ["observedModelType"] = observed.ModelType,
            ["requestedCustomizeSignature"] = Signature(desired.Customize),
            ["observedCustomizeSignature"] = observed.Customize is null ? null : Signature(observed.Customize),
            ["requestedRace"] = At(desired.Customize, 0),
            ["observedRace"] = At(observed.Customize, 0),
            ["requestedGender"] = At(desired.Customize, 1),
            ["observedGender"] = At(observed.Customize, 1),
            ["requestedBodyType"] = At(desired.Customize, 2),
            ["observedBodyType"] = At(observed.Customize, 2),
            ["requestedEquipmentSignature"] = Signature(desired.Equipment),
            ["observedEquipmentSignature"] = observed.Equipment is null ? null : Signature(observed.Equipment),
            ["equipmentInputSource"] = observed.EquipmentInput is null ? "Unavailable" : "CharacterBase.GetEquipmentSlotModel",
            ["equipmentInputSignature"] = observed.EquipmentInput is null ? null : Signature(observed.EquipmentInput),
            ["slotNeedsUpdateBitfield"] = observed.SlotNeedsUpdateBitfield,
            ["requestedModelScale"] = desired.ModelScale,
            ["observedModelScale"] = observed.ModelScale,
            ["requestedMainhand"] = desired.Mainhand,
            ["observedMainhand"] = observed.Mainhand,
            ["requestedOffhand"] = desired.Offhand,
            ["observedOffhand"] = observed.Offhand,
            ["requestedFacewearModelId"] = desired.FacewearModelId,
            ["observedFacewearModelId"] = observed.FacewearModelId,
            ["requestedHatVisible"] = desired.HatVisible,
            ["hatVisibleBacking"] = observed.HatVisibleBacking,
            ["hatVisibleBackingSource"] = observed.HatVisibleBacking is null ? "Unavailable" : "Character.DrawData.IsHatHidden",
            ["hatVisibleEffective"] = observed.HatVisibleEffective,
            ["hatVisibleEffectiveSource"] = observed.HatVisibleEffective is null ? "Unavailable" : "ManagedAppearance",
            ["hatVisibleObserved"] = observed.HatVisibleObserved,
            ["hatVisibleObservedSource"] = "Unavailable",
            ["requestedVisorToggled"] = desired.VisorToggled,
            ["observedVisorToggled"] = observed.VisorToggled,
        };
        for (var index = 0; index < EquipmentSlotNames.Length; ++index)
        {
            var name = EquipmentSlotNames[index];
            properties[$"requested{name}"] = At(desired.Equipment, index);
            properties[$"observed{name}"] = At(observed.Equipment, index);
            properties[$"input{name}"] = At(observed.EquipmentInput, index);
        }
        return properties;
    }

    private static void CompareRequired<T>(
        string field,
        T? expected,
        T? actual,
        ICollection<string> mismatches,
        ICollection<string> unavailable)
        where T : struct
    {
        if (actual is null)
            unavailable.Add(field);
        else if (expected is null || !EqualityComparer<T>.Default.Equals(expected.Value, actual.Value))
            mismatches.Add(field);
    }

    private static void CompareSequence<T>(
        string field,
        IEnumerable<T> expected,
        IReadOnlyList<T>? actual,
        ICollection<string> mismatches,
        ICollection<string> unavailable)
    {
        if (actual is null)
            unavailable.Add(field);
        else if (!expected.SequenceEqual(actual))
            mismatches.Add(field);
    }

    private static T? At<T>(IReadOnlyList<T>? values, int index)
        where T : struct
        => values is not null && index >= 0 && index < values.Count ? values[index] : null;

    internal static string Signature(IEnumerable<byte> values)
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

    internal static string Signature(IEnumerable<ulong> values)
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

    internal delegate nint CreateOriginalCall(
        nint customizeAddress,
        nint equipmentAddress);

    private delegate void EnableDrawDelegate(GameObject* gameObject);

    private delegate void EnableDrawAction(GameObject* gameObject);

    internal sealed record HumanDiagnosticObservation(
        uint? ModelCharaId,
        string ModelCharaIdSource,
        ModelCategory? ModelType,
        IReadOnlyList<byte>? Customize,
        IReadOnlyList<ulong>? Equipment,
        float? ModelScale,
        ulong? Mainhand,
        ulong? Offhand,
        ushort? FacewearModelId,
        bool? HatVisibleBacking,
        bool? HatVisibleEffective,
        bool? HatVisibleObserved,
        bool? VisorToggled,
        string CharacterBaseContinuity,
        string HumanSnapshotStatus,
        string? UnavailableReason,
        string CallResult)
    {
        public IReadOnlyList<ulong>? EquipmentInput { get; init; }
        public uint? SlotNeedsUpdateBitfield { get; init; }
    }

    private sealed class InjectionContext(
        LogicalActorKey actor,
        ActorRepresentationKey representation,
        AppearanceData appearance,
        Guid? operationId = null)
    {
        public LogicalActorKey Actor { get; } = actor;
        public ActorRepresentationKey Representation { get; } = representation;
        public AppearanceData Appearance { get; set; } = appearance;
        public Guid? OperationId { get; } = operationId;
        public ushort RuntimeObjectIndex { get; init; }
        public bool IsCutsceneCopy { get; init; }
        public bool CreateCallObserved { get; set; }
        public bool CreateCallSucceeded { get; set; }
        public nint CreatedCharacterBaseAddress { get; set; }
        public OneShotAppearanceConsumerTransaction.Transaction? ConsumerTransaction { get; set; }
    }
}
