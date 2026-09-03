using System.Collections.Immutable;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace ActorMorpher.Interop;

public sealed unsafe class NativeOutfitMemory : IOutfitMemory
{
    private readonly IObjectTable objectTable;
    private readonly IHumanModelClassifier humanModelClassifier;
    private readonly IDiagnosticLog diagnostics;
    internal Func<ushort, byte, FacewearAppearance>? ResolveFacewear { get; set; }
    internal Func<LogicalActorKey, OutfitData?>? GetColorOutfit { get; set; }
    internal Action<LogicalActorKey, OutfitData>? SetColorOutfit { get; set; }

    public NativeOutfitMemory(
        IObjectTable objectTable,
        IHumanModelClassifier humanModelClassifier,
        IDiagnosticLog diagnostics)
    {
        this.objectTable = objectTable;
        this.humanModelClassifier = humanModelClassifier;
        this.diagnostics = diagnostics;
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

    public bool TryApply(ActorSnapshot actor, OutfitData outfit)
    {
        if (!TryResolveHuman(actor, out var character)
            || outfit.Equipment.Length != character->DrawData.EquipmentModelIds.Length)
            return false;

        var characterBase = ((GameObject*)character)->GetCharacterBase();
        if (characterBase is null || characterBase->GetModelType() != CharacterBase.ModelType.Human)
            return false;
        var human = (Human*)characterBase;

        var previousColors = GetColorOutfit?.Invoke(actor.LogicalKey);
        SetColorOutfit?.Invoke(actor.LogicalKey, outfit);
        ApplyRenderedEquipment(human, outfit);
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
        return true;
    }

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
