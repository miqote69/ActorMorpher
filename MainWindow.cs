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
    private string applyStatus = string.Empty;
    private bool applySucceeded;

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

        if (ImGui.BeginTable("##model-results", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable, Vector2.Zero))
        {
            ImGui.TableSetupColumn("Characters", ImGuiTableColumnFlags.WidthFixed, 280.0f);
            ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            if (ImGui.BeginChild("##model-name-list", Vector2.Zero, true))
            {
                foreach (var model in models)
                {
                    var selected = IsSelectedModel(model);
                    if (ImGui.Selectable($"{model.Name}##model-{model.RowId}-{model.Source}-{model.SourceId}", selected))
                        selectedModel = model;
                }
            }

            ImGui.EndChild();
            ImGui.TableNextColumn();
            DrawModelDetails();
            ImGui.EndTable();
        }
    }

    private void DrawModelDetails()
    {
        if (ImGui.BeginChild("##model-details", Vector2.Zero, true))
        {
            if (selectedModel is not { } model)
            {
                ImGui.TextDisabled("Select a character from the list.");
                ImGui.EndChild();
                return;
            }

            ImGui.TextUnformatted(model.Name);
            ImGui.Separator();

            if (ImGui.BeginTable("##model-detail-fields", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
            {
                ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthFixed, 110.0f);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
                DrawDetailRow("Model ID", model.ModelId.ToString());
                DrawDetailRow("Category", model.Category.ToString());
                DrawDetailRow("Source", $"{model.Source} #{model.SourceId}");
                DrawDetailRow("Type", model.Type.ToString());
                DrawDetailRow("Model", model.Model.ToString());
                DrawDetailRow("Base", model.Base.ToString());
                DrawDetailRow("Variant", model.Variant.ToString());

                if (model.Category == ModelCategory.Human)
                {
                    DrawDetailRow("Race", GetRaceName(model.Race));
                    DrawDetailRow("Gender", GetGenderName(model.Gender));
                    DrawDetailRow("Age", GetAgeName(model.BodyType));
                    DrawDetailRow("Body Type", model.BodyType.ToString());
                }

                ImGui.EndTable();
            }

            ImGui.Spacing();
            var unavailableReason = Plugin.GetApplyUnavailableReason(model);
            ImGui.BeginDisabled();
            ImGui.Button("Apply to Yourself");
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(unavailableReason);
            applyStatus = unavailableReason;
            applySucceeded = false;

            if (!string.IsNullOrWhiteSpace(applyStatus))
            {
                var color = applySucceeded
                    ? new Vector4(0.35f, 0.85f, 0.45f, 1.0f)
                    : new Vector4(0.95f, 0.35f, 0.35f, 1.0f);
                ImGui.TextColored(color, applyStatus);
            }
        }

        ImGui.EndChild();
    }

    private static void DrawDetailRow(string field, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.TextDisabled(field);
        ImGui.TableNextColumn();
        ImGui.TextUnformatted(value);
    }

    private bool IsSelectedModel(ModelSearchEntry model)
    {
        return selectedModel is { } selected
            && selected.RowId == model.RowId
            && selected.Source == model.Source
            && selected.SourceId == model.SourceId;
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

    private static string GetRaceName(uint race)
    {
        return HumanRaces.FirstOrDefault(entry => entry.Id == race) is var entry && entry.Name is not null
            ? entry.Name.Replace("Any race", "Unknown")
            : $"Unknown ({race})";
    }

    private static string GetGenderName(byte gender)
    {
        return gender switch
        {
            0 => "Male",
            1 => "Female",
            _ => $"Unknown ({gender})",
        };
    }

    private static string GetAgeName(byte bodyType)
    {
        return bodyType switch
        {
            (byte)NpcAge.Normal => "Adult",
            (byte)NpcAge.Old => "Old",
            (byte)NpcAge.Young => "Young",
            _ => $"Unknown ({bodyType})",
        };
    }
}
