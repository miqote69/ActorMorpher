using System.Numerics;
using ActorMorpher.Appearance;
using ActorMorpher.Localization;
using Dalamud.Bindings.ImGui;

namespace ActorMorpher;

public sealed partial class MainWindow
{
    private EquipmentCopyData? equipmentCopy;
    private string equipmentCopySource = string.Empty;
    private string equipmentCopyStatus = string.Empty;
    private Guid? equipmentCopyOperation;
    private (LogicalActorKey Actor, ActorRepresentationKey Representation)? equipmentCopyStatusTarget;
    private string bulkEquipmentCopyStatus = string.Empty;
    private ModelSearchEntry? modelEquipmentCopyTarget;
    private string modelEquipmentCopyStatus = string.Empty;

    private void DrawModelEquipmentCopyActions(ModelSearchEntry model)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 6));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.16f, 0.17f, 0.18f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.23f, 0.24f, 0.25f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.30f, 0.31f, 0.32f, 0.95f));
        ImGui.BeginDisabled(!EquipmentCopyData.CanCopy(model.ModelAppearance));
        if (ImGui.Button($"{T(TextKey.EquipmentCopy)}###model-equipment-copy")
            && EquipmentCopyData.TryCreate(model.ModelAppearance, out var data))
        {
            equipmentCopy = data;
            equipmentCopySource = model.Name;
            modelEquipmentCopyTarget = model;
            modelEquipmentCopyStatus = T(TextKey.EquipmentCopied, model.Name);
        }
        ImGui.EndDisabled();
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(2);
    }

    private void DrawBulkEquipmentCopyActions()
    {
        var source = plugin.SourceOutfit;
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 6));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.16f, 0.17f, 0.18f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.23f, 0.24f, 0.25f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.30f, 0.31f, 0.32f, 0.95f));
        ImGui.BeginDisabled(source is null);
        if (ImGui.Button($"{T(TextKey.BulkEquipmentCopy)}###bulk-equipment-copy"))
        {
            equipmentCopy = EquipmentCopyData.FromOutfit(source!);
            equipmentCopySource = T(TextKey.BulkSourceEquipment);
            bulkEquipmentCopyStatus = T(TextKey.BulkEquipmentCopied);
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(equipmentCopy is null);
        if (ImGui.Button($"{T(TextKey.EquipmentPaste)}###bulk-equipment-paste"))
        {
            plugin.PasteEquipmentToSource(equipmentCopy!);
            bulkEquipmentCopyStatus = T(TextKey.BulkEquipmentPasted);
        }
        ImGui.EndDisabled();
        if (equipmentCopy is not null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(T(TextKey.EquipmentCopySource, equipmentCopySource));
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(2);
        if (!string.IsNullOrEmpty(bulkEquipmentCopyStatus))
            ImGui.TextWrapped(bulkEquipmentCopyStatus);
        ImGui.Spacing();
    }

    private void DrawEquipmentCopyActions(ActorSnapshot current)
    {
        var target = (current.LogicalKey, current.RepresentationKey);
        if (equipmentCopyStatusTarget != target)
        {
            equipmentCopyStatusTarget = target;
            equipmentCopyStatus = string.Empty;
            equipmentCopyOperation = null;
        }
        var available = EquipmentCopyData.CanCopy(current.CurrentAppearance);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 6));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.16f, 0.17f, 0.18f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.23f, 0.24f, 0.25f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.30f, 0.31f, 0.32f, 0.95f));
        ImGui.BeginDisabled(!available);
        if (ImGui.Button($"{T(TextKey.EquipmentCopy)}###equipment-copy"))
        {
            equipmentCopyOperation = null;
            if (plugin.TryCopyEquipment(current, out var data, out _))
            {
                equipmentCopy = data;
                equipmentCopySource = current.Name;
                equipmentCopyStatus = T(TextKey.EquipmentCopied, current.Name);
            }
            else equipmentCopyStatus = T(TextKey.EquipmentCopyFailed);
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!available || equipmentCopy is null);
        if (ImGui.Button($"{T(TextKey.EquipmentPaste)}###equipment-paste"))
        {
            var accepted = plugin.TryPasteEquipment(current, equipmentCopy!, out var operation, out _);
            equipmentCopyOperation = accepted ? operation : null;
            equipmentCopyStatus = T(accepted ? TextKey.EquipmentPasting : TextKey.EquipmentPasteFailed);
        }
        ImGui.EndDisabled();
        if (equipmentCopy is not null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(T(TextKey.EquipmentCopySource, equipmentCopySource));
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(2);
        if (!string.IsNullOrEmpty(equipmentCopyStatus))
            ImGui.TextWrapped(equipmentCopyStatus);
        ImGui.Spacing();
    }

    private void OnEquipmentOperationCompleted(Guid operation, bool succeeded)
    {
        if (equipmentCopyOperation == operation)
        {
            equipmentCopyStatus = T(succeeded ? TextKey.EquipmentPasted : TextKey.EquipmentPasteFailed);
            equipmentCopyOperation = null;
        }
    }
}
