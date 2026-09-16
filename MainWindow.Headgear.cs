using Dalamud.Bindings.ImGui;
using ActorMorpher.Localization;

namespace ActorMorpher;

public sealed partial class MainWindow
{
    private void DrawHeadgearSettings(string id, OutfitData outfit, LogicalActorKey? actor)
    {
        ImGui.PushID($"{id}-head-settings");
        if (ImGui.SmallButton(T(TextKey.HeadgearDetails)))
            ImGui.OpenPopup("settings");
        if (ImGui.BeginPopup("settings"))
        {
            ImGui.TextUnformatted(T(TextKey.HeadgearSettings));
            ImGui.Separator();
            var visible = outfit.HatVisible;
            var visor = outfit.VisorToggled;
            DrawHeadgearToggle(TextKey.ShowHeadgear, HeadgearSetting.Visible, ref visible, actor);
            DrawHeadgearToggle(TextKey.ToggleVisor, HeadgearSetting.VisorToggled, ref visor, actor);
            var earLabel = outfit.VieraEarsHidden switch
            {
                true => T(TextKey.Hidden), false => T(TextKey.Visible), _ => T(TextKey.KeepEarVisibility),
            };
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 12);
            if (ImGui.BeginCombo(T(TextKey.ShowVieraEars), earLabel))
            {
                if (ImGui.Selectable(T(TextKey.Visible), outfit.VieraEarsHidden == false))
                    ApplyHeadgearSetting(HeadgearSetting.VieraEarsVisible, true, actor);
                if (ImGui.Selectable(T(TextKey.Hidden), outfit.VieraEarsHidden == true))
                    ApplyHeadgearSetting(HeadgearSetting.VieraEarsVisible, false, actor);
                ImGui.EndCombo();
            }
            ImGui.Spacing();
            ImGui.TextDisabled(T(TextKey.HeadgearSettingsHint));
            ImGui.EndPopup();
        }
        ImGui.PopID();
    }

    private void DrawHeadgearToggle(TextKey label, HeadgearSetting setting, ref bool value, LogicalActorKey? actor)
    {
        if (!ImGui.Checkbox(T(label), ref value)) return;
        ApplyHeadgearSetting(setting, value, actor);
    }

    private void ApplyHeadgearSetting(HeadgearSetting setting, bool value, LogicalActorKey? actor)
    {
        var succeeded = plugin.SelectHeadgearSetting(setting, value, actor, out var message);
        if (actor is not null) { applySucceeded = succeeded; applyStatus = message; }
        else bulkActionStatus = message;
    }
}
