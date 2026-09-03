using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using LuminaSupplemental.Excel.Model;
using LuminaSupplemental.Excel.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
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
    [PluginService] private static ITargetManager TargetManager { get; set; } = null!;
    [PluginService] private static IDataManager DataManager { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;
    [PluginService] private static IClientState ClientState { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IGameInteropProvider GameInteropProvider { get; set; } = null!;
    [PluginService] private static ITextureProvider TextureProvider { get; set; } = null!;

    private readonly MainWindow mainWindow;
    private readonly WindowSystem windowSystem = new("ActorMorpher");
    private readonly DiagnosticLogRouter diagnosticRouter;
    private readonly DiagnosticController diagnosticController;
    private readonly ActorRegistry actorRegistry;
    private readonly IActorResolver actorResolver;
    private readonly ActorIdentityService actorIdentity = new();
    private readonly RedrawCoordinator redrawCoordinator;
    private readonly GPoseCoordinator gposeCoordinator;
    private readonly AppearanceApplyService appearanceApplyService;
    private readonly IAppearanceMemory appearanceMemory;
    private readonly NativeOutfitMemory outfitMemory;
    private readonly NativeEquipmentColors equipmentColors;
    private readonly BulkOutfitService bulkOutfitService;
    private readonly PinnedOutfitStore pinnedOutfitStore;
    private readonly IHumanModelClassifier humanModelClassifier;
    private readonly NativeDrawObjectInjector drawObjectInjector;
    private readonly NativeRedrawBackend redrawBackend;
    private readonly ModelPreviewController modelPreview;
    private readonly SoftwareModelPreviewBackend softwareModelPreviewBackend;
    private readonly HumanPreviewDataBuilder humanPreviewDataBuilder = new();
    private readonly ModelPreviewSupportResolver modelPreviewSupportResolver;
    private readonly ModelPreviewAssetResolver modelPreviewAssetResolver;
    private readonly ModelPreviewGeometryInspector modelPreviewGeometryInspector;
    private readonly LuminaModelGeometrySource modelPreviewGeometrySource;
    private readonly ModelPreviewTextureCache modelPreviewTextureCache;
    private readonly ActorAppearancePersistence appearancePersistence = new();
    private readonly NativeActorContinuity actorContinuity = new();
    private readonly CommandRegistrationLease primaryCommandRegistration;
    private readonly CommandRegistrationLease aliasCommandRegistration;
    private readonly BulkOutfitTargetResolver bulkOutfitTargetResolver = new();
    private readonly Dictionary<ClientLanguage, IReadOnlyList<ModelSearchEntry>> modelSearchCaches = new();
    private readonly Dictionary<ClientLanguage, IReadOnlyDictionary<(OutfitSlot Slot, uint ModelKey), EquipmentItemDisplay>> equipmentDisplayCaches = new();
    private readonly Dictionary<(ClientLanguage, int), EquipmentChoice[]> equipmentChoices = new();
    private (LogicalActorKey Actor, EquipmentChoiceKey Choice)? pendingEquipmentSelection;
    private readonly Dictionary<ClientLanguage, IReadOnlyDictionary<byte, StainDisplayEntry>> stainDisplayCaches = new();
    private readonly Dictionary<(uint RowId, ModelCategory Category, ModelSource Source, uint SourceId), ModelPreviewAssetReport> previewAssetCaches = new();
    private readonly Dictionary<(uint RowId, ModelCategory Category, ModelSource Source, uint SourceId), ModelPreviewSupport> previewSupportCaches = new();
    private readonly Dictionary<(uint RowId, ModelCategory Category, ModelSource Source, uint SourceId), ModelPreviewGeometryReport> previewGeometryCaches = new();
    private long nextPinnedOutfitScanTick;
    private uint pinnedOutfitTerritory;
    private LogicalActorKey? pinnedOutfitOperationActor;
    private OutfitData? pinnedOutfitOperationObservedState;
    private readonly Dictionary<LogicalActorKey, OutfitData> failedPinnedOutfitReapplyStates = new();
    private LogicalActorKey? pinnedAppearanceOperationActor;
    private AppearanceData? pinnedAppearanceOperationObservedState;
    private readonly Dictionary<LogicalActorKey, AppearanceData> failedPinnedAppearanceStates = new();
    private readonly HashSet<LogicalActorKey> pendingActorRestoreRedraws = new();
    private string restoreStatus = string.Empty;
    private ModelPreviewSelectionKey? previewTextureSelection;

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
        pinnedOutfitStore = new PinnedOutfitStore(
            Configuration,
            () => PluginInterface.SavePluginConfig(Configuration));
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
        modelPreviewGeometrySource = new LuminaModelGeometrySource(DataManager);
        modelPreviewAssetResolver = new ModelPreviewAssetResolver(
            DataManager.FileExists,
            humanPreviewDataBuilder,
            modelPreviewGeometrySource.CanDeform);
        modelPreviewSupportResolver = new ModelPreviewSupportResolver(
            humanPreviewDataBuilder,
            ModelPreviewBackendCapabilities.SoftwarePreview);
        modelPreviewGeometryInspector = new ModelPreviewGeometryInspector(modelPreviewGeometrySource.Load);
        modelPreviewTextureCache = new ModelPreviewTextureCache(
            TextureProvider,
            new ModelPreviewTextureSource(DataManager));
        actorRegistry = new ActorRegistry(
            ObjectTable,
            ClientState,
            Framework,
            humanModelClassifier,
            diagnosticRouter,
            appearancePersistence.GetRetainedAppearance,
            appearancePersistence,
            actorContinuity);
        actorIdentity = new ActorIdentityService(diagnosticRouter);
        var clientContext = new DalamudClientContext(ClientState);
        softwareModelPreviewBackend = new SoftwareModelPreviewBackend(
            GetModelPreviewAssets,
            modelPreviewGeometrySource.LoadCpuModel,
            modelPreviewGeometrySource.ShowsBackfaces,
            modelPreviewGeometrySource.IsBodySkin,
            modelPreviewGeometrySource.IsLowerBodyEquipment);
        modelPreview = new ModelPreviewController(
            Framework,
            softwareModelPreviewBackend,
            clientContext,
            diagnosticRouter);
        actorResolver = new RegistryActorResolver(actorRegistry, clientContext);
        drawObjectInjector = new NativeDrawObjectInjector(
            GameInteropProvider,
            ObjectTable,
            ClientState,
            diagnosticRouter,
            appearancePersistence,
            actorContinuity);
        actorRegistry.ResolveCopyActor = drawObjectInjector.GetCopyActor;
        appearanceMemory = new NativeAppearanceMemory(
            ObjectTable,
            humanModelClassifier,
            diagnosticRouter,
            Log);
        redrawBackend = new NativeRedrawBackend(ObjectTable, drawObjectInjector);
        redrawCoordinator = new RedrawCoordinator(
            Framework,
            actorResolver,
            redrawBackend,
            clientContext,
            diagnosticRouter);
        gposeCoordinator = new GPoseCoordinator(Framework, ClientState, actorRegistry, diagnosticRouter);
        appearanceApplyService = new AppearanceApplyService(
            Framework,
            actorResolver,
            clientContext,
            redrawCoordinator,
            diagnosticRouter);
        outfitMemory = new NativeOutfitMemory(ObjectTable, humanModelClassifier, diagnosticRouter);
        var facewearModels = new FacewearModelLookup(DataManager.GetExcelSheet<Glasses>()
            .Select(row => ((ushort)row.RowId, (ushort)(row.Model & 0xFFFF), (byte)((row.Model >> 16) & 0xFF))));
        outfitMemory.ResolveFacewear = facewearModels.Resolve;
        actorRegistry.ResolveFacewear = facewearModels.Resolve;
        drawObjectInjector.ResolveFacewear = facewearModels.Resolve;
        outfitMemory.GetColorOutfit = appearancePersistence.GetColorOutfit;
        outfitMemory.SetColorOutfit = appearancePersistence.SetColorOutfit;
        equipmentColors = new NativeEquipmentColors(GameInteropProvider, ObjectTable,
            drawObjectInjector.ResolveActor, appearancePersistence);
        actorRegistry.GetColorOutfit = outfitMemory.GetColorOutfit;
        bulkOutfitService = new BulkOutfitService(
            Framework,
            actorResolver,
            outfitMemory,
            clientContext,
            appearancePersistence.Outfits,
            diagnosticRouter);
        appearanceApplyService.OperationCompleted += OnAppearanceOperationCompleted;
        bulkOutfitService.ActorOperationCompleted += OnBulkOutfitActorOperationCompleted;
        bulkOutfitTargetResolver = new BulkOutfitTargetResolver(diagnosticRouter, () => ClientState.ClientLanguage);
        mainWindow = new MainWindow(this);
        windowSystem.AddWindow(mainWindow);

        primaryCommandRegistration = CreateCommandRegistration(CommandName);
        aliasCommandRegistration = CreateCommandRegistration(CommandAlias);
        EnsureCommandsRegistered();

        PluginInterface.UiBuilder.Draw += DrawUi;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;
        Framework.Update += OnPluginFrameworkUpdate;
    }

    public void Dispose()
    {
        Framework.Update -= OnPluginFrameworkUpdate;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;
        PluginInterface.UiBuilder.Draw -= DrawUi;
        primaryCommandRegistration.Dispose();
        aliasCommandRegistration.Dispose();

        windowSystem.RemoveAllWindows();
        mainWindow.Dispose();
        modelPreview.Dispose();
        modelPreviewTextureCache.Dispose();
        appearanceApplyService.OperationCompleted -= OnAppearanceOperationCompleted;
        bulkOutfitService.ActorOperationCompleted -= OnBulkOutfitActorOperationCompleted;
        bulkOutfitService.Dispose();
        appearanceApplyService.Dispose();
        gposeCoordinator.Dispose();
        redrawCoordinator.Dispose();
        equipmentColors.Dispose();
        drawObjectInjector.Dispose();
        actorRegistry.Dispose();
        diagnosticController.Dispose();
    }

    private void DrawUi()
    {
        windowSystem.Draw();
        // Draw outside the main WindowSystem window's temporary focus styling.
        mainWindow.DrawEquipmentPicker();
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

    public bool TryResolveActorRepresentation(
        LogicalActorKey key,
        out ActorEntry actor,
        out ActorSnapshot representation)
    {
        representation = null!;
        return TryResolveActor(key, out actor)
            && actorResolver.TryResolve(actor.Key, out representation);
    }

    public BulkOutfitPreview GetBulkOutfitPreview(BulkOutfitSettings settings)
        => bulkOutfitTargetResolver.Resolve(GetBulkOutfitActors(), settings, SelectBulkOutfitRepresentation);

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
        if (!CanStartBulkOutfitInCurrentContext(out message))
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
        if (!CanStartBulkOutfitInCurrentContext(out message))
        {
            LogBulkTargetPreview(preview, BulkOperationType.UnequipAll, false);
            return false;
        }
        var started = bulkOutfitService.StartUnequip(preview.EligibleTargets, out message);
        LogBulkTargetPreview(preview, BulkOperationType.UnequipAll, started);
        return started;
    }

    public bool StartRestoreModifiedActors(out string message)
    {
        if (!CanStartBulkOutfitInCurrentContext(out message))
            return false;
        var targets = GetRestorableModifiedOutfitActors();
        return bulkOutfitService.StartActorRestore(targets,
            actor => TryRestoreActor(actor, out _, restoreWithinBatch: true), out message);
    }

    public void CancelBulkOperation()
        => bulkOutfitService.Cancel();

    public OutfitData? SourceOutfit => bulkOutfitService.SourceOutfit;
    public bool SelectEquipment(EquipmentChoiceKey choice, LogicalActorKey? actor, out string message)
    {
        if (actor is not null && !CanStartBulkOutfitInCurrentContext(out message))
            return false;
        var started = bulkOutfitService.SelectEquipment(choice, actor, out message);
        if (started && actor is { } selectedActor)
            pendingEquipmentSelection = (selectedActor, choice);
        return started;
    }

    public EquipmentChoice[] GetEquipmentChoices(int slot)
    {
        var cacheKey = (ClientState.ClientLanguage, slot);
        if (equipmentChoices.TryGetValue(cacheKey, out var cached))
            return cached;
        IEnumerable<EquipmentChoice> choices;
        if (slot == 10)
            choices = DataManager.GetExcelSheet<Glasses>(ClientState.ClientLanguage)
                .Where(row => row.RowId != 0 && !row.Name.IsEmpty)
                .Select(row => new EquipmentChoice(new(slot, (ushort)(row.Model & 0xFFFF),
                    (byte)((row.Model >> 16) & 0xFF), (ushort)row.RowId), row.Name.ToString(), (uint)row.Icon));
        else
            choices = GetEquipmentDisplays(ClientState.ClientLanguage)
                .Where(entry => (int)entry.Key.Slot == slot)
                .Select(entry => new EquipmentChoice(new(slot, (ushort)(entry.Key.ModelKey & 0xFFFF),
                    (byte)(entry.Key.ModelKey >> 16)), entry.Value.Name, entry.Value.IconId));
        cached = choices.OrderBy(choice => choice.Name, GameTextComparison.GetComparer(ClientState.ClientLanguage)).ToArray();
        equipmentChoices[cacheKey] = cached;
        return cached;
    }

    public EquipmentChoice[] SearchEquipment(int slot, string query, bool favoritesOnly)
    {
        var choices = GetEquipmentChoices(slot);
        var keys = choices.Select(choice => choice.Key).ToHashSet();
        var manualFavorites = Configuration.FavoriteEquipment.Where(key => key.Slot == slot && !keys.Contains(key))
            .Select(key => new EquipmentChoice(key, Localizer[TextKey.ManualEquipment], 0));
        return choices.Concat(manualFavorites)
            .Where(choice => (!favoritesOnly || Configuration.FavoriteEquipment.Contains(choice.Key))
                && choice.Matches(query, ClientState.ClientLanguage)).ToArray();
    }

    public void ToggleEquipmentFavorite(EquipmentChoiceKey key)
    {
        if (!Configuration.FavoriteEquipment.Remove(key))
            Configuration.FavoriteEquipment.Add(key);
        Save();
    }
    public void SetSourceColor(OutfitSlot slot, int channel, DyeColor? color)
        => bulkOutfitService.SetSourceColor(slot, channel, color);

    public void ClearSourceDye(OutfitSlot slot, int channel)
        => bulkOutfitService.ClearSourceDye(slot, channel);
    public bool TryUnequipSourceOutfitSlot(OutfitSlot slot, out string message)
        => bulkOutfitService.TryUnequipSourceSlot(slot, out message);

    public IReadOnlyList<EquipmentDisplayEntry> GetOutfitEquipment(OutfitData? outfit)
        => outfit is { } source
            ? source.Equipment.Select((armor, index) => CreateEquipmentDisplay(
                (OutfitSlot)index,
                armor.Set,
                armor.Variant,
                GetEquipmentDisplays(ClientState.ClientLanguage))).ToArray()
            : Array.Empty<EquipmentDisplayEntry>();

    public (EquipmentItemDisplay Item, ushort Model, byte Variant)? GetFacewearDisplay(ushort glassesId)
    {
        var sheet = DataManager.GetExcelSheet<Glasses>(ClientState.ClientLanguage);
        return sheet.TryGetRow(glassesId, out var row)
            ? (new EquipmentItemDisplay(row.Name.ToString(), (uint)row.Icon),
                (ushort)(row.Model & 0xFFFF), (byte)((row.Model >> 16) & 0xFF))
            : null;
    }

    public bool TryGetActorOutfit(LogicalActorKey actor, out OutfitData outfit)
    {
        if (actorResolver.TryResolve(actor, out var current))
            return outfitMemory.TryCaptureRendered(current, out outfit);
        outfit = null!;
        return false;
    }

    public StainDisplayEntry? GetStainDisplay(byte stainId)
    {
        if (stainId == 0)
            return new StainDisplayEntry(0, Localizer[TextKey.None], 0, 0, 0, false);
        var language = ClientState.ClientLanguage;
        if (!stainDisplayCaches.TryGetValue(language, out var cache))
        {
            cache = DataManager.GetExcelSheet<Stain>(language)
                .Where(static stain => stain.RowId is > 0 and <= byte.MaxValue)
                .ToDictionary(
                    static stain => checked((byte)stain.RowId),
                    stain =>
                    {
                        var (red, green, blue) = EquipmentDisplayFormatting.DecodeStainColor(stain.Color);
                        return new StainDisplayEntry(
                            checked((byte)stain.RowId),
                            stain.Name.IsEmpty ? Localizer.Get(TextKey.Unknown, stain.RowId) : stain.Name.ToString(),
                            red,
                            green,
                            blue,
                            true);
                    });
            stainDisplayCaches[language] = cache;
        }
        return cache.GetValueOrDefault(stainId);
    }

    public bool TryGetIconTexture(uint iconId, out IDalamudTextureWrap? texture)
    {
        texture = null;
        if (iconId == 0)
            return false;
        try
        {
            texture = TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrEmpty();
            return texture is not null;
        }
        catch (Exception exception)
        {
            Log.Debug(exception, "Failed to load equipment icon {IconId}.", iconId);
            return false;
        }
    }

    public BulkOperation? CurrentBulkOperation => bulkOutfitService.CurrentOperation;
    public string BulkOutfitStatus => bulkOutfitService.LastStatus;
    public int RestorableModifiedOutfitActorCount => GetRestorableModifiedOutfitActors().Count;

    public IDisposable PushIconFont()
        => PluginInterface.UiBuilder.IconFontFixedWidthHandle.Push();

    private IReadOnlyList<LogicalActorKey> GetRestorableModifiedOutfitActors()
        => BulkOutfitRestoreTargetResolver.Resolve(
            bulkOutfitService.Store.States.Keys,
            actorRegistry.Entries.Select(static actor => actor.Key),
            actor => IsActorModified(actor) || IsOutfitPinned(actor));

    private IReadOnlyList<ActorEntry> GetBulkOutfitActors()
        => GPoseBulkActorSelector.Select(
            actorRegistry.Entries,
            ClientState.IsGPosing,
            gposeCoordinator.State == GPoseState.Ready,
            key => actorRegistry.TryGetGPoseLocalPlayer(key, out _));

    private ActorSnapshot? SelectBulkOutfitRepresentation(ActorEntry actor)
    {
        ActorSnapshot? directGPose = null;
        if (ClientState.IsGPosing
            && actor.IsLocalPlayer
            && actorRegistry.TryGetGPoseLocalPlayer(actor.Key, out var resolvedGPose))
            directGPose = resolvedGPose;
        return RegistryActorResolver.SelectRepresentation(actor, ClientState.IsGPosing, directGPose);
    }

    private bool CanStartBulkOutfitInCurrentContext(out string message)
    {
        if (ClientState.IsGPosing && gposeCoordinator.State != GPoseState.Ready)
        {
            message = "GPose actor mapping is not ready yet.";
            return false;
        }
        message = string.Empty;
        return true;
    }

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
        }

        return cache;
    }

    public IReadOnlyList<EquipmentDisplayEntry> GetHumanEquipment(ModelSearchEntry model)
    {
        if (model.HumanAppearance is not { } appearance)
            return Array.Empty<EquipmentDisplayEntry>();
        var displays = GetEquipmentDisplays(ClientState.ClientLanguage);
        return appearance.Equipment.Select((packed, index) =>
        {
            var set = checked((ushort)(packed & 0xFFFF));
            var variant = checked((byte)((packed >> 16) & 0xFF));
            return CreateEquipmentDisplay((OutfitSlot)index, set, variant, displays);
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

    public OutfitData? GetHumanOutfit(ModelSearchEntry model)
        => EquipmentDisplayFormatting.CreateHumanOutfit(model.HumanAppearance);

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

    private IReadOnlyDictionary<(OutfitSlot Slot, uint ModelKey), EquipmentItemDisplay> GetEquipmentDisplays(ClientLanguage language)
    {
        if (equipmentDisplayCaches.TryGetValue(language, out var cache))
            return cache;

        var candidates = new List<((OutfitSlot Slot, uint ModelKey) Key, string Name, uint IconId)>();
        foreach (var item in DataManager.GetExcelSheet<Item>(language))
        {
            if (item.Name.IsEmpty
                || item.ModelMain == 0
                || !item.EquipSlotCategory.IsValid
                || item.EquipSlotCategory.RowId == 0)
                continue;
            var modelKey = checked((uint)(item.ModelMain & 0xFFFFFF));
            var slots = GetOutfitSlots(item.EquipSlotCategory.Value);
            candidates.AddRange(slots.Select(slot => ((slot, modelKey), item.Name.ToString(), (uint)item.Icon)));
        }

        cache = candidates
            .GroupBy(static candidate => candidate.Key)
            .ToDictionary(
                static group => group.Key,
                group => new EquipmentItemDisplay(
                    string.Join(" / ", group.Select(static item => item.Name).Distinct(GameTextComparison.GetComparer(language))),
                    group.Select(static item => item.IconId).FirstOrDefault(static icon => icon != 0)));
        equipmentDisplayCaches[language] = cache;
        return cache;
    }

    private static EquipmentDisplayEntry CreateEquipmentDisplay(
        OutfitSlot slot,
        ushort set,
        byte variant,
        IReadOnlyDictionary<(OutfitSlot Slot, uint ModelKey), EquipmentItemDisplay> displays)
    {
        var modelKey = (uint)set | ((uint)variant << 16);
        var display = modelKey == 0
            ? null
            : displays.GetValueOrDefault((slot, modelKey));
        return new EquipmentDisplayEntry(slot, set, variant, display?.Name ?? string.Empty, display?.IconId ?? 0);
    }

    private static IEnumerable<OutfitSlot> GetOutfitSlots(EquipSlotCategory category)
    {
        if (category.Head == 1) yield return OutfitSlot.Head;
        if (category.Body == 1) yield return OutfitSlot.Body;
        if (category.Gloves == 1) yield return OutfitSlot.Hands;
        if (category.Legs == 1) yield return OutfitSlot.Legs;
        if (category.Feet == 1) yield return OutfitSlot.Feet;
        if (category.Ears == 1) yield return OutfitSlot.Ears;
        if (category.Neck == 1) yield return OutfitSlot.Neck;
        if (category.Wrists == 1) yield return OutfitSlot.Wrists;
        if (category.FingerR == 1) yield return OutfitSlot.RightRing;
        if (category.FingerL == 1) yield return OutfitSlot.LeftRing;
    }

    public bool TryApplyModelToLocalPlayer(ModelSearchEntry model, out string message)
        => TryApplyModelToLocalPlayer(model, out _, out message);

    public bool TryApplyModelToLocalPlayer(ModelSearchEntry model, out Guid operationId, out string message)
    {
        operationId = Guid.Empty;
        var local = actorRegistry.Entries.FirstOrDefault(static actor => actor.IsLocalPlayer);
        if (local is null)
        {
            message = "Local player is not available.";
            return false;
        }
        return TryApplyModel(local.Key, model, out operationId, out message);
    }

    public bool TryApplyModel(LogicalActorKey actor, ModelSearchEntry model, out string message)
        => TryApplyModel(actor, model, out _, out message);

    public bool TryApplyModelToTarget(ModelSearchEntry model, out Guid operationId, out string message)
    {
        operationId = Guid.Empty;
        restoreStatus = string.Empty;
        var target = ClientState.IsGPosing ? TargetManager.GPoseTarget : TargetManager.Target;
        if (target is null)
        {
            message = Localizer.Get(TextKey.NoTargetSelected);
            return false;
        }
        var snapshot = RegistryActorResolver.FindTarget(actorRegistry.Entries,
            target.ObjectIndex, target.GameObjectId, target.EntityId, ClientState.TerritoryType);
        if (snapshot is null)
        {
            message = Localizer.Get(TextKey.TargetActorUnavailable);
            return false;
        }
        return appearanceApplyService.TryApply(snapshot, model.ModelAppearance, out operationId, out message);
    }

    public bool TryApplyModel(LogicalActorKey actor, ModelSearchEntry model, out Guid operationId, out string message)
    {
        restoreStatus = string.Empty;
        if (!appearanceApplyService.TryApply(actor, model.ModelAppearance, out operationId, out message))
            return false;
        return true;
    }

    public bool TryRestoreActor(LogicalActorKey actor, out string message)
        => TryRestoreActor(actor, out message, restoreWithinBatch: false);

    private bool TryRestoreActor(LogicalActorKey actor, out string message, bool restoreWithinBatch)
    {
        restoreStatus = string.Empty;
        if (!actorResolver.TryResolve(actor, out var current))
        {
            message = "The actor is no longer available.";
            restoreStatus = message;
            return false;
        }
        if (bulkOutfitService.Store.TryGet(actor, out _))
        {
            if (restoreWithinBatch)
            {
                redrawCoordinator.Cancel(actor, "Explicit restore requested.");
                if (!bulkOutfitService.RestoreOriginalOutfitNow(actor))
                {
                    message = "Original outfit restore failed.";
                    restoreStatus = message;
                    return false;
                }
                return CompleteActorRestore(actor, out message);
            }
            if (!pendingActorRestoreRedraws.Add(actor))
            {
                message = "Original outfit restore is already in progress.";
                restoreStatus = message;
                return false;
            }
            if (!bulkOutfitService.StartRestore(actor, out message))
            {
                pendingActorRestoreRedraws.Remove(actor);
                restoreStatus = message;
                return false;
            }
            redrawCoordinator.Cancel(actor, "Explicit restore requested.");

            message = "Original outfit restore started; game appearance redraw will follow.";
            restoreStatus = message;
            return true;
        }

        redrawCoordinator.Cancel(actor, "Explicit restore requested.");
        return CompleteActorRestore(actor, out message);
    }

    private bool CompleteActorRestore(LogicalActorKey actor, out string message)
    {
        actorRegistry.ClearManagedAppearance(actor);
        appearancePersistence.Restore(actor);
        UnpinActor(actor);
        if (!actorResolver.TryResolve(actor, out var current))
        {
            message = "The actor is no longer available for redraw.";
            restoreStatus = message;
            return false;
        }
        var succeeded = TryRequestGameAppearanceRedraw(current, out message);
        restoreStatus = message;
        return succeeded;
    }

    private bool TryRequestGameAppearanceRedraw(ActorSnapshot actor, out string message)
    {
        if (!redrawBackend.TryDisable(actor) || !redrawBackend.TryEnable(actor, null, Guid.Empty))
        {
            message = "The actor could not be redrawn.";
            return false;
        }

        message = "Original game appearance regeneration requested.";
        return true;
    }

    public bool HasOutfitOverride(LogicalActorKey actor)
        => bulkOutfitService.Store.TryGet(actor, out _);

    public bool IsOutfitModified(LogicalActorKey actor)
        => bulkOutfitService.IsOutfitModified(actor);

    public bool IsActorModified(LogicalActorKey actor)
        => (actorResolver.TryResolve(actor, out var current) && current.IsAppearanceManaged)
            || IsOutfitModified(actor);

    public bool TryGetOutfitOverride(LogicalActorKey actor, out OutfitOverrideState state)
        => bulkOutfitService.Store.TryGet(actor, out state!);

    public bool IsOutfitPinned(LogicalActorKey actor)
        => actorRegistry.TryGet(actor, out var entry) && pinnedOutfitStore.IsPinned(entry);

    public bool TryGetPinnedOutfit(LogicalActorKey actor, out OutfitData outfit)
    {
        if (actorRegistry.TryGet(actor, out var entry)
            && pinnedOutfitStore.TryGet(entry, out outfit))
            return true;
        outfit = null!;
        return false;
    }

    public bool TrySetOutfitPinned(LogicalActorKey actor, bool pinned, out string message)
    {
        if (!actorRegistry.TryGet(actor, out var entry))
        {
            message = "Actor is unavailable.";
            return false;
        }
        if (!pinned)
        {
            pinnedOutfitStore.Unpin(entry);
            failedPinnedOutfitReapplyStates.Remove(actor);
            failedPinnedAppearanceStates.Remove(actor);
            message = Localizer.Get(TextKey.AppearancePinsRemoved, 1);
            return true;
        }
        var result = pinnedOutfitStore.PinCurrent([entry], CapturePinAppearance);
        message = Localizer.Get(TextKey.AppearancePinsSaved, result.Pinned, result.Unavailable);
        return result.Pinned != 0;
    }

    public int AppearancePinCount => pinnedOutfitStore.Count;

    public IReadOnlyList<ActorEntry> GetModifiedPinActors()
        => actorRegistry.Entries.Where(actor => IsActorModified(actor.Key)).ToArray();

    public string PinModifiedActors()
    {
        var result = pinnedOutfitStore.PinCurrent(GetModifiedPinActors(), CapturePinAppearance);
        return Localizer.Get(TextKey.AppearancePinsSaved, result.Pinned, result.Unavailable);
    }

    public string UnpinAllActors()
    {
        var removed = pinnedOutfitStore.UnpinAll();
        failedPinnedOutfitReapplyStates.Clear();
        failedPinnedAppearanceStates.Clear();
        return Localizer.Get(TextKey.AppearancePinsRemoved, removed);
    }

    private AppearanceData? CapturePinAppearance(ActorEntry actor)
    {
        if (!actorResolver.TryResolve(actor.Key, out var current))
            return null;
        var appearance = actorRegistry.CaptureCurrentAppearance(current);
        if (appearance is not null)
        {
            appearancePersistence.RecordModel(current, appearance);
            drawObjectInjector.EnablePersistentAppearance(current);
            failedPinnedOutfitReapplyStates.Remove(actor.Key);
            failedPinnedAppearanceStates.Remove(actor.Key);
        }
        return appearance;
    }

    public bool IsAppearancePending(LogicalActorKey actor)
        => appearanceApplyService.IsPending(actor);

    public bool IsLocalPlayerAppearancePending()
        => actorRegistry.Entries.FirstOrDefault(static actor => actor.IsLocalPlayer) is { } local
        && appearanceApplyService.IsPending(local.Key);

    public string AppearanceStatus => string.IsNullOrWhiteSpace(restoreStatus)
        ? appearanceApplyService.LastStatus
        : restoreStatus;
    public bool? AppearanceSucceeded => appearanceApplyService.LastSucceeded;
    public Guid? AppearanceOperationId => appearanceApplyService.LastOperationId;
    public ModelPreviewSnapshot ModelPreview => modelPreview.Snapshot;
    public SoftwareModelPreviewView? SoftwareModelPreview => softwareModelPreviewBackend.GetView();
    public void SelectPreviewModel(ModelSearchEntry? model)
    {
        var next = ModelPreviewSelectionKey.From(model);
        if (previewTextureSelection != next)
        {
            previewTextureSelection = next;
            modelPreviewTextureCache.Select(model);
        }
        modelPreview.Select(model);
    }
    public ImTextureID GetModelPreviewTextureHandle(string materialPath)
        => modelPreviewTextureCache.GetHandle(materialPath);
    public void SetModelPreviewActive(bool active) => modelPreview.SetActive(active);
    public void ResetModelPreviewCamera() => modelPreview.ResetCamera();
    public void OrbitModelPreview(float deltaX, float deltaY) => softwareModelPreviewBackend.Orbit(deltaX, deltaY);
    public void ZoomModelPreview(float wheelDelta) => softwareModelPreviewBackend.AdjustZoom(wheelDelta);
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
                ["skippedMeshCount"] = report.SkippedMeshCount,
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

    private void OnAppearanceOperationCompleted(
        Guid _,
        LogicalActorKey actor,
        ActorRepresentationKey representation,
        AppearanceData applied,
        bool succeeded)
    {
        if (pinnedAppearanceOperationActor == actor)
        {
            if (succeeded)
                failedPinnedAppearanceStates.Remove(actor);
            else if (pinnedAppearanceOperationObservedState is { } failedState)
                failedPinnedAppearanceStates[actor] = failedState;
            pinnedAppearanceOperationActor = null;
            pinnedAppearanceOperationObservedState = null;
            nextPinnedOutfitScanTick = Environment.TickCount64 + 500;
        }
        if (!actorResolver.TryResolve(actor, representation, out var current)
            || !CanPublishAppearanceCompletion(succeeded, representation, current))
            return;
        var publishedAppearance = CreatePublishedAppearance(applied);
        if (!actorRegistry.RecordAppliedAppearance(actor, representation, publishedAppearance))
            return;

        appearancePersistence.RecordModel(current, publishedAppearance);
        drawObjectInjector.EnablePersistentAppearance(current);
    }

    internal static bool CanPublishAppearanceCompletion(
        bool succeeded,
        ActorRepresentationKey targetRepresentation,
        ActorSnapshot current)
        => succeeded && current.RepresentationKey == targetRepresentation;

    private void OnBulkOutfitActorOperationCompleted(
        LogicalActorKey actor,
        BulkOperationType type,
        OutfitData? desired,
        bool succeeded)
    {
        if (pendingEquipmentSelection is { } selection && selection.Actor == actor)
        {
            pendingEquipmentSelection = null;
            if (type == BulkOperationType.ApplyOutfit && succeeded && desired is not null
                && actorRegistry.TryGet(actor, out var selectedActor))
                pinnedOutfitStore.UpdateSelectedEquipment(selectedActor, selection.Choice, desired);
        }
        if (pinnedOutfitOperationActor == actor)
        {
            if (succeeded)
                failedPinnedOutfitReapplyStates.Remove(actor);
            else if (desired is not null && pinnedOutfitOperationObservedState is { } failedState)
                failedPinnedOutfitReapplyStates[actor] = failedState;
            pinnedOutfitOperationActor = null;
            pinnedOutfitOperationObservedState = null;
            nextPinnedOutfitScanTick = Environment.TickCount64 + 500;
        }
        var isExplicitActorRestore = type == BulkOperationType.Restore
            && pendingActorRestoreRedraws.Remove(actor);
        if (isExplicitActorRestore)
        {
            if (!succeeded)
            {
                restoreStatus = "Original outfit restore failed.";
                return;
            }

            if (!CompleteActorRestore(actor, out var redrawMessage))
            {
                ReportRestoreRedrawFailure(actor, redrawMessage);
                return;
            }
            restoreStatus = redrawMessage;
            return;
        }

        if (succeeded && type == BulkOperationType.Restore
            && actorRegistry.TryGet(actor, out var restoredActor))
            pinnedOutfitStore.Unpin(restoredActor);

        if (succeeded && desired is not null
            && actorResolver.TryResolve(actor, out var current))
        {
            appearancePersistence.RecordOutfit(current, desired);
            drawObjectInjector.EnablePersistentAppearance(current);
            if (appearancePersistence.GetModel(actor) is { } updated)
                actorRegistry.RecordAppliedAppearance(actor, current.RepresentationKey, updated);
        }

    }

    private void ReportRestoreRedrawFailure(LogicalActorKey actor, string message)
    {
        restoreStatus = message;
        diagnosticRouter.Write(new DiagnosticLogEntry
        {
            Level = DiagnosticLogLevel.Error,
            EventId = DiagnosticEventIds.RedrawFailed,
            Category = DiagnosticCategory.Redraw,
            Message = message,
            ActorKey = DiagnosticActorKeys.Format(diagnosticRouter, actor),
            Outcome = "Failed",
        });
    }

    private void OnPluginFrameworkUpdate(IFramework framework)
    {
        EnsureCommandsRegistered();
        var now = Environment.TickCount64;
        if (pinnedOutfitTerritory != ClientState.TerritoryType || !ClientState.IsLoggedIn)
        {
            pinnedOutfitTerritory = ClientState.TerritoryType;
            pinnedOutfitOperationActor = null;
            pinnedOutfitOperationObservedState = null;
            failedPinnedOutfitReapplyStates.Clear();
            pinnedAppearanceOperationActor = null;
            pinnedAppearanceOperationObservedState = null;
            failedPinnedAppearanceStates.Clear();
        }
        if (!ClientState.IsLoggedIn)
            return;

        TryReapplyPinnedOutfit(now);
    }

    private CommandRegistrationLease CreateCommandRegistration(string command)
    {
        var info = new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Actor Morpher.",
        };
        return new CommandRegistrationLease(
            () => CommandManager.Commands.ContainsKey(command),
            () => CommandManager.AddHandler(command, info),
            () => CommandManager.RemoveHandler(command));
    }

    private void EnsureCommandsRegistered()
    {
        primaryCommandRegistration.EnsureRegistered();
        aliasCommandRegistration.EnsureRegistered();
    }

    private bool TryReapplyPinnedOutfit(long now)
    {
        if (now < nextPinnedOutfitScanTick
            || pinnedOutfitOperationActor is not null
            || pinnedAppearanceOperationActor is not null
            || bulkOutfitService.CurrentOperation is not null)
            return false;

        nextPinnedOutfitScanTick = now + 500;
        foreach (var actor in actorRegistry.Entries)
        {
            if (pinnedOutfitStore.TryGetAppearance(actor, out var pinnedAppearance))
            {
                if (!actorResolver.TryResolve(actor.Key, out var representation)
                    || actorRegistry.CaptureCurrentAppearance(representation) is not { } rendered
                    || appearanceApplyService.IsPending(actor.Key))
                    continue;
                if (PinnedOutfitStore.AppearanceEquals(rendered, pinnedAppearance))
                    continue;
                if (failedPinnedAppearanceStates.TryGetValue(actor.Key, out var failed)
                    && PinnedOutfitStore.AppearanceEquals(rendered, failed))
                    continue;
                if (!appearanceApplyService.TryApply(actor.Key, pinnedAppearance, out _))
                    continue;
                pinnedAppearanceOperationActor = actor.Key;
                pinnedAppearanceOperationObservedState = rendered;
                return true;
            }
            if (actor.Current.Race is null
                || !pinnedOutfitStore.TryGet(actor, out var desired)
                || !bulkOutfitService.TryCaptureOutfit(actor.Key, out var current))
                continue;

            if (failedPinnedOutfitReapplyStates.TryGetValue(actor.Key, out var failedState))
            {
                if (OutfitDataValueComparer.AreEqual(current, failedState))
                    continue;
                failedPinnedOutfitReapplyStates.Remove(actor.Key);
            }
            if (OutfitDataValueComparer.AreEqual(current, desired))
                continue;

            if (!bulkOutfitService.StartPersistentApply(actor.Key, desired, out _))
                continue;
            pinnedOutfitOperationActor = actor.Key;
            pinnedOutfitOperationObservedState = current;
            diagnosticRouter.Write(new DiagnosticLogEntry
            {
                EventId = DiagnosticEventIds.BulkBatchStarted,
                Category = DiagnosticCategory.BulkOutfit,
                Message = "Pinned outfit reapply queued.",
                ActorKey = DiagnosticActorKeys.Format(diagnosticRouter, actor.Key),
                Properties = new Dictionary<string, object?>
                {
                    ["territoryId"] = ClientState.TerritoryType,
                    ["isLocalPlayer"] = actor.IsLocalPlayer,
                },
            });
            return true;
        }
        return false;
    }

    private void UnpinActor(LogicalActorKey actor)
    {
        failedPinnedOutfitReapplyStates.Remove(actor);
        failedPinnedAppearanceStates.Remove(actor);
        if (actorRegistry.TryGet(actor, out var entry))
            pinnedOutfitStore.Unpin(entry);
    }

    public static bool ShouldRegisterModelSearchAppearance(AppearanceData? appearance)
    {
        if (appearance is null)
            return false;
        if (appearance.Category != ModelCategory.Human)
            return true;

        var customize = appearance.Customize;
        return customize.Length == 26
            && customize[0] is >= 1 and <= 8
            && customize[1] <= 1
            && customize[4] != 0
            && HumanTribeCatalog.IsValidForRace(customize[0], customize[4]);
    }

    public static bool ShouldRegisterUnreferencedModelChara(byte modelType)
        => modelType == 3;

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
                name = $"Event NPC {row.RowId}";

            var appearance = model.Type == 1 ? CreateHumanAppearance(row) : null;
            if (model.Type == 1 && appearance is null)
                continue;
            var modelAppearance = model.Type switch
            {
                1 when appearance is not null => CreateHumanModelAppearance(modelId, row.RowId, appearance, row.Scale),
                2 => CreateDemihumanAppearance(row),
                3 => CreateMonsterAppearance(modelId, row.RowId, row.Scale),
                _ => null,
            };
            if (!ShouldRegisterModelSearchAppearance(modelAppearance))
                continue;

            entries.Add(CreateSearchEntry(
                model,
                ModelSource.EventNpc,
                row.RowId,
                name,
                (uint)row.Race.RowId,
                (byte)row.Gender,
                row.BodyType,
                appearance,
                modelAppearance!));
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
            var appearance = model.Type == 1 && customize is { } humanCustomize
                ? CreateHumanAppearance(humanCustomize, npcEquip)
                : null;
            var modelAppearance = model.Type switch
            {
                1 when appearance is not null => CreateHumanModelAppearance(modelId, row.RowId, appearance, row.Scale),
                2 => CreateDemihumanAppearance(row.RowId, modelId, customize, npcEquip, row.Scale),
                3 => CreateMonsterAppearance(modelId, row.RowId, row.Scale),
                _ => null,
            };
            if (!ShouldRegisterModelSearchAppearance(modelAppearance))
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
                    modelAppearance!));
            }
        }

        var referencedModelIds = entries.Select(static entry => entry.ModelId).ToHashSet();
        foreach (var model in modelChara.Values)
        {
            if (!ShouldRegisterUnreferencedModelChara(model.Type) || model.RowId == 0)
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
                humanAppearance: null,
                modelAppearance: CreateMonsterAppearance(model.RowId, model.RowId, null)));
        }

        return entries
            .DistinctBy(static entry => $"{entry.Name}\u001f{entry.Source}\u001f{entry.SourceId}\u001f{CreateAppearanceSignature(entry.ModelAppearance)}")
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
            appearance.ModelScale?.ToString("R", System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            Convert.ToHexString(appearance.Customize.AsSpan()),
            string.Join(',', appearance.Equipment.Select(static value => value.ToString("X16"))),
            appearance.Mainhand?.ToString("X16") ?? string.Empty,
            appearance.Offhand?.ToString("X16") ?? string.Empty,
            appearance.VisorToggled?.ToString() ?? string.Empty,
            appearance.FacewearModelId?.ToString() ?? string.Empty,
            appearance.HatVisible?.ToString() ?? string.Empty);

    private static ModelSearchEntry CreateSearchEntry(
        ModelChara model,
        ModelSource source,
        uint sourceId,
        string name,
        uint race,
        byte gender,
        byte bodyType,
        HumanAppearance? humanAppearance,
        AppearanceData modelAppearance)
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
            modelAppearance.Completeness,
            modelAppearance);
    }

    internal static AppearanceData CreateHumanModelAppearance(
        uint modelCharaId,
        uint sourceRowId,
        HumanAppearance appearance,
        float? modelScale)
        => AppearanceData.Create(
            modelCharaId,
            ModelCategory.Human,
            sourceRowId,
            AppearanceCompleteness.Complete,
            appearance.Customize,
            appearance.Equipment,
            modelScale,
            appearance.Mainhand,
            appearance.Offhand,
            appearance.VisorToggled,
            appearance.FacewearModelId,
            appearance.HatVisible);

    internal static AppearanceData CreatePublishedAppearance(AppearanceData applied)
        => ActorRegistry.ApplyCategoryContract(applied);

    private static AppearanceData CreateMonsterAppearance(uint modelCharaId, uint sourceRowId, float? modelScale)
        => AppearanceData.Create(
            modelCharaId,
            ModelCategory.Monster,
            sourceRowId,
            AppearanceCompleteness.ModelOnly,
            Array.Empty<byte>(),
            Array.Empty<ulong>(),
            modelScale);

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
        var npcEquip = row.NpcEquip.RowId is not 0
            ? row.NpcEquip.ValueNullable
            : null;
        var useNpcEquip = npcEquip is not null && row is { ModelBody: 0, ModelLegs: 0 };
        var equipment = useNpcEquip ? CreateEquipment(npcEquip!.Value) : CreateEquipment(row);
        var mainhand = useNpcEquip
            ? PackWeapon(npcEquip!.Value.ModelMainHand, npcEquip.Value.DyeMainHand.RowId, npcEquip.Value.Dye2MainHand.RowId)
            : PackWeapon(row.ModelMainHand, row.DyeMainHand.RowId, row.Dye2MainHand.RowId);
        var offhand = useNpcEquip
            ? PackWeapon(npcEquip!.Value.ModelOffHand, npcEquip.Value.DyeOffHand.RowId, npcEquip.Value.Dye2OffHand.RowId)
            : PackWeapon(row.ModelOffHand, row.DyeOffHand.RowId, row.Dye2OffHand.RowId);
        var facewearModelId = npcEquip is { } selectedNpcEquip
            ? GetNpcFacewearModelId(selectedNpcEquip)
            : (ushort)0;
        var hatVisible = (useNpcEquip ? npcEquip!.Value.ModelHead : row.ModelHead) != 0;

        return new HumanAppearance(
            customize,
            equipment,
            mainhand,
            offhand,
            row.Visor,
            facewearModelId,
            hatVisible);
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
            equipment,
            row.Scale);
    }

    private static AppearanceData CreateDemihumanAppearance(
        uint sourceRowId,
        uint modelCharaId,
        BNpcCustomize? customizeRow,
        NpcEquip? equip,
        float? modelScale)
    {
        var customize = customizeRow is { } value
            ? new byte[]
            {
                (byte)value.Race.RowId, (byte)value.Gender, value.BodyType, value.Height,
                (byte)value.Tribe.RowId, value.Face, value.HairStyle, value.HairHighlight,
                value.SkinColor, value.EyeHeterochromia, value.HairColor, value.HairHighlightColor,
                value.FacialFeature, value.FacialFeatureColor, value.Eyebrows, value.EyeColor,
                value.EyeShape, value.Nose, value.Jaw, value.Mouth, value.LipColor,
                value.BustOrTone1, value.ExtraFeature1, value.ExtraFeature2OrBust,
                value.FacePaint, value.FacePaintColor,
            }
            : Array.Empty<byte>();
        var equipment = equip is { } equipmentRow
            ? CreateEquipment(equipmentRow)
            : Array.Empty<ulong>();
        return CreateDemihumanAppearance(
            sourceRowId,
            modelCharaId,
            customize,
            equipment,
            modelScale);
    }

    internal static AppearanceData CreateDemihumanAppearance(
        uint sourceRowId,
        uint modelCharaId,
        IEnumerable<byte>? customize,
        IEnumerable<ulong>? equipment,
        float? modelScale)
        => AppearanceData.Create(
            modelCharaId,
            ModelCategory.Demihuman,
            sourceRowId,
            AppearanceCompleteness.Complete,
            customize ?? Array.Empty<byte>(),
            equipment ?? Array.Empty<ulong>(),
            modelScale);

    private static HumanAppearance CreateHumanAppearance(BNpcCustomize row, NpcEquip? equip)
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
        return new HumanAppearance(
            customize,
            equip is null ? new ulong[10] : CreateEquipment(equip.Value),
            equip is null ? 0 : PackWeapon(equip.Value.ModelMainHand, equip.Value.DyeMainHand.RowId, equip.Value.Dye2MainHand.RowId),
            equip is null ? 0 : PackWeapon(equip.Value.ModelOffHand, equip.Value.DyeOffHand.RowId, equip.Value.Dye2OffHand.RowId),
            equip?.Visor ?? false,
            equip is { } npcEquip ? GetNpcFacewearModelId(npcEquip) : (ushort)0,
            equip is { ModelHead: not 0 });
    }

    private static ushort GetNpcFacewearModelId(NpcEquip equip)
        => SelectNpcFacewearModelId(
            equip.Glasses.Count == 0 ? null : equip.Glasses[0].RowId);

    internal static ushort SelectNpcFacewearModelId(uint? firstGlassesRowId)
        => firstGlassesRowId is { } rowId ? checked((ushort)rowId) : (ushort)0;

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
    bool VisorToggled,
    ushort FacewearModelId = 0,
    bool HatVisible = false)
{
    public string Signature { get; } = string.Join(
        ':',
        Convert.ToHexString(Customize),
        string.Join(',', Equipment.Select(static value => value.ToString("X16"))),
        Mainhand.ToString("X16"),
        Offhand.ToString("X16"),
        VisorToggled ? "1" : "0",
        FacewearModelId,
        HatVisible ? "1" : "0");
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
    AppearanceData ModelAppearance)
{
    public uint ModelId => RowId;

    public uint Tribe => Category == ModelCategory.Human
        && HumanAppearance is { Customize.Length: > 4 } appearance
            ? appearance.Customize[4]
            : 0U;

    public bool IsYoungNpc => Category == ModelCategory.Human && BodyType == (byte)NpcAge.Young;
}
