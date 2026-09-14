using ActorMorpher.Localization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System.Numerics;

namespace ActorMorpher;

public sealed partial class MainWindow
{
    internal void DrawEquipmentPicker()
    {
        if (!IsOpen || !equipmentPickerOpen)
            return;
        var weaponPicker = equipmentPickerSlot is 11 or 12;
        if (weaponPicker && (equipmentPickerJob != plugin.CurrentWeaponJob || equipmentPickerLanguage != plugin.GameLanguage))
            RefreshEquipmentResults();
        if (equipmentPickerFocusRequested)
        {
            ImGui.SetNextWindowFocus();
            equipmentPickerFocusRequested = false;
            equipmentManualExpanded = false;
            RefreshEquipmentResults();
        }

        var scale = ImGui.GetFontSize() / 17f;
        var available = ImGui.GetMainViewport().WorkSize - new Vector2(24);
        ImGui.SetNextWindowSize(new Vector2(Math.Min(800 * scale, available.X), Math.Min(900 * scale, available.Y)), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(Math.Min(560 * scale, available.X), Math.Min(560 * scale, available.Y)), available);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.12f, 0.13f, 0.145f, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0.37f, 0.18f, 0.20f, 0.80f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0.43f, 0.23f, 0.25f, 0.85f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(12 * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8 * scale));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10 * scale, 6 * scale));
        try
        {
            var slotName = equipmentPickerSlot switch { 11 => T(TextKey.Mainhand), 12 => T(TextKey.Offhand),
                10 => T(TextKey.Facewear), _ => ((OutfitSlot)equipmentPickerSlot).ToString() };
            var title = $"{T(TextKey.ChooseEquipment)} · {slotName}###ActorMorpherEquipmentPicker";
            var visible = ImGui.Begin(title, ref equipmentPickerOpen, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings
                | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
            try
            {
                if (visible)
                    DrawEquipmentPickerContent(weaponPicker, scale);
            }
            finally { ImGui.End(); }
        }
        finally
        {
            ImGui.PopStyleVar(3);
            ImGui.PopStyleColor(4);
        }
    }

    private void DrawEquipmentPickerContent(bool weaponPicker, float scale)
    {
        ImGui.SetScrollY(0);
        ImGui.TextWrapped(T(equipmentPickerActor is null ? TextKey.PickerSourceHint : TextKey.PickerActorHint));
        if (weaponPicker) ImGui.TextWrapped(T(TextKey.WeaponPickerJobHint));
        var sourceImportPending = equipmentPickerActor is null && plugin.IsPlateImportPending;
        var spacing = ImGui.GetStyle().ItemSpacing;
        var buttonSize = ImGui.GetFrameHeight();
        using (plugin.PushIconFont()) ImGui.TextUnformatted(FontAwesomeIcon.Search.ToIconString());
        ImGui.SameLine();
        ImGui.SetNextItemWidth(-buttonSize - spacing.X);
        var changed = ImGui.InputTextWithHint("##equipment-search", T(weaponPicker ? TextKey.WeaponSearchHint : TextKey.EquipmentSearchHint), ref equipmentSearch, 128);
        ImGui.SameLine();
        if (ImGui.Button("##clear-search", new Vector2(buttonSize)))
        {
            equipmentSearch = string.Empty;
            changed = true;
        }
        // Draw from the actual button rectangle, independent of icon-font bearings and baseline.
        var clearCenter = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) * 0.5f;
        var radius = 4 * scale;
        var clearDraw = ImGui.GetWindowDrawList();
        clearDraw.AddLine(clearCenter - new Vector2(radius), clearCenter + new Vector2(radius), ImGui.GetColorU32(ImGuiCol.Text), 1.6f * scale);
        clearDraw.AddLine(clearCenter + new Vector2(-radius, radius), clearCenter + new Vector2(radius, -radius), ImGui.GetColorU32(ImGuiCol.Text), 1.6f * scale);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(T(TextKey.PickerClearSearch));

        var count = T(TextKey.PickerResultCount, equipmentResults.Length);
        var filterWidth = Math.Min(170 * scale, (ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(count).X - spacing.X * 4) / 3);
        if (DrawEquipmentFilter(T(TextKey.All), equipmentFilter == EquipmentPickerFilter.All, filterWidth))
        {
            equipmentFilter = EquipmentPickerFilter.All;
            changed = true;
        }
        ImGui.SameLine();
        if (DrawEquipmentFilter(T(TextKey.FashionEquipment), equipmentFilter == EquipmentPickerFilter.Fashion, filterWidth))
        {
            equipmentFilter = EquipmentPickerFilter.Fashion;
            changed = true;
        }
        ImGui.SameLine();
        if (DrawEquipmentFilter(T(TextKey.PickerFavorites), equipmentFilter == EquipmentPickerFilter.Favorites, filterWidth))
        {
            equipmentFilter = EquipmentPickerFilter.Favorites;
            changed = true;
        }
        if (changed) RefreshEquipmentResults();
        count = T(TextKey.PickerResultCount, equipmentResults.Length);
        ImGui.SameLine();
        ImGui.TextDisabled(count);
        ImGui.Separator();
        DrawPickerCurrentEquipment(weaponPicker, sourceImportPending, scale);
        ImGui.Separator();

        var manual = equipmentPickerSlot < 10;
        var help = T(equipmentPickerActor is null ? TextKey.PickerSourceActionHint : TextKey.PickerActorActionHint);
        var closeWidth = Math.Max(90 * scale, ImGui.CalcTextSize(T(TextKey.CloseEquipmentPicker)).X + 24 * scale);
        var helpWidth = ImGui.GetContentRegionAvail().X - closeWidth - spacing.X;
        var footerHeight = Math.Max(ImGui.GetFrameHeight(), ImGui.CalcTextSize(help, false, helpWidth).Y) + spacing.Y * 3 + 1;
        if (manual) footerHeight += ImGui.GetFrameHeightWithSpacing() * (equipmentManualExpanded ? 3 : 1);
        if (!string.IsNullOrEmpty(equipmentPickerStatus))
            footerHeight += ImGui.CalcTextSize(equipmentPickerStatus, false, ImGui.GetContentRegionAvail().X).Y + spacing.Y;
        var resultsHeight = Math.Max(ImGui.GetTextLineHeight() * 3, ImGui.GetContentRegionAvail().Y - footerHeight);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        if (ImGui.BeginChild("##equipment-results", new Vector2(0, resultsHeight), true))
            DrawPickerResults(sourceImportPending, scale);
        ImGui.EndChild();
        ImGui.PopStyleVar();

        if (manual)
        {
            // State is explicit so reopening the picker starts with the number controls collapsed.
            ImGui.SetNextItemOpen(equipmentManualExpanded, ImGuiCond.Always);
            var showManualControls = equipmentManualExpanded;
            equipmentManualExpanded = ImGui.CollapsingHeader($"{T(TextKey.PickerDirectNumber)}###picker-manual");
            if (showManualControls) DrawPickerManualEquipment(sourceImportPending, scale);
        }
        if (!string.IsNullOrEmpty(equipmentPickerStatus)) ImGui.TextWrapped(equipmentPickerStatus);
        ImGui.Separator();
        var footerPosition = ImGui.GetCursorPos();
        var closeX = ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - closeWidth;
        ImGui.PushTextWrapPos(closeX - spacing.X);
        ImGui.TextUnformatted(help);
        ImGui.PopTextWrapPos();
        ImGui.SetCursorPos(new Vector2(closeX, footerPosition.Y));
        if (ImGui.Button(T(TextKey.CloseEquipmentPicker), new Vector2(closeWidth, 0))) equipmentPickerOpen = false;
    }

    private static bool DrawEquipmentFilter(string label, bool selected, float width)
    {
        if (selected) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.Header]);
        var clicked = ImGui.Button(label, new Vector2(width, 0));
        if (selected) ImGui.PopStyleColor();
        return clicked;
    }

    private bool DrawPickerFavorite(EquipmentChoiceKey key, string id, Vector2 size, bool current = false)
    {
        var favorite = plugin.Configuration.FavoriteEquipment.Contains(key);
        var position = ImGui.GetCursorScreenPos();
        ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
        var clicked = ImGui.Button($"##{id}", size);
        ImGui.PopStyleColor();
        var draw = ImGui.GetWindowDrawList();
        var center = position + size / 2;
        var radius = Math.Min(size.X, size.Y) * 0.34f;
        var color = ImGui.GetColorU32(favorite ? new Vector4(1f, 0.80f, 0.34f, 1f)
            : ImGui.IsItemHovered() ? Vector4.One : new Vector4(0.64f, 0.67f, 0.71f, 1f));
        for (var point = 0; point < 10; ++point)
        {
            var angle = -MathF.PI / 2 + point * MathF.PI / 5;
            var nextAngle = angle + MathF.PI / 5;
            var a = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius * (point % 2 == 0 ? 1 : 0.45f);
            var b = center + new Vector2(MathF.Cos(nextAngle), MathF.Sin(nextAngle)) * radius * (point % 2 == 0 ? 0.45f : 1);
            if (favorite) draw.AddTriangleFilled(center, a, b, color);
            else draw.AddLine(a, b, color, Math.Max(1, radius / 8));
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(T(current
            ? favorite ? TextKey.RemoveCurrentFavorite : TextKey.FavoriteCurrentEquipment
            : favorite ? TextKey.RemoveFavorite : TextKey.AddFavorite));
        if (clicked) plugin.ToggleEquipmentFavorite(key);
        return clicked;
    }

    private void DrawPickerCurrentEquipment(bool weaponPicker, bool sourceImportPending, float scale)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(T(TextKey.PickerCurrentEquipment));
        ImGui.SameLine();
        var position = ImGui.GetCursorScreenPos();
        var height = ImGui.GetFrameHeight();
        var name = equipmentPickerCurrent.Model == 0 ? T(TextKey.NoEquipment)
            : equipmentPickerCurrentDisplay?.Name ?? T(TextKey.ManualEquipment);
        var unequipWidth = weaponPicker ? 0 : ImGui.CalcTextSize(T(TextKey.PickerUnequip)).X + 24 * scale;
        var favoriteText = T(TextKey.FavoriteCurrentEquipment);
        var showFavoriteText = ImGui.GetContentRegionAvail().X > 680 * scale;
        var favoriteWidth = height + (showFavoriteText ? ImGui.CalcTextSize(favoriteText).X + 8 * scale : 0);
        var nameWidth = Math.Max(80 * scale, ImGui.GetContentRegionAvail().X - unequipWidth - favoriteWidth - 24 * scale);
        ImGui.Dummy(new Vector2(nameWidth, height));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(name);
        var draw = ImGui.GetWindowDrawList();
        if (equipmentPickerCurrentDisplay is { IconId: not 0 } item && plugin.TryGetIconTexture(item.IconId, out var texture))
            draw.AddImage(texture!.Handle, position, position + new Vector2(height));
        draw.PushClipRect(position, position + new Vector2(nameWidth, height), true);
        draw.AddText(position + new Vector2(height + 8 * scale, 4 * scale), ImGui.GetColorU32(ImGuiCol.Text), name);
        draw.PopClipRect();
        ImGui.SameLine();
        if (DrawPickerFavorite(equipmentPickerCurrent, "current-favorite", new Vector2(height), true)) RefreshEquipmentResults();
        if (showFavoriteText) { ImGui.SameLine(); ImGui.TextUnformatted(favoriteText); }
        if (!weaponPicker)
        {
            ImGui.SameLine();
            ImGui.BeginDisabled(sourceImportPending);
            if (ImGui.Button(T(TextKey.PickerUnequip), new Vector2(unequipWidth, 0)))
                SelectPickerEquipment(new EquipmentChoiceKey(equipmentPickerSlot, 0, 0));
            ImGui.EndDisabled();
        }
    }

    private void DrawPickerResults(bool sourceImportPending, float scale)
    {
        if (equipmentResults.Length == 0) ImGui.TextWrapped(T(TextKey.EquipmentNoResults));
        var changed = false;
        var rowHeight = Math.Max(72 * scale, ImGui.GetTextLineHeight() * 2 + 16 * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.Zero);
        var clipper = ImGui.ImGuiListClipper();
        try
        {
            clipper.Begin(equipmentResults.Length, rowHeight);
            while (clipper.Step())
            for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; ++i)
            {
                var choice = equipmentResults[i];
                ImGui.PushID(i);
                var position = ImGui.GetCursorScreenPos();
                var width = ImGui.GetContentRegionAvail().X;
                var starWidth = 48 * scale;
                var selectWidth = width - starWidth;
                ImGui.BeginDisabled(sourceImportPending);
                ImGui.PushStyleColor(ImGuiCol.Header, Vector4.Zero);
                ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Vector4.Zero);
                ImGui.PushStyleColor(ImGuiCol.HeaderActive, Vector4.Zero);
                if (ImGui.Selectable("##choice", choice.Key == equipmentPickerCurrent,
                    ImGuiSelectableFlags.DontClosePopups, new Vector2(selectWidth, rowHeight)))
                    SelectPickerEquipment(choice.Key);
                ImGui.PopStyleColor(3);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{choice.Name}\n{choice.Number} / {choice.DisplayVariant}\n"
                    + string.Join("\n", choice.Items.Select(item => $"{item.Name}: IL {item.ItemLevel}")));
                var draw = ImGui.GetWindowDrawList();
                var rowEnd = position + new Vector2(width, rowHeight);
                var selected = choice.Key == equipmentPickerCurrent;
                if (selected || !sourceImportPending && ImGui.IsMouseHoveringRect(position, rowEnd))
                    draw.AddRectFilled(position, rowEnd, ImGui.GetColorU32(selected ? ImGuiCol.Header : ImGuiCol.HeaderHovered));
                var iconSize = rowHeight - 12 * scale;
                if (choice.IconId != 0 && plugin.TryGetIconTexture(choice.IconId, out var icon))
                    draw.AddImage(icon!.Handle, position + new Vector2(8 * scale, 6 * scale), position + new Vector2(8 * scale + iconSize, 6 * scale + iconSize));
                var textPosition = position + new Vector2(iconSize + 22 * scale, (rowHeight - ImGui.GetTextLineHeight() * 2 - 6 * scale) / 2);
                draw.PushClipRect(textPosition, new Vector2(position.X + selectWidth - 8 * scale, position.Y + rowHeight), true);
                draw.AddText(textPosition, ImGui.GetColorU32(ImGuiCol.Text), choice.Name);
                draw.PopClipRect();
                draw.PushClipRect(position, position + new Vector2(selectWidth - 8 * scale, rowHeight), true);
                draw.AddText(textPosition + new Vector2(0, ImGui.GetTextLineHeight() + 6 * scale),
                    ImGui.GetColorU32(ImGuiCol.TextDisabled), $"{choice.Number}  |  Variant {choice.DisplayVariant}  |  IL {choice.ItemLevels}");
                draw.PopClipRect();
                ImGui.EndDisabled();
                ImGui.SameLine();
                changed |= DrawPickerFavorite(choice.Key, "favorite", new Vector2(starWidth, rowHeight));
                draw.AddLine(new Vector2(position.X, position.Y + rowHeight), position + new Vector2(width, rowHeight), ImGui.GetColorU32(ImGuiCol.Border));
                ImGui.PopID();
            }
        }
        finally { clipper.Destroy(); ImGui.PopStyleVar(); }
        if (changed) RefreshEquipmentResults();
    }

    private void DrawPickerManualEquipment(bool sourceImportPending, float scale)
    {
        ImGui.SetNextItemWidth(140 * scale);
        ImGui.InputTextWithHint($"{T(TextKey.PickerModelNumber)}##direct-model", "e9005", ref equipmentNumber, 16);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(85 * scale);
        ImGui.InputInt($"{T(TextKey.Variant)}##direct-variant", ref equipmentVariant, 0, 0);
        var valid = EquipmentChoice.TryParseModel(equipmentNumber, equipmentPickerSlot, out var model)
            && equipmentVariant is >= 0 and <= byte.MaxValue;
        ImGui.BeginDisabled(!valid);
        ImGui.BeginDisabled(sourceImportPending);
        if (ImGui.Button(T(TextKey.UseEquipmentNumber)))
            SelectPickerEquipment(new EquipmentChoiceKey(equipmentPickerSlot, model, (byte)equipmentVariant));
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (DrawPickerFavorite(new(equipmentPickerSlot, model, valid ? (byte)equipmentVariant : (byte)0), "direct-favorite", new Vector2(ImGui.GetFrameHeight())))
            RefreshEquipmentResults();
        ImGui.SameLine(); ImGui.TextUnformatted(T(TextKey.PickerFavorites));
        ImGui.EndDisabled();
    }
}
