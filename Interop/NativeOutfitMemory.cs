using System.Collections.Immutable;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace ActorMorpher.Interop;

public sealed unsafe class NativeOutfitMemory : IOutfitMemory
{
    // TimelineContainer's existing weapon-motion update, also called by the game's equipment-change path.
    private const string UpdateWeaponAnimationSignature = "40 53 48 83 EC 20 48 8B D9 48 8B 49 08 48 8B 01 FF 90 C0 00 00 00 48 85 C0 0F 84 ?? ?? ?? ?? 48 8B 4B 08 80 8B 4E 03 00 00 10";
    private readonly delegate* unmanaged<TimelineContainer*, void> updateWeaponAnimation;
    private readonly IObjectTable objectTable;
    private readonly IHumanModelClassifier humanModelClassifier;
    private readonly IDiagnosticLog diagnostics;
    internal Func<ushort, byte, FacewearAppearance>? ResolveFacewear { get; set; }
    internal Func<LogicalActorKey, OutfitData?>? GetColorOutfit { get; set; }
    internal Action<LogicalActorKey, OutfitData>? SetColorOutfit { get; set; }

    public NativeOutfitMemory(
        IObjectTable objectTable,
        IHumanModelClassifier humanModelClassifier,
        IDiagnosticLog diagnostics,
        ISigScanner sigScanner)
    {
        this.objectTable = objectTable;
        this.humanModelClassifier = humanModelClassifier;
        this.diagnostics = diagnostics;
        updateWeaponAnimation = (delegate* unmanaged<TimelineContainer*, void>)sigScanner.ScanText(UpdateWeaponAnimationSignature);
    }

    public bool TryCapture(ActorSnapshot actor, out OutfitData outfit)
    {
        if (!TryResolveHuman(actor, out var character))
        {
            outfit = null!;
            return false;
        }
        var equipment = character->DrawData.EquipmentModelIds
            .ToArray()
            .Select(static item => new ArmorAppearance(item.Id, item.Variant, item.Stain0, item.Stain1));
        outfit = OutfitData.Create(
            equipment,
            new FacewearAppearance(true, character->DrawData.GlassesIds[0]),
            !character->DrawData.IsHatHidden,
            character->DrawData.IsVisorToggled);
        return true;
    }

    public bool TryCaptureRendered(ActorSnapshot actor, out OutfitData outfit)
    {
        if (!TryResolve(actor, out var character))
        {
            outfit = null!;
            return false;
        }

        var characterBase = ((GameObject*)character)->GetCharacterBase();
        if (characterBase is null || characterBase->GetModelType() != CharacterBase.ModelType.Human)
        {
            outfit = null!;
            return false;
        }

        var human = (Human*)characterBase;
        outfit = CaptureRendered(character, human, ResolveFacewear);
        if (GetColorOutfit?.Invoke(actor.LogicalKey) is { } colors)
            outfit = WithColors(outfit, colors);
        return true;
    }

    internal static OutfitData WithColors(OutfitData current, OutfitData colors)
        => current with { Equipment = current.Equipment.Select((armor, slot) =>
            slot < colors.Equipment.Length
                && armor.Set == colors.Equipment[slot].Set && armor.Variant == colors.Equipment[slot].Variant
                ? armor with { Color1 = colors.Equipment[slot].Color1, Color2 = colors.Equipment[slot].Color2 }
                : armor).ToImmutableArray() };

    internal static OutfitData CaptureRendered(Character* character, Human* human,
        Func<ushort, byte, FacewearAppearance>? resolveFacewear = null)
    {
        var equipment = human->EquipmentModels
            .ToArray()
            .Select(static item => new ArmorAppearance(item.Id, item.Variant, item.Stain0, item.Stain1));
        return OutfitData.Create(
            equipment,
            human->Glasses0.Id == 0 ? new FacewearAppearance(true, 0)
                : resolveFacewear?.Invoke(human->Glasses0.Id, human->Glasses0.Variant)
                    ?? FacewearAppearance.Unavailable,
            !character->DrawData.IsHatHidden,
            ((CharacterBase*)human)->VisorToggled);
    }

