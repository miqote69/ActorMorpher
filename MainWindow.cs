using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace ActorMorpher;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string actorFilter = string.Empty;
    private string modelFilter = string.Empty;
    private ActorEntry? selectedActor;
    private ModelCharaEntry? selectedModel;

    public MainWindow(Plugin plugin)
        : base($"{Plugin.DisplayName} v{Plugin.DisplayVersion}###ActorMorpherMain")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(680, 420),
            MaximumSize = new Vector2(1200, 10000),
        };
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        var actors = plugin.GetVisibleActors().Where(MatchesActorFilter).ToArray();
        var models = plugin.GetModelCharaEntries().Where(MatchesModelFilter).ToArray();

        if (ImGui.BeginTable("##actor-morpher-layout", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Actors", ImGuiTableColumnFlags.WidthStretch, 0.45f);
            ImGui.TableSetupColumn("Forms", ImGuiTableColumnFlags.WidthStretch, 0.55f);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            DrawActors(actors);

            ImGui.TableNextColumn();
            DrawModels(models);

            ImGui.EndTable();
        }

        ImGui.Separator();
        DrawSelectionFooter();
    }

    private void DrawActors(IReadOnlyList<ActorEntry> actors)
    {
        ImGui.TextUnformatted($"Visible actors ({actors.Count})");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##actor-filter", "Filter actors", ref actorFilter, 128);

        if (ImGui.BeginChild("##actors", Vector2.Zero, true))
        {
            foreach (var actor in actors)
            {
                var selected = selectedActor?.GameObjectId == actor.GameObjectId;
                var label = $"{actor.Name} [{actor.Kind}]##actor-{actor.GameObjectId}";
                if (ImGui.Selectable(label, selected))
                    selectedActor = actor;

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"BaseId: {actor.BaseId}\nEntityId: {actor.EntityId:X}\nTargetable: {actor.IsTargetable}");
            }
        }

        ImGui.EndChild();
    }

    private void DrawModels(IReadOnlyList<ModelCharaEntry> models)
    {
        ImGui.TextUnformatted($"ModelChara forms ({models.Count})");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##model-filter", "Filter by row id, type, model, base, or variant", ref modelFilter, 128);

        if (ImGui.BeginChild("##models", Vector2.Zero, true))
        {
            foreach (var model in models)
            {
                var selected = selectedModel?.RowId == model.RowId;
                var label = $"#{model.RowId}  Type {model.Type}  Model {model.Model}  Base {model.Base}  Variant {model.Variant}";
                if (ImGui.Selectable($"{label}##model-{model.RowId}", selected))
                    selectedModel = model;
            }
        }

        ImGui.EndChild();
    }

    private void DrawSelectionFooter()
    {
        var actorText = selectedActor is null
            ? "No actor selected"
            : $"{selectedActor.Name} [{selectedActor.Kind}]";
        var modelText = selectedModel is null
            ? "No form selected"
            : $"ModelChara #{selectedModel.RowId} (Type {selectedModel.Type})";

        ImGui.TextUnformatted(actorText);
        ImGui.SameLine();
        ImGui.TextUnformatted("->");
        ImGui.SameLine();
        ImGui.TextUnformatted(modelText);

        var canApply = selectedActor is not null && selectedModel is not null;
        if (!canApply)
            ImGui.BeginDisabled();

        if (ImGui.Button("Apply prototype"))
            ImGui.OpenPopup("##not-implemented");

        if (!canApply)
            ImGui.EndDisabled();

        if (ImGui.BeginPopup("##not-implemented"))
        {
            ImGui.TextWrapped("Model replacement is intentionally not wired yet. This first pass builds the project shell and browser for actors and ModelChara rows.");
            ImGui.EndPopup();
        }
    }

    private bool MatchesActorFilter(ActorEntry actor)
    {
        return string.IsNullOrWhiteSpace(actorFilter)
            || actor.Name.Contains(actorFilter, StringComparison.CurrentCultureIgnoreCase)
            || actor.Kind.ToString().Contains(actorFilter, StringComparison.CurrentCultureIgnoreCase)
            || actor.BaseId.ToString().Contains(actorFilter, StringComparison.Ordinal);
    }

    private bool MatchesModelFilter(ModelCharaEntry model)
    {
        return string.IsNullOrWhiteSpace(modelFilter)
            || model.RowId.ToString().Contains(modelFilter, StringComparison.Ordinal)
            || model.Type.ToString().Contains(modelFilter, StringComparison.Ordinal)
            || model.Model.ToString().Contains(modelFilter, StringComparison.Ordinal)
            || model.Base.ToString().Contains(modelFilter, StringComparison.Ordinal)
            || model.Variant.ToString().Contains(modelFilter, StringComparison.Ordinal);
    }
}
