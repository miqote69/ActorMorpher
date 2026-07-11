using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using System.Numerics;

namespace ActorMorpher;

public sealed class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string actorFilter = string.Empty;
    private int selectedActorType;
    private int selectedActorRace;
    private int selectedActorGender;
    private string modelNameFilter = string.Empty;
    private string modelIdFilter = string.Empty;
    private int selectedCategory;
    private int selectedRace;
    private int selectedGender;
    private bool includeAdultHumans = true;
    private bool includeYoungNpc = true;
    private LogicalActorKey? selectedActorKey;
    private ModelSearchEntry? selectedModel;
    private string applyStatus = string.Empty;
    private bool applySucceeded;
    private string bulkNameFilter = string.Empty;
    private int bulkActorType;
    private int bulkRace;
    private int bulkGender;
    private bool bulkIncludeYourself;
    private string diagnosticMarker = string.Empty;
    private bool diagnosticSettingsDirty;

    private static readonly string[] CategoryNames =
    [
        "Human",
        "Demihuman",
        "Monster",
    ];

    private static readonly string[] ActorTypeNames =
    [
        "All",
        "Players",
        "NPCs",
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

            if (ImGui.BeginTabItem("Bulk Outfit"))
            {
                DrawBulkOutfitTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Diagnostics"))
            {
                DrawDiagnosticsTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

    }

    private void DrawDiagnosticsTab()
    {
        var diagnostics = plugin.Diagnostics;
        var configuration = plugin.Configuration;
        var modes = new[] { "Off", "Errors Only", "Full Troubleshooting" };
        var selectedMode = (int)diagnostics.Log.Mode;
        ImGui.SetNextItemWidth(220.0f);
        if (ImGui.Combo("Diagnostic File Logging", ref selectedMode, modes, modes.Length))
            diagnostics.SetPersistentMode((FileDiagnosticMode)selectedMode);

        if (diagnostics.Log.Mode == FileDiagnosticMode.Off)
        {
            ImGui.TextWrapped("No Actor Morpher diagnostic files will be created.");
            ImGui.TextDisabled("Critical errors may still appear in the standard Dalamud log.");
        }

        var settingsChanged = false;
        var includeNames = configuration.IncludeActorNamesInDiagnostics;
        if (ImGui.Checkbox("Include Actor Names", ref includeNames))
        {
            configuration.IncludeActorNamesInDiagnostics = includeNames;
            settingsChanged = true;
        }
        var includeAddresses = configuration.IncludeRawAddressesInDiagnostics;
        if (ImGui.Checkbox("Include Raw Memory Addresses", ref includeAddresses))
        {
            configuration.IncludeRawAddressesInDiagnostics = includeAddresses;
            settingsChanged = true;
        }
        var mirror = configuration.MirrorDiagnosticsBesidePluginAssembly;
        if (ImGui.Checkbox("Mirror Logs Beside Plugin Assembly", ref mirror))
        {
            configuration.MirrorDiagnosticsBesidePluginAssembly = mirror;
            settingsChanged = true;
        }
        var retentionDays = configuration.DiagnosticRetentionDays;
        if (ImGui.InputInt("Retention Days", ref retentionDays))
        {
            configuration.DiagnosticRetentionDays = retentionDays;
            settingsChanged = true;
        }
        var maximumSessions = configuration.DiagnosticMaximumSessions;
        if (ImGui.InputInt("Maximum Sessions", ref maximumSessions))
        {
            configuration.DiagnosticMaximumSessions = maximumSessions;
            settingsChanged = true;
        }
        var maximumFileSize = configuration.DiagnosticMaximumFileSizeMb;
        if (ImGui.InputInt("Maximum File Size (MB)", ref maximumFileSize))
        {
            configuration.DiagnosticMaximumFileSizeMb = maximumFileSize;
            settingsChanged = true;
        }
        var maximumTotalSize = configuration.DiagnosticMaximumTotalSizeMb;
        if (ImGui.InputInt("Maximum Total Size (MB)", ref maximumTotalSize))
        {
            configuration.DiagnosticMaximumTotalSizeMb = maximumTotalSize;
            settingsChanged = true;
        }
        diagnosticSettingsDirty |= settingsChanged;
        var canApplySettings = diagnosticSettingsDirty;
        if (!canApplySettings)
            ImGui.BeginDisabled();
        if (ImGui.Button("Apply Diagnostic Settings"))
            diagnosticSettingsDirty = !diagnostics.ApplySettings();
        if (!canApplySettings)
            ImGui.EndDisabled();

        ImGui.Separator();
        var status = diagnostics.Log.Status;
        DrawDiagnosticStatus("Session ID", diagnostics.SessionId);
        DrawDiagnosticStatus("Current Mode", diagnostics.Log.Mode.ToString());
        DrawDiagnosticStatus("Current Log File", status.CurrentLogFile ?? "None");
        DrawDiagnosticStatus("Standard Log Directory", status.StandardLogDirectory ?? "None");
        DrawDiagnosticStatus("Mirror Log Directory", status.MirrorLogDirectory ?? "None");
        DrawDiagnosticStatus("Queued Events", status.QueuedEvents.ToString());
        DrawDiagnosticStatus("Dropped Events", status.DroppedEvents.ToString());
        DrawDiagnosticStatus("Current File Size", status.CurrentFileSize.ToString());
        DrawDiagnosticStatus("Last File Logging Error", status.LastFileLoggingError ?? "None");
        DrawDiagnosticStatus("Troubleshooting Capture", diagnostics.CaptureActive ? "Active" : "Inactive");

        ImGui.Separator();
        var enabled = diagnostics.Log.IsEnabled;
        if (!enabled)
            ImGui.BeginDisabled();
        if (ImGui.Button("Open Log Folder"))
            diagnostics.OpenLogFolder();
        ImGui.SameLine();
        if (ImGui.Button("Copy Log Path") && status.CurrentLogFile is not null)
            ImGui.SetClipboardText(status.CurrentLogFile);
        if (!enabled)
            ImGui.EndDisabled();

        ImGui.SetNextItemWidth(360.0f);
        ImGui.InputTextWithHint("##diagnostic-marker", "Optional marker note", ref diagnosticMarker, 200);
        if (!enabled)
            ImGui.BeginDisabled();
        if (ImGui.Button("Add Diagnostic Marker"))
        {
            diagnostics.AddMarker(diagnosticMarker);
            diagnosticMarker = string.Empty;
        }
        if (!enabled)
            ImGui.EndDisabled();

        if (!diagnostics.CaptureActive)
        {
            if (ImGui.Button("Begin Troubleshooting Capture"))
                diagnostics.BeginCapture();
        }
        else if (ImGui.Button("End Troubleshooting Capture"))
        {
            diagnostics.EndCapture();
        }

        ImGui.SameLine();
        if (!enabled)
            ImGui.BeginDisabled();
        if (ImGui.Button("Create Diagnostic Snapshot"))
            diagnostics.CreateSnapshot();
        ImGui.SameLine();
        if (ImGui.Button("Clear Old Logs"))
            diagnostics.ClearOldLogs();
        if (!enabled)
            ImGui.EndDisabled();

        if (diagnostics.LastSnapshotDirectory is { } snapshotDirectory)
            ImGui.TextWrapped($"Latest Snapshot: {snapshotDirectory}");
    }

    private static void DrawDiagnosticStatus(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.SameLine(190.0f);
        ImGui.TextWrapped(value);
    }

    private void DrawBulkOutfitTab()
    {
        ImGui.TextUnformatted("Source Outfit: Current Player Appearance");
        ImGui.SameLine();
        ImGui.BeginDisabled();
        ImGui.Button("Refresh Source Preview");
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip("Standalone outfit reading is not available in this build.");

        ImGui.Separator();
        ImGui.TextUnformatted("Target Filters");
        ImGui.SetNextItemWidth(160.0f);
        ImGui.Combo("Actor Type##bulk", ref bulkActorType, ActorTypeNames, ActorTypeNames.Length);
        ImGui.SetNextItemWidth(160.0f);
        if (ImGui.BeginCombo("Race##bulk", HumanRaces[bulkRace].Name))
        {
            for (var i = 0; i < HumanRaces.Length; ++i)
            {
                if (ImGui.Selectable(HumanRaces[i].Name, bulkRace == i))
                    bulkRace = i;
            }
            ImGui.EndCombo();
        }
        ImGui.SetNextItemWidth(160.0f);
        if (ImGui.BeginCombo("Gender##bulk", HumanGenders[bulkGender].Name))
        {
            for (var i = 0; i < HumanGenders.Length; ++i)
            {
                if (ImGui.Selectable(HumanGenders[i].Name, bulkGender == i))
                    bulkGender = i;
            }
            ImGui.EndCombo();
        }
        ImGui.SetNextItemWidth(260.0f);
        ImGui.InputTextWithHint("Name##bulk", "Filter by name", ref bulkNameFilter, 128);
        ImGui.Checkbox("Include Yourself", ref bulkIncludeYourself);

        var gender = HumanGenders[bulkGender].Id;
        var preview = plugin.GetBulkOutfitPreview(new BulkOutfitSettings(
            (ActorTargetType)bulkActorType,
            HumanRaces[bulkRace].Id,
            gender == byte.MaxValue ? null : gender,
            bulkNameFilter,
            bulkIncludeYourself));

        ImGui.Separator();
        ImGui.TextUnformatted($"Matching logical actors: {preview.MatchingLogicalActors}");
        ImGui.TextUnformatted($"Eligible human actors: {preview.EligibleHumanActors}");
        ImGui.TextUnformatted($"Skipped non-human actors: {preview.SkippedNonHumanActors}");
        ImGui.TextUnformatted($"Unavailable actors: {preview.UnavailableActors}");

        ImGui.Spacing();
        ImGui.BeginDisabled();
        ImGui.Button("Apply to Matching Actors");
        ImGui.SameLine();
        ImGui.Button("Unequip All");
        ImGui.SameLine();
        ImGui.Button("Restore Modified Actors");
        ImGui.SameLine();
        ImGui.Button("Cancel Pending Operation");
        ImGui.EndDisabled();
        ImGui.TextDisabled("Outfit apply is unavailable until standalone equipment memory access is verified.");
        ImGui.TextDisabled("Facewear unavailable");
    }

    private void DrawActorsTab()
    {
        var actors = plugin.GetVisibleActors().Where(MatchesActorFilter).ToArray();
        if (selectedActorKey is { } selectedKey && !plugin.TryResolveActor(selectedKey, out _))
            selectedActorKey = null;
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
        if (ImGui.BeginTable("##actor-results", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable, Vector2.Zero))
        {
            ImGui.TableSetupColumn("Actors", ImGuiTableColumnFlags.WidthFixed, 280.0f);
            ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            ImGui.TextUnformatted($"Visible actors ({actors.Count})");
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##actor-filter", "Filter by name", ref actorFilter, 128);
            ImGui.SetNextItemWidth(-1);
            ImGui.Combo("##actor-type", ref selectedActorType, ActorTypeNames, ActorTypeNames.Length);
            ImGui.SetNextItemWidth(-1);
            DrawActorRaceFilter();
            ImGui.SetNextItemWidth(-1);
            DrawActorGenderFilter();

            var listHeight = Math.Max(120.0f, ImGui.GetContentRegionAvail().Y);
            if (ImGui.BeginChild("##actors", new Vector2(0, listHeight), true))
            {
                foreach (var actor in actors)
                {
                    var selected = selectedActorKey == actor.Key;
                    var localPlayerLabel = actor.IsLocalPlayer ? " (You)" : string.Empty;
                    var label = $"{actor.Name}{localPlayerLabel}##actor-{actor.Key.GetHashCode()}";
                    if (ImGui.Selectable(label, selected))
                    {
                        selectedActorKey = actor.Key;
                        plugin.Diagnostics.Log.Write(new DiagnosticLogEntry
                        {
                            EventId = DiagnosticEventIds.UserActionRequested,
                            Category = DiagnosticCategory.UserAction,
                            Message = "Actor selected.",
                            ActorKey = plugin.Diagnostics.FormatActorKey(actor.Key, actor.Name),
                            Properties = new Dictionary<string, object?> { ["objectKind"] = actor.Kind, ["representationCount"] = actor.Representations.Count },
                        });
                    }
                }
            }

            ImGui.EndChild();
            ImGui.TableNextColumn();
            DrawActorDetails();
            ImGui.EndTable();
        }
    }

    private void DrawActorDetails()
    {
        if (ImGui.BeginChild("##actor-details", Vector2.Zero, true))
        {
            if (selectedActorKey is not { } key || !plugin.TryResolveActor(key, out var actor))
            {
                ImGui.TextDisabled("Select an actor from the list.");
                ImGui.EndChild();
                return;
            }

            var current = actor.Current;
            ImGui.TextUnformatted(actor.Name);
            ImGui.Separator();
            if (ImGui.BeginTable("##actor-detail-fields", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
            {
                ImGui.TableSetupColumn("Field", ImGuiTableColumnFlags.WidthFixed, 150.0f);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
                DrawDetailRow("Actor Type", actor.Kind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc ? "Player" : "NPC");
                DrawDetailRow("ObjectKind", actor.Kind.ToString());
                DrawDetailRow("Representation", actor.Representations.Count.ToString());
                DrawDetailRow("Object Index", current.RepresentationKey.ObjectIndex.ToString());
                DrawDetailRow("Original Object Index", actor.Key.OriginalObjectIndex.ToString());
                DrawDetailRow("GameObject ID", $"0x{current.RepresentationKey.GameObjectId:X}");
                DrawDetailRow("Entity ID", $"0x{current.RepresentationKey.EntityId:X}");
                DrawDetailRow("Base ID", current.BaseId.ToString());
                DrawDetailRow("ModelChara ID", current.ModelCharaId.ToString());
                DrawDetailRow("Race", current.Race is { } race ? GetRaceName(race) : "Non-Human / Unknown");
                DrawDetailRow("Gender", current.Gender is { } gender ? GetGenderName(gender) : "Non-Human / Unknown");
                DrawDetailRow("Body Type", current.BodyType?.ToString() ?? "Non-Human / Unknown");
                DrawDetailRow("Class Job", current.ClassJob.ToString());
                DrawDetailRow("Level", current.Level.ToString());
                DrawDetailRow("Is Local Player", current.IsLocalPlayer ? "Yes" : "No");
                DrawDetailRow("Current Morph", "None");
                DrawDetailRow("Bulk Outfit Modified", "No");
                DrawDetailRow("Snapshot Available", "No");
                DrawDetailRow("GPose Representation", current.RepresentationKey.IsGPoseRepresentation ? "Yes" : "No");
                ImGui.EndTable();
            }
        }

        ImGui.EndChild();
    }

    private void DrawActorRaceFilter()
    {
        if (ImGui.BeginCombo("##actor-race", HumanRaces[selectedActorRace].Name))
        {
            for (var i = 0; i < HumanRaces.Length; ++i)
            {
                if (ImGui.Selectable(HumanRaces[i].Name, selectedActorRace == i))
                    selectedActorRace = i;
            }

            ImGui.EndCombo();
        }
    }

    private void DrawActorGenderFilter()
    {
        if (ImGui.BeginCombo("##actor-gender", HumanGenders[selectedActorGender].Name))
        {
            for (var i = 0; i < HumanGenders.Length; ++i)
            {
                if (ImGui.Selectable(HumanGenders[i].Name, selectedActorGender == i))
                    selectedActorGender = i;
            }

            ImGui.EndCombo();
        }
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
                    {
                        selectedModel = model;
                        plugin.Diagnostics.Log.Write(new DiagnosticLogEntry
                        {
                            EventId = DiagnosticEventIds.UserActionRequested,
                            Category = DiagnosticCategory.UserAction,
                            Message = "Model selected.",
                            Properties = new Dictionary<string, object?>
                            {
                                ["modelCharaId"] = model.ModelId,
                                ["category"] = model.Category,
                                ["completeness"] = model.Completeness,
                                ["source"] = model.Source,
                                ["sourceRowId"] = model.SourceId,
                            },
                        });
                    }
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
                DrawDetailRow("Data", model.Completeness.ToString());

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
        if (!string.IsNullOrWhiteSpace(actorFilter)
            && !actor.Name.Contains(actorFilter, StringComparison.CurrentCultureIgnoreCase))
            return false;

        if (selectedActorType == 1 && actor.Kind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc)
            return false;
        if (selectedActorType == 2 && actor.Kind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc)
            return false;

        var race = HumanRaces[selectedActorRace].Id;
        if (race != 0 && actor.Race != race)
            return false;

        var gender = HumanGenders[selectedActorGender].Id;
        return gender == byte.MaxValue || actor.Gender == gender;
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