    internal bool TryApplyWeapon(ActorSnapshot actor, bool offhand, ulong weapon)
    {
        if (!TryResolve(actor, out var character))
            return false;
        // Load just this weapon's draw object. skipGameObject=1 preserves the original game equipment.
        character->DrawData.LoadWeapon(offhand ? DrawDataContainer.WeaponSlot.OffHand : DrawDataContainer.WeaponSlot.MainHand,
            new WeaponModelId { Value = weapon }, 1, 0, 1, 0, false);
        // Refresh resident weapon motions from both rendered hands without recreating the body or restarting its timeline.
        updateWeaponAnimation(&character->Timeline);
        return true;
    }

    public bool TryApply(ActorSnapshot actor, OutfitData outfit)
    {
        if (!TryResolveHuman(actor, out var character)
            || outfit.Equipment.Length != character->DrawData.EquipmentModelIds.Length)
            return false;

        var characterBase = ((GameObject*)character)->GetCharacterBase();
        if (characterBase is null || characterBase->GetModelType() != CharacterBase.ModelType.Human)
            return false;
        var human = (Human*)characterBase;

        ObserveAnimation(diagnostics, characterBase, "BeforeOutfitQueue");
        var previousColors = GetColorOutfit?.Invoke(actor.LogicalKey);
        SetColorOutfit?.Invoke(actor.LogicalKey, outfit);
        ApplyRenderedEquipment(human, outfit);
        ObserveAnimation(diagnostics, characterBase, "AfterOutfitQueue");
        for (var slot = 0; slot < outfit.Equipment.Length; ++slot)
            NativeEquipmentColors.ApplySlot(characterBase, slot, outfit.Equipment[slot],
                previousColors is not null && slot < previousColors.Equipment.Length
                && (previousColors.Equipment[slot].Color1 is not null || previousColors.Equipment[slot].Color2 is not null));
        if (outfit.Facewear.IsAvailable && character->DrawData.GlassesIds[0] != outfit.Facewear.ModelId)
            character->DrawData.SetGlasses(0, outfit.Facewear.ModelId);
        if (character->DrawData.IsHatHidden == outfit.HatVisible)
            character->DrawData.HideHeadgear(0, !outfit.HatVisible);
        if (character->DrawData.IsVisorToggled != outfit.VisorToggled)
            character->DrawData.SetVisor(outfit.VisorToggled);
        ObserveAnimation(diagnostics, characterBase, "AfterOutfitMetadata");
        return true;
    }

    // Observation only: neither availability nor logging failure affects the outfit operation.
    internal static void ObserveAnimation(IDiagnosticLog diagnostics, CharacterBase* model,
        string phase, uint? slot = null)
    {
        try
        {
            if (diagnostics.Mode != FileDiagnosticMode.Full || model is null)
                return;
            var skeleton = model->Skeleton;
            var partial = skeleton is not null && skeleton->PartialSkeletonCount > 0
                ? skeleton->PartialSkeletons : null;
            var animation0 = partial is not null ? partial->GetHavokAnimatedSkeleton(0) : null;
            var animation1 = partial is not null ? partial->GetHavokAnimatedSkeleton(1) : null;
            var properties = new Dictionary<string, object?>
                {
                    ["slot"] = slot,
                    ["characterBaseId"] = AnimationIdentity(diagnostics.SessionId, (nint)model),
                    ["skeletonId"] = AnimationIdentity(diagnostics.SessionId, (nint)skeleton),
                    ["stateFlags"] = $"0x{(ulong)model->StateFlags:X}",
                    ["animationVariant"] = model->AnimationVariant,
                    ["partialSkeletonCount"] = skeleton is not null ? (int?)skeleton->PartialSkeletonCount : null,
                    ["baseAnimationId0"] = AnimationIdentity(diagnostics.SessionId, (nint)animation0),
                    ["baseAnimationId1"] = AnimationIdentity(diagnostics.SessionId, (nint)animation1),
                    ["baseControlCount0"] = animation0 is not null ? (int?)animation0->AnimationControls.Length : null,
                    ["baseControlCount1"] = animation1 is not null ? (int?)animation1->AnimationControls.Length : null,
                    ["renderModelCallbackId"] = AnimationIdentity(diagnostics.SessionId, (nint)(&model->RenderModelCallback)),
                };
            ObserveRenderSlots(properties, diagnostics.SessionId, model, slot);
            diagnostics.Write(new DiagnosticLogEntry
            {
                EventId = DiagnosticEventIds.OutfitAnimationObserved,
                Category = DiagnosticCategory.BulkOutfit,
                Message = "Equipment-path animation state observed.",
                Phase = phase,
                Properties = properties,
            });
        }
        catch (Exception)
        {
            // Diagnostic failures must not interrupt native setup or outfit application.
        }
    }

