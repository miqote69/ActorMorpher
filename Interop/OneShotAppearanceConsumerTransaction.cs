using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace ActorMorpher.Interop;

/// <summary>
/// Removable experiment that substitutes one selected payload only at normal game
/// consumer calls made by the exact synchronous Apply transaction.
/// </summary>
internal sealed unsafe class OneShotAppearanceConsumerTransaction : IDisposable
{
    private readonly Hook<ArmorConsumerDelegate>? armorHook;
    private readonly Hook<WeaponConsumerDelegate>? weaponHook;
    private readonly Hook<FacewearConsumerDelegate>? facewearHook;
    private readonly Hook<HatConsumerDelegate>? hatHook;
    private readonly Hook<VisorConsumerDelegate>? visorHook;
    private Transaction? active;

    public OneShotAppearanceConsumerTransaction(IGameInteropProvider interop)
    {
        armorHook = interop.HookFromAddress<ArmorConsumerDelegate>(
            (nint)DrawDataContainer.MemberFunctionPointers.LoadEquipment,
            ArmorConsumerDetour);
        weaponHook = interop.HookFromAddress<WeaponConsumerDelegate>(
            (nint)DrawDataContainer.MemberFunctionPointers.LoadWeapon,
            WeaponConsumerDetour);
        facewearHook = interop.HookFromAddress<FacewearConsumerDelegate>(
            (nint)DrawDataContainer.MemberFunctionPointers.SetGlasses,
            FacewearConsumerDetour);
        hatHook = interop.HookFromAddress<HatConsumerDelegate>(
            (nint)DrawDataContainer.MemberFunctionPointers.HideHeadgear,
            HatConsumerDetour);
        visorHook = interop.HookFromAddress<VisorConsumerDelegate>(
            (nint)DrawDataContainer.MemberFunctionPointers.SetVisor,
            VisorConsumerDetour);

        armorHook.Enable();
        weaponHook.Enable();
        facewearHook.Enable();
        hatHook.Enable();
        visorHook.Enable();
    }

    internal OneShotAppearanceConsumerTransaction()
    {
    }

    internal Transaction Begin(
        Guid operationId,
        ActorRepresentationKey representation,
        ushort runtimeObjectIndex,
        nint targetAddress,
        AppearanceData appearance)
    {
        if (active is not null)
            throw new InvalidOperationException("An appearance consumer transaction is already active.");

        active = new Transaction(
            operationId,
            representation,
            runtimeObjectIndex,
            targetAddress,
            appearance);
        return active;
    }

    internal bool End(
        Transaction transaction,
        Guid operationId,
        ActorRepresentationKey representation,
        ushort runtimeObjectIndex)
    {
        var matches = ReferenceEquals(active, transaction)
            && transaction.Matches(operationId, representation, runtimeObjectIndex);
        if (ReferenceEquals(active, transaction))
            active = null;
        return matches && transaction.IsComplete;
    }

    internal void Abort(Transaction? transaction)
    {
        if (ReferenceEquals(active, transaction))
            active = null;
    }

    public void Dispose()
    {
        active = null;
        visorHook?.Dispose();
        hatHook?.Dispose();
        facewearHook?.Dispose();
        weaponHook?.Dispose();
        armorHook?.Dispose();
    }

    private void ArmorConsumerDetour(
        nint drawData,
        uint slot,
        nint data,
        byte force)
    {
        GetOwner((DrawDataContainer*)drawData, out var ownerAddress, out var ownerObjectIndex);
        DispatchArmor(
            drawData,
            ownerAddress,
            ownerObjectIndex,
            slot,
            data,
            force,
            (container, forwardedSlot, forwardedData, forwardedForce)
                => armorHook!.Original(container, forwardedSlot, forwardedData, forwardedForce));
    }

