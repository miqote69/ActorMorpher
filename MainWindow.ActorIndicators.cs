using ActorMorpher.Localization;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using System.Numerics;

namespace ActorMorpher;

public sealed partial class MainWindow
{
    private static readonly Vector4 ActorSelectionColor = new(0.20f, 0.90f, 0.96f, 1.0f);
    private static readonly Vector4 ActorMarkerColor = new(0.26f, 0.82f, 0.88f, 0.84f);
    private static readonly MarkerVertex[][] RoundedMarkerLayers =
        [CreateRoundedMarkerLayer(11.5f), CreateRoundedMarkerLayer(9.5f),
            CreateRoundedMarkerLayer(7.5f), CreateRoundedMarkerLayer(5.5f)];

    private bool DrawActorIndicatorRow(LogicalActorKey key, string name, bool selected,
        bool targeted, bool modified, bool pinned, out bool hovered)
    {
        var position = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = ImGui.GetTextLineHeight();
        var scale = ImGui.GetFontSize() / 13.0f;
        ImGui.PushStyleColor(ImGuiCol.Header, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Vector4.Zero);
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Vector4.Zero);
        var activated = ImGui.Selectable($"##actor-{key.GetHashCode()}", selected,
            ImGuiSelectableFlags.None, new Vector2(width, height));
        hovered = ImGui.IsItemHovered();
        ImGui.PopStyleColor(3);

        var draw = ImGui.GetWindowDrawList();
        var rowEnd = position + new Vector2(width, ImGui.GetTextLineHeightWithSpacing());
        if (selected || activated || hovered)
            draw.AddRectFilled(position, rowEnd, ImGui.GetColorU32(
                new Vector4(0.20f, 0.90f, 0.96f, selected || activated ? 0.16f : 0.08f)));
        if (selected || activated)
            draw.AddRectFilled(position, new Vector2(position.X + 3 * scale, rowEnd.Y),
                ImGui.GetColorU32(ActorSelectionColor));

        var right = position.X + width - 5 * scale;
        if (selected || activated)
        {
            var label = T(TextKey.ActorSelected);
            var size = ImGui.CalcTextSize(label);
            right -= size.X;
            draw.AddText(new Vector2(right, position.Y), ImGui.GetColorU32(ActorSelectionColor), label);
            right -= 10 * scale;
        }
        if (targeted)
        {
            var label = T(TextKey.ActorTargeted);
            right -= ImGui.CalcTextSize(label).X;
            draw.AddText(new Vector2(right, position.Y), ImGui.GetColorU32(ImGuiCol.Text), label);
            using (plugin.PushIconFont())
            {
                var icon = FontAwesomeIcon.Crosshairs.ToIconString();
                var size = ImGui.CalcTextSize(icon);
                right -= size.X + 4 * scale;
                draw.AddText(new Vector2(right, position.Y + (height - size.Y) / 2),
                    ImGui.GetColorU32(ImGuiCol.Text), icon);
            }
            right -= 8 * scale;
        }

