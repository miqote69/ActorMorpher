using ActorMorpher.Customization;
using ActorMorpher.Localization;
using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace ActorMorpher;

public sealed partial class MainWindow
{
    private CustomizeEditState? customizeDraft;
    private int customizeCategory;
    private int customizeColorMode;
    private string customizeStatus = string.Empty;
    private Guid? customizeOperation;
    private bool? customizeSucceeded;
    private int actorDetailTab;
    private CustomizeCopyData? customizeCopy;
    private string customizeCopySource = string.Empty;
    private ModelSearchEntry? modelCustomizeCopyTarget;
    private string modelCustomizeCopyStatus = string.Empty;

    private void DrawModelCustomizeCopyActions(ModelSearchEntry model)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 6));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.16f, 0.17f, 0.18f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.23f, 0.24f, 0.25f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.30f, 0.31f, 0.32f, 0.95f));
        ImGui.BeginDisabled(model.ModelAppearance is not { Category: ModelCategory.Human, Customize.Length: 26 });
        if (ImGui.Button($"{T(TextKey.CustomizeCopy)}###model-customize-copy")
            && CustomizeCopyData.TryCreate(model.ModelAppearance, out var data))
        {
            customizeCopy = data;
            customizeCopySource = model.Name;
            modelCustomizeCopyTarget = model;
            modelCustomizeCopyStatus = T(TextKey.CustomizeCopied, model.Name);
        }
        ImGui.EndDisabled();
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(2);
    }

    private void DrawActorDetailTabs()
    {
        TextKey[] tabs = [TextKey.CustomizeEditor, TextKey.Equipment, TextKey.CustomizeInfoTab];
        CustomizeEditor.DrawTabs("actor-detail-tab", ref actorDetailTab, tabs.Select(key => T(key)).ToArray(), false);
    }

    internal void RebaseCustomizeDraft(ActorSnapshot current, AppearanceData applied, CustomizeLiveRequest? pending)
    {
        if (customizeDraft is null || !customizeDraft.Matches(current))
            return;
        if (applied is not { Category: ModelCategory.Human, Customize.Length: 26 })
        {
            customizeDraft = null;
            return;
        }
        var draft = new CustomizeEditState(current, applied.Customize, applied.CustomizeColors);
        if (pending is not null && CustomizeCopyData.TryCreate(pending.CreateAppearance(applied), out var data))
            draft.Paste(data);
        customizeDraft = draft;
    }

    internal void ClearCustomizeDraft(LogicalActorKey actor)
    {
        if (customizeDraft?.Actor == actor)
            customizeDraft = null;
    }

    private void DrawCustomizeEditor(ActorSnapshot current)
    {
        if (customizeDraft is null || !customizeDraft.Matches(current))
        {
            customizeDraft = null;
            customizeOperation = null;
            customizeSucceeded = null;
            if (!plugin.TryBeginCustomizeEdit(current, out var draft, out _))
            {
                DrawCustomizeCopyActions(current, false);
                ImGui.TextWrapped(T(TextKey.CustomizeUnavailable));
                return;
            }
            customizeDraft = draft;
            customizeStatus = string.Empty;
        }
        if (customizeSucceeded is { } succeeded)
        {
            customizeStatus = T(succeeded ? TextKey.CustomizeApplied : TextKey.CustomizeApplyFailed);
            customizeOperation = null;
            customizeSucceeded = null;
        }
        CustomizeEditor.DrawCategories(ref customizeCategory, key => T(key));
        DrawCustomizeCopyActions(current, true);
        if (customizeCategory is 2 or 3)
        {
            ImGui.SetNextItemWidth(Math.Min(250, ImGui.GetContentRegionAvail().X * 0.6f));
            ImGui.Combo($"{T(TextKey.CustomizeColorMode)}##customize-color-mode", ref customizeColorMode,
                new[] { T(TextKey.CustomizeGamePalette), T(TextKey.FreeColor) }, 2);
            if (customizeColorMode == 1)
                customizeDraft.UpdateRenderedColors(plugin.CaptureCustomizeColors(current));
        }
        var before = customizeDraft.Revision;
        customizeDraft.BeginFieldChanges();
        var footerWidth = ImGui.GetContentRegionAvail().X;
        var hintWidth = Math.Max(1, footerWidth - 120 - ImGui.GetStyle().ItemSpacing.X);
        var footer = 18f + Math.Max(34, ImGui.CalcTextSize(T(TextKey.CustomizeRealtimeHint), false, hintWidth).Y)
            + (!string.IsNullOrEmpty(customizeStatus)
                ? ImGui.CalcTextSize(customizeStatus, false, Math.Max(1, footerWidth)).Y + ImGui.GetStyle().ItemSpacing.Y : 0);
        if (ImGui.BeginChild("##customize-scroll", new Vector2(0, -footer), false, ImGuiWindowFlags.AlwaysVerticalScrollbar))
            CustomizeEditor.DrawFields(customizeDraft, plugin.CustomizeCatalog, customizeCategory, key => T(key),
                icon => plugin.TryGetIconTexture(icon, out var wrap) ? wrap!.Handle : null, customizeColorMode == 1);
        ImGui.EndChild();
        if (customizeDraft.Revision != before)
            SubmitCustomizeEdit(current, customizeDraft.GetFieldChangeMasks(), customizeDraft.ColorChangeMask);
        ImGui.Separator();
        if (!string.IsNullOrEmpty(customizeStatus)) ImGui.TextWrapped(customizeStatus);
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushID("customize-reset");
        var reset = Dalamud.Interface.Components.ImGuiComponents.IconButtonWithText(Dalamud.Interface.FontAwesomeIcon.Undo,
            T(TextKey.CustomizeReset), new Vector2(120, 34));
        ImGui.PopID();
        if (reset)
        {
            customizeDraft.Reset();
            SubmitCustomizeEdit(current);
        }
        ImGui.PopStyleColor();
        ImGui.SameLine();
        ImGui.PushTextWrapPos();
        ImGui.TextDisabled(T(TextKey.CustomizeRealtimeHint));
        ImGui.PopTextWrapPos();
    }

    private void DrawCustomizeCopyActions(ActorSnapshot current, bool hasHumanDraft)
    {
        var human = hasHumanDraft && current.CurrentAppearance is { Category: ModelCategory.Human, Customize.Length: 26 };
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 6));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.16f, 0.17f, 0.18f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.23f, 0.24f, 0.25f, 0.95f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.30f, 0.31f, 0.32f, 0.95f));
        ImGui.BeginDisabled(!human);
        if (ImGui.Button($"{T(TextKey.CustomizeCopy)}###customize-copy"))
        {
            if (plugin.TryCopyCustomize(current, out var data, out _))
            {
                customizeCopy = data;
                customizeCopySource = current.Name;
                customizeStatus = T(TextKey.CustomizeCopied, current.Name);
            }
            else customizeStatus = T(TextKey.CustomizeCopyFailed);
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!human || customizeCopy is null);
        if (ImGui.Button($"{T(TextKey.CustomizePaste)}###customize-paste"))
        {
            var requestId = Guid.NewGuid();
            customizeOperation = requestId;
            customizeSucceeded = null;
            SetCustomizeSubmissionStatus(plugin.TryPasteCustomize(current, customizeDraft!, customizeCopy!, requestId, out _));
        }
        ImGui.EndDisabled();
        if (customizeCopy is not null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(T(TextKey.CustomizeCopySource, customizeCopySource));
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(2);
        ImGui.PushTextWrapPos();
        ImGui.TextDisabled(T(TextKey.CustomizeCopyHint));
        ImGui.PopTextWrapPos();
        ImGui.Spacing();
    }

    private void SubmitCustomizeEdit(ActorSnapshot current, System.Collections.Immutable.ImmutableArray<byte> customizeMasks = default,
        uint colorMask = uint.MaxValue)
    {
        var requestId = Guid.NewGuid();
        customizeOperation = requestId;
        customizeSucceeded = null;
        SetCustomizeSubmissionStatus(plugin.TryApplyCustomize(current, customizeDraft!, requestId, out _, customizeMasks, colorMask));
    }

    private void SetCustomizeSubmissionStatus(bool accepted)
    {
        if (accepted)
        {
            customizeStatus = T(customizeSucceeded is { } succeeded
                ? succeeded ? TextKey.CustomizeApplied : TextKey.CustomizeApplyFailed
                : TextKey.CustomizeApplying);
        }
        else
        {
            customizeSucceeded = false;
            customizeStatus = T(TextKey.CustomizeApplyFailed);
        }
    }

    private void OnCustomizeOperationCompleted(Guid operation, bool succeeded)
    {
        if (customizeOperation == operation)
            customizeSucceeded = succeeded;
    }
}