    internal void DispatchArmor(
        nint drawData,
        nint ownerAddress,
        ushort ownerObjectIndex,
        uint slot,
        nint data,
        byte force,
        ArmorForwarder original)
    {
        var replacement = data == nint.Zero ? default : *(ulong*)data;
        var forwarded = data;
        active?.ObserveArmorCall(ownerAddress, ownerObjectIndex);
        if (data != nint.Zero
            && active?.TrySubstituteArmor(ownerAddress, ownerObjectIndex, slot, ref replacement) is true)
            forwarded = (nint)(&replacement);
        original(drawData, slot, forwarded, force);
    }

    private void WeaponConsumerDetour(
        DrawDataContainer* drawData,
        uint slot,
        ulong weapon,
        byte redrawOnEquality,
        byte unknown2,
        byte skipGameObject,
        byte unknown4,
        byte unknown5)
    {
        GetOwner(drawData, out var ownerAddress, out var ownerObjectIndex);
        DispatchWeapon(
            (nint)drawData,
            ownerAddress,
            ownerObjectIndex,
            slot,
            weapon,
            redrawOnEquality,
            unknown2,
            skipGameObject,
            unknown4,
            unknown5,
            (container, forwardedSlot, forwardedWeapon, first, second, third, fourth, fifth)
                => weaponHook!.Original(
                    (DrawDataContainer*)container,
                    forwardedSlot,
                    forwardedWeapon,
                    first,
                    second,
                    third,
                    fourth,
                    fifth));
    }

    internal void DispatchWeapon(
        nint drawData,
        nint ownerAddress,
        ushort ownerObjectIndex,
        uint slot,
        ulong weapon,
        byte redrawOnEquality,
        byte unknown2,
        byte skipGameObject,
        byte unknown4,
        byte unknown5,
        WeaponForwarder original)
    {
        active?.TrySubstituteWeapon(ownerAddress, ownerObjectIndex, slot, ref weapon);
        original(
            drawData,
            slot,
            weapon,
            redrawOnEquality,
            unknown2,
            skipGameObject,
            unknown4,
            unknown5);
    }

    private void FacewearConsumerDetour(DrawDataContainer* drawData, int slot, ushort id)
    {
        GetOwner(drawData, out var ownerAddress, out var ownerObjectIndex);
        DispatchFacewear(
            (nint)drawData,
            ownerAddress,
            ownerObjectIndex,
            slot,
            id,
            (container, forwardedSlot, forwardedId)
                => facewearHook!.Original((DrawDataContainer*)container, forwardedSlot, forwardedId));
    }

    internal void DispatchFacewear(
        nint drawData,
        nint ownerAddress,
        ushort ownerObjectIndex,
        int slot,
        ushort id,
        FacewearForwarder original)
    {
        active?.TrySubstituteFacewear(ownerAddress, ownerObjectIndex, slot, ref id);
        original(drawData, slot, id);
    }

    private void HatConsumerDetour(DrawDataContainer* drawData, uint id, byte hidden)
    {
        GetOwner(drawData, out var ownerAddress, out var ownerObjectIndex);
        DispatchHat(
            (nint)drawData,
            ownerAddress,
            ownerObjectIndex,
            id,
            hidden,
            (container, forwardedId, forwardedHidden)
                => hatHook!.Original((DrawDataContainer*)container, forwardedId, forwardedHidden));
    }

    internal void DispatchHat(
        nint drawData,
        nint ownerAddress,
        ushort ownerObjectIndex,
        uint id,
        byte hidden,
        HatForwarder original)
    {
        active?.TrySubstituteHat(ownerAddress, ownerObjectIndex, id, ref hidden);
        original(drawData, id, hidden);
    }

    private void VisorConsumerDetour(DrawDataContainer* drawData, byte toggled)
    {
        GetOwner(drawData, out var ownerAddress, out var ownerObjectIndex);
        DispatchVisor(
            (nint)drawData,
            ownerAddress,
            ownerObjectIndex,
            toggled,
            (container, forwarded) => visorHook!.Original((DrawDataContainer*)container, forwarded));
    }

