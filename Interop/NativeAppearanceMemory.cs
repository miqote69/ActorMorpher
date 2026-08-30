using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;

namespace ActorMorpher.Interop;

public sealed unsafe class NativeAppearanceMemory : IAppearanceMemory, IAppearanceBackingStore, IAppearanceFinalizer
{
    private const int CharacterBaseGlobalScaleOffset = 0x2A0;

    private readonly IObjectTable objectTable;
    private readonly IHumanModelClassifier humanModelClassifier;
    private readonly IDiagnosticLog diagnostics;
    private readonly IPluginLog pluginLog;

    public NativeAppearanceMemory(
        IObjectTable objectTable,
        IHumanModelClassifier humanModelClassifier,
        IDiagnosticLog diagnostics,
        IPluginLog pluginLog)
    {
        this.objectTable = objectTable;
        this.humanModelClassifier = humanModelClassifier;
        this.diagnostics = diagnostics;
        this.pluginLog = pluginLog;
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
            equipment,
            NativeModelScale.Capture((GameObject*)character));
        WriteScaleSnapshot(actor, character, appearance, "BeforeOperation");
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
        var needsHumanEquipment = appearance.Category == ModelCategory.Human
            && !appearance.Equipment.IsDefaultOrEmpty;
        if (!needsHumanEquipment && appearance.ModelScale is null)
            return true;
        if (!TryResolve(actor, out var character))
            return false;

        if (needsHumanEquipment)
        {
            if (!humanModelClassifier.IsHuman(checked((uint)character->ModelContainer.ModelCharaId))
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
        }

        var characterBase = ((GameObject*)character)->GetCharacterBase();
        if (!NativeModelScale.TryWrite(characterBase, appearance.ModelScale))
            return false;
        WriteScaleSnapshot(actor, character, appearance, "AfterRedraw");
        return true;
    }

    public bool IsApplied(ActorSnapshot actor, AppearanceData appearance)
    {
        if (!TryResolve(actor, out var character)
            || character->ModelContainer.ModelCharaId != appearance.ModelCharaId)
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

        var characterBase = ((GameObject*)character)->GetCharacterBase();
        return NativeModelScale.IsApplied(characterBase, appearance.ModelScale);
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

    private void WriteScaleSnapshot(
        ActorSnapshot actor,
        Character* character,
        AppearanceData appearance,
        string phase)
    {
        if (!actor.IsLocalPlayer)
            return;

        var gameObject = (GameObject*)character;
        var characterBase = gameObject->GetCharacterBase();
        var characterBaseGlobalScale = characterBase == null
            ? null
            : (float?)*(float*)((byte*)characterBase + CharacterBaseGlobalScaleOffset);
        var characterBaseModelScale = NativeModelScale.ReadOptional(characterBase);
        pluginLog.Information(
            "AM3010 RuntimeScaleCaptured Phase={Phase} ModelCharaId={ModelCharaId} SourceRowId={SourceRowId} GameObjectScale={GameObjectScale} GameObjectHeight={GameObjectHeight} CharacterDataModelScale={CharacterDataModelScale} CharacterBaseAvailable={CharacterBaseAvailable} CharacterBaseGlobalScale={CharacterBaseGlobalScale} CharacterBaseModelScale={CharacterBaseModelScale} DrawObjectScale=({DrawObjectScaleX},{DrawObjectScaleY},{DrawObjectScaleZ})",
            phase,
            appearance.ModelCharaId,
            appearance.SourceRowId,
            gameObject->Scale,
            gameObject->Height,
            character->ModelScale,
            characterBase != null,
            FormatOptionalScale(characterBaseGlobalScale),
            FormatOptionalScale(characterBaseModelScale),
            FormatOptionalScale(characterBase == null ? null : characterBase->Scale.X),
            FormatOptionalScale(characterBase == null ? null : characterBase->Scale.Y),
            FormatOptionalScale(characterBase == null ? null : characterBase->Scale.Z));
        diagnostics.Write(new DiagnosticLogEntry
        {
            EventId = DiagnosticEventIds.RuntimeScaleCaptured,
            Category = DiagnosticCategory.Appearance,
            Message = "Local player runtime scale captured without modification.",
            ActorKey = DiagnosticActorKeys.Format(diagnostics, actor.LogicalKey),
            Phase = phase,
            Properties = new Dictionary<string, object?>
            {
                ["modelCharaId"] = appearance.ModelCharaId,
                ["sourceRowId"] = appearance.SourceRowId,
                ["gameObjectScale"] = gameObject->Scale,
                ["gameObjectHeight"] = gameObject->Height,
                ["characterDataModelScale"] = character->ModelScale,
                ["characterBaseAvailable"] = characterBase != null,
                ["characterBaseGlobalScale"] = characterBaseGlobalScale,
                ["characterBaseModelScale"] = characterBaseModelScale,
                ["drawObjectScaleX"] = characterBase == null ? null : characterBase->Scale.X,
                ["drawObjectScaleY"] = characterBase == null ? null : characterBase->Scale.Y,
                ["drawObjectScaleZ"] = characterBase == null ? null : characterBase->Scale.Z,
            },
        });
    }

    private static string FormatOptionalScale(float? value)
        => value?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? "null";

}
