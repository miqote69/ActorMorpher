using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

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

    public bool TryApply(ActorSnapshot actor, OutfitData outfit)
    {
        if (!TryResolveHuman(actor, out var character)
            || outfit.Equipment.Length != character->DrawData.EquipmentModelIds.Length)
            return false;

        for (var index = 0; index < outfit.Equipment.Length; ++index)
        {
            var source = outfit.Equipment[index];
            var current = character->DrawData.EquipmentModelIds[index];
            if (current.Id == source.Set
                && current.Variant == source.Variant
                && current.Stain0 == source.Stain1
                && current.Stain1 == source.Stain2)
                continue;
            var model = new EquipmentModelId
            {
                Id = source.Set,
                Variant = source.Variant,
                Stain0 = source.Stain1,
                Stain1 = source.Stain2,
            };
            character->DrawData.LoadEquipment((DrawDataContainer.EquipmentSlot)index, &model, true);
        }
        if (outfit.Facewear.IsAvailable && character->DrawData.GlassesIds[0] != outfit.Facewear.ModelId)
            character->DrawData.SetGlasses(0, outfit.Facewear.ModelId);
        if (character->DrawData.IsHatHidden == outfit.HatVisible)
            character->DrawData.HideHeadgear(0, !outfit.HatVisible);
        if (character->DrawData.IsVisorToggled != outfit.VisorToggled)
            character->DrawData.SetVisor(outfit.VisorToggled);
        return true;
    }

    public bool IsApplied(ActorSnapshot actor, OutfitData outfit)
    {
        if (!TryResolveHuman(actor, out var character)
            || outfit.Equipment.Length != character->DrawData.EquipmentModelIds.Length)
            return false;
        for (var index = 0; index < outfit.Equipment.Length; ++index)
        {
            var expected = outfit.Equipment[index];
            var actual = character->DrawData.EquipmentModelIds[index];
            if (actual.Id != expected.Set
                || actual.Variant != expected.Variant
                || actual.Stain0 != expected.Stain1
                || actual.Stain1 != expected.Stain2)
                return false;
        }
        return (!outfit.Facewear.IsAvailable || character->DrawData.GlassesIds[0] == outfit.Facewear.ModelId)
            && character->DrawData.IsHatHidden == !outfit.HatVisible
            && character->DrawData.IsVisorToggled == outfit.VisorToggled;
    }

    private bool TryResolveHuman(ActorSnapshot expected, out Character* character)
    {
        var key = expected.RepresentationKey;
        var current = objectTable.FirstOrDefault(obj => obj is not null && obj.ObjectIndex == key.ObjectIndex);
        if (current is null
            || current.Address == nint.Zero
            || current.GameObjectId != key.GameObjectId
            || current.EntityId != key.EntityId)
        {
            character = null;
            return false;
        }
        character = (Character*)current.Address;
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
}