    private static void ObserveRenderSlots(Dictionary<string, object?> properties,
        string session, CharacterBase* model, uint? requestedSlot)
    {
        if (model->Models is null)
            return;
        // Queue observations cover the ten outfit slots; setup observations cover its one slot.
        var first = requestedSlot is { } selected ? (long)selected : 0;
        var end = requestedSlot is not null ? Math.Min(first + 1, model->SlotCount) : Math.Min(10, model->SlotCount);
        for (var index = first; index < end; ++index)
        {
            var render = model->Models[index];
            var prefix = $"renderSlot{index}.";
            properties[prefix + "index"] = index;
            properties[prefix + "modelId"] = AnimationIdentity(session, (nint)render);
            properties[prefix + "skeletonId"] = render is not null ? AnimationIdentity(session, (nint)render->Skeleton) : null;
            properties[prefix + "boneListId"] = render is not null ? AnimationIdentity(session, (nint)render->BoneList) : null;
            properties[prefix + "boneCount"] = render is not null ? (int?)render->BoneCount : null;
            properties[prefix + "callbackId"] = render is not null ? AnimationIdentity(session, (nint)render->RenderModelCallback) : null;
        }
    }

    // Correlate stages within this session without putting raw native addresses in logs.
    private static string? AnimationIdentity(string session, nint address)
        => address == 0 ? null : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"{session}:{address:X}")).AsSpan(0, 8));

    internal static void ApplyRenderedEquipment(Human* human, OutfitData outfit)
    {
        for (var index = 0; index < outfit.Equipment.Length; ++index)
        {
            var source = outfit.Equipment[index];
            var model = new EquipmentModelId
            {
                Id = source.Set,
                Variant = source.Variant,
                Stain0 = source.Stain1,
                Stain1 = source.Stain2,
            };
            if (human->EquipmentModels[index].Value != model.Value)
            {
                var requested = model.Value;
                ((CharacterBase*)human)->SetEquipmentSlotModel((uint)index, &model);
                if (index == 0)
                {
                    // A nested setter can reinterpret an empty Head as a hat-visibility
                    // update. Keep this operation's head in the existing pending slot.
                    ((EquipmentModelId*)human->ChangedEquipData)->Value = requested;
                    human->SlotNeedsUpdateBitfield |= 1u;
                }
            }
        }
    }

    private bool TryResolveHuman(ActorSnapshot expected, out Character* character)
    {
        if (!TryResolve(expected, out character))
            return false;
        if (humanModelClassifier.IsHuman(checked((uint)character->ModelContainer.ModelCharaId)))
            return true;
        diagnostics.Write(new DiagnosticLogEntry
        {
            Level = DiagnosticLogLevel.Warning,
            EventId = DiagnosticEventIds.OutfitSkipped,
            Category = DiagnosticCategory.BulkOutfit,
            Message = "Outfit write skipped because the current representation is non-Human.",
            ActorKey = DiagnosticActorKeys.Format(diagnostics, expected.LogicalKey),
        });
        character = null;
        return false;
    }

    private bool TryResolve(ActorSnapshot expected, out Character* character)
    {
        var key = expected.RepresentationKey;
        var current = objectTable[key.ObjectIndex];
        if (current is null
            || current.Address == nint.Zero
            || current.GameObjectId != key.GameObjectId
            || current.EntityId != key.EntityId)
        {
            character = null;
            return false;
        }

        character = (Character*)current.Address;
        return true;
    }
}