    internal void DispatchVisor(
        nint drawData,
        nint ownerAddress,
        ushort ownerObjectIndex,
        byte toggled,
        VisorForwarder original)
    {
        active?.TrySubstituteVisor(ownerAddress, ownerObjectIndex, ref toggled);
        original(drawData, toggled);
    }

    private static void GetOwner(
        DrawDataContainer* drawData,
        out nint ownerAddress,
        out ushort ownerObjectIndex)
    {
        var owner = drawData is null ? null : drawData->OwnerObject;
        ownerAddress = (nint)owner;
        ownerObjectIndex = owner is null ? ushort.MaxValue : ((GameObject*)owner)->ObjectIndex;
    }

    private delegate void ArmorConsumerDelegate(
        nint drawData,
        uint slot,
        nint data,
        byte force);

    private delegate void WeaponConsumerDelegate(
        DrawDataContainer* drawData,
        uint slot,
        ulong weapon,
        byte redrawOnEquality,
        byte unknown2,
        byte skipGameObject,
        byte unknown4,
        byte unknown5);

    private delegate void FacewearConsumerDelegate(DrawDataContainer* drawData, int slot, ushort id);

    private delegate void HatConsumerDelegate(DrawDataContainer* drawData, uint id, byte hidden);

    private delegate void VisorConsumerDelegate(DrawDataContainer* drawData, byte toggled);

    internal delegate void ArmorForwarder(nint drawData, uint slot, nint data, byte force);

    internal delegate void WeaponForwarder(
        nint drawData,
        uint slot,
        ulong weapon,
        byte redrawOnEquality,
        byte unknown2,
        byte skipGameObject,
        byte unknown4,
        byte unknown5);

    internal delegate void FacewearForwarder(nint drawData, int slot, ushort id);

    internal delegate void HatForwarder(nint drawData, uint id, byte hidden);

    internal delegate void VisorForwarder(nint drawData, byte toggled);

