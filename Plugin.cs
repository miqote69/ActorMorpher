using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using LuminaSupplemental.Excel.Model;
using LuminaSupplemental.Excel.Services;
using ActorMorpher.Localization;
using ActorMorpher.Preview;

namespace ActorMorpher;

public sealed class Plugin : IDalamudPlugin
{
    public const string DisplayName = "Actor Morpher";

    private const string CommandName = "/actormorpher";
    private const string CommandAlias = "/amorph";

    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] private static IObjectTable ObjectTable { get; set; } = null!;
    [PluginService] private static IDataManager DataManager { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;
    [PluginService] private static IClientState ClientState { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IGameInteropProvider GameInteropProvider { get; set; } = null!;

    private readonly MainWindow mainWindow;
    private readonly WindowSystem windowSystem = new("ActorMorpher");
    private readonly DiagnosticLogRouter diagnosticRouter;
    private readonly DiagnosticController diagnosticController;
    private readonly ActorRegistry actorRegistry;
    private readonly ActorIdentityService actorIdentity = new();
    private readonly RedrawCoordinator redrawCoordinator;
    private readonly GPoseCoordinator gposeCoordinator;
    private readonly AppearanceApplyService appearanceApplyService;
    private readonly BulkOutfitService bulkOutfitService;
    private readonly IHumanModelClassifier humanModelClassifier;
    private readonly NativeDrawObjectInjector drawObjectInjector;
    private readonly HashSet<LogicalActorKey> combinedRestorePending = new();
    private readonly ModelPreviewController modelPreview;
    private readonly HumanPreviewDataBuilder humanPreviewDataBuilder = new();
    private readonly ModelPreviewSupportResolver modelPreviewSupportResolver;
    private readonly ModelPreviewAssetResolver modelPreviewAssetResolver;
    private readonly ModelPreviewGeometryInspector modelPreviewGeometryInspector;
    private readonly BulkOutfitTargetResolver bulkOutfitTargetResolver = new();
    private readonly Dictionary<ClientLanguage, IReadOnlyList<ModelSearchEntry>> modelSearchCaches = new();
    private readonly Dictionary<ClientLanguage, IReadOnlyDictionary<uint, string>> equipmentNameCaches = new();
    private readonly Dictionary<(uint RowId, ModelCategory Category, ModelSource Source, uint SourceId), ModelPreviewAssetReport> previewAssetCaches = new();
    private readonly Dictionary<(uint RowId, ModelCategory Category, ModelSource Source, uint SourceId), ModelPreviewSupport> previewSupportCaches = new();
    private readonly Dictionary<(uint RowId, ModelCategory Category, ModelSource Source, uint SourceId), ModelPreviewGeometryReport> previewGeometryCaches = new();

    public Configuration Configuration { get; }
    public Localizer Localizer { get; }
    public ClientLanguage GameLanguage => ClientState.ClientLanguage;
    public DiagnosticController Diagnostics => diagnosticController;

    public static string DisplayVersion =>
        typeof(Plugin).Assembly
            .GetCustomAttributes(false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()
            ?.InformationalVersion
            .Split('+')[0]
        ?? typeof(Plugin).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    public Plugin()
    {
        var isDev = PluginInterface.IsDev;
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? Configuration.Create(isDev);
        Configuration.MigrateAndValidate(isDev);
        PluginInterface.SavePluginConfig(Configuration);
        Localizer = new Localizer(Configuration, ClientState);
        diagnosticRouter = new DiagnosticLogRouter(
            PluginInterface.ConfigDirectory.FullName,
            Path.GetDirectoryName(typeof(Plugin).Assembly.Location),
            Log);
        diagnosticController = new DiagnosticController(
            diagnosticRouter,
            Configuration,
            () => PluginInterface.SavePluginConfig(Configuration),
            Log,
            ClientState,
            isDev);
        diagnosticController.Start();
        humanModelClassifier = new HumanModelClassifier(DataManager);
        modelPreviewAssetResolver = new ModelPreviewAssetResolver(DataManager.FileExists, humanPreviewDataBuilder);
        modelPreviewSupportResolver = new ModelPreviewSupportResolver(humanPreviewDataBuilder);
        modelPreviewGeometryInspector = new ModelPreviewGeometryInspector(new LuminaModelGeometrySource(DataManager).Load);
        actorRegistry = new ActorRegistry(ObjectTable, ClientState, Framework, humanModelClassifier, diagnosticRouter);
        actorIdentity = new ActorIdentityService(diagnosticRouter);
        var clientContext = new DalamudClientContext(ClientState);
        modelPreview = new ModelPreviewController(
            Framework,
            new UnavailableModelPreviewBackend(),
            clientContext,
            diagnosticRouter);
        var actorResolver = new RegistryActorResolver(actorRegistry, clientContext);
        var appearanceMemory = new NativeAppearanceMemory(ObjectTable, humanModelClassifier, diagnosticRouter);
        drawObjectInjector = new NativeDrawObjectInjector(GameInteropProvider, diagnosticRouter);
        redrawCoordinator = new RedrawCoordinator(
            Framework,
            actorResolver,
            appearanceMemory,
            new NativeRedrawBackend(ObjectTable, drawObjectInjector),
            clientContext,
            diagnosticRouter);
        gposeCoordinator = new GPoseCoordinator(Framework, ClientState, actorRegistry, diagnosticRouter);
        appearanceApplyService = new AppearanceApplyService(
            Framework,
            actorResolver,
            appearanceMemory,
            clientContext,
            redrawCoordinator,
            new AppearanceOverrideStore(),
            diagnosticRouter);
        bulkOutfitService = new BulkOutfitService(
            Framework,
            actorResolver,
            new NativeOutfitMemory(ObjectTable, humanModelClassifier, diagnosticRouter),
            clientContext,
            new OutfitOverrideStore(),
            diagnosticRouter);
        gposeCoordinator.MappingsReady += OnRepresentationContextChanged;
        gposeCoordinator.Exited += OnRepresentationContextChanged;
        appearanceApplyService.OperationCompleted += OnAppearanceOperationCompleted;
        bulkOutfitTargetResolver = new BulkOutfitTargetResolver(diagnosticRouter, () => ClientState.ClientLanguage);
        mainWindow = new MainWindow(this);
        windowSystem.AddWindow(mainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Actor Morpher.",
        });
        CommandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Actor Morpher.",
        });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(CommandAlias);

        windowSystem.RemoveAllWindows();
        mainWindow.Dispose();
        modelPreview.Dispose();
        gposeCoordinator.MappingsReady -= OnRepresentationContextChanged;
        gposeCoordinator.Exited -= OnRepresentationContextChanged;
        appearanceApplyService.OperationCompleted -= OnAppearanceOperationCompleted;
        bulkOutfitService.Dispose();
        appearanceApplyService.Dispose();
        gposeCoordinator.Dispose();
        redrawCoordinator.Dispose();
        drawObjectInjector.Dispose();
        actorRegistry.Dispose();
        diagnosticController.Dispose();
    }

    public void Save()
    {
        Configuration.MigrateAndValidate(PluginInterface.IsDev);
        PluginInterface.SavePluginConfig(Configuration);
        diagnosticRouter.Write(new DiagnosticLogEntry
        {
            EventId = DiagnosticEventIds.ConfigurationSaved,
            Category = DiagnosticCategory.Configuration,
            Message = "Configuration saved.",
        });
    }

    public void ToggleMainUi()
    {
        mainWindow.Toggle();
        diagnosticRouter.Write(new DiagnosticLogEntry
        {
            EventId = DiagnosticEventIds.UserActionRequested,
            Category = DiagnosticCategory.UserAction,
            Message = "Actor Morpher UI toggled.",
            Properties = new Dictionary<string, object?> { ["isOpen"] = mainWindow.IsOpen },
        });
    }

    private void OnCommand(string command, string args)
    {
        ToggleMainUi();
    }

    public IReadOnlyList<ActorEntry> GetVisibleActors()
        => actorRegistry.Entries;

    public bool TryResolveActor(LogicalActorKey key, out ActorEntry actor)
        => actorIdentity.TryResolve(actorRegistry, key, out actor);

    public BulkOutfitPreview GetBulkOutfitPreview(BulkOutfitSettings settings)
        => bulkOutfitTargetResolver.Resolve(actorRegistry.Entries, settings);

    public bool RefreshSourceOutfit(out string message)
    {
        var local = actorRegistry.Entries.FirstOrDefault(static actor => actor.IsLocalPlayer);
        if (local is null)
        {
            message = "Local player is unavailable.";
            return false;
        }
        return bulkOutfitService.RefreshSource(local.Key, out message);
    }

    public bool StartBulkOutfit(BulkOutfitPreview preview, out string message)
    {
        if (!RefreshSourceOutfit(out message))
        {
            LogBulkTargetPreview(preview, BulkOperationType.ApplyOutfit, false);
            return false;
        }
        var started = bulkOutfitService.StartApply(preview.EligibleTargets, out message);
        LogBulkTargetPreview(preview, BulkOperationType.ApplyOutfit, started);
        return started;
    }

    public bool StartUnequipAll(BulkOutfitPreview preview, out string message)
    {
        var started = bulkOutfitService.StartUnequip(preview.EligibleTargets, out message);
        LogBulkTargetPreview(preview, BulkOperationType.UnequipAll, started);
        return started;
    }

    public bool StartRestoreModifiedActors(out string message)
        => bulkOutfitService.StartRestore(out message);

    public void CancelBulkOperation()
        => bulkOutfitService.Cancel();

    public OutfitData? SourceOutfit => bulkOutfitService.SourceOutfit;
    public bool CanUseLocalPlayerAsOutfitSource
        => actorRegistry.Entries.FirstOrDefault(static actor => actor.IsLocalPlayer)?.Current.Race is not null;
    public BulkOperation? CurrentBulkOperation => bulkOutfitService.CurrentOperation;
    public string BulkOutfitStatus => bulkOutfitService.LastStatus;
    public int ModifiedOutfitActorCount => bulkOutfitService.ModifiedActorCount;

    private void LogBulkTargetPreview(BulkOutfitPreview preview, BulkOperationType type, bool started)
        => diagnosticRouter.Write(new DiagnosticLogEntry
        {
            EventId = DiagnosticEventIds.BulkTargetResolved,
            Category = DiagnosticCategory.BulkOutfit,
            Message = "Bulk Outfit target and exclusion filters resolved.",
            Outcome = started ? "Accepted" : "Rejected",
            Properties = new Dictionary<string, object?>
            {
                ["type"] = type,
                ["matchingLogicalActors"] = preview.MatchingLogicalActors,
                ["excludedLogicalActors"] = preview.ExcludedLogicalActors,
                ["eligibleHumanActors"] = preview.EligibleHumanActors,
                ["skippedNonHumanActors"] = preview.SkippedNonHumanActors,
                ["unavailableActors"] = preview.UnavailableActors,
            },
        });

    public IReadOnlyList<ModelSearchEntry> GetModelSearchEntries()
    {
        var language = ClientState.ClientLanguage;
        if (modelSearchCaches.TryGetValue(language, out var cache))
            return cache;

        using var operation = diagnosticRouter.BeginOperation(
            DiagnosticCategory.ModelSearch,
            DiagnosticEventIds.UserActionRequested,
            "BuildModelSearchCache");
        try
        {
            operation.SetPhase("LoadSheets");
            cache = BuildModelSearchEntries(language);
            modelSearchCaches[language] = cache;
            operation.Complete("Success", new Dictionary<string, object?>
            {
                ["resultCount"] = cache.Count,
                ["humanCount"] = cache.Count(entry => entry.Category == ModelCategory.Human),
                ["demihumanCount"] = cache.Count(entry => entry.Category == ModelCategory.Demihuman),
                ["monsterCount"] = cache.Count(entry => entry.Category == ModelCategory.Monster),
                ["language"] = language,
            });
        }
        catch (Exception ex)
        {
            operation.Fail(ex, "Model search cache build failed.");
            Log.Error(ex, "Failed to load model search data.");
            cache = Array.Empty<ModelSearchEntry>();
            modelSearchCaches[language] = cache;
        }

        return cache;
    }

    public IReadOnlyList<EquipmentDisplayEntry> GetHumanEquipment(ModelSearchEntry model)
    {
        if (model.HumanAppearance is not { } appearance)
            return Array.Empty<EquipmentDisplayEntry>();
        var names = GetEquipmentNames(ClientState.ClientLanguage);
        return appearance.Equipment.Select((packed, index) =>
        {
            var modelKey = checked((uint)(packed & 0xFFFFFF));
            var set = checked((ushort)(packed & 0xFFFF));
            var variant = checked((byte)((packed >> 16) & 0xFF));
            return new EquipmentDisplayEntry((OutfitSlot)index, set, variant,
                modelKey == 0 ? string.Empty : names.GetValueOrDefault(modelKey, string.Empty));
        }).ToArray();
    }

    public string GetRaceName(uint race)
    {
        if (race == 0)
            return Localizer[TextKey.AnyRace];
        var sheet = DataManager.GetExcelSheet<Race>(ClientState.ClientLanguage);
        return sheet.TryGetRow(race, out var row) && !row.Masculine.IsEmpty
            ? row.Masculine.ToString()
            : Localizer.Get(TextKey.Unknown, race);
    }

    public string GetTribeName(uint tribe)
    {
        if (tribe == 0)
            return Localizer[TextKey.AnyTribe];
        var sheet = DataManager.GetExcelSheet<Tribe>(ClientState.ClientLanguage);
        return sheet.TryGetRow(tribe, out var row) && !row.Masculine.IsEmpty
            ? row.Masculine.ToString()
            : Localizer.Get(TextKey.Unknown, tribe);
    }

    public bool ContainsGameText(string value, string search)
        => GameTextComparison.Contains(value, search, ClientState.ClientLanguage);

    private IReadOnlyDictionary<uint, string> GetEquipmentNames(ClientLanguage language)
    {
        if (equipmentNameCaches.TryGetValue(language, out var cache))
            return cache;
        cache = DataManager.GetExcelSheet<Item>(language)
            .Where(static item => !item.Name.IsEmpty && item.ModelMain != 0)
            .GroupBy(static item => checked((uint)(item.ModelMain & 0xFFFFFF)))
            .ToDictionary(
                static group => group.Key,
                group => string.Join(" / ", group.Select(item => item.Name.ToString()).Distinct(GameTextComparison.GetComparer(language)).Take(3)));
        equipmentNameCaches[language] = cache;
        return cache;
    }

    public bool TryApplyModelToLocalPlayer(ModelSearchEntry model, out string message)
    {
        var local = actorRegistry.Entries.FirstOrDefault(static actor => actor.IsLocalPlayer);
        if (local is null)
        {
            message = "Local player is not available.";
            return false;
        }
        return TryApplyModel(local.Key, model, out message);
    }

    public bool TryApplyModel(LogicalActorKey actor, ModelSearchEntry model, out string message)
        => model.ModelAppearance is { } appearance
            ? appearanceApplyService.TryApply(actor, appearance, out message)
            : FailUnsupported(out message);

    public bool TryRestoreActor(LogicalActorKey actor, out string message)
    {
        if (appearanceApplyService.Store.TryGet(actor, out _))
        {
            combinedRestorePending.Add(actor);
            if (appearanceApplyService.TryRestore(actor, out message))
                return true;
            combinedRestorePending.Remove(actor);
            return false;
        }

        return bulkOutfitService.StartRestore(actor, out message);
    }

    public bool HasAppearanceOverride(LogicalActorKey actor)
        => appearanceApplyService.Store.TryGet(actor, out _);

    public bool TryGetAppearanceOverride(LogicalActorKey actor, out AppearanceOverrideState state)
        => appearanceApplyService.Store.TryGet(actor, out state!);

    public bool HasOutfitOverride(LogicalActorKey actor)
        => bulkOutfitService.Store.TryGet(actor, out _);

    public bool IsAppearancePending(LogicalActorKey actor)
        => appearanceApplyService.IsPending(actor);

    public bool IsLocalPlayerAppearancePending()
        => actorRegistry.Entries.FirstOrDefault(static actor => actor.IsLocalPlayer) is { } local
        && appearanceApplyService.IsPending(local.Key);

    public string AppearanceStatus => appearanceApplyService.LastStatus;
    public ModelPreviewSnapshot ModelPreview => modelPreview.Snapshot;
    public void SelectPreviewModel(ModelSearchEntry? model) => modelPreview.Select(model);
    public void SetModelPreviewActive(bool active) => modelPreview.SetActive(active);
    public void ResetModelPreviewCamera() => modelPreview.ResetCamera();
    public ModelPreviewAssetReport GetModelPreviewAssets(ModelSearchEntry model)
    {
        var key = PreviewCacheKey(model);
        if (!previewAssetCaches.TryGetValue(key, out var report))
        {
            report = modelPreviewAssetResolver.Resolve(model);
            previewAssetCaches.Add(key, report);
        }
        return report;
    }

    public ModelPreviewSupport GetModelPreviewSupport(ModelSearchEntry model)
    {
        var key = PreviewCacheKey(model);
        if (!previewSupportCaches.TryGetValue(key, out var support))
        {
            support = modelPreviewSupportResolver.Resolve(model, GetModelPreviewAssets(model));
            previewSupportCaches.Add(key, support);
        }
        return support;
    }

    public ModelPreviewGeometryReport GetModelPreviewGeometry(ModelSearchEntry model)
    {
        var key = PreviewCacheKey(model);
        if (previewGeometryCaches.TryGetValue(key, out var report))
            return report;
        report = modelPreviewGeometryInspector.Inspect(GetModelPreviewAssets(model));
        previewGeometryCaches.Add(key, report);
        diagnosticRouter.Write(new DiagnosticLogEntry
        {
            EventId = DiagnosticEventIds.PreviewGeometryInspected,
            Category = DiagnosticCategory.ModelSearch,
            Message = "Model preview geometry inspected.",
            Outcome = report.State.ToString(),
            Properties = new Dictionary<string, object?>
            {
                ["modelCharaId"] = model.ModelId,
                ["category"] = model.Category,
                ["readyParts"] = report.ReadyPartCount,
                ["failedParts"] = report.FailedPartCount,
                ["meshCount"] = report.MeshCount,
                ["vertexCount"] = report.VertexCount,
                ["indexCount"] = report.IndexCount,
                ["lodCount"] = report.MaximumLodCount,
                ["boundsMin"] = report.Bounds is { } bounds
                    ? new[] { bounds.Min.X, bounds.Min.Y, bounds.Min.Z }
                    : null,
                ["boundsMax"] = report.Bounds is { } maxBounds
                    ? new[] { maxBounds.Max.X, maxBounds.Max.Y, maxBounds.Max.Z }
                    : null,
                ["autoFrameDistance"] = report.AutoFrame?.Distance,
            },
        });
        return report;
    }

    private static (uint RowId, ModelCategory Category, ModelSource Source, uint SourceId) PreviewCacheKey(ModelSearchEntry model)
        => (model.RowId, model.Category, model.Source, model.SourceId);

    private static bool FailUnsupported(out string message)
    {
        message = "The selected model does not have an applicable appearance payload.";
        return false;
    }

    private void OnAppearanceOperationCompleted(LogicalActorKey actor, uint modelCharaId, bool isRestore, bool succeeded)
    {
        if (!succeeded)
        {
            combinedRestorePending.Remove(actor);
            return;
        }
        if (isRestore && combinedRestorePending.Remove(actor))
        {
            // The appearance base snapshot already contains the true pre-morph equipment.
            // Applying a Bulk snapshot captured while morphed would mix NPC gear onto that body.
            bulkOutfitService.ForgetOverride(actor);
            return;
        }
        if (humanModelClassifier.IsHuman(modelCharaId))
            bulkOutfitService.Reapply(actor);
    }

    private void OnRepresentationContextChanged()
    {
        combinedRestorePending.Clear();
        var appearanceKeys = appearanceApplyService.Store.States.Keys.ToArray();
        appearanceApplyService.ReapplyAll();
        bulkOutfitService.ReapplyWithoutAppearanceOverrides(appearanceKeys);
    }

    public static string GetApplyUnavailableReason(ModelSearchEntry model)
        => model.Completeness switch
        {
            AppearanceCompleteness.Unsupported
                => "This model does not have a supported standalone appearance representation.",
            AppearanceCompleteness.ModelOnly when model.Category == ModelCategory.Monster
                => "Only the Monster model ID is available; the required redraw behavior has not been verified.",
            AppearanceCompleteness.ModelOnly when model.Category == ModelCategory.Demihuman
                => "This Demihuman is missing complete customize or equipment part data.",
            _ when model.Category == ModelCategory.Human && model.HumanAppearance is null
                => "This Human NPC does not have complete appearance data.",
            _ => "Standalone appearance apply is not available in this build.",
        };

    public static bool CanApplyModel(ModelSearchEntry model)
        => model.ModelAppearance is { } appearance
        && appearance.Category switch
        {
            ModelCategory.Human or ModelCategory.Demihuman => appearance.Completeness == AppearanceCompleteness.Complete,
            ModelCategory.Monster => appearance.Completeness is AppearanceCompleteness.Complete or AppearanceCompleteness.ModelOnly,
            _ => false,
        };

    private IReadOnlyList<ModelSearchEntry> BuildModelSearchEntries(ClientLanguage language)
    {
        var modelChara = DataManager.GetExcelSheet<ModelChara>(language)
            .ToDictionary(static row => row.RowId);
        var eNpcResidents = DataManager.GetExcelSheet<ENpcResident>(language);
        var bNpcNames = DataManager.GetExcelSheet<BNpcName>(language);
        var bNpcNameLinks = LoadBattleNpcNameLinks();
        var entries = new List<ModelSearchEntry>(modelChara.Count);

        foreach (var row in DataManager.GetExcelSheet<ENpcBase>(language))
        {
            var modelId = row.ModelChara.RowId;
            if (!modelChara.TryGetValue(modelId, out var model))
                continue;

            var name = eNpcResidents.TryGetRow(row.RowId, out var resident)
                ? resident.Singular.ToString()
                : string.Empty;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var appearance = model.Type == 1 ? CreateHumanAppearance(row) : null;
            if (model.Type == 1 && appearance is null)
                continue;
            var modelAppearance = model.Type switch
            {
                1 when appearance is not null => CreateHumanModelAppearance(modelId, row.RowId, appearance),
                2 => CreateDemihumanAppearance(row),
                3 => CreateMonsterAppearance(modelId, row.RowId),
                _ => null,
            };

            entries.Add(CreateSearchEntry(
                model,
                ModelSource.EventNpc,
                row.RowId,
                name,
                (uint)row.Race.RowId,
                (byte)row.Gender,
                row.BodyType,
                appearance,
                modelAppearance));
        }

        foreach (var row in DataManager.GetExcelSheet<BNpcBase>(language))
        {
            var modelId = row.ModelChara.RowId;
            if (!modelChara.TryGetValue(modelId, out var model))
                continue;

            var customize = row.BNpcCustomize.ValueNullable;
            var gender = (byte)(customize?.Gender ?? 0);
            var bodyType = customize?.BodyType ?? 0;
            var names = bNpcNameLinks.TryGetValue(row.RowId, out var nameIds)
                ? nameIds.Select(id => bNpcNames.TryGetRow(id, out var nameRow) ? nameRow.Singular.ToString() : string.Empty)
                    .Where(static name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(GameTextComparison.GetComparer(language))
                    .ToArray()
                : Array.Empty<string>();

            var npcEquip = row.NpcEquip.ValueNullable;
            var appearance = model.Type == 1 && customize is { } humanCustomize && npcEquip is { } humanEquip
                ? CreateHumanAppearance(humanCustomize, humanEquip)
                : null;
            var modelAppearance = model.Type switch
            {
                1 when appearance is not null => CreateHumanModelAppearance(modelId, row.RowId, appearance),
                2 when customize is { } demihumanCustomize && npcEquip is { } demihumanEquip
                    => CreateDemihumanAppearance(row.RowId, modelId, demihumanCustomize, demihumanEquip),
                3 => CreateMonsterAppearance(modelId, row.RowId),
                _ => null,
            };
            if (model.Type == 1 && (appearance is null || names.Length == 0))
                continue;

            if (names.Length == 0)
                names = [CreateBattleNpcFallbackName(row.RowId, gender, bodyType)];

            foreach (var name in names)
            {
                entries.Add(CreateSearchEntry(
                    model,
                    ModelSource.BattleNpc,
                    row.RowId,
                    name,
                    customize?.Race.RowId ?? 0,
                    gender,
                    bodyType,
                    appearance,
                    modelAppearance));
            }
        }

        var referencedModelIds = entries.Select(static entry => entry.ModelId).ToHashSet();
        foreach (var model in modelChara.Values)
        {
            if (model.Type == 1 || model.RowId == 0)
                continue;

            if (!referencedModelIds.Add(model.RowId))
                continue;

            entries.Add(CreateSearchEntry(
                model,
                ModelSource.ModelChara,
                model.RowId,
                $"ModelChara {model.RowId}",
                0,
                0,
                0,
                modelAppearance: model.Type == 3 ? CreateMonsterAppearance(model.RowId, model.RowId) : null));
        }

        return entries
            .DistinctBy(static entry => entry switch
            {
                { HumanAppearance: { } human } => $"{entry.Name}\u001f{human.Signature}",
                { ModelAppearance: { } model } => $"{entry.Name}\u001f{CreateAppearanceSignature(model)}",
                _ => $"{entry.Name}\u001f{entry.ModelId}\u001f{entry.Source}\u001f{entry.SourceId}",
            })
            .OrderBy(static row => row.Category)
            .ThenBy(static row => row.Name, GameTextComparison.GetComparer(language))
            .ThenBy(static row => row.ModelId)
            .ToArray();
    }

    private static string CreateAppearanceSignature(AppearanceData appearance)
        => string.Join(
            ':',
            appearance.ModelCharaId,
            appearance.Category,
            Convert.ToHexString(appearance.Customize.AsSpan()),
            string.Join(',', appearance.Equipment.Select(static value => value.ToString("X16"))));

    private static ModelSearchEntry CreateSearchEntry(
        ModelChara model,
        ModelSource source,
        uint sourceId,
        string name,
        uint race,
        byte gender,
        byte bodyType,
        HumanAppearance? humanAppearance = null,
        AppearanceData? modelAppearance = null)
    {
        return new ModelSearchEntry(
            model.RowId,
            model.Type switch
            {
                1 => ModelCategory.Human,
                2 => ModelCategory.Demihuman,
                3 => ModelCategory.Monster,
                _ => ModelCategory.Other,
            },
            source,
            sourceId,
            name,
            model.Type,
            model.Model,
            model.Base,
            model.Variant,
            race,
            gender,
            bodyType,
            humanAppearance,
            model.Type switch
            {
                _ when modelAppearance is not null => modelAppearance.Completeness,
                _ => AppearanceCompleteness.Unsupported,
            },
            modelAppearance);
    }

    private static AppearanceData CreateHumanModelAppearance(uint modelCharaId, uint sourceRowId, HumanAppearance appearance)
        => AppearanceData.Create(
            modelCharaId,
            ModelCategory.Human,
            sourceRowId,
            AppearanceCompleteness.Complete,
            appearance.Customize,
            appearance.Equipment);

    private static AppearanceData CreateMonsterAppearance(uint modelCharaId, uint sourceRowId)
        => AppearanceData.Create(
            modelCharaId,
            ModelCategory.Monster,
            sourceRowId,
            AppearanceCompleteness.ModelOnly,
            Array.Empty<byte>(),
            Array.Empty<ulong>());

    private static HumanAppearance? CreateHumanAppearance(ENpcBase row)
    {
        var customize = new byte[]
        {
            (byte)row.Race.RowId, (byte)row.Gender, row.BodyType, row.Height, (byte)row.Tribe.RowId,
            row.Face, row.HairStyle, row.HairHighlight, row.SkinColor, row.EyeHeterochromia,
            row.HairColor, row.HairHighlightColor, row.FacialFeature, row.FacialFeatureColor,
            row.Eyebrows, row.EyeColor, row.EyeShape, row.Nose, row.Jaw, row.Mouth,
            row.LipColor, row.BustOrTone1, row.ExtraFeature1, row.ExtraFeature2OrBust,
            row.FacePaint, row.FacePaintColor,
        };
        if (!IsValidHumanCustomize(customize))
            return null;

        var equipment = row.NpcEquip.RowId is not 0
            && row.NpcEquip.ValueNullable is { } npcEquip
            && row is { ModelBody: 0, ModelLegs: 0 }
                ? CreateEquipment(npcEquip)
                : CreateEquipment(row);
        var mainhand = row.NpcEquip.RowId is not 0
            && row.NpcEquip.ValueNullable is { } weaponEquip
            && row is { ModelBody: 0, ModelLegs: 0 }
                ? PackWeapon(weaponEquip.ModelMainHand, weaponEquip.DyeMainHand.RowId, weaponEquip.Dye2MainHand.RowId)
                : PackWeapon(row.ModelMainHand, row.DyeMainHand.RowId, row.Dye2MainHand.RowId);
        var offhand = row.NpcEquip.RowId is not 0
            && row.NpcEquip.ValueNullable is { } offhandEquip
            && row is { ModelBody: 0, ModelLegs: 0 }
                ? PackWeapon(offhandEquip.ModelOffHand, offhandEquip.DyeOffHand.RowId, offhandEquip.Dye2OffHand.RowId)
                : PackWeapon(row.ModelOffHand, row.DyeOffHand.RowId, row.Dye2OffHand.RowId);

        return new HumanAppearance(customize, equipment, mainhand, offhand, row.Visor);
    }

    private static AppearanceData CreateDemihumanAppearance(ENpcBase row)
    {
        var customize = new byte[]
        {
            (byte)row.Race.RowId, (byte)row.Gender, row.BodyType, row.Height, (byte)row.Tribe.RowId,
            row.Face, row.HairStyle, row.HairHighlight, row.SkinColor, row.EyeHeterochromia,
            row.HairColor, row.HairHighlightColor, row.FacialFeature, row.FacialFeatureColor,
            row.Eyebrows, row.EyeColor, row.EyeShape, row.Nose, row.Jaw, row.Mouth,
            row.LipColor, row.BustOrTone1, row.ExtraFeature1, row.ExtraFeature2OrBust,
            row.FacePaint, row.FacePaintColor,
        };
        var equipment = row.NpcEquip.RowId is not 0
            && row.NpcEquip.ValueNullable is { } npcEquip
            && row is { ModelBody: 0, ModelLegs: 0 }
                ? CreateEquipment(npcEquip)
                : CreateEquipment(row);

        return AppearanceData.Create(
            row.ModelChara.RowId,
            ModelCategory.Demihuman,
            row.RowId,
            AppearanceCompleteness.Complete,
            customize,
            equipment);
    }

    private static AppearanceData CreateDemihumanAppearance(
        uint sourceRowId,
        uint modelCharaId,
        BNpcCustomize customizeRow,
        NpcEquip equip)
    {
        var customize = new byte[]
        {
            (byte)customizeRow.Race.RowId, (byte)customizeRow.Gender, customizeRow.BodyType, customizeRow.Height,
            (byte)customizeRow.Tribe.RowId, customizeRow.Face, customizeRow.HairStyle, customizeRow.HairHighlight,
            customizeRow.SkinColor, customizeRow.EyeHeterochromia, customizeRow.HairColor, customizeRow.HairHighlightColor,
            customizeRow.FacialFeature, customizeRow.FacialFeatureColor, customizeRow.Eyebrows, customizeRow.EyeColor,
            customizeRow.EyeShape, customizeRow.Nose, customizeRow.Jaw, customizeRow.Mouth, customizeRow.LipColor,
            customizeRow.BustOrTone1, customizeRow.ExtraFeature1, customizeRow.ExtraFeature2OrBust,
            customizeRow.FacePaint, customizeRow.FacePaintColor,
        };

        return AppearanceData.Create(
            modelCharaId,
            ModelCategory.Demihuman,
            sourceRowId,
            AppearanceCompleteness.Complete,
            customize,
            CreateEquipment(equip));
    }

    private static HumanAppearance? CreateHumanAppearance(BNpcCustomize row, NpcEquip equip)
    {
        var customize = new byte[]
        {
            (byte)row.Race.RowId, (byte)row.Gender, row.BodyType, row.Height, (byte)row.Tribe.RowId,
            row.Face, row.HairStyle, row.HairHighlight, row.SkinColor, row.EyeHeterochromia,
            row.HairColor, row.HairHighlightColor, row.FacialFeature, row.FacialFeatureColor,
            row.Eyebrows, row.EyeColor, row.EyeShape, row.Nose, row.Jaw, row.Mouth,
            row.LipColor, row.BustOrTone1, row.ExtraFeature1, row.ExtraFeature2OrBust,
            row.FacePaint, row.FacePaintColor,
        };
        if (!IsValidHumanCustomize(customize))
            return null;

        return new HumanAppearance(
            customize,
            CreateEquipment(equip),
            PackWeapon(equip.ModelMainHand, equip.DyeMainHand.RowId, equip.Dye2MainHand.RowId),
            PackWeapon(equip.ModelOffHand, equip.DyeOffHand.RowId, equip.Dye2OffHand.RowId),
            equip.Visor);
    }

    private static bool IsValidHumanCustomize(IReadOnlyList<byte> customize)
    {
        var race = customize[0];
        var gender = customize[1];
        var tribe = customize[4];
        return race is >= 1 and <= 8
            && gender is 0 or 1
            && tribe is >= 1 and <= 16
            && HumanTribeCatalog.IsValidForRace(race, tribe);
    }

    private static ulong[] CreateEquipment(ENpcBase row)
        =>
        [
            PackArmor(row.ModelHead, row.DyeHead.RowId, row.Dye2Head.RowId),
            PackArmor(row.ModelBody, row.DyeBody.RowId, row.Dye2Body.RowId),
            PackArmor(row.ModelHands, row.DyeHands.RowId, row.Dye2Hands.RowId),
            PackArmor(row.ModelLegs, row.DyeLegs.RowId, row.Dye2Legs.RowId),
            PackArmor(row.ModelFeet, row.DyeFeet.RowId, row.Dye2Feet.RowId),
            PackArmor(row.ModelEars, row.DyeEars.RowId, row.Dye2Ears.RowId),
            PackArmor(row.ModelNeck, row.DyeNeck.RowId, row.Dye2Neck.RowId),
            PackArmor(row.ModelWrists, row.DyeWrists.RowId, row.Dye2Wrists.RowId),
            PackArmor(row.ModelRightRing, row.DyeRightRing.RowId, row.Dye2RightRing.RowId),
            PackArmor(row.ModelLeftRing, row.DyeLeftRing.RowId, row.Dye2LeftRing.RowId),
        ];

    private static ulong[] CreateEquipment(NpcEquip row)
        =>
        [
            PackArmor(row.ModelHead, row.DyeHead.RowId, row.Dye2Head.RowId),
            PackArmor(row.ModelBody, row.DyeBody.RowId, row.Dye2Body.RowId),
            PackArmor(row.ModelHands, row.DyeHands.RowId, row.Dye2Hands.RowId),
            PackArmor(row.ModelLegs, row.DyeLegs.RowId, row.Dye2Legs.RowId),
            PackArmor(row.ModelFeet, row.DyeFeet.RowId, row.Dye2Feet.RowId),
            PackArmor(row.ModelEars, row.DyeEars.RowId, row.Dye2Ears.RowId),
            PackArmor(row.ModelNeck, row.DyeNeck.RowId, row.Dye2Neck.RowId),
            PackArmor(row.ModelWrists, row.DyeWrists.RowId, row.Dye2Wrists.RowId),
            PackArmor(row.ModelRightRing, row.DyeRightRing.RowId, row.Dye2RightRing.RowId),
            PackArmor(row.ModelLeftRing, row.DyeLeftRing.RowId, row.Dye2LeftRing.RowId),
        ];

    private static ulong PackArmor(ulong model, uint stain1, uint stain2)
        => model | ((ulong)stain1 << 24) | ((ulong)stain2 << 32);

    private static ulong PackWeapon(ulong model, uint stain1, uint stain2)
        => model | ((ulong)stain1 << 48) | ((ulong)stain2 << 56);

    private static string CreateBattleNpcFallbackName(uint rowId, byte gender, byte bodyType)
    {
        var description = (bodyType, gender) switch
        {
            ((byte)NpcAge.Young, 0) => "少年 / Young Boy",
            ((byte)NpcAge.Young, 1) => "少女 / Young Girl",
            ((byte)NpcAge.Old, 0) => "老人 / Old Man",
            ((byte)NpcAge.Old, 1) => "老婆 / Old Woman",
            _ => "Battle NPC",
        };

        return $"{description} {rowId}";
    }

    private static IReadOnlyDictionary<uint, uint[]> LoadBattleNpcNameLinks()
    {
        var links = CsvLoader.LoadResource<BNpcLink>(
            CsvLoader.BNpcLinkResourceName,
            true,
            out var failedLines,
            out var exceptions);

        if (failedLines.Count > 0 || exceptions.Count > 0)
            Log.Warning("Failed to read {FailedCount} battle NPC name links.", failedLines.Count);

        return links
            .GroupBy(static link => link.BNpcBaseId)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static link => link.BNpcNameId).Distinct().ToArray());
    }

}

