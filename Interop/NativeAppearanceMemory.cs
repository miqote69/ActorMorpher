using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace ActorMorpher.Interop;

public sealed unsafe class NativeAppearanceMemory : IAppearanceMemory, IAppearanceBackingStore, IAppearanceFinalizer
{
    private readonly IObjectTable objectTable;
    private readonly IHumanModelClassifier humanModelClassifier;
    private readonly IDiagnosticLog diagnostics;

    public NativeAppearanceMemory(
        IObjectTable objectTable,
        IHumanModelClassifier humanModelClassifier,
        IDiagnosticLog diagnostics)
    {
        this.objectTable = objectTable;
        this.humanModelClassifier = humanModelClassifier;
        this.diagnostics = diagnostics;
    }

    public bool TryCapture(ActorSnapshot actor, out AppearanceData appearance)
    {
        if (!TryResolve(actor, out var character))
        {
            appearance = null!;
            return false;
        }

        var customize = character->DrawData.CustomizeData.Data.ToArray();
        var equipment = character->DrawData.EquipmentModelIds
            .ToArray()
            .Select(static model => model.Value)
            .ToArray();
        var modelId = checked((uint)character->ModelContainer.ModelCharaId);
        appearance = AppearanceData.Create(
            modelId,
            humanModelClassifier.IsHuman(modelId) ? ModelCategory.Human : ModelCategory.Other,
            0,
            AppearanceCompleteness.Complete,
            customize,
            equipment);
        return true;
    }

    public bool TryWrite(ActorSnapshot actor, AppearanceData appearance)
    {
        if (!TryResolve(actor, out var character) || !ValidateShape(character, appearance))
            return false;

        character->ModelContainer.ModelCharaId = checked((int)appearance.ModelCharaId);
        if (!appearance.Customize.IsDefaultOrEmpty)
            appearance.Customize.AsSpan().CopyTo(character->DrawData.CustomizeData.Data);
        if (!appearance.Equipment.IsDefaultOrEmpty)
        {
            var target = character->DrawData.EquipmentModelIds;
            for (var index = 0; index < target.Length; ++index)
                target[index].Value = appearance.Equipment[index];
        }
        return true;
    }

    public bool TryNormalizeBacking(ActorSnapshot actor, AppearanceData appearance)
        => TryWrite(actor, appearance);

    public bool TryFinalize(ActorSnapshot actor, AppearanceData appearance)
    {
        if (appearance.Category != ModelCategory.Human || appearance.Equipment.IsDefaultOrEmpty)
            return true;
        if (!TryResolve(actor, out var character)
            || !humanModelClassifier.IsHuman(checked((uint)character->ModelContainer.ModelCharaId))
            || appearance.Equipment.Length != character->DrawData.EquipmentModelIds.Length)
            return false;

        // Rewriting DrawData alone does not guarantee that a newly-created Human draw object
        // reloads its equipment after one or more non-Human models. Force every slot through
        // the game's equipment loader before the redraw is considered complete.
        for (var index = 0; index < appearance.Equipment.Length; ++index)
        {
            var model = new EquipmentModelId { Value = appearance.Equipment[index] };
            character->DrawData.LoadEquipment((DrawDataContainer.EquipmentSlot)index, &model, true);
        }
        return true;
    }

    public bool IsApplied(ActorSnapshot actor, AppearanceData appearance)
    {
        if (!TryResolve(actor, out var character) || character->ModelContainer.ModelCharaId != appearance.ModelCharaId)
            return false;
        if (!appearance.Customize.IsDefaultOrEmpty
            && !appearance.Customize.AsSpan().SequenceEqual(character->DrawData.CustomizeData.Data))
            return false;
        if (!appearance.Equipment.IsDefaultOrEmpty)
        {
            var equipment = character->DrawData.EquipmentModelIds;
            for (var index = 0; index < equipment.Length; ++index)
                if (equipment[index].Value != appearance.Equipment[index])
                    return false;
        }
        return true;
    }

    private bool ValidateShape(Character* character, AppearanceData appearance)
    {
        var customizeLength = character->DrawData.CustomizeData.Data.Length;
        var equipmentLength = character->DrawData.EquipmentModelIds.Length;
        var customizeValid = appearance.Customize.IsDefaultOrEmpty || appearance.Customize.Length == customizeLength;
        var equipmentValid = appearance.Equipment.IsDefaultOrEmpty || appearance.Equipment.Length == equipmentLength;
        if (customizeValid && equipmentValid)
            return true;

        diagnostics.Write(new DiagnosticLogEntry
        {
            Level = DiagnosticLogLevel.Error,
            EventId = DiagnosticEventIds.ActorValidationFailed,
            Category = DiagnosticCategory.Safety,
            Message = "Appearance data shape did not match current FFXIVClientStructs.",
            Properties = new Dictionary<string, object?>
            {
                ["expectedCustomizeLength"] = customizeLength,
                ["actualCustomizeLength"] = appearance.Customize.Length,
                ["expectedEquipmentLength"] = equipmentLength,
                ["actualEquipmentLength"] = appearance.Equipment.Length,
            },
        });
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
            diagnostics.Write(new DiagnosticLogEntry
            {
                Level = DiagnosticLogLevel.Warning,
                EventId = DiagnosticEventIds.ActorIdentityMismatch,
                Category = DiagnosticCategory.ActorIdentity,
                Message = "Actor identity changed before appearance memory access.",
                ActorKey = DiagnosticActorKeys.Format(diagnostics, expected.LogicalKey),
            });
            character = null;
            return false;
        }

        character = (Character*)current.Address;
        return true;
    }
}
