using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using System.Numerics;
using ActorMorpher.Localization;
using ActorMorpher.Preview;

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
    private uint selectedTribe;
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
    private bool bulkExclusionEnabled;
    private string bulkExcludeNameFilter = string.Empty;
    private int bulkExcludeActorType;
    private int bulkExcludeRace;
    private int bulkExcludeGender;
    private bool bulkIncludeYourself;
    private string bulkActionStatus = string.Empty;
    private string diagnosticMarker = string.Empty;
    private bool diagnosticSettingsDirty;

    private static readonly uint[] HumanRaces =
    [
        0, 1, 2, 3, 4, 5, 6, 7, 8,
    ];

    private static readonly byte[] HumanGenders =
    [
        byte.MaxValue, 0, 1,
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
        plugin.SetModelPreviewActive(false);
    }

    public override void Draw()
    {
        var modelSearchVisible = false;
        if (ImGui.BeginTabBar("##actor-morpher-tabs"))
        {
            if (ImGui.BeginTabItem($"{T(TextKey.Actors)}###actors"))
            {
                DrawActorsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem($"{T(TextKey.ModelSearch)}###model-search"))
            {
                modelSearchVisible = true;
                DrawModelSearchTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem($"{T(TextKey.BulkOutfit)}###bulk-outfit"))
            {
                DrawBulkOutfitTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem($"{T(TextKey.Diagnostics)}###diagnostics"))
            {
                DrawDiagnosticsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem($"{T(TextKey.Settings)}###settings"))
            {
                DrawSettingsTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
        plugin.SetModelPreviewActive(modelSearchVisible);
    }

    private void DrawSettingsTab()
    {
        var language = (int)plugin.Configuration.UiLanguage;
        var languages = new[] { T(TextKey.Automatic), T(TextKey.English), T(TextKey.Japanese), T(TextKey.German), T(TextKey.French) };
        ImGui.SetNextItemWidth(220.0f);
        if (ImGui.Combo($"{T(TextKey.UiLanguage)}###ui-language", ref language, languages, languages.Length))
        {
            plugin.Configuration.UiLanguage = (UiLanguage)language;
            plugin.Save();
        }
        ImGui.TextDisabled(T(TextKey.GameDataLanguage, plugin.GameLanguage));
    }

    private void DrawDiagnosticsTab()
    {
        var diagnostics = plugin.Diagnostics;
        var configuration = plugin.Configuration;
        var modes = new[] { T(TextKey.Off), T(TextKey.ErrorsOnly), T(TextKey.FullTroubleshooting) };
        var selectedMode = (int)diagnostics.Log.Mode;
        ImGui.SetNextItemWidth(220.0f);
        if (ImGui.Combo($"{T(TextKey.DiagnosticFileLogging)}###diagnostic-mode", ref selectedMode, modes, modes.Length))
            diagnostics.SetPersistentMode((FileDiagnosticMode)selectedMode);

        if (diagnostics.Log.Mode == FileDiagnosticMode.Off)
        {
            ImGui.TextWrapped(T(TextKey.NoDiagnosticFiles));
            ImGui.TextDisabled(T(TextKey.CriticalErrorsStandardLog));
        }

        var settingsChanged = false;
        var includeNames = configuration.IncludeActorNamesInDiagnostics;
        if (ImGui.Checkbox($"{T(TextKey.IncludeActorNames)}###diagnostic-names", ref includeNames))
        {
            configuration.IncludeActorNamesInDiagnostics = includeNames;
            settingsChanged = true;
        }
        var includeAddresses = configuration.IncludeRawAddressesInDiagnostics;
        if (ImGui.Checkbox($"{T(TextKey.IncludeRawAddresses)}###diagnostic-addresses", ref includeAddresses))
        {
            configuration.IncludeRawAddressesInDiagnostics = includeAddresses;
            settingsChanged = true;
        }
        var mirror = configuration.MirrorDiagnosticsBesidePluginAssembly;
        if (ImGui.Checkbox($"{T(TextKey.MirrorLogs)}###diagnostic-mirror", ref mirror))
        {
            configuration.MirrorDiagnosticsBesidePluginAssembly = mirror;
            settingsChanged = true;
        }
        var retentionDays = configuration.DiagnosticRetentionDays;
        if (ImGui.InputInt($"{T(TextKey.RetentionDays)}###retention-days", ref retentionDays))
        {
            configuration.DiagnosticRetentionDays = retentionDays;
            settingsChanged = true;
        }
        var maximumSessions = configuration.DiagnosticMaximumSessions;
        if (ImGui.InputInt($"{T(TextKey.MaximumSessions)}###maximum-sessions", ref maximumSessions))
        {
            configuration.DiagnosticMaximumSessions = maximumSessions;
            settingsChanged = true;
        }
        var maximumFileSize = configuration.DiagnosticMaximumFileSizeMb;
        if (ImGui.InputInt($"{T(TextKey.MaximumFileSize)}###maximum-file-size", ref maximumFileSize))
        {
            configuration.DiagnosticMaximumFileSizeMb = maximumFileSize;
            settingsChanged = true;
        }
        var maximumTotalSize = configuration.DiagnosticMaximumTotalSizeMb;
        if (ImGui.InputInt($"{T(TextKey.MaximumTotalSize)}###maximum-total-size", ref maximumTotalSize))
        {
            configuration.DiagnosticMaximumTotalSizeMb = maximumTotalSize;
            settingsChanged = true;
        }
        diagnosticSettingsDirty |= settingsChanged;
        var canApplySettings = diagnosticSettingsDirty;
        if (!canApplySettings)
            ImGui.BeginDisabled();
        if (ImGui.Button($"{T(TextKey.ApplyDiagnosticSettings)}###apply-diagnostics"))
            diagnosticSettingsDirty = !diagnostics.ApplySettings();
        if (!canApplySettings)
            ImGui.EndDisabled();

        ImGui.Separator();
        var status = diagnostics.Log.Status;
        DrawDiagnosticStatus(T(TextKey.SessionId), diagnostics.SessionId);
        DrawDiagnosticStatus(T(TextKey.CurrentMode), diagnostics.Log.Mode.ToString());
        DrawDiagnosticStatus(T(TextKey.CurrentLogFile), status.CurrentLogFile ?? T(TextKey.None));
        DrawDiagnosticStatus(T(TextKey.StandardLogDirectory), status.StandardLogDirectory ?? T(TextKey.None));
        DrawDiagnosticStatus(T(TextKey.MirrorLogDirectory), status.MirrorLogDirectory ?? T(TextKey.None));
        DrawDiagnosticStatus(T(TextKey.QueuedEvents), status.QueuedEvents.ToString());
        DrawDiagnosticStatus(T(TextKey.DroppedEvents), status.DroppedEvents.ToString());
        DrawDiagnosticStatus(T(TextKey.CurrentFileSize), status.CurrentFileSize.ToString());
        DrawDiagnosticStatus(T(TextKey.LastFileLoggingError), status.LastFileLoggingError ?? T(TextKey.None));
        DrawDiagnosticStatus(T(TextKey.TroubleshootingCapture), diagnostics.CaptureActive ? T(TextKey.Active) : T(TextKey.Inactive));

        ImGui.Separator();
        var enabled = diagnostics.Log.IsEnabled;
        if (!enabled)
            ImGui.BeginDisabled();
        if (ImGui.Button($"{T(TextKey.OpenLogFolder)}###open-log-folder"))
            diagnostics.OpenLogFolder();
        ImGui.SameLine();
        if (ImGui.Button($"{T(TextKey.CopyLogPath)}###copy-log-path") && status.CurrentLogFile is not null)
            ImGui.SetClipboardText(status.CurrentLogFile);
        if (!enabled)
            ImGui.EndDisabled();

        ImGui.SetNextItemWidth(360.0f);
        ImGui.InputTextWithHint("##diagnostic-marker", T(TextKey.OptionalMarkerNote), ref diagnosticMarker, 200);
        if (!enabled)
            ImGui.BeginDisabled();
        if (ImGui.Button($"{T(TextKey.AddDiagnosticMarker)}###add-marker"))
        {
            diagnostics.AddMarker(diagnosticMarker);
            diagnosticMarker = string.Empty;
        }
        if (!enabled)
            ImGui.EndDisabled();

        if (!diagnostics.CaptureActive)
        {
            if (ImGui.Button($"{T(TextKey.BeginTroubleshootingCapture)}###begin-capture"))
                diagnostics.BeginCapture();
        }
        else if (ImGui.Button($"{T(TextKey.EndTroubleshootingCapture)}###end-capture"))
        {
            diagnostics.EndCapture();
        }

        ImGui.SameLine();
        if (!enabled)
            ImGui.BeginDisabled();
        if (ImGui.Button($"{T(TextKey.CreateDiagnosticSnapshot)}###create-snapshot"))
            diagnostics.CreateSnapshot();
        ImGui.SameLine();
        if (ImGui.Button($"{T(TextKey.ClearOldLogs)}###clear-logs"))
            diagnostics.ClearOldLogs();
        if (!enabled)
            ImGui.EndDisabled();

        if (diagnostics.LastSnapshotDirectory is { } snapshotDirectory)
            ImGui.TextWrapped(T(TextKey.LatestSnapshot, snapshotDirectory));
    }

    private static void DrawDiagnosticStatus(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.SameLine(190.0f);
        ImGui.TextWrapped(value);
    }

    private void DrawBulkOutfitTab()
    {
        ImGui.TextUnformatted(T(TextKey.SourceOutfit));
        ImGui.SameLine();
        if (ImGui.Button($"{T(TextKey.RefreshSourcePreview)}###refresh-source"))
            plugin.RefreshSourceOutfit(out bulkActionStatus);

        var source = plugin.SourceOutfit;
        DrawOutfitDisplay("source-outfit", source);

        ImGui.Separator();
        ImGui.TextUnformatted(T(TextKey.TargetFilters));
        ImGui.Checkbox($"{T(TextKey.IncludeYourself)}###bulk-include-yourself", ref bulkIncludeYourself);
        DrawBulkFilterControls("target", ref bulkActorType, ref bulkRace, ref bulkGender, ref bulkNameFilter);

        ImGui.Spacing();
        ImGui.TextUnformatted(T(TextKey.ExclusionFilters));
        ImGui.Checkbox($"{T(TextKey.EnableExclusionFilters)}###bulk-exclusion-enabled", ref bulkExclusionEnabled);
        if (!bulkExclusionEnabled)
            ImGui.BeginDisabled();
        DrawBulkFilterControls("exclude", ref bulkExcludeActorType, ref bulkExcludeRace, ref bulkExcludeGender, ref bulkExcludeNameFilter);
        if (!bulkExclusionEnabled)
            ImGui.EndDisabled();

        var gender = HumanGenders[bulkGender];
        var excludeGender = HumanGenders[bulkExcludeGender];
        var preview = plugin.GetBulkOutfitPreview(new BulkOutfitSettings(
            new BulkOutfitFilter(
                (ActorTargetType)bulkActorType,
                HumanRaces[bulkRace],
                gender == byte.MaxValue ? null : gender,
                bulkNameFilter),
            bulkExclusionEnabled
                ? new BulkOutfitFilter(
                    (ActorTargetType)bulkExcludeActorType,
                    HumanRaces[bulkExcludeRace],
                    excludeGender == byte.MaxValue ? null : excludeGender,
                    bulkExcludeNameFilter)
                : null,
            bulkIncludeYourself));

        ImGui.Separator();
        ImGui.TextUnformatted(T(TextKey.MatchingActors, preview.MatchingLogicalActors));
        ImGui.TextUnformatted(T(TextKey.ExcludedActors, preview.ExcludedLogicalActors));
        ImGui.TextUnformatted(T(TextKey.EligibleHumanActors, preview.EligibleHumanActors));
        ImGui.TextUnformatted(T(TextKey.SkippedNonHumanActors, preview.SkippedNonHumanActors));
        ImGui.TextUnformatted(T(TextKey.UnavailableActors, preview.UnavailableActors));

        ImGui.Spacing();
        var operationRunning = plugin.CurrentBulkOperation is not null;
        var canApply = !operationRunning
            && plugin.CanUseLocalPlayerAsOutfitSource
            && preview.EligibleHumanActors > 0;
        if (!canApply)
            ImGui.BeginDisabled();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.12f, 0.45f, 0.25f, 1.0f));
        if (ImGui.Button($"{T(TextKey.ApplyMatchingActors)}###bulk-apply"))
            plugin.StartBulkOutfit(preview, out bulkActionStatus);
        ImGui.PopStyleColor();
        if (!canApply)
            ImGui.EndDisabled();
        ImGui.SameLine();
        var canUnequip = !operationRunning && preview.EligibleHumanActors > 0;
        if (!canUnequip)
            ImGui.BeginDisabled();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.65f, 0.28f, 0.08f, 1.0f));
        if (ImGui.Button($"{T(TextKey.UnequipAll)}###bulk-unequip"))
            plugin.StartUnequipAll(preview, out bulkActionStatus);
        ImGui.PopStyleColor();
        if (!canUnequip)
            ImGui.EndDisabled();
        ImGui.SameLine();
        var canRestore = !operationRunning && plugin.ModifiedOutfitActorCount > 0;
        if (!canRestore)
            ImGui.BeginDisabled();
        if (ImGui.Button($"{T(TextKey.RestoreModifiedActors)}###bulk-restore"))
            plugin.StartRestoreModifiedActors(out bulkActionStatus);
        if (!canRestore)
            ImGui.EndDisabled();
        ImGui.SameLine();
        if (!operationRunning)
            ImGui.BeginDisabled();
        if (ImGui.Button($"{T(TextKey.CancelPendingOperation)}###bulk-cancel"))
            plugin.CancelBulkOperation();
        if (!operationRunning)
            ImGui.EndDisabled();

        if (plugin.CurrentBulkOperation is { } operation)
        {
            ImGui.TextUnformatted(T(TextKey.Processing, operation.CurrentIndex, operation.Targets.Count));
            ImGui.TextUnformatted($"{T(TextKey.Succeeded)}: {operation.Succeeded}  {T(TextKey.Skipped)}: {operation.Skipped}  {T(TextKey.Failed)}: {operation.Failed}");
        }
        if (!string.IsNullOrWhiteSpace(bulkActionStatus))
            ImGui.TextWrapped(bulkActionStatus);
        if (!string.IsNullOrWhiteSpace(plugin.BulkOutfitStatus))
            ImGui.TextWrapped(plugin.BulkOutfitStatus);
    }

    private void DrawBulkFilterControls(
        string id,
        ref int actorType,
        ref int race,
        ref int gender,
        ref string name)
    {
        ImGui.SetNextItemWidth(160.0f);
        var actorTypeNames = ActorTypeNames();
        ImGui.Combo($"{T(TextKey.ActorType)}###bulk-{id}-actor-type", ref actorType, actorTypeNames, actorTypeNames.Length);
        ImGui.SetNextItemWidth(160.0f);
        if (ImGui.BeginCombo($"{T(TextKey.Race)}###bulk-{id}-race", GetRaceFilterName(race)))
        {
            for (var i = 0; i < HumanRaces.Length; ++i)
            {
                if (ImGui.Selectable($"{GetRaceFilterName(i)}###bulk-{id}-race-{i}", race == i))
                    race = i;
            }
            ImGui.EndCombo();
        }
        ImGui.SetNextItemWidth(160.0f);
        if (ImGui.BeginCombo($"{T(TextKey.Gender)}###bulk-{id}-gender", GetGenderFilterName(gender)))
        {
            for (var i = 0; i < HumanGenders.Length; ++i)
            {
                if (ImGui.Selectable($"{GetGenderFilterName(i)}###bulk-{id}-gender-{i}", gender == i))
                    gender = i;
            }
            ImGui.EndCombo();
        }
        ImGui.SetNextItemWidth(260.0f);
        ImGui.InputTextWithHint($"{T(TextKey.Name)}###bulk-{id}-name", T(TextKey.FilterByName), ref name, 128);
    }

    private void DrawOutfitDisplay(string id, OutfitData? outfit)
    {
        var equipment = plugin.GetOutfitEquipment(outfit).ToDictionary(static item => item.Slot);
        if (ImGui.BeginTable($"##{id}-table", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
        {
            ImGui.TableSetupColumn(T(TextKey.Slot), ImGuiTableColumnFlags.WidthFixed, 80.0f);
            ImGui.TableSetupColumn(T(TextKey.ItemName), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn(T(TextKey.Set), ImGuiTableColumnFlags.WidthFixed, 65.0f);
            ImGui.TableSetupColumn(T(TextKey.Variant), ImGuiTableColumnFlags.WidthFixed, 55.0f);
            ImGui.TableSetupColumn(T(TextKey.Stain1), ImGuiTableColumnFlags.WidthFixed, 48.0f);
            ImGui.TableSetupColumn(T(TextKey.Stain2), ImGuiTableColumnFlags.WidthFixed, 48.0f);
            ImGui.TableHeadersRow();
            foreach (var slot in Enum.GetValues<OutfitSlot>())
            {
                var armor = outfit?.Equipment[(int)slot] ?? default;
                equipment.TryGetValue(slot, out var item);
                ImGui.TableNextRow();
                ImGui.TableNextColumn(); ImGui.TextUnformatted(slot.ToString());
                ImGui.TableNextColumn(); DrawEquipmentItem(outfit is not null, item);
                ImGui.TableNextColumn(); ImGui.TextUnformatted(outfit is null ? "-" : EquipmentDisplayFormatting.FormatSet(slot, armor.Set));
                ImGui.TableNextColumn(); ImGui.TextUnformatted(outfit is null ? "-" : EquipmentDisplayFormatting.FormatVariant(armor.Variant));
                ImGui.TableNextColumn(); DrawStainSwatch($"{id}-{slot}-1", outfit is not null, armor.Stain1);
                ImGui.TableNextColumn(); DrawStainSwatch($"{id}-{slot}-2", outfit is not null, armor.Stain2);
            }
            ImGui.EndTable();
        }

        if (outfit is null)
            return;
        ImGui.TextUnformatted($"{T(TextKey.Facewear)}: {(outfit.Facewear.IsAvailable ? outfit.Facewear.ModelId : T(TextKey.Unavailable))}");
        ImGui.SameLine();
        ImGui.TextUnformatted($"{T(TextKey.Hat)}: {(outfit.HatVisible ? T(TextKey.Visible) : T(TextKey.Hidden))}");
        ImGui.SameLine();
        ImGui.TextUnformatted($"{T(TextKey.Visor)}: {(outfit.VisorToggled ? T(TextKey.Toggled) : T(TextKey.Normal))}");
    }

    private void DrawEquipmentItem(bool sourceAvailable, EquipmentDisplayEntry? item)
    {
        var iconSize = new Vector2(32.0f, 32.0f);
        if (sourceAvailable && item is { IconId: > 0 } && plugin.TryGetIconTexture(item.IconId, out var texture))
            ImGui.Image(texture!.Handle, iconSize);
        else
            ImGui.Dummy(iconSize);
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        var name = !sourceAvailable
            ? "-"
            : item is { Set: 0 }
                ? T(TextKey.NoEquipment)
                : string.IsNullOrWhiteSpace(item?.Name)
                    ? T(TextKey.Unavailable)
                    : item.Name;
        ImGui.TextWrapped(name);
    }

    private void DrawStainSwatch(string id, bool sourceAvailable, byte stainId)
    {
        if (!sourceAvailable)
        {
            ImGui.TextUnformatted("-");
            return;
        }

        var stain = plugin.GetStainDisplay(stainId);
        var color = stain is { HasColor: true }
            ? new Vector4(stain.R / 255.0f, stain.G / 255.0f, stain.B / 255.0f, 1.0f)
            : Vector4.Zero;
        ImGui.ColorButton(
            $"##stain-{id}",
            color,
            ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker | ImGuiColorEditFlags.NoDragDrop,
            new Vector2(24.0f, 24.0f));
        if (!ImGui.IsItemHovered())
            return;
        if (stain is { HasColor: true })
            ImGui.SetTooltip($"{stain.Name}\nRGB: {stain.R}, {stain.G}, {stain.B}\n#{stain.R:X2}{stain.G:X2}{stain.B:X2}");
        else
            ImGui.SetTooltip(stain?.Name ?? T(TextKey.Unavailable));
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
            ImGui.TableSetupColumn(T(TextKey.Actors), ImGuiTableColumnFlags.WidthFixed, 280.0f);
            ImGui.TableSetupColumn(T(TextKey.Details), ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();

            ImGui.TextUnformatted(T(TextKey.VisibleActors, actors.Count));
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##actor-filter", T(TextKey.FilterByName), ref actorFilter, 128);
            ImGui.SetNextItemWidth(-1);
            var actorTypeNames = ActorTypeNames();
            ImGui.Combo("##actor-type", ref selectedActorType, actorTypeNames, actorTypeNames.Length);
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
                    var localPlayerLabel = actor.IsLocalPlayer ? $" ({T(TextKey.IncludeYourself)})" : string.Empty;
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
                ImGui.TextDisabled(T(TextKey.SelectActor));
                ImGui.EndChild();
                return;
            }

            var current = actor.Current;
            ImGui.TextUnformatted(actor.Name);
            ImGui.Separator();
            if (ImGui.BeginTable("##actor-detail-fields", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
            {
                ImGui.TableSetupColumn(T(TextKey.Field), ImGuiTableColumnFlags.WidthFixed, 150.0f);
                ImGui.TableSetupColumn(T(TextKey.Value), ImGuiTableColumnFlags.WidthStretch);
                DrawDetailRow(T(TextKey.ActorType), actor.Kind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc ? T(TextKey.Players) : T(TextKey.Npcs));
                DrawDetailRow(T(TextKey.ObjectKind), actor.Kind.ToString());
                DrawDetailRow(T(TextKey.Representation), actor.Representations.Count.ToString());
                DrawDetailRow(T(TextKey.ObjectIndex), current.RepresentationKey.ObjectIndex.ToString());
                DrawDetailRow(T(TextKey.OriginalObjectIndex), actor.Key.OriginalObjectIndex.ToString());
                DrawDetailRow(T(TextKey.GameObjectId), $"0x{current.RepresentationKey.GameObjectId:X}");
                DrawDetailRow(T(TextKey.EntityId), $"0x{current.RepresentationKey.EntityId:X}");
                DrawDetailRow(T(TextKey.BaseId), current.BaseId.ToString());
                DrawDetailRow(T(TextKey.ModelCharaId), current.ModelCharaId.ToString());
                DrawDetailRow(T(TextKey.Race), current.Race is { } race ? plugin.GetRaceName(race) : T(TextKey.NonHumanUnknown));
                DrawDetailRow(T(TextKey.Gender), current.Gender is { } gender ? GetGenderName(gender) : T(TextKey.NonHumanUnknown));
                DrawDetailRow(T(TextKey.BodyType), current.BodyType?.ToString() ?? T(TextKey.NonHumanUnknown));
                DrawDetailRow(T(TextKey.ClassJob), current.ClassJob.ToString());
                DrawDetailRow(T(TextKey.Level), current.Level.ToString());
                DrawDetailRow(T(TextKey.IsLocalPlayer), current.IsLocalPlayer ? T(TextKey.Yes) : T(TextKey.No));
                var hasMorph = plugin.TryGetAppearanceOverride(actor.Key, out var morphState);
                DrawDetailRow(T(TextKey.CurrentMorph), hasMorph ? $"Model ID {morphState.DesiredData.ModelCharaId}" : T(TextKey.None));
                DrawDetailRow(T(TextKey.BulkOutfitModified), plugin.HasOutfitOverride(actor.Key) ? T(TextKey.Yes) : T(TextKey.No));
                DrawDetailRow(T(TextKey.SnapshotAvailable), hasMorph ? T(TextKey.Yes) : T(TextKey.No));
                DrawDetailRow(T(TextKey.GPoseRepresentation), current.RepresentationKey.IsGPoseRepresentation ? T(TextKey.Yes) : T(TextKey.No));
                ImGui.EndTable();
            }

            ImGui.Spacing();
            ImGui.TextUnformatted(T(TextKey.Equipment));
            if (current.Race is not null && plugin.TryGetActorOutfit(actor.Key, out var actorOutfit))
                DrawOutfitDisplay($"actor-outfit-{actor.Key.GetHashCode()}", actorOutfit);
            else
                ImGui.TextDisabled(T(TextKey.Unavailable));

            ImGui.Spacing();
            var canRestore = (plugin.HasAppearanceOverride(actor.Key) || plugin.HasOutfitOverride(actor.Key))
                && !plugin.IsAppearancePending(actor.Key)
                && plugin.CurrentBulkOperation is null;
            if (!canRestore)
                ImGui.BeginDisabled();
            if (ImGui.Button($"{T(TextKey.RestoreOriginalState)}###restore-actor"))
                applySucceeded = plugin.TryRestoreActor(actor.Key, out applyStatus);
            if (!canRestore)
                ImGui.EndDisabled();
            if (!string.IsNullOrWhiteSpace(plugin.AppearanceStatus))
                ImGui.TextWrapped(plugin.AppearanceStatus);
        }

        ImGui.EndChild();
    }

    private void DrawActorRaceFilter()
    {
        if (ImGui.BeginCombo("##actor-race", GetRaceFilterName(selectedActorRace)))
        {
            for (var i = 0; i < HumanRaces.Length; ++i)
            {
                if (ImGui.Selectable($"{GetRaceFilterName(i)}###actor-race-{i}", selectedActorRace == i))
                    selectedActorRace = i;
            }

            ImGui.EndCombo();
        }
    }

    private void DrawActorGenderFilter()
    {
        if (ImGui.BeginCombo("##actor-gender", GetGenderFilterName(selectedActorGender)))
        {
            for (var i = 0; i < HumanGenders.Length; ++i)
            {
                if (ImGui.Selectable($"{GetGenderFilterName(i)}###actor-gender-{i}", selectedActorGender == i))
                    selectedActorGender = i;
            }

            ImGui.EndCombo();
        }
    }

    private void DrawModels(IReadOnlyList<ModelSearchEntry> models)
    {
        ImGui.TextUnformatted(T(TextKey.Results, models.Count));

        if (ImGui.BeginTable("##model-results", 2, ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable, Vector2.Zero))
        {
            ImGui.TableSetupColumn(T(TextKey.Characters), ImGuiTableColumnFlags.WidthFixed, 280.0f);
            ImGui.TableSetupColumn(T(TextKey.Details), ImGuiTableColumnFlags.WidthStretch);
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
                        var previewAssets = plugin.GetModelPreviewAssets(model);
                        var previewSupport = plugin.GetModelPreviewSupport(model);
                        plugin.Diagnostics.Log.Write(new DiagnosticLogEntry
                        {
                            EventId = DiagnosticEventIds.UserActionRequested,
                            Category = DiagnosticCategory.UserAction,
                            Message = "Model selected.",
                            Properties = new Dictionary<string, object?>
                            {
                                ["modelCharaId"] = model.ModelId,
                                ["modelName"] = model.Name,
                                ["category"] = model.Category,
                                ["race"] = model.Race,
                                ["tribe"] = model.Tribe,
                                ["completeness"] = model.Completeness,
                                ["source"] = model.Source,
                                ["sourceRowId"] = model.SourceId,
                                ["previewReadiness"] = previewAssets.Readiness,
                                ["previewAssetsPresent"] = previewAssets.PresentCount,
                                ["previewAssetsMissing"] = previewAssets.MissingCount,
                                ["previewAssetsNotUsed"] = previewAssets.OptionalMissingCount,
                                ["previewBackend"] = previewSupport.PreferredBackend,
                                ["previewCompleteness"] = previewSupport.Completeness,
                                ["previewSupported"] = previewSupport.CanPreview,
                                ["previewSupportReason"] = previewSupport.Reason,
                            },
                        });
                        plugin.Diagnostics.Log.Write(new DiagnosticLogEntry
                        {
                            EventId = DiagnosticEventIds.PreviewAssetsResolved,
                            Category = DiagnosticCategory.ModelSearch,
                            Message = "Model preview assets resolved.",
                            Outcome = previewAssets.Readiness.ToString(),
                            Properties = new Dictionary<string, object?>
                            {
                                ["modelCharaId"] = model.ModelId,
                                ["category"] = model.Category,
                                ["type"] = model.Type,
                                ["model"] = model.Model,
                                ["base"] = model.Base,
                                ["variant"] = model.Variant,
                                ["presentAssets"] = previewAssets.PresentCount,
                                ["missingAssets"] = previewAssets.MissingCount,
                                ["notUsedAssets"] = previewAssets.OptionalMissingCount,
                                ["preferredBackend"] = previewSupport.PreferredBackend,
                                ["previewCompleteness"] = previewSupport.Completeness,
                                ["previewSupported"] = previewSupport.CanPreview,
                                ["supportReason"] = previewSupport.Reason,
                                ["assetPaths"] = previewAssets.Assets.Select(static asset => $"{asset.Label}:{asset.IsRequired}:{asset.IsPresent}:{asset.Path}").ToArray(),
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
                plugin.SelectPreviewModel(null);
                ImGui.TextDisabled(T(TextKey.SelectCharacter));
                ImGui.EndChild();
                return;
            }

            ImGui.TextUnformatted(model.Name);
            ImGui.Separator();
            plugin.SelectPreviewModel(model);
            DrawModelPreview();

            if (ImGui.BeginTable("##model-detail-fields", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
            {
                ImGui.TableSetupColumn(T(TextKey.Field), ImGuiTableColumnFlags.WidthFixed, 110.0f);
                ImGui.TableSetupColumn(T(TextKey.Value), ImGuiTableColumnFlags.WidthStretch);
                DrawDetailRow(T(TextKey.ModelId), model.ModelId.ToString());
                DrawDetailRow(T(TextKey.Category), CategoryNames()[(int)model.Category]);
                DrawDetailRow(T(TextKey.Source), $"{model.Source} #{model.SourceId}");
                DrawDetailRow(T(TextKey.Type), model.Type.ToString());
                DrawDetailRow(T(TextKey.Model), model.Model.ToString());
                DrawDetailRow(T(TextKey.Base), model.Base.ToString());
                DrawDetailRow(T(TextKey.Variant), model.Variant.ToString());
                DrawDetailRow(T(TextKey.Data), model.Completeness.ToString());

                if (model.Category == ModelCategory.Human)
                {
                    DrawDetailRow(T(TextKey.Race), plugin.GetRaceName(model.Race));
                    DrawDetailRow(T(TextKey.Tribe), plugin.GetTribeName(model.Tribe));
                    DrawDetailRow("Gender", GetGenderName(model.Gender));
                    DrawDetailRow("Age", GetAgeName(model.BodyType));
                    DrawDetailRow("Body Type", model.BodyType.ToString());
                }

                ImGui.EndTable();
            }

            DrawPreviewAssetReport(plugin.GetModelPreviewAssets(model));
            if (model.Category != ModelCategory.Human
                && plugin.ModelPreview is { State: ModelPreviewState.Unsupported, ModelId: var previewModelId }
                && previewModelId == model.ModelId)
                DrawPreviewGeometryReport(plugin.GetModelPreviewGeometry(model));

            if (model.Category == ModelCategory.Human)
            {
                ImGui.Spacing();
                ImGui.TextUnformatted(T(TextKey.Equipment));
                if (ImGui.BeginTable("##model-equipment", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
                {
                    ImGui.TableSetupColumn(T(TextKey.Slot), ImGuiTableColumnFlags.WidthFixed, 85.0f);
                    ImGui.TableSetupColumn(T(TextKey.Set), ImGuiTableColumnFlags.WidthFixed, 65.0f);
                    ImGui.TableSetupColumn(T(TextKey.Variant), ImGuiTableColumnFlags.WidthFixed, 65.0f);
                    ImGui.TableSetupColumn(T(TextKey.ItemName), ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableHeadersRow();
                    foreach (var item in plugin.GetHumanEquipment(model))
                    {
                        ImGui.TableNextRow();
                        ImGui.TableNextColumn(); ImGui.TextUnformatted(item.Slot.ToString());
                        ImGui.TableNextColumn(); ImGui.TextUnformatted(item.Set.ToString());
                        ImGui.TableNextColumn(); ImGui.TextUnformatted(item.Variant.ToString());
                        ImGui.TableNextColumn(); ImGui.TextUnformatted(string.IsNullOrEmpty(item.Name) ? T(TextKey.NoEquipment) : item.Name);
                    }
                    ImGui.EndTable();
                }
            }

            ImGui.Spacing();
            var unavailableReason = Plugin.GetApplyUnavailableReason(model);
            var canApply = Plugin.CanApplyModel(model);
            var canApplyYourself = canApply && !plugin.IsLocalPlayerAppearancePending();
            if (!canApplyYourself)
                ImGui.BeginDisabled();
            if (ImGui.Button($"{T(TextKey.ApplyToYourself)}###apply-yourself"))
                applySucceeded = plugin.TryApplyModelToLocalPlayer(model, out applyStatus);
            if (!canApplyYourself)
                ImGui.EndDisabled();
            if (!canApply && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(unavailableReason);

            ImGui.SameLine();
            var canApplySelected = canApply
                && selectedActorKey is { } selectedKey
                && !plugin.IsAppearancePending(selectedKey);
            if (!canApplySelected)
                ImGui.BeginDisabled();
            if (ImGui.Button($"{T(TextKey.ApplyToSelectedActor)}###apply-selected" ) && selectedActorKey is { } actorKey)
                applySucceeded = plugin.TryApplyModel(actorKey, model, out applyStatus);
            if (!canApplySelected)
                ImGui.EndDisabled();

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

    private void DrawModelPreview()
    {
        var available = ImGui.GetContentRegionAvail();
        var size = Math.Clamp(Math.Min(available.X, 320.0f), 240.0f, 512.0f);
        if (ImGui.BeginChild("##model-preview", new Vector2(size, size), true))
        {
            var preview = plugin.ModelPreview;
            ImGui.TextDisabled(T(TextKey.Preview3D));
            ImGui.Spacing();
            ImGui.TextWrapped(GetPreviewStatus(preview));
        }
        ImGui.EndChild();
        var canReset = plugin.ModelPreview.State == ModelPreviewState.Ready;
        if (!canReset)
            ImGui.BeginDisabled();
        if (ImGui.Button(T(TextKey.ResetCamera)))
            plugin.ResetModelPreviewCamera();
        if (!canReset)
            ImGui.EndDisabled();
        ImGui.Spacing();
    }

    private string GetPreviewStatus(ModelPreviewSnapshot preview)
        => preview.State switch
        {
            ModelPreviewState.Idle => T(TextKey.PreviewSelectModel),
            ModelPreviewState.Suspended => T(TextKey.PreviewSuspended),
            ModelPreviewState.Loading => T(TextKey.PreviewLoading),
            ModelPreviewState.Unsupported when selectedModel is { } model
                => GetPreviewSupportStatus(plugin.GetModelPreviewSupport(model)),
            ModelPreviewState.Unsupported => T(TextKey.PreviewUnavailable),
            ModelPreviewState.Failed => T(TextKey.PreviewFailed),
            _ => preview.Status,
        };

    private string GetPreviewSupportStatus(ModelPreviewSupport support)
    {
        var backend = support.PreferredBackend switch
        {
            ModelPreviewBackendKind.CharaView => "CharaView",
            ModelPreviewBackendKind.AssetRenderer => "Asset Renderer",
            _ => T(TextKey.None),
        };
        var reason = support.Reason switch
        {
            ModelPreviewSupportReason.InvalidHumanData => T(TextKey.PreviewInputInvalid),
            ModelPreviewSupportReason.MissingModel => T(TextKey.PreviewModelMissing),
            ModelPreviewSupportReason.MissingSkeleton => T(TextKey.PreviewSkeletonMissing),
            ModelPreviewSupportReason.MissingModelAndSkeleton => T(TextKey.PreviewModelAndSkeletonMissing),
            ModelPreviewSupportReason.CharaViewSlotOwnershipUnavailable
                or ModelPreviewSupportReason.CharaViewTextureOwnershipUnavailable
                or ModelPreviewSupportReason.CharaViewSlotAndTextureOwnershipUnavailable
                => T(TextKey.PreviewCharaViewUnsafe),
            ModelPreviewSupportReason.AssetRendererUnavailable => T(TextKey.PreviewAssetRendererUnavailable),
            _ => T(TextKey.PreviewUnavailable),
        };
        return $"{T(TextKey.PreviewBackend, backend)}\n{reason}";
    }

    private void DrawPreviewAssetReport(ModelPreviewAssetReport report)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted(T(TextKey.PreviewAssets));
        ImGui.SameLine();
        var readinessColor = report.Readiness is ModelPreviewReadiness.HumanDataReady or ModelPreviewReadiness.AssetsComplete
            ? new Vector4(0.35f, 0.85f, 0.45f, 1.0f)
            : report.Readiness == ModelPreviewReadiness.AssetsPartial
                ? new Vector4(0.95f, 0.75f, 0.25f, 1.0f)
                : new Vector4(0.95f, 0.35f, 0.35f, 1.0f);
        ImGui.TextColored(readinessColor, GetPreviewReadinessName(report.Readiness));

        if (report.Assets.Count == 0)
            return;
        if (!ImGui.BeginTable("##preview-assets", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
            return;
        ImGui.TableSetupColumn(T(TextKey.Asset), ImGuiTableColumnFlags.WidthFixed, 80.0f);
        ImGui.TableSetupColumn(T(TextKey.Status), ImGuiTableColumnFlags.WidthFixed, 65.0f);
        ImGui.TableSetupColumn(T(TextKey.Path), ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();
        foreach (var asset in report.Assets)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.TextUnformatted(GetPreviewAssetLabel(asset.Label));
            ImGui.TableNextColumn();
            var statusColor = asset.IsPresent
                ? new Vector4(0.35f, 0.85f, 0.45f, 1.0f)
                : asset.IsRequired
                    ? new Vector4(0.95f, 0.45f, 0.35f, 1.0f)
                    : new Vector4(0.65f, 0.65f, 0.65f, 1.0f);
            var statusText = asset.IsPresent
                ? T(TextKey.Ready)
                : asset.IsRequired
                    ? T(TextKey.Missing)
                    : T(TextKey.NotUsed);
            ImGui.TextColored(
                statusColor,
                statusText);
            ImGui.TableNextColumn(); ImGui.TextWrapped(asset.Path ?? T(TextKey.InMemoryAppearance));
        }
        ImGui.EndTable();
    }

    private void DrawPreviewGeometryReport(ModelPreviewGeometryReport report)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted(T(TextKey.PreviewGeometry));
        ImGui.SameLine();
        var ready = report.State == ModelPreviewGeometryState.Ready;
        var partial = report.State == ModelPreviewGeometryState.Partial;
        var color = ready
            ? new Vector4(0.35f, 0.85f, 0.45f, 1.0f)
            : partial
                ? new Vector4(0.95f, 0.75f, 0.25f, 1.0f)
                : new Vector4(0.95f, 0.35f, 0.35f, 1.0f);
        var status = ready
            ? T(TextKey.GeometryReady)
            : partial
                ? T(TextKey.GeometryPartial)
                : T(TextKey.GeometryUnavailable);
        ImGui.TextColored(color, status);

        if (report.Bounds is not { } bounds)
            return;
        if (!ImGui.BeginTable("##preview-geometry", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH))
            return;
        ImGui.TableSetupColumn(T(TextKey.Field), ImGuiTableColumnFlags.WidthFixed, 150.0f);
        ImGui.TableSetupColumn(T(TextKey.Value), ImGuiTableColumnFlags.WidthStretch);
        DrawDetailRow(T(TextKey.Meshes), report.MeshCount.ToString());
        DrawDetailRow(T(TextKey.SkippedMeshes), report.SkippedMeshCount.ToString());
        DrawDetailRow(T(TextKey.Vertices), report.VertexCount.ToString());
        DrawDetailRow(T(TextKey.Indices), report.IndexCount.ToString());
        DrawDetailRow(T(TextKey.Triangles), report.TriangleCount.ToString());
        DrawDetailRow(T(TextKey.LodCount), report.MaximumLodCount.ToString());
        DrawDetailRow(T(TextKey.Bounds),
            $"({bounds.Min.X:F2}, {bounds.Min.Y:F2}, {bounds.Min.Z:F2}) - ({bounds.Max.X:F2}, {bounds.Max.Y:F2}, {bounds.Max.Z:F2})");
        if (report.AutoFrame is { } autoFrame)
            DrawDetailRow(T(TextKey.AutoFrameDistance), autoFrame.Distance.ToString("F2"));
        ImGui.EndTable();
    }

    private string GetPreviewReadinessName(ModelPreviewReadiness readiness)
        => T(readiness switch
        {
            ModelPreviewReadiness.HumanDataReady => TextKey.HumanDataReady,
            ModelPreviewReadiness.AssetsComplete => TextKey.AssetsComplete,
            ModelPreviewReadiness.AssetsPartial => TextKey.AssetsPartial,
            ModelPreviewReadiness.AssetsMissing => TextKey.AssetsMissing,
            _ => TextKey.InvalidModelData,
        });

    private string GetPreviewAssetLabel(string label)
        => label switch
        {
            "Head" => T(TextKey.Head),
            "Body" => T(TextKey.Body),
            "Hands" => T(TextKey.Hands),
            "Legs" => T(TextKey.Legs),
            "Feet" => T(TextKey.Feet),
            "Skeleton" => T(TextKey.Skeleton),
            "Customize + Equipment" => T(TextKey.InMemoryAppearance),
            _ => label,
        };

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
        ImGui.TextUnformatted(T(TextKey.Category));
        ImGui.SetNextItemWidth(180.0f);
        var categoryNames = CategoryNames();
        ImGui.Combo("##model-category", ref selectedCategory, categoryNames, categoryNames.Length);

        ImGui.SameLine();
        ImGui.TextUnformatted(T(TextKey.Name));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Math.Max(180.0f, ImGui.GetContentRegionAvail().X * 0.45f));
        ImGui.InputTextWithHint("##model-name-filter", T(TextKey.SearchHint), ref modelNameFilter, 128);

        ImGui.SameLine();
        ImGui.TextUnformatted(T(TextKey.ModelId));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110.0f);
        ImGui.InputTextWithHint("##model-id-filter", "e.g. 123", ref modelIdFilter, 32);

        if ((ModelCategory)selectedCategory != ModelCategory.Human)
            return;

        ImGui.SetNextItemWidth(150.0f);
        if (ImGui.BeginCombo($"{T(TextKey.Race)}###model-race", GetRaceFilterName(selectedRace)))
        {
            for (var i = 0; i < HumanRaces.Length; ++i)
            {
                if (ImGui.Selectable($"{GetRaceFilterName(i)}###model-race-{i}", selectedRace == i))
                {
                    selectedRace = i;
                    if (!HumanTribeCatalog.IsValidForRace(HumanRaces[selectedRace], selectedTribe))
                        selectedTribe = 0;
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(170.0f);
        if (ImGui.BeginCombo($"{T(TextKey.Tribe)}###model-tribe", GetTribeFilterName(selectedTribe)))
        {
            if (ImGui.Selectable($"{T(TextKey.AnyTribe)}###model-tribe-0", selectedTribe == 0))
                selectedTribe = 0;
            foreach (var tribe in HumanTribeCatalog.GetTribes(HumanRaces[selectedRace]))
            {
                if (ImGui.Selectable($"{plugin.GetTribeName(tribe)}###model-tribe-{tribe}", selectedTribe == tribe))
                    selectedTribe = tribe;
            }
            ImGui.EndCombo();
        }

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150.0f);
        if (ImGui.BeginCombo($"{T(TextKey.Gender)}###model-gender", GetGenderFilterName(selectedGender)))
        {
            for (var i = 0; i < HumanGenders.Length; ++i)
            {
                if (ImGui.Selectable($"{GetGenderFilterName(i)}###model-gender-{i}", selectedGender == i))
                    selectedGender = i;
            }

            ImGui.EndCombo();
        }

        ImGui.Checkbox($"{T(TextKey.Adult)}###include-adult", ref includeAdultHumans);
        ImGui.SameLine();
        ImGui.Checkbox($"{T(TextKey.YoungNpc)}###include-young", ref includeYoungNpc);
    }

    private bool MatchesActorFilter(ActorEntry actor)
    {
        if (!string.IsNullOrWhiteSpace(actorFilter)
            && !plugin.ContainsGameText(actor.Name, actorFilter))
            return false;

        if (selectedActorType == 1 && actor.Kind != Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc)
            return false;
        if (selectedActorType == 2 && actor.Kind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc)
            return false;

        var race = HumanRaces[selectedActorRace];
        if (race != 0 && actor.Race != race)
            return false;

        var gender = HumanGenders[selectedActorGender];
        return gender == byte.MaxValue || actor.Gender == gender;
    }

    private bool MatchesModelFilter(ModelSearchEntry model)
    {
        var category = (ModelCategory)selectedCategory;
        if (model.Category != category)
            return false;

        if (!string.IsNullOrWhiteSpace(modelNameFilter)
            && !plugin.ContainsGameText(model.Name, modelNameFilter))
            return false;

        if (!string.IsNullOrWhiteSpace(modelIdFilter)
            && !model.ModelId.ToString().Contains(modelIdFilter, StringComparison.Ordinal))
            return false;

        if (category != ModelCategory.Human)
            return true;

        var race = HumanRaces[selectedRace];
        if (race != 0 && model.Race != race)
            return false;

        if (selectedTribe != 0 && model.Tribe != selectedTribe)
            return false;

        var gender = HumanGenders[selectedGender];
        if (gender != byte.MaxValue && model.Gender != gender)
            return false;

        return (includeAdultHumans && !model.IsYoungNpc)
            || (includeYoungNpc && model.IsYoungNpc);
    }

    private string GetGenderName(byte gender)
    {
        return gender switch
        {
            0 => T(TextKey.Male),
            1 => T(TextKey.Female),
            _ => T(TextKey.Unknown, gender),
        };
    }

    private string GetAgeName(byte bodyType)
    {
        return bodyType switch
        {
            (byte)NpcAge.Normal => T(TextKey.Adult),
            (byte)NpcAge.Old => T(TextKey.Old),
            (byte)NpcAge.Young => T(TextKey.YoungNpc),
            _ => T(TextKey.Unknown, bodyType),
        };
    }

    private string[] CategoryNames() => [T(TextKey.Human), T(TextKey.Demihuman), T(TextKey.Monster)];
    private string[] ActorTypeNames() => [T(TextKey.All), T(TextKey.Players), T(TextKey.Npcs)];
    private string GetRaceFilterName(int index) => HumanRaces[index] == 0 ? T(TextKey.AnyRace) : plugin.GetRaceName(HumanRaces[index]);
    private string GetTribeFilterName(uint tribe) => tribe == 0 ? T(TextKey.AnyTribe) : plugin.GetTribeName(tribe);
    private string GetGenderFilterName(int index) => HumanGenders[index] == byte.MaxValue ? T(TextKey.AnyGender) : GetGenderName(HumanGenders[index]);
    private string T(TextKey key, params object[] arguments) => plugin.Localizer.Get(key, arguments);
}
