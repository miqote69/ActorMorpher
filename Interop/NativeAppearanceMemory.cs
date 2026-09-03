using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace ActorMorpher.Interop;

public sealed unsafe class NativeAppearanceMemory : IAppearanceMemory
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
        var characterBase = ((GameObject*)character)->GetCharacterBase();
        if (characterBase is null && !humanModelClassifier.IsHuman(modelId))
        {
            appearance = null!;
            return false;
        }
        var category = characterBase is null
            ? ModelCategory.Human
            : characterBase->GetModelType() switch
            {
                CharacterBase.ModelType.Human => ModelCategory.Human,
                CharacterBase.ModelType.DemiHuman => ModelCategory.Demihuman,
                CharacterBase.ModelType.Monster => ModelCategory.Monster,
                _ => ModelCategory.Other,
            };
        appearance = AppearanceData.Create(
            modelId,
            category,
            0,
            AppearanceCompleteness.Complete,
            customize,
            equipment,
            NativeModelScale.Capture(character),
            category == ModelCategory.Human
                ? character->DrawData.Weapon(DrawDataContainer.WeaponSlot.MainHand).ModelId.Value
                : null,
            category == ModelCategory.Human
                ? character->DrawData.Weapon(DrawDataContainer.WeaponSlot.OffHand).ModelId.Value
                : null,
            category == ModelCategory.Human ? character->DrawData.IsVisorToggled : null);
        WriteScaleSnapshot(actor, character, appearance, "BeforeOperation");
        return true;
    }

    internal static ulong CaptureRenderedWeapon(Character* character, DrawDataContainer.WeaponSlot slot)
    {
        var rendered = character->DrawData.Weapon(slot).Weapon;
        if (rendered is null)
            return 0;
        return new WeaponModelId
        {
            Id = rendered->ModelSetId,
            Type = rendered->SecondaryId,
            Variant = rendered->Variant,
            Stain0 = rendered->Stain0,
            Stain1 = rendered->Stain1,
        }.Value;
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
        var characterBaseModelScale = NativeModelScale.ReadCharacterBaseOptional(characterBase);
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
