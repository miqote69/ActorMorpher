using ActorMorpher.Appearance;
using ActorMorpher.Localization;
using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace ActorMorpher.Customization;

internal static class CustomizeEditor
{
    private static readonly Vector4 Selected = new(0.54f, 0.23f, 0.26f, 0.88f);
    private static readonly Vector4 SelectedBorder = new(0.91f, 0.43f, 0.46f, 1f);
    private static readonly TextKey[] Categories =
        [TextKey.CustomizeBasic, TextKey.CustomizeFaceTab, TextKey.CustomizeHairTab, TextKey.CustomizeColors];

    internal static void DrawCategories(ref int category, Func<TextKey, string> text)
        => DrawTabs("customize-category", ref category, Categories.Select(text).ToArray(), true);

    internal static void DrawTabs(string id, ref int category, IReadOnlyList<string> labels, bool fillWidth)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.26f, 0.27f, 0.28f, 0.95f));
        var gap = ImGui.GetStyle().ItemSpacing.X;
        var width = (ImGui.GetContentRegionAvail().X - gap * (labels.Count - 1)) / labels.Count;
        for (var i = 0; i < labels.Count; i++)
        {
            if (i != 0) ImGui.SameLine();
            var selected = i == category;
            ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
            ImGui.PushStyleColor(ImGuiCol.Text, selected ? SelectedBorder : ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
            if (ImGui.Button($"{labels[i]}##{id}-{i}", new Vector2(fillWidth ? width : ImGui.CalcTextSize(labels[i]).X + 32, 32))) category = i;
            ImGui.PopStyleColor(2);
            if (selected)
                ImGui.GetWindowDrawList().AddLine(ImGui.GetItemRectMin() + new Vector2(1, 31),
                    ImGui.GetItemRectMax() - new Vector2(1, 1), ImGui.GetColorU32(SelectedBorder), 2);
        }
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);
        ImGui.Separator();
    }

    internal static void DrawFields(CustomizeEditState draft, CustomizeCatalog catalog, int category,
        Func<TextKey, string> text, Func<uint, ImTextureID?> texture, bool freeColors = false)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.14f, 0.15f, 0.16f, 1));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.20f, 0.21f, 0.22f, 1));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.24f, 0.25f, 0.26f, 1));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.17f, 0.18f, 0.19f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.23f, 0.24f, 0.25f, 1));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.28f, 0.29f, 0.30f, 1));
        switch (category)
        {
            case 0:
                Origin(draft, catalog, text);
                Gender(draft, catalog, text);
                Named(draft, 2, TextKey.BodyType,
                    catalog.GetBodyTypes(draft.ToCustomize().AsSpan()).Select(value => new CustomizeNamedValue(value,
                        text(value switch { 1 => TextKey.Adult, 3 => TextKey.Old, 4 => TextKey.YoungNpc, _ => TextKey.Unknown }))).ToArray(), text);
                Slider(draft, catalog, 3, TextKey.CustomHeight, text);
                Slider(draft, catalog, 21, TextKey.CustomSize, text);
                Slider(draft, catalog, 23, TextKey.CustomBust, text);
                Choices(draft, catalog, 22, TextKey.CustomExtraShape, text, texture);
                break;
            case 1:
                Choices(draft, catalog, 5, TextKey.CustomFace, text, texture);
                Section(text(TextKey.CustomizeEyeArea));
                if (ImGui.BeginTable("##customize-eye-layout", 2, ImGuiTableFlags.SizingStretchSame))
                {
                    ImGui.TableNextColumn();
                    Types(draft, catalog, 14, TextKey.CustomEyebrows, text, aligned: true);
                    ImGui.TableNextColumn();
                    Types(draft, catalog, 16, TextKey.CustomEyeShape, text, 0x7F, true);
                    ImGui.SetCursorPosX(ImGui.GetCursorPosX() + LabelWidth());
                    Toggle(draft, 16, 0x80, TextKey.CustomSmallIris, text);
                    ImGui.EndTable();
                }
                Section(text(TextKey.CustomizeContourMouth));
                if (ImGui.BeginTable("##customize-mouth-layout", 2, ImGuiTableFlags.SizingStretchSame))
                {
                    ImGui.TableNextColumn();
                    Types(draft, catalog, 17, TextKey.CustomNose, text, aligned: true);
                    Types(draft, catalog, 18, TextKey.CustomJaw, text, aligned: true);
                    Types(draft, catalog, 19, TextKey.CustomMouth, text, 0x7F, true);
                    ImGui.EndTable();
                }
                break;
            case 2:
                Choices(draft, catalog, 6, TextKey.CustomHair, text, texture);
                Colors(draft, catalog, 10, TextKey.CustomHairColor, text, freeColors);
                Toggle(draft, 7, 0x80, TextKey.CustomHighlights, text);
                Colors(draft, catalog, 11, TextKey.CustomHighlightColor, text, freeColors);
                break;
            case 3:
                Colors(draft, catalog, 8, TextKey.CustomSkin, text, freeColors);
                Colors(draft, catalog, 9, TextKey.CustomRightEye, text, freeColors);
                Colors(draft, catalog, 15, TextKey.CustomLeftEye, text, freeColors);
                Flag(draft, 19, 0x80, TextKey.CustomLipstick, text);
                if (catalog.GetOptions(draft.ToCustomize().AsSpan(), 20).Count != 0)
                    Choices(draft, catalog, 20, TextKey.CustomLipColor, text, texture);
                else Colors(draft, catalog, 20, TextKey.CustomLipColor, text, freeColors);
                Features(draft, catalog, text, texture);
                Colors(draft, catalog, 13, TextKey.CustomFeatureColor, text, freeColors);
                Choices(draft, catalog, 24, TextKey.CustomFacePaint, text, texture, 0x7F);
                Flag(draft, 24, 0x80, TextKey.CustomReversed, text);
                Colors(draft, catalog, 25, TextKey.CustomFacePaintColor, text, freeColors);
                break;
        }
        ImGui.PopStyleColor(6);
        ImGui.PopStyleVar(2);
    }

    private static void Origin(CustomizeEditState draft, CustomizeCatalog catalog, Func<TextKey, string> text)
    {
        var race = draft.GetValue(0);
        var tribe = draft.GetValue(4);
        var raceName = catalog.GetRaceName(race) ?? Unknown(race, text);
        var tribeName = catalog.GetTribeName(tribe) ?? Unknown(tribe, text);
        ImGui.SetNextItemWidth(Math.Min(350, ImGui.GetContentRegionAvail().X * 0.65f));
        if (!ImGui.BeginCombo($"{text(TextKey.CustomizeOrigin)}##customize-origin", $"{raceName} / {tribeName}")) return;
        foreach (var choice in catalog.GetOrigins())
            if (ImGui.Selectable($"{choice.RaceName} / {choice.TribeName}##{choice.Race}-{choice.Tribe}",
                race == choice.Race && tribe == choice.Tribe))
            {
                // Both values belong to the one explicitly selected origin; no other values are changed.
                draft.SetValue(0, choice.Race);
                draft.SetValue(4, choice.Tribe);
            }
        ImGui.EndCombo();
    }

    private static string Unknown(byte value, Func<TextKey, string> text)
        => string.Format(text(TextKey.Unknown), value);

    private static void Gender(CustomizeEditState draft, CustomizeCatalog catalog, Func<TextKey, string> text)
    {
        var value = draft.GetValue(1);
        for (byte sex = 0; sex < 2; sex++)
        {
            if (sex != 0) ImGui.SameLine();
            var selected = value == sex;
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.17f, 0.18f, 0.19f, 0.9f));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 3);
            if (ImGui.Button($"##customize-gender-{sex}", new Vector2(42)) && value != sex)
            {
                draft.SetValue(1, sex);
                if (!catalog.HasFaceModel(draft.ToCustomize().AsSpan())) draft.SetValue(5, 1);
            }
            ImGui.PopStyleVar();
            ImGui.PopStyleColor();
            var position = ImGui.GetItemRectMin();
            var draw = ImGui.GetWindowDrawList();
            var ink = ImGui.GetColorU32(ImGuiCol.Text);
            void Line(Vector2 from, Vector2 to, uint color, float thickness)
            {
                draw.AddLine(from, to, color, thickness);
                draw.AddCircleFilled(from, thickness / 2, color, 8);
                draw.AddCircleFilled(to, thickness / 2, color, 8);
            }
            if (sex == 0)
            {
                var center = position + new Vector2(18, 24);
                var tip = position + new Vector2(32, 10);
                draw.AddCircle(center, 8, ink, 32, 2);
                Line(center + new Vector2(5.6f, -5.6f), tip, ink, 2);
                Line(tip, tip + new Vector2(-8, 0), ink, 2);
                Line(tip, tip + new Vector2(0, 8), ink, 2);
            }
            else
            {
                var center = position + new Vector2(21, 15);
                draw.AddCircle(center, 8, ink, 32, 2);
                Line(center + new Vector2(0, 8), center + new Vector2(0, 20), ink, 2);
                Line(center + new Vector2(-6, 15), center + new Vector2(6, 15), ink, 2);
            }
            draw.AddRect(position, ImGui.GetItemRectMax(),
                ImGui.GetColorU32(new Vector4(0.45f, 0.46f, 0.48f, 0.8f)),
                3, ImDrawFlags.None, 1);
            if (selected)
            {
                var check = ImGui.GetColorU32(SelectedBorder);
                Line(position + new Vector2(4, 35), position + new Vector2(7, 38), check, 2);
                Line(position + new Vector2(7, 38), position + new Vector2(12, 32), check, 2);
            }

        }
        ImGui.SameLine();
        ImGui.TextUnformatted(value <= 1 ? text(TextKey.Gender) : $"{text(TextKey.Gender)}: {Unknown(value, text)}");
    }

    private static void Named(CustomizeEditState draft, int index, TextKey label,
        IReadOnlyList<CustomizeNamedValue> choices, Func<TextKey, string> text)
    {
        var value = draft.GetValue(index);
        var name = choices.FirstOrDefault(choice => choice.Value == value).Name;
        ImGui.SetNextItemWidth(Math.Min(300, ImGui.GetContentRegionAvail().X * 0.6f));
        if (!ImGui.BeginCombo($"{text(label)}##customize-{index}", name ?? Unknown(value, text))) return;
        foreach (var choice in choices)
            if (ImGui.Selectable($"{choice.Name}##{choice.Value}", value == choice.Value)) draft.SetValue(index, choice.Value);
        ImGui.EndCombo();
    }

    private static void Unavailable(TextKey label, Func<TextKey, string> text)
    {
        ImGui.TextUnformatted(text(label));
        ImGui.TextDisabled(text(TextKey.CustomizeNoSamples));
    }

    private static void Slider(CustomizeEditState draft, CustomizeCatalog catalog, int index, TextKey label, Func<TextKey, string> text)
    {
        var choices = catalog.GetOptions(draft.ToCustomize().AsSpan(), index);
        if (choices.Count == 0) { Unavailable(label, text); return; }
        var value = (int)draft.GetValue(index);
        ImGui.SetNextItemWidth(Math.Min(350, ImGui.GetContentRegionAvail().X * 0.65f));
        if (ImGui.SliderInt($"{text(label)}##customize-{index}", ref value, choices.Min(c => c.Value), choices.Max(c => c.Value),
            "%d", ImGuiSliderFlags.NoInput))
            draft.SetValue(index, (byte)value);
    }

    private static void Flag(CustomizeEditState draft, int index, byte mask, TextKey label, Func<TextKey, string> text)
    {
        var enabled = draft.GetValue(index, mask) != 0;
        if (ImGui.Checkbox($"{text(label)}##customize-flag-{index}", ref enabled)) draft.SetValue(index, enabled ? mask : (byte)0, mask);
    }

    private static void Toggle(CustomizeEditState draft, int index, byte mask, TextKey label, Func<TextKey, string> text)
    {
        var enabled = draft.GetValue(index, mask) != 0;
        var height = ImGui.GetFrameHeight();
        var size = new Vector2(height * 1.8f, height);
        var pos = ImGui.GetCursorScreenPos();
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, height / 2);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0);
        ImGui.PushStyleColor(ImGuiCol.Button, enabled ? Selected : new Vector4(0.17f, 0.18f, 0.19f, 1));
        if (ImGui.Button($"##customize-toggle-{index}", size))
        {
            enabled = !enabled;
            draft.SetValue(index, enabled ? mask : (byte)0, mask);
        }
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRect(pos, pos + size, ImGui.GetColorU32(enabled ? SelectedBorder : new Vector4(0.36f, 0.38f, 0.4f, 1)), height / 2);
        draw.AddCircleFilled(pos + new Vector2(enabled ? size.X - height / 2 : height / 2, height / 2),
            height / 2 - 3, ImGui.GetColorU32(ImGuiCol.Text), 32);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X);
        ImGui.TextUnformatted(text(label));
        ImGui.PopTextWrapPos();
    }

    private static void Types(CustomizeEditState draft, CustomizeCatalog catalog, int index,
        TextKey label, Func<TextKey, string> text, byte mask = 255, bool aligned = false)
    {
        var options = catalog.GetOptions(draft.ToCustomize().AsSpan(), index);
        if (options.Count == 0) { Unavailable(label, text); return; }
        var value = draft.GetValue(index, mask);
        var selectedName = options.FirstOrDefault(option => option.Value == value).Label;
        if (aligned)
        {
            var left = ImGui.GetCursorPosX();
            var labelWidth = LabelWidth();
            ImGui.AlignTextToFramePadding();
            ImGui.TextUnformatted(text(label));
            ImGui.SameLine();
            ImGui.SetCursorPosX(left + labelWidth);
            ImGui.SetNextItemWidth(-1);
        }
        else ImGui.SetNextItemWidth(Math.Min(300, ImGui.GetContentRegionAvail().X * 0.6f));
        if (!ImGui.BeginCombo($"{(aligned ? "" : text(label))}##customize-{index}", selectedName ?? Unknown(value, text))) return;
        foreach (var option in options)
            if (ImGui.Selectable($"{option.Label ?? Unknown(option.Value, text)}##{option.Value}", option.Value == value))
                draft.SetValue(index, option.Value, mask);
        ImGui.EndCombo();
    }

    private static void Heading(TextKey label, byte value, Func<TextKey, string> text)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted(text(label));
        var id = $"{text(TextKey.CustomizeSelectedNumber)}: {value}";
        var right = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(id).X;
        if (right > ImGui.GetItemRectSize().X + 20) { ImGui.SameLine(right); ImGui.TextDisabled(id); }
    }

    private static float LabelWidth() => Math.Min(90, ImGui.GetContentRegionAvail().X * 0.25f);

    private static void Section(string label)
    {
        ImGui.Spacing();
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = ImGui.GetTextLineHeight();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(pos, pos + new Vector2(3, height), ImGui.GetColorU32(SelectedBorder));
        draw.AddText(pos + new Vector2(12, 0), ImGui.GetColorU32(ImGuiCol.Text), label);
        var lineStart = 24 + ImGui.CalcTextSize(label).X;
        if (lineStart < width) draw.AddLine(pos + new Vector2(lineStart, height / 2),
            pos + new Vector2(width, height / 2), ImGui.GetColorU32(ImGuiCol.Separator));
        ImGui.Dummy(new Vector2(width, height + 12));
    }

    private static void Choices(CustomizeEditState draft, CustomizeCatalog catalog, int index, TextKey label,
        Func<TextKey, string> text, Func<uint, ImTextureID?> texture, byte mask = 255)
    {
        var options = catalog.GetOptions(draft.ToCustomize().AsSpan(), index);
        if (options.Count != 0 && options.All(option => option.IconId == 0))
        {
            Types(draft, catalog, index, label, text, mask);
            return;
        }
        Section(text(label));
        if (options.Count == 0)
        {
            ImGui.TextDisabled(text(TextKey.CustomizeNoSamples));
        }
        else
        {
            var columns = Math.Max(1, Math.Min(4, (int)(ImGui.GetContentRegionAvail().X / 95)));
            var gap = ImGui.GetStyle().ItemSpacing.X;
            var width = (ImGui.GetContentRegionAvail().X - gap * (columns - 1)) / columns;
            var height = Math.Clamp(width * 0.85f, 70, 200) + ImGui.GetTextLineHeight() + 12;
            var rowHeight = height + ImGui.GetStyle().ItemSpacing.Y;
            var rows = (options.Count + columns - 1) / columns;
            var clipper = ImGui.ImGuiListClipper();
            try
            {
                clipper.Begin(rows, rowHeight);
                while (clipper.Step())
                    for (var row = clipper.DisplayStart; row < clipper.DisplayEnd; ++row)
                        for (var column = 0; column < columns && row * columns + column < options.Count; ++column)
                        {
                            if (column != 0) ImGui.SameLine();
                            var choice = options[row * columns + column];
                            var selected = index == 5
                                ? CustomizeCatalog.IsFaceSelected(draft.ToCustomize().AsSpan(), choice.Value)
                                : draft.GetValue(index, mask) == choice.Value;
                            if (Card($"{index}-{choice.Value}-{choice.IconId}", choice, selected,
                                new Vector2(width, height), texture,
                                choice.Label is { } name && !byte.TryParse(name, out _)
                                    ? name : string.Format(text(TextKey.CustomizeTypeLabel), choice.Value))) draft.SetValue(index, choice.Value, mask);
                        }
            }
            finally { clipper.Destroy(); }
        }
        ImGui.Spacing();
    }

    private static bool Card(string id, CustomizeOption choice, bool selected, Vector2 size, Func<uint, ImTextureID?> texture, string? label = null)
    {
        var position = ImGui.GetCursorScreenPos();
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0);
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1, 1, 1, 0.04f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(1, 1, 1, 0.08f));
        var clicked = ImGui.Button($"##customize-card-{id}", size);
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar();
        var draw = ImGui.GetWindowDrawList();
        if (choice.IconId != 0 && texture(choice.IconId) is { } icon)
        {
            var side = Math.Min(size.X, size.Y - ImGui.GetTextLineHeight() - 12);
            var top = position + new Vector2((size.X - side) / 2, 0);
            draw.AddImage(icon, top, top + new Vector2(side));
        }
        label ??= choice.Value.ToString();
        var textSize = ImGui.CalcTextSize(label);
        var textPos = position + new Vector2(Math.Max(0, (size.X - textSize.X - (selected ? 18 : 0)) / 2), size.Y - ImGui.GetTextLineHeight() - 3);
        var ink = ImGui.GetColorU32(selected ? SelectedBorder : ImGui.GetStyle().Colors[(int)ImGuiCol.Text]);
        draw.AddText(textPos, ink, label);
        if (selected)
        {
            draw.AddLine(position + new Vector2(0, size.Y - ImGui.GetTextLineHeight() - 8),
                position + new Vector2(size.X, size.Y - ImGui.GetTextLineHeight() - 8), ink, 2);
            var check = textPos + new Vector2(textSize.X + 6, ImGui.GetTextLineHeight() * 0.5f);
            draw.AddLine(check, check + new Vector2(4, 4), ink, 2);
            draw.AddLine(check + new Vector2(4, 4), check + new Vector2(11, -5), ink, 2);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(choice.Value.ToString());
        return clicked;
    }

    private static void Colors(CustomizeEditState draft, CustomizeCatalog catalog, int index, TextKey label,
        Func<TextKey, string> text, bool freeColors)
    {
        if (freeColors)
        {
            ImGui.TextUnformatted(text(label));
            if (draft.GetColor(index) is { } rgb)
            {
                ImGui.SetNextItemWidth(Math.Min(250, ImGui.GetContentRegionAvail().X));
                if (ImGui.ColorPicker3($"##customize-free-color-{index}", ref rgb,
                    ImGuiColorEditFlags.PickerHueBar | ImGuiColorEditFlags.NoInputs
                    | ImGuiColorEditFlags.NoSidePreview | ImGuiColorEditFlags.NoSmallPreview))
                    draft.SetColor(index, rgb);
            }
            else ImGui.TextDisabled(text(TextKey.CustomizeColorUnavailable));
            ImGui.Separator();
            return;
        }
        var colors = catalog.GetColors(draft.ToCustomize().AsSpan(), index);
        Heading(label, draft.GetValue(index), text);
        if (colors.Count == 0)
        {
            ImGui.TextDisabled(text(TextKey.CustomizeNoSamples));
        }
        else
        {
            var columns = Math.Max(1, Math.Min(16, (int)(ImGui.GetContentRegionAvail().X / 26)));
            var gap = 4f;
            var side = (ImGui.GetContentRegionAvail().X - gap * (columns - 1)) / columns;
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(gap));
            var clipper = ImGui.ImGuiListClipper();
            try
            {
                clipper.Begin((colors.Count + columns - 1) / columns, side + gap);
                while (clipper.Step())
                    for (var row = clipper.DisplayStart; row < clipper.DisplayEnd; ++row)
                        for (var column = 0; column < columns && row * columns + column < colors.Count; ++column)
                        {
                            if (column != 0) ImGui.SameLine();
                            var choice = colors[row * columns + column];
                            var selected = !draft.HasColorOverride(index) && draft.GetValue(index) == choice.Value;
                            if (ImGui.ColorButton($"##customize-color-{index}-{choice.Value}", choice.Color,
                                ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoDragDrop, new Vector2(side))) draft.SetValue(index, choice.Value);
                            if (selected) ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin() - new Vector2(1),
                                ImGui.GetItemRectMax() + new Vector2(1), ImGui.GetColorU32(SelectedBorder), 3, ImDrawFlags.None, 2);
                            if (ImGui.IsItemHovered()) ImGui.SetTooltip(choice.Value.ToString());
                        }
            }
            finally { clipper.Destroy(); }
            ImGui.PopStyleVar();
        }
        ImGui.Separator();
    }

    private static void Features(CustomizeEditState draft, CustomizeCatalog catalog, Func<TextKey, string> text, Func<uint, ImTextureID?> texture)
    {
        var choices = catalog.GetOptions(draft.ToCustomize().AsSpan(), 12);
        Heading(TextKey.CustomFeatures, draft.GetValue(12), text);
        var columns = Math.Max(1, Math.Min(7, (int)(ImGui.GetContentRegionAvail().X / (62 + ImGui.GetStyle().ItemSpacing.X))));
        for (var i = 0; i < choices.Count; i++)
        {
            if (i % columns != 0) ImGui.SameLine();
            var choice = choices[i];
            var selected = draft.GetValue(12, choice.Value) != 0;
            if (Card($"feature-{choice.Value}", choice, selected, new Vector2(62, 62 + ImGui.GetTextLineHeight() + 12), texture))
                draft.SetValue(12, selected ? (byte)0 : choice.Value, choice.Value);
        }
        if (choices.Count == 0) ImGui.TextDisabled(text(TextKey.CustomizeNoSamples));
        ImGui.Separator();
    }
}
