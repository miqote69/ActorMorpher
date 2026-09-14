using ActorMorpher.Actors;
using ActorMorpher.Localization;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Objects.Enums;

namespace ActorMorpher;

public sealed partial class MainWindow
{
    private static readonly (int Index, TextKey Label)[] CustomizeFields =
    [
        (3, TextKey.CustomHeight), (5, TextKey.CustomFace), (6, TextKey.CustomHair), (7, TextKey.CustomHighlights),
        (8, TextKey.CustomSkin), (9, TextKey.CustomRightEye), (10, TextKey.CustomHairColor), (11, TextKey.CustomHighlightColor),
        (12, TextKey.CustomFeatures), (13, TextKey.CustomFeatureColor), (14, TextKey.CustomEyebrows), (15, TextKey.CustomLeftEye),
        (16, TextKey.CustomEyeShape), (17, TextKey.CustomNose), (18, TextKey.CustomJaw), (19, TextKey.CustomMouth),
        (20, TextKey.CustomLipColor), (21, TextKey.CustomSize), (22, TextKey.CustomExtraShape), (23, TextKey.CustomBust),
        (24, TextKey.CustomFacePaint), (25, TextKey.CustomFacePaintColor),
    ];

    private void DrawActorAppearanceInfo(ActorSnapshot actor)
    {
        var appearance = actor.CurrentAppearance;
        var human = appearance?.Category == ModelCategory.Human;
        ImGui.TextUnformatted(T(human ? TextKey.CharacterAppearance : TextKey.Model));
        if (!ImGui.BeginTable("##actor-appearance", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH)) return;
        ImGui.TableSetupColumn(T(TextKey.Field), ImGuiTableColumnFlags.WidthFixed, 150);
        ImGui.TableSetupColumn(T(TextKey.Value), ImGuiTableColumnFlags.WidthStretch);
        if (actor.ObjectKind == ObjectKind.BattleNpc)
            DrawDetailRow(T(TextKey.BattleNpcId), actor.BaseId.ToString());
        if (human)
        {
            DrawDetailRow(T(TextKey.Race), actor.Race is { } race
                ? race == 0 ? T(TextKey.Unknown, 0) : plugin.GetRaceName(race) : T(TextKey.Unavailable));
            DrawDetailRow(T(TextKey.Tribe), appearance!.Customize.Length > 4
                ? appearance.Customize[4] == 0 ? T(TextKey.Unknown, 0) : plugin.GetTribeName(appearance.Customize[4])
                : T(TextKey.Unavailable));
            DrawDetailRow(T(TextKey.Gender), actor.Gender is { } gender ? GetGenderName(gender) : T(TextKey.Unavailable));
        }
        DrawDetailRow(T(TextKey.BodyType), actor.BodyType?.ToString() ?? T(TextKey.Unavailable));
        DrawDetailRow(T(TextKey.ModelScale), appearance?.ModelScale?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? T(TextKey.Unavailable));
        DrawDetailRow(T(TextKey.ClassJob), plugin.GetClassJobName(actor.ClassJob));
        ImGui.EndTable();
        if (!human) return;
        ImGui.TextDisabled(T(TextKey.CustomizeValuesHint));
        if (!ImGui.BeginTable("##actor-customize-values", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH)) return;
        ImGui.TableSetupColumn("##field1", ImGuiTableColumnFlags.WidthStretch, 2);
        ImGui.TableSetupColumn("##value1", ImGuiTableColumnFlags.WidthStretch, 1);
        ImGui.TableSetupColumn("##field2", ImGuiTableColumnFlags.WidthStretch, 2);
        ImGui.TableSetupColumn("##value2", ImGuiTableColumnFlags.WidthStretch, 1);
        for (var i = 0; i < CustomizeFields.Length; ++i)
        {
            if (i % 2 == 0) ImGui.TableNextRow();
            var (index, label) = CustomizeFields[i];
            ImGui.TableSetColumnIndex(i % 2 * 2); ImGui.TextWrapped(T(label));
            ImGui.TableSetColumnIndex(i % 2 * 2 + 1);
            var value = appearance!.Customize.Length > index ? appearance.Customize[index] : (byte?)null;
            var display = value?.ToString() ?? T(TextKey.Unavailable);
            if (value is { } raw)
            {
                if (index == 7) display = T((raw & 0x80) != 0 ? TextKey.Yes : TextKey.No);
                else if (index is 16 or 19 or 24)
                {
                    var flagLabel = index == 16 ? TextKey.CustomSmallIris : index == 19 ? TextKey.CustomLipstick : TextKey.CustomReversed;
                    display = $"{raw & 0x7F} / {T(flagLabel)}: {T((raw & 0x80) != 0 ? TextKey.Yes : TextKey.No)}";
                }
            }
            ImGui.TextWrapped(display);
        }
        ImGui.EndTable();
    }
}