public enum NpcAge : byte
{
    Normal = 1,
    Old = 3,
    Young = 4,
}

public enum ModelCategory
{
    Human,
    Demihuman,
    Monster,
    Other,
}

public enum ModelSource
{
    ModelChara,
    EventNpc,
    BattleNpc,
}

public sealed record HumanAppearance(
    byte[] Customize,
    ulong[] Equipment,
    ulong Mainhand,
    ulong Offhand,
    bool VisorToggled)
{
    public string Signature { get; } = string.Join(
        ':',
        Convert.ToHexString(Customize),
        string.Join(',', Equipment.Select(static value => value.ToString("X16"))),
        Mainhand.ToString("X16"),
        Offhand.ToString("X16"),
        VisorToggled ? "1" : "0");
}

public sealed record ModelSearchEntry(
    uint RowId,
    ModelCategory Category,
    ModelSource Source,
    uint SourceId,
    string Name,
    byte Type,
    ushort Model,
    ushort Base,
    byte Variant,
    uint Race,
    byte Gender,
    byte BodyType,
    HumanAppearance? HumanAppearance,
    AppearanceCompleteness Completeness,
    AppearanceData? ModelAppearance)
{
    public uint ModelId => RowId;

    public uint Tribe => Category == ModelCategory.Human
        && HumanAppearance is { Customize.Length: > 4 } appearance
            ? appearance.Customize[4]
            : 0U;

    public bool IsYoungNpc => Category == ModelCategory.Human && BodyType == (byte)NpcAge.Young;
}
