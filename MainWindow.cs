using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace ActorMorpher;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string actorFilter = string.Empty;
    private string modelNameFilter = string.Empty;
    private string modelIdFilter = string.Empty;
    private int selectedCategory;
    private int selectedRace;
    private int selectedGender;
    private bool includeAdultHumans = true;
    private bool includeYoungNpc = true;
    private ActorEntry? selectedActor;
    private ModelSearchEntry? selectedModel;

    private static readonly string[] CategoryNames =
    [
        "Human",
        "Demihuman",
        "Monster",
    ];

    private static readonly (uint Id, string Name)[] HumanRaces =
    [
        (0, "Any race"),
        (1, "Hyur"),
        (2, "Elezen"),
        (3, "Lalafell"),
        (4, "Miqo'te"),
        (5, "Roegadyn"),
        (6, "Au Ra"),
        (7, "Hrothgar"),
        (8, "Viera"),
    ];

    private static readonly (byte Id, string Name)[] HumanGenders =
    [
        (byte.MaxValue, "Any gender"),
        (0, "Male"),
        (1, "Female"),
    ];

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
        if (ImGui.BeginTabBar("##actor-morpher-tabs"))
        {
            if (ImGui.BeginTabItem("Actors"))
            {
                DrawActorsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Model Search"))
            {
                DrawModelSearchTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        ImGui.Separator();
        DrawSelectionFooter();
    }

    private void DrawActorsTab()
    {
        var actors = plugin.GetVisibleActors().Where(MatchesActorFilter).ToArray();
        DrawActors(actors);
    }

    private void DrawModelSearchTab()
    {
        var models = plugin.GetModelSearchEntries().Where(MatchesModelFilter).ToArray();
        DrawModelSearchControls();
        DrawModels(models);
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
                var localPlayerLabel = actor.IsLocalPlayer ? " (You)" : string.Empty;
                var label = $"{actor.Name}{localPlayerLabel} [{actor.Kind}]##actor-{actor.GameObjectId}";
                if (ImGui.Selectable(label, selected))
                    selectedActor = actor;

                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip($"BaseId: {actor.BaseId}\nEntityId: {actor.EntityId:X}\nTargetable: {actor.IsTargetable}");
            }
        }

        ImGui.EndChild();
    }

    private void DrawModels(IReadOnlyList<ModelSearchEntry> models)
    {
        ImGui.TextUnformatted($"Results ({models.Count})");

        if (ImGui.BeginTable("##model-results", 5, ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollY, Vector2.Zero))
        {
            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Model ID", ImGuiTableColumnFlags.WidthFixed, 80.0f);
            ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed, 92.0f);
            ImGui.TableSetupColumn("Source", ImGuiTableColumnFlags.WidthFixed, 86.0f);
            ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();

            foreach (var model in models)
            {
                var selected = selectedModel?.RowId == model.RowId;
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                if (ImGui.Selectable($"{model.Name}##model-{model.RowId}-{model.Source}-{model.SourceId}", selected, ImGuiSelectableFlags.SpanAllColumns))
                    selectedModel = model;

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(model.ModelId.ToString());
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(model.Category.ToString());
                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{model.Source} #{model.SourceId}");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(FormatModelDetails(model));
            }

            ImGui.EndTable();
        }
    }

    private void DrawModelSearchControls()
    {
        ImGui.TextUnformatted("Category");
        ImGui.SetNextItemWidth(180.0f);
        ImGui.Combo("##model-category", ref selectedCategory, CategoryNames, CategoryNames.Length);

        ImGui.SameLine();
        ImGui.TextUnformatted("Name");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Math.Max(180.0f, ImGui.GetContentRegionAvail().X * 0.45f));
        ImGui.InputTextWithHint("##model-name-filter", "Search by NPC, monster, or model name", ref modelNameFilter, 128);

        ImGui.SameLine();
        ImGui.TextUnformatted("Model ID");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110.0f);
        ImGui.InputTextWithHint("##model-id-filter", "e.g. 123", ref modelIdFilter, 32);

        if ((ModelCategory)selectedCategory != ModelCategory.Human)
            return;

        ImGui.SetNextItemWidth(150.0f);
        if (ImGui.BeginCombo("Race", HumanRaces[selectedRace].Name))
        {
            for (var i = 0; i < HumanRaces.Length; ++i)
            {
                if (ImGui.Selectable(HumanRaces[i].Name, selectedRace == i))
                    selectedRace = i;
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150.0f);
        if (ImGui.BeginCombo("Gender", HumanGenders[selectedGender].Name))
        {
            for (var i = 0; i < HumanGenders.Length; ++i)
            {
                if (ImGui.Selectable(HumanGenders[i].Name, selectedGender == i))
                    selectedGender = i;
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.Checkbox("Adult", ref includeAdultHumans);
        ImGui.SameLine();
        ImGui.Checkbox("Young NPC", ref includeYoungNpc);
    }

    private void DrawSelectionFooter()
    {
        var actorText = selectedActor is null
            ? "No actor selected"
            : $"{selectedActor.Name} [{selectedActor.Kind}]";
        var modelText = selectedModel is null
            ? "No form selected"
            : $"{selectedModel.Name} / Model ID {selectedModel.ModelId}";

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
            ImGui.TextWrapped("Model replacement is intentionally not wired yet. This UI now selects an actor and a target Model ID for the next implementation step.");
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

    private bool MatchesModelFilter(ModelSearchEntry model)
    {
        var category = (ModelCategory)selectedCategory;
        if (model.Category != category)
            return false;

        if (!string.IsNullOrWhiteSpace(modelNameFilter)
            && !model.Name.Contains(modelNameFilter, StringComparison.CurrentCultureIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(modelIdFilter)
            && !model.ModelId.ToString().Contains(modelIdFilter, StringComparison.Ordinal))
            return false;

        if (category != ModelCategory.Human)
            return true;

        var race = HumanRaces[selectedRace].Id;
        if (race != 0 && model.Race != race)
            return false;

        var gender = HumanGenders[selectedGender].Id;
        if (gender != byte.MaxValue && model.Gender != gender)
            return false;

        return (includeAdultHumans && !model.IsYoungNpc)
            || (includeYoungNpc && model.IsYoungNpc);
    }

    private static string FormatModelDetails(ModelSearchEntry model)
    {
        var text = $"Type {model.Type}, Model {model.Model}, Base {model.Base}, Variant {model.Variant}";
        if (model.Category != ModelCategory.Human)
            return text;

        return $"{text}, Race {model.Race}, Gender {model.Gender}, BodyType {model.BodyType}";
    }
}