        var color = pinned ? new Vector4(1.0f, 0.74f, 0.25f, 1.0f)
            : modified ? new Vector4(0.35f, 0.86f, 0.55f, 1.0f) : ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        var namePosition = position + new Vector2(7 * scale, 0);
        // Reserve separate space for both indicators when selection and game target coincide.
        draw.PushClipRect(position, new Vector2(Math.Max(namePosition.X, right), rowEnd.Y), true);
        draw.AddText(namePosition, ImGui.GetColorU32(color), name);
        draw.PopClipRect();
        return activated;
    }

    private void DrawSelectedActorMarker()
    {
        if (selectedActorKey is not { } key
            || !plugin.TryGetActorHeadScreenPosition(key, out var actor, out var head))
            return;

        var scale = ImGui.GetFontSize() / 13.0f;
        var draw = ImGui.GetBackgroundDrawList(ImGuiHelpers.MainViewport);
        var cyan = ImGui.GetColorU32(ActorMarkerColor);
        var shadow = ImGui.GetColorU32(new Vector4(0.01f, 0.02f, 0.03f, 0.68f));
        var tip = head - new Vector2(0, 16 * scale);
        DrawActorSelectionPointer(draw, tip, scale, cyan);

        var brand = "ActorMorpher";
        var selected = T(TextKey.ActorSelected);
        var lineHeight = ImGui.GetTextLineHeight();
        var gap = 2 * scale;
        var padding = new Vector2(8, 6) * scale;
        var width = Math.Max(ImGui.CalcTextSize(brand).X,
            Math.Max(ImGui.CalcTextSize(actor.Name).X, ImGui.CalcTextSize(selected).X));
        var size = new Vector2(width + padding.X * 2, lineHeight * 3 + gap * 2 + padding.Y * 2);
        var bottom = new Vector2(head.X, tip.Y - 42 * scale);
        var topLeft = bottom - new Vector2(size.X / 2, size.Y);
        draw.AddRectFilled(topLeft, topLeft + size, shadow, 3 * scale);
        var y = topLeft.Y + padding.Y;
        draw.AddText(new Vector2(head.X - ImGui.CalcTextSize(brand).X / 2, y), cyan, brand);
        y += lineHeight + gap;
        draw.AddText(new Vector2(head.X - ImGui.CalcTextSize(actor.Name).X / 2, y),
            ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), actor.Name);
        y += lineHeight + gap;
        draw.AddText(new Vector2(head.X - ImGui.CalcTextSize(selected).X / 2, y), cyan, selected);
    }

    private static void DrawActorSelectionPointer(ImDrawListPtr draw, Vector2 tip, float scale, uint color)
    {
        // A narrow, fading halo softens the same arrowhead without making its core brighter.
        DrawActorSelectionPointerLayer(draw, tip, scale, WithActorMarkerOpacity(color, 0.035f), RoundedMarkerLayers[0]);
        DrawActorSelectionPointerLayer(draw, tip, scale, WithActorMarkerOpacity(color, 0.065f), RoundedMarkerLayers[1]);
        DrawActorSelectionPointerLayer(draw, tip, scale, WithActorMarkerOpacity(color, 0.12f), RoundedMarkerLayers[2]);
        DrawActorSelectionPointerLayer(draw, tip, scale, color, RoundedMarkerLayers[3]);
    }

    private static uint WithActorMarkerOpacity(uint color, float opacity)
        => (color & 0x00FFFFFFu) | ((uint)((color >> 24) * opacity) << 24);

    private readonly record struct MarkerVertex(Vector2 Position, bool Transparent = false);

    private static void DrawActorSelectionPointerLayer(ImDrawListPtr draw, Vector2 tip, float scale,
        uint color, MarkerVertex[] vertices)
    {
        // One non-overlapping fill per layer; no opaque seams from intersecting thick strokes.
        var uv = ImGui.GetIO().Fonts.TexUvWhitePixel;
        draw.PrimReserve(vertices.Length, vertices.Length);
        foreach (var vertex in vertices)
            draw.PrimVtx(tip + vertex.Position * scale, uv,
                vertex.Transparent ? color & 0x00FFFFFFu : color);
    }

    private static MarkerVertex[] CreateRoundedMarkerLayer(float thickness)
    {
        Vector2[] line = [new(-3, -26), new(-13.5f, -28.5f), new(0, -6),
            new(13.5f, -28.5f), new(3, -26)];
        var offsets = new Vector2[line.Length];
        var previousNormal = Vector2.Zero;
        for (var i = 0; i < line.Length - 1; i++)
        {
            var direction = Vector2.Normalize(line[i + 1] - line[i]);
            var normal = new Vector2(-direction.Y, direction.X);
            if (i == 0)
                offsets[0] = normal;
            else
            {
                var bisector = Vector2.Normalize(previousNormal + normal);
                offsets[i] = bisector / Vector2.Dot(bisector, normal);
            }
            previousNormal = normal;
        }
        offsets[^1] = previousNormal;

        // The open arrowhead is a simple concave outline, so it needs no hole or overlapping caps.
        var outline = new Vector2[line.Length * 2];
        for (var i = 0; i < line.Length; i++)
        {
            outline[i] = line[i] - offsets[i] * thickness * 0.5f;
            outline[^(i + 1)] = line[i] + offsets[i] * thickness * 0.5f;
        }
        var rounded = new List<Vector2>();
        for (var i = 0; i < outline.Length; i++)
        {
            var before = outline[(i + outline.Length - 1) % outline.Length] - outline[i];
            var after = outline[(i + 1) % outline.Length] - outline[i];
            var trim = MathF.Min(3.5f, MathF.Min(before.Length(), after.Length()) / 3);
            var start = outline[i] + Vector2.Normalize(before) * trim;
            var end = outline[i] + Vector2.Normalize(after) * trim;
            for (var step = 0; step <= 6; step++)
            {
                var t = step / 6f;
                rounded.Add((1 - t) * (1 - t) * start + 2 * (1 - t) * t * outline[i] + t * t * end);
            }
        }

        var vertices = new List<MarkerVertex>();
        var remaining = Enumerable.Range(0, rounded.Count).ToList();
        var winding = MathF.Sign(Enumerable.Range(0, rounded.Count)
            .Sum(i => MarkerCross(rounded[i], rounded[(i + 1) % rounded.Count])));
        while (remaining.Count > 2)
        {
            var ear = -1;
            for (var i = 0; i < remaining.Count; i++)
            {
                var a = rounded[remaining[(i + remaining.Count - 1) % remaining.Count]];
                var b = rounded[remaining[i]];
                var c = rounded[remaining[(i + 1) % remaining.Count]];
                if (MarkerCross(b - a, c - b) * winding <= 0)
                    continue;
                if (remaining.Any(index => index != remaining[(i + remaining.Count - 1) % remaining.Count]
                    && index != remaining[i] && index != remaining[(i + 1) % remaining.Count]
                    && MarkerCross(b - a, rounded[index] - a) * winding >= 0
                    && MarkerCross(c - b, rounded[index] - b) * winding >= 0
                    && MarkerCross(a - c, rounded[index] - c) * winding >= 0))
                    continue;
                vertices.AddRange([new(a), new(b), new(c)]);
                ear = i;
                break;
            }
            if (ear < 0)
                throw new InvalidOperationException("Invalid rounded actor marker outline.");
            remaining.RemoveAt(ear);
        }

        // A single outer AA fringe avoids seams along the internal triangle edges.
        var fringe = new Vector2[rounded.Count];
        for (var i = 0; i < rounded.Count; i++)
        {
            var beforeEdge = rounded[i] - rounded[(i + rounded.Count - 1) % rounded.Count];
            var afterEdge = rounded[(i + 1) % rounded.Count] - rounded[i];
            var before = Vector2.Normalize(beforeEdge);
            var after = Vector2.Normalize(afterEdge);
            var normal = Vector2.Normalize(new Vector2(before.Y + after.Y, -before.X - after.X)) * winding;
            var width = 0.75f;
            if (MarkerCross(before, after) * winding < 0)
                width = MathF.Min(width,
                    0.9f * MathF.Min(beforeEdge.Length(), afterEdge.Length()) / (before - after).Length());
            fringe[i] = rounded[i] + normal * width;
        }
        for (var i = 0; i < rounded.Count; i++)
        {
            var next = (i + 1) % rounded.Count;
            vertices.AddRange([new(rounded[i]), new(rounded[next]), new(fringe[next], true),
                new(rounded[i]), new(fringe[next], true), new(fringe[i], true)]);
        }
        return vertices.ToArray();
    }

    private static float MarkerCross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
}
