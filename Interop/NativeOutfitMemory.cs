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
        outfit = CaptureRendered(character, human);
        return true;
    }

    internal static OutfitData CaptureRendered(Character* character, Human* human)
    {
        var equipment = human->EquipmentModels
            .ToArray()
            .Select(static item => new ArmorAppearance(item.Id, item.Variant, item.Stain0, item.Stain1));
        return OutfitData.Create(
            equipment,
            new FacewearAppearance(true, checked((ushort)human->Glasses0.Id)),
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

        for (var index = 0; index < outfit.Equipment.Length; ++index)
        {
            var source = outfit.Equipment[index];
            var current = character->DrawData.EquipmentModelIds[index];
            var model = new EquipmentModelId
            {
                Id = source.Set,
                Variant = source.Variant,
                Stain0 = source.Stain1,
                Stain1 = source.Stain2,
            };
            if (current.Value != model.Value)
                character->DrawData.LoadEquipment((DrawDataContainer.EquipmentSlot)index, &model, true);
            if (human->EquipmentModels[index].Value != model.Value)
                characterBase->SetEquipmentSlotModel((uint)index, &model);
        }
        if (outfit.Facewear.IsAvailable && character->DrawData.GlassesIds[0] != outfit.Facewear.ModelId)
            character->DrawData.SetGlasses(0, outfit.Facewear.ModelId);
        if (character->DrawData.IsHatHidden == outfit.HatVisible)
            character->DrawData.HideHeadgear(0, !outfit.HatVisible);
        if (character->DrawData.IsVisorToggled != outfit.VisorToggled)
            character->DrawData.SetVisor(outfit.VisorToggled);
        return true;
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