    internal sealed class Transaction(
        Guid operationId,
        ActorRepresentationKey representation,
        ushort runtimeObjectIndex,
        nint targetAddress,
        AppearanceData appearance)
    {
        private const int EquipmentSlotCount = 10;
        private const ushort CompleteEquipmentMask = (1 << EquipmentSlotCount) - 1;

        private readonly ushort requiredEquipmentMask = appearance.Equipment.IsDefaultOrEmpty
            ? (ushort)0
            : CompleteEquipmentMask;
        private ushort observedEquipmentMask;
        private bool createInProgress;
        private bool createObserved;
        private bool createSucceeded;
        private bool mainhandObserved;
        private bool offhandObserved;
        private bool facewearObserved;
        private bool hatObserved;
        private bool visorObserved;
        private int armorCallsDuringCreate;
        private int armorCallsOutsideCreate;
        private int armorCallsWithOtherOwner;

        internal Guid OperationId { get; } = operationId;
        internal ActorRepresentationKey Representation { get; } = representation;
        internal ushort RuntimeObjectIndex { get; } = runtimeObjectIndex;
        internal nint TargetAddress { get; } = targetAddress;
        internal AppearanceData Appearance { get; } = appearance;

        internal bool IsComplete
            => createObserved
                && createSucceeded
                && observedEquipmentMask == requiredEquipmentMask
                && (Appearance.Mainhand is null || mainhandObserved)
                && (Appearance.Offhand is null || offhandObserved)
                && (Appearance.FacewearModelId is null || facewearObserved)
                && (Appearance.HatVisible is null || hatObserved)
                && (Appearance.VisorToggled is null || visorObserved);

        internal void ObserveArmorCall(nint ownerAddress, ushort ownerObjectIndex)
        {
            if (createInProgress)
                ++armorCallsDuringCreate;
            else
                ++armorCallsOutsideCreate;
            if (!MatchesOwner(ownerAddress, ownerObjectIndex))
                ++armorCallsWithOtherOwner;
        }

        internal IReadOnlyDictionary<string, object?> CaptureObservation()
            => new Dictionary<string, object?>
            {
                ["observedEquipmentMask"] = observedEquipmentMask,
                ["requiredEquipmentMask"] = requiredEquipmentMask,
                ["armorCallsDuringCreate"] = armorCallsDuringCreate,
                ["armorCallsOutsideCreate"] = armorCallsOutsideCreate,
                ["armorCallsWithOtherOwner"] = armorCallsWithOtherOwner,
                ["mainhandObserved"] = mainhandObserved,
                ["offhandObserved"] = offhandObserved,
                ["facewearObserved"] = facewearObserved,
                ["hatObserved"] = hatObserved,
                ["visorObserved"] = visorObserved,
            };

        internal bool Matches(
            Guid candidateOperationId,
            ActorRepresentationKey candidateRepresentation,
            ushort candidateRuntimeObjectIndex)
            => OperationId == candidateOperationId
                && Representation == candidateRepresentation
                && RuntimeObjectIndex == candidateRuntimeObjectIndex
                && Representation.ObjectIndex == RuntimeObjectIndex;

        internal bool TryBeginCreate()
        {
            if (createInProgress || createObserved)
                return false;
            createInProgress = true;
            return true;
        }

        internal void CompleteCreate(nint address)
        {
            createInProgress = false;
            createObserved = true;
            createSucceeded = address != nint.Zero;
        }

        internal bool TrySubstituteArmor(
            nint ownerAddress,
            ushort ownerObjectIndex,
            uint slot,
            ref ulong value)
        {
            if (slot >= EquipmentSlotCount
                || slot >= Appearance.Equipment.Length
                || !MatchesOwner(ownerAddress, ownerObjectIndex))
                return false;

            value = Appearance.Equipment[(int)slot];
            observedEquipmentMask |= (ushort)(1 << (int)slot);
            return true;
        }

        internal bool TrySubstituteWeapon(
            nint ownerAddress,
            ushort ownerObjectIndex,
            uint slot,
            ref ulong value)
        {
            if (!MatchesOwner(ownerAddress, ownerObjectIndex))
                return false;
            if (slot == (uint)DrawDataContainer.WeaponSlot.MainHand && Appearance.Mainhand is { } mainhand)
            {
                value = mainhand;
                mainhandObserved = true;
                return true;
            }
            if (slot == (uint)DrawDataContainer.WeaponSlot.OffHand && Appearance.Offhand is { } offhand)
            {
                value = offhand;
                offhandObserved = true;
                return true;
            }
            return false;
        }

        internal bool TrySubstituteFacewear(
            nint ownerAddress,
            ushort ownerObjectIndex,
            int slot,
            ref ushort id)
        {
            if (!MatchesOwner(ownerAddress, ownerObjectIndex)
                || slot != 0
                || Appearance.FacewearModelId is not { } facewear)
                return false;
            id = facewear;
            facewearObserved = true;
            return true;
        }

        internal bool TrySubstituteHat(
            nint ownerAddress,
            ushort ownerObjectIndex,
            uint id,
            ref byte hidden)
        {
            if (!MatchesOwner(ownerAddress, ownerObjectIndex)
                || Appearance.HatVisible is not { } visible)
                return false;
            hidden = visible ? (byte)0 : (byte)1;
            hatObserved = true;
            return true;
        }

        internal bool TrySubstituteVisor(
            nint ownerAddress,
            ushort ownerObjectIndex,
            ref byte toggled)
        {
            if (!MatchesOwner(ownerAddress, ownerObjectIndex)
                || Appearance.VisorToggled is not { } visor)
                return false;
            toggled = visor ? (byte)1 : (byte)0;
            visorObserved = true;
            return true;
        }

        private bool MatchesOwner(nint ownerAddress, ushort ownerObjectIndex)
            => ownerAddress == TargetAddress
                && ownerObjectIndex == RuntimeObjectIndex;
    }
}
