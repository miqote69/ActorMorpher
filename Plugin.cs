using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.Game.Inventory;
using Dalamud.Game.Inventory.InventoryEventArgTypes;
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

public sealed partial class Plugin : IDalamudPlugin
{
    public const string DisplayName = "Actor Morpher";

    private const string CommandName = "/actormorpher";
    private const string CommandAlias = "/amorph";

    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] private static IObjectTable ObjectTable { get; set; } = null!;
    [PluginService] private static ITargetManager TargetManager { get; set; } = null!;
    [PluginService] private static IGameGui GameGui { get; set; } = null!;
    [PluginService] private static IDataManager DataManager { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;
    [PluginService] private static IClientState ClientState { get; set; } = null!;
    [PluginService] private static IGameInventory GameInventory { get; set; } = null!;
    [PluginService] private static IPlayerState PlayerState { get; set; } = null!;
    [PluginService] private static ISigScanner SigScanner { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IGameInteropProvider GameInteropProvider { get; set; } = null!;
    [PluginService] private static ITextureProvider TextureProvider { get; set; } = null!;

    private readonly MainWindow mainWindow;
    private readonly HashSet<Guid> modelSearchOperations = new();
    private readonly GlamourPlateImportOperation plateImport = new();
    private readonly GlamourPlateCache plateCache;
    private ulong PlateOwner => ClientState.IsLoggedIn ? PlayerState.ContentId : 0;
    public DateTimeOffset? PlateAcquiredAt => plateCache.Get(PlateOwner)?.AcquiredAt;
    private string? completedPlateImportMessage;
    private GlamourPlateSource.Observation? lastPlateObservation;
    private string? lastPlateObservationPhase;
    public bool IsPlateImportPending => plateImport.IsPending;
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
    private readonly ActorAppearancePersistence appearancePersistence;
    private readonly NativeActorContinuity actorContinuity;
    private readonly CommandRegistrationLease primaryCommandRegistration;
    private readonly CommandRegistrationLease aliasCommandRegistration;
    private readonly BulkOutfitTargetResolver bulkOutfitTargetResolver = new();
    private readonly Dictionary<ClientLanguage, IReadOnlyList<ModelSearchEntry>> modelSearchCaches = new();
    private readonly Dictionary<ClientLanguage, IReadOnlyDictionary<(OutfitSlot Slot, uint ModelKey), EquipmentItemDisplay>> equipmentDisplayCaches = new();
    private readonly Dictionary<ClientLanguage, IReadOnlyDictionary<ulong, EquipmentItemDisplay>> weaponDisplayCaches = new();
    private readonly Dictionary<(ClientLanguage, int), EquipmentChoice[]> equipmentChoices = new();
    private readonly System.Collections.Concurrent.ConcurrentDictionary<(ClientLanguage, uint, int), EquipmentChoice[]> weaponChoices = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<(ActorSnapshot Actor, EquipmentChoiceKey Choice,
        int? DyeChannel, DyeColor? Color, bool ClearDye)> weaponSelections = new();
    public uint CurrentWeaponJob => ObjectTable.LocalPlayer?.ClassJob.RowId ?? 0;
    public string WeaponSelectionStatus { get; private set; } = string.Empty;
    public LogicalActorKey? WeaponSelectionActor { get; private set; }
    private (LogicalActorKey Actor, EquipmentChoiceKey Choice)? pendingEquipmentSelection;
    private LogicalActorKey? pendingHeadgearSetting;
    private readonly Dictionary<ClientLanguage, IReadOnlyDictionary<byte, StainDisplayEntry>> stainDisplayCaches = new();
    private readonly Dictionary<(uint RowId, ModelCategory Category, ModelSource Source, uint SourceId), ModelPreviewAssetReport> previewAssetCaches = new();
    private readonly Dictionary<(uint RowId, ModelCategory Category, ModelSource Source, uint SourceId), ModelPreviewSupport> previewSupportCaches = new();
    private readonly Dictionary<(uint RowId, ModelCategory Category, ModelSource Source, uint SourceId), ModelPreviewGeometryReport> previewGeometryCaches = new();
    private long nextPinnedOutfitScanTick;
    private readonly PinnedActorDiscovery pinnedActorDiscovery = new();
    private uint pinnedOutfitTerritory;
    private ulong pinnedOutfitOwner;
    private LogicalActorKey? pinnedOutfitOperationActor;
    private OutfitData? pinnedOutfitOperationObservedState;
    private readonly Dictionary<LogicalActorKey, OutfitData> failedPinnedOutfitReapplyStates = new();
    private LogicalActorKey? pinnedAppearanceOperationActor;
    private AppearanceData? pinnedAppearanceOperationObservedState;
    private readonly Dictionary<LogicalActorKey, AppearanceData> failedPinnedAppearanceStates = new();
    private readonly HashSet<LogicalActorKey> pendingActorRestoreRedraws = new();
    private string restoreStatus = string.Empty;
    private ModelPreviewSelectionKey? previewTextureSelection;
    private (int Count, bool LoggedIn, ulong Owner)? lastPinObservation;

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
        var loadedConfiguration = PluginInterface.GetPluginConfig();
        Configuration = loadedConfiguration as Configuration ?? Configuration.Create(isDev);
        var loadedPinCount = StoredPinCount;
        Configuration.MigrateAndValidate(isDev);
        var migratedPins = Configuration.MigrateLegacyPins(PlateOwner);
        pinnedOutfitStore = new PinnedOutfitStore(Configuration,
            () => SaveConfiguration("PinChange"), () => PlateOwner, FindNpcEquipmentGroup);
        appearancePersistence = new ActorAppearancePersistence(() => PlateOwner, pinnedOutfitStore.GetWeaponPinScope);
        Log.Information("[PinPersistence] Loaded Type={Type} BeforeMigration={Before} AfterMigration={After} Owner={Owner} OwnerPinCount={OwnerPinCount} IsDev={IsDev} ConfigFile={ConfigFile}",
            loadedConfiguration?.GetType().FullName ?? "null", loadedPinCount, StoredPinCount,
            PlateOwner, pinnedOutfitStore.Count, isDev, PluginInterface.ConfigFile.FullName);
        Configuration.GlamourPlates ??= [];
        plateCache = new GlamourPlateCache(Configuration.GlamourPlates,
            () => SaveConfiguration("PlateCache"));
        SaveConfiguration(migratedPins ? "CharacterPinMigration" : "Startup");
        Localizer = new Localizer(Configuration, ClientState);
        diagnosticRouter = new DiagnosticLogRouter(
            PluginInterface.ConfigDirectory.FullName,
            Path.GetDirectoryName(typeof(Plugin).Assembly.Location),
            Log);
        diagnosticController = new DiagnosticController(
            diagnosticRouter,
            Configuration,
            () => SaveConfiguration("Diagnostics"),
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
        var minionModels = DataManager.GetExcelSheet<Lumina.Excel.Sheets.Companion>()
            .ToDictionary(static row => row.RowId, static row => row.Model.RowId);
        actorContinuity = new NativeActorContinuity(new NpcFollowerIpc(PluginInterface, Framework), minionModels);
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
        outfitMemory = new NativeOutfitMemory(ObjectTable, humanModelClassifier, diagnosticRouter, SigScanner);
        var glasses = DataManager.GetExcelSheet<Glasses>();
        var facewearModels = new FacewearModelLookup(glasses
            .Select(row => ((ushort)row.RowId, (ushort)(row.Model & 0xFFFF), (byte)((row.Model >> 16) & 0xFF))));
        outfitMemory.ResolveFacewear = facewearModels.Resolve;
        outfitMemory.GetFacewearModel = id => glasses.TryGetRow(id, out var row) ? (uint?)row.Model : null;
        actorRegistry.ResolveFacewear = facewearModels.Resolve;
        drawObjectInjector.ResolveFacewear = facewearModels.Resolve;
        outfitMemory.GetColorOutfit = appearancePersistence.GetColorOutfit;
        outfitMemory.SetColorOutfit = appearancePersistence.SetColorOutfit;
        equipmentColors = new NativeEquipmentColors(GameInteropProvider, ObjectTable,
            drawObjectInjector.ResolveActor, appearancePersistence, diagnosticRouter, DataManager);
        actorRegistry.GetColorOutfit = outfitMemory.GetColorOutfit;
        actorRegistry.GetRetainedOutfit = appearancePersistence.GetRetainedOutfit;
        actorRegistry.GetWeaponDyes = appearancePersistence.GetWeaponDyes;
        actorRegistry.GetEquipmentLoadRevision = drawObjectInjector.GetEquipmentLoadRevision;
        bulkOutfitService = new BulkOutfitService(
            Framework,
            actorResolver,
            outfitMemory,
            clientContext,
            appearancePersistence.Outfits,
            diagnosticRouter)
        {
            RestoreWithGameHeadSettings = key => appearancePersistence.GetModel(key)?.UsesGameHeadSettings == true,
        };
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
        ClientState.ClassJobChanged += OnLocalPlayerClassJobChanged;
        GameInventory.InventoryChangedRaw += OnLocalPlayerInventoryChanged;
        ClientState.Logout += OnPlateLogout;
    }

    public void Dispose()
    {
        ObservePinState("Dispose");
        pendingCustomizeEdits.Clear();
        plateImport.Cancel();
        ClientState.Logout -= OnPlateLogout;
        Framework.Update -= OnPluginFrameworkUpdate;
        ClientState.ClassJobChanged -= OnLocalPlayerClassJobChanged;
        GameInventory.InventoryChangedRaw -= OnLocalPlayerInventoryChanged;
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
        SaveConfiguration("Settings");
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

    public LogicalActorKey? GetCurrentTargetActorKey()
    {
        var target = ClientState.IsGPosing ? TargetManager.GPoseTarget : TargetManager.Target;
        return target is null ? null : RegistryActorResolver.FindTarget(actorRegistry.Entries,
            target.ObjectIndex, target.GameObjectId, target.EntityId, ClientState.TerritoryType)?.LogicalKey;
    }

    public unsafe bool TryGetActorHeadScreenPosition(LogicalActorKey key, out ActorEntry actor,
        out System.Numerics.Vector2 screen)
    {
        screen = default;
        // Disappearance is normal for a display marker; do not generate an action warning each draw.
        if (!actorRegistry.TryGet(key, out actor))
            return false;

        ActorSnapshot? directGPose = null;
        if (ClientState.IsGPosing && actor.IsLocalPlayer
            && actorRegistry.TryGetGPoseLocalPlayer(key, out var resolvedGPose))
            directGPose = resolvedGPose;
        return ActorMarkerProjection.TryGetScreenPosition(actor, ClientState.IsGPosing,
            TryProjectMarkerRepresentation, out screen, directGPose);
    }

    private static unsafe bool TryProjectMarkerRepresentation(ActorSnapshot representation,
        out System.Numerics.Vector2 screen)
    {
        screen = default;
        var draw = GetVisibleMarkerDraw(representation);
        if (draw == null)
            return false;

        // The displayed model can differ from the game actor's height (which can be zero).
        // These bounds are already in world space, including the draw transform and scale.
        FFXIVClientStructs.FFXIV.Common.Math.AxisAlignedBounds bounds;
        draw->ComputeAxisAlignedBounds(&bounds);
        // Control.ViewProjectionMatrix used by GameGui can retain the field camera in cutscenes.
        // Read the camera that is rendering the current scene; do not retain a screen position.
        var cameraManager = FFXIVClientStructs.FFXIV.Client.Graphics.Scene.CameraManager.Instance();
        var camera = cameraManager == null ? null : cameraManager->CurrentCamera;
        var device = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device.Instance();
        if (camera == null || camera->RenderCamera == null || device == null)
            return false;
        return ActorMarkerProjection.TryProjectModelTop(bounds.Min, bounds.Max,
            camera->ViewMatrix * camera->RenderCamera->ProjectionMatrix,
            Dalamud.Interface.Utility.ImGuiHelpers.MainViewport.Pos,
            new System.Numerics.Vector2(device->Width, device->Height), out screen);
    }

    private static unsafe FFXIVClientStructs.FFXIV.Client.Graphics.Scene.DrawObject* GetVisibleMarkerDraw(
        ActorSnapshot representation)
    {
        var identity = representation.RepresentationKey;
        if (identity.TerritoryId != ClientState.TerritoryType)
            return null;
        var current = ObjectTable[identity.ObjectIndex];
        if (current is null || current.Address == nint.Zero
            || current.GameObjectId != identity.GameObjectId || current.EntityId != identity.EntityId)
            return null;
        var draw = ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)current.Address)->DrawObject;
        return draw != null && draw->IsVisible ? draw : null;
    }

    private int StoredPinCount => (Configuration.PinnedOutfits?.Count ?? 0)
        + (Configuration.PinnedOutfitsByCharacter?.Values.Sum(pins => pins?.Count ?? 0) ?? 0);

    private void SaveConfiguration(string reason)
    {
        PluginInterface.SavePluginConfig(Configuration);
        Log.Information("[PinPersistence] Saved Reason={Reason} PinCount={PinCount} Owner={Owner} OwnerPinCount={OwnerPinCount} IsDev={IsDev}",
            reason, StoredPinCount, PlateOwner, pinnedOutfitStore.Count, PluginInterface.IsDev);
    }

    private void ObservePinState(string phase)
    {
        var state = (Count: pinnedOutfitStore.Count, LoggedIn: ClientState.IsLoggedIn, Owner: PlateOwner);
        if (phase == "Framework" && lastPinObservation == state)
            return;
        lastPinObservation = state;
        Log.Information("[PinPersistence] State Phase={Phase} PinCount={PinCount} Owner={Owner} LoggedIn={LoggedIn} IsDev={IsDev}",
            phase, state.Count, state.Owner, state.LoggedIn, PluginInterface.IsDev);
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

    public bool RefreshSourceOutfitFromTarget(out string message)
        => TryGetCurrentTarget(out var target, out message)
            && bulkOutfitService.RefreshSource(target.LogicalKey, out message, target.RepresentationKey);

    public bool ImportGlamourPlate(int index, out string message)
    {
        if (!ClientState.IsLoggedIn)
        {
            message = Localizer.Get(TextKey.PlateSourceRequestFailed);
            return false;
        }
        ObserveGlamourPlateImport("import-click", index + 1);
        UpdateGlamourPlateCache();
        if (plateCache.Get(PlateOwner) != null)
            return CompleteGlamourPlateImport(index, out message);
        if (!plateCache.IsWaiting)
        {
            message = Localizer.Get(TextKey.PlateSourceNotCached);
            return false;
        }
        if (!plateImport.Start(index, PlayerState.ContentId, ClientState.TerritoryType,
            () => plateCache.IsWaiting))
        {
            message = Localizer.Get(TextKey.PlateSourceRequestFailed);
            return false;
        }
        message = Localizer.Get(TextKey.PlateSourceLoading, index + 1);
        return true;
    }

    public void CancelGlamourPlateImport()
    {
        plateImport.Cancel();
        ObserveGlamourPlateImport("cancelled");
        completedPlateImportMessage = Localizer.Get(TextKey.PlateSourceCancelled);
    }

    public string? TakePlateImportMessage()
    {
        var message = completedPlateImportMessage;
        completedPlateImportMessage = null;
        return message;
    }

    private void ProcessGlamourPlateImport()
    {
        if (!plateImport.IsPending)
            return;
        ObserveGlamourPlateImport("pending-poll");
        var result = plateImport.Poll(ClientState.IsLoggedIn ? PlayerState.ContentId : 0,
            ClientState.TerritoryType, plateCache.Get(PlateOwner) != null, out var index);
        if (result == PlateImportProgress.Ready)
            CompleteGlamourPlateImport(index, out completedPlateImportMessage);
        else if (result == PlateImportProgress.ContextChanged)
        {
            completedPlateImportMessage = Localizer.Get(TextKey.PlateSourceContextChanged);
            ObserveGlamourPlateImport("context-changed");
        }
        else if (result == PlateImportProgress.Waiting && !plateCache.IsWaiting)
        {
            plateImport.Cancel();
            completedPlateImportMessage = Localizer.Get(TextKey.PlateSourceUnavailable);
        }
    }

    private void OnPlateLogout(int type, int code)
    {
        ObservePinState("Logout");
        plateCache.EndSession();
        if (plateImport.IsPending)
        {
            plateImport.Cancel();
            completedPlateImportMessage = Localizer.Get(TextKey.PlateSourceContextChanged);
        }
    }

    private void UpdateGlamourPlateCache()
    {
        try
        {
            var owner = PlateOwner;
            plateCache.Update(owner, ClientState.TerritoryType,
                owner != 0 && GlamourPlateSource.CanRequest,
                owner != 0 && GlamourPlateSource.IsLoaded,
                owner != 0 && GlamourPlateSource.IsRequested,
                () =>
                {
                    var invoked = GlamourPlateSource.Request(SigScanner);
                    ObserveGlamourPlateImport("automatic-request-returned", nativeFunctionInvoked: invoked);
                    return invoked;
                }, GlamourPlateSource.Capture);
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Could not update saved glamour plates.");
        }
    }

    private void ObserveGlamourPlateImport(string phase, int? plateNumber = null, bool? nativeFunctionInvoked = null)
        => ObserveGlamourPlateImport(() => RecordGlamourPlateObservation(phase, plateNumber, nativeFunctionInvoked));

    internal static void ObserveGlamourPlateImport(System.Action observation)
    {
        try
        {
            observation();
        }
        catch (Exception exception)
        {
            Log?.Warning(exception, "Actor Morpher could not record the glamour plate observation.");
        }
    }

    private void RecordGlamourPlateObservation(string phase, int? plateNumber, bool? nativeFunctionInvoked)
    {
        if (!diagnosticRouter.IsEnabled)
            return;
        var state = GlamourPlateSource.Observe();
        if (phase == "pending-poll" && lastPlateObservationPhase == phase && lastPlateObservation == state)
            return;
        lastPlateObservation = state;
        lastPlateObservationPhase = phase;
        diagnosticRouter.Write(new DiagnosticLogEntry
        {
            EventId = DiagnosticEventIds.DiagnosticMarker,
            EventName = "GlamourPlateImportObserved",
            Category = DiagnosticCategory.BulkOutfit,
            Phase = phase,
            Message = "Glamour plate native state observed.",
            Properties = new Dictionary<string, object?>
            {
                ["territoryId"] = ClientState.TerritoryType,
                ["loggedIn"] = ClientState.IsLoggedIn,
                ["pending"] = plateImport.IsPending,
                ["plateNumber"] = plateNumber,
                ["nativeFunctionInvoked"] = nativeFunctionInvoked,
                ["managerAvailable"] = state.ManagerAddress != 0,
                ["managerAddress"] = Configuration.IncludeRawAddressesInDiagnostics ? $"0x{state.ManagerAddress:X}" : null,
                ["requestAddress"] = Configuration.IncludeRawAddressesInDiagnostics ? $"0x{GlamourPlateSource.RequestAddress:X}" : null,
                ["requested"] = state.Requested,
                ["loaded"] = state.Loaded,
                ["nonEmptyPlates"] = state.NonEmptyPlates,
                ["nonEmptyItems"] = state.NonEmptyItems,
                ["plateDataHash"] = state.DataHash,
            },
        });
    }

    private bool CompleteGlamourPlateImport(int index, out string message)
    {
        ObserveGlamourPlateImport("read-plate", index + 1);
        uint missingItem = 0;
        var saved = plateCache.Get(PlateOwner);
        if (saved == null || (uint)index >= (uint)saved.Plates.Length)
        {
            message = Localizer.Get(TextKey.PlateSourceNotCached);
            return false;
        }
        var plate = saved.Plates[index];
        if (!GlamourPlateSource.TryConvert(plate.Items, plate.Stain1, plate.Stain2,
            itemId => DataManager.GetExcelSheet<Item>().TryGetRow(
                Dalamud.Utility.ItemUtil.GetBaseId(itemId).ItemId, out var item) ? item.ModelMain : null,
            out var equipment, out missingItem))
        {
            message = missingItem == 0 ? Localizer.Get(TextKey.PlateSourceUnavailable)
                : Localizer.Get(TextKey.PlateSourceMissingItem, missingItem);
            return false;
        }
        bulkOutfitService.SetSourceEquipment(equipment);
        message = Localizer.Get(TextKey.PlateSourceImported, index + 1);
        return true;
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

    public bool StartOutfitToTarget(out string message)
    {
        if (!CanStartBulkOutfitInCurrentContext(out message)
            || !TryGetCurrentTarget(out var target, out message))
            return false;
        return bulkOutfitService.StartApplyToTarget(target, out message);
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
    public bool SelectHeadgearSetting(HeadgearSetting setting, bool value, LogicalActorKey? actor, out string message)
    {
        if (actor is not null && !CanStartBulkOutfitInCurrentContext(out message))
            return false;
        var started = bulkOutfitService.SelectHeadgearSetting(setting, value, actor, out message);
        if (started && actor is { } selectedActor)
            pendingHeadgearSetting = selectedActor;
        return started;
    }

    public bool SelectEquipment(EquipmentChoiceKey choice, LogicalActorKey? actor, out string message)
    {
        if (actor is not null && !CanStartBulkOutfitInCurrentContext(out message))
            return false;
        if (choice.IsWeapon)
        {
            if (!WeaponSelection.CanSelect(choice, GetEquipmentChoices(choice.Slot)))
            {
                message = Localizer[TextKey.WeaponJobMismatch];
                return false;
            }
            if (actor is not { } weaponActor || !actorResolver.TryResolve(weaponActor, out var target))
            {
                message = Localizer[TextKey.TargetActorUnavailable];
                return false;
            }
            weaponSelections.Enqueue((target, choice, null, null, false));
            WeaponSelectionActor = actor;
            message = WeaponSelectionStatus = Localizer[TextKey.WeaponSelectionQueued];
            return true;
        }
        var started = bulkOutfitService.SelectEquipment(choice, actor, out message);
        if (started && actor is { } selectedActor)
            pendingEquipmentSelection = (selectedActor, choice);
        return started;
    }

    public EquipmentChoice[] GetEquipmentChoices(int slot)
    {
        if (slot is 11 or 12)
            return GetWeaponChoices(slot);
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
                    (byte)(entry.Key.ModelKey >> 16)), entry.Value.Name, entry.Value.IconId)
                    { Items = GetEquipmentItemInfos(slot, entry.Key.ModelKey) });
        cached = choices.OrderBy(choice => choice.Name, GameTextComparison.GetComparer(ClientState.ClientLanguage)).ToArray();
        equipmentChoices[cacheKey] = cached;
        return cached;
    }

    public EquipmentChoice[] SearchEquipment(int slot, string query, EquipmentPickerFilter filter)
    {
        var choices = GetEquipmentChoices(slot);
        if (slot is 11 or 12)
            return EquipmentChoice.FilterForPicker(WeaponSelection.Filter(choices, Configuration.FavoriteEquipment, false),
                Configuration.FavoriteEquipment, filter, query, ClientState.ClientLanguage);
        var keys = choices.Select(choice => choice.Key).ToHashSet();
        var manualFavorites = Configuration.FavoriteEquipment.Where(key => key.Slot == slot && !keys.Contains(key))
            .Select(key => new EquipmentChoice(key, Localizer[TextKey.ManualEquipment], 0));
        return EquipmentChoice.FilterForPicker(choices.Concat(manualFavorites), Configuration.FavoriteEquipment,
            filter, query, ClientState.ClientLanguage);
    }

    public void ToggleEquipmentFavorite(EquipmentChoiceKey key)
    {
        if (!Configuration.FavoriteEquipment.Remove(key))
            Configuration.FavoriteEquipment.Add(key);
        Save();
    }

    private EquipmentChoice[] GetWeaponChoices(int slot)
    {
        var job = CurrentWeaponJob;
        if (job == 0)
            return [];
        var key = (ClientState.ClientLanguage, job, slot);
        if (weaponChoices.TryGetValue(key, out var cached))
            return cached;
        var abbreviation = DataManager.GetExcelSheet<ClassJob>(ClientLanguage.English).GetRow(job).Abbreviation.ToString();
        var categories = DataManager.GetExcelSheet<ClassJobCategory>(ClientLanguage.English)
            .Where(category => WeaponSelection.AllowsJob(category, abbreviation))
            .Select(category => category.RowId).ToHashSet();
        var choices = new List<EquipmentChoice>();
        foreach (var item in DataManager.GetExcelSheet<Item>(ClientState.ClientLanguage))
        {
            if (!categories.Contains(item.ClassJobCategory.RowId) || !item.EquipSlotCategory.IsValid)
                continue;
            var slots = item.EquipSlotCategory.Value;
            var model = WeaponSelection.ModelForSlot(slot, slots.MainHand == 1, slots.OffHand == 1,
                item.ModelMain, item.ModelSub);
            if (model != 0)
                choices.Add(new(new(slot, (ushort)model, 0, WeaponModel: model), item.Name.ToString(), item.Icon)
                    { Items = [CreateEquipmentItemInfo(item)] });
        }
        cached = choices.GroupBy(choice => choice.Key)
            .Select(group => group.First() with { Name = string.Join(" / ", group.Select(choice => choice.Name).Distinct()),
                Items = group.SelectMany(choice => choice.Items).ToArray() })
            .OrderBy(choice => choice.Name, GameTextComparison.GetComparer(ClientState.ClientLanguage)).ToArray();
        weaponChoices[key] = cached;
        return cached;
    }

    private void ProcessWeaponSelections()
    {
        while (weaponSelections.TryDequeue(out var selection))
        {
            WeaponSelectionActor = selection.Actor.LogicalKey;
            // Re-check the user-requested job restriction at execution, not only when opening the picker.
            if (selection.DyeChannel is null && !WeaponSelection.CanSelect(selection.Choice, GetEquipmentChoices(selection.Choice.Slot)))
            {
                WeaponSelectionStatus = Localizer[TextKey.WeaponJobMismatch];
                continue;
            }
            if (!actorResolver.TryResolve(selection.Actor.LogicalKey, selection.Actor.RepresentationKey, out var current)
                || actorRegistry.CaptureCurrentAppearance(current) is not { } appearance)
            {
                WeaponSelectionStatus = Localizer[TextKey.TargetActorUnavailable];
                continue;
            }
            if (selection.Choice.Slot < 10 && selection.DyeChannel is { } armorChannel)
            {
                if (!bulkOutfitService.TryApplyDye(current, (OutfitSlot)selection.Choice.Slot, armorChannel,
                    selection.Color, selection.ClearDye, out var outfit, out var confirmed))
                {
                    WeaponSelectionStatus = Localizer[TextKey.TargetActorUnavailable];
                    continue;
                }
                appearancePersistence.RecordOutfit(current, outfit);
                drawObjectInjector.EnablePersistentAppearance(current);
                if (appearancePersistence.GetModel(current.LogicalKey) is { } updatedModel)
                    actorRegistry.RecordAppliedAppearance(current.LogicalKey, current.RepresentationKey, updatedModel);
                if (!ShareNpcEquipment(current, EquipmentCopyData.FromOutfit(outfit))
                    && actorRegistry.TryGet(current.LogicalKey, out var dyedActor))
                    pinnedOutfitStore.UpdateSelectedEquipment(dyedActor, selection.Choice, outfit);
                WeaponSelectionStatus = Localizer[confirmed ? TextKey.DyeApplied : TextKey.DyeSavedUnconfirmed];
                continue;
            }
            var offhand = selection.Choice.Slot == 12;
            var dyes = offhand ? appearance.OffhandDyes : appearance.MainhandDyes;
            AppearanceData updated;
            ulong weapon;
            bool applied;
            var colorResult = new NativeEquipmentColors.ColorApplyResult();
            if (selection.DyeChannel is { } channel)
            {
                applied = outfitMemory.TryApplyWeaponDyes(current, offhand, channel, selection.Color,
                    selection.ClearDye, dyes, out weapon, out dyes, out colorResult);
                updated = offhand ? appearance with { Offhand = weapon, OffhandDyes = dyes }
                    : appearance with { Mainhand = weapon, MainhandDyes = dyes };
            }
            else
            {
                updated = WeaponSelection.Replace(appearance, selection.Choice);
                dyes = offhand ? updated.OffhandDyes : updated.MainhandDyes;
                weapon = offhand ? updated.Offhand!.Value : updated.Mainhand!.Value;
                applied = outfitMemory.TryApplyWeapon(current, offhand, weapon, dyes);
            }
            if (!applied)
            {
                WeaponSelectionStatus = Localizer[TextKey.TargetActorUnavailable];
                continue;
            }
            appearancePersistence.RecordWeapon(current, offhand, weapon, dyes);
            if (appearancePersistence.GetModel(current.LogicalKey) is not null)
                actorRegistry.RecordAppliedAppearance(current.LogicalKey, current.RepresentationKey, updated);
            drawObjectInjector.EnablePersistentAppearance(current);
            if (!ShareNpcEquipment(current) && actorRegistry.TryGet(current.LogicalKey, out var entry))
                pinnedOutfitStore.UpdateSelectedWeapon(entry, offhand, weapon, dyes);
            WeaponSelectionStatus = Localizer[selection.DyeChannel is null ? TextKey.WeaponSelectionApplied
                : colorResult.Applied ? TextKey.DyeApplied : TextKey.DyeSavedUnconfirmed];
        }
    }
    public void SetSourceColor(OutfitSlot slot, int channel, DyeColor? color)
        => bulkOutfitService.SetSourceColor(slot, channel, color);

    public bool SetActorWeaponDye(LogicalActorKey actor, bool offhand, int channel, DyeColor? color,
        bool clearDye, out string message)
        => QueueActorDye(actor, offhand ? 12 : 11, channel, color, clearDye, out message);

    public bool SetActorDye(LogicalActorKey actor, OutfitSlot slot, int channel, DyeColor? color, bool clearDye, out string message)
        => QueueActorDye(actor, (int)slot, channel, color, clearDye, out message);

    private bool QueueActorDye(LogicalActorKey actor, int slot, int channel, DyeColor? color,
        bool clearDye, out string message)
    {
        if (!CanStartBulkOutfitInCurrentContext(out message))
            return false;
        if (!actorResolver.TryResolve(actor, out var target))
        {
            message = Localizer[TextKey.TargetActorUnavailable];
            return false;
        }
        weaponSelections.Enqueue((target, new(slot, 0, 0), channel, color, clearDye));
        WeaponSelectionActor = actor;
        WeaponSelectionStatus = message = Localizer[TextKey.DyeChangeQueued];
        return true;
    }

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
            IsActorModified,
            IsOutfitPinned);

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
        => GetModelSearchEntries(ClientState.ClientLanguage);

    private IReadOnlyList<ModelSearchEntry> GetModelSearchEntries(ClientLanguage language)
    {
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

    public EquipmentItemDisplay? GetWeaponDisplay(ulong weapon)
    {
        var key = weapon & 0xFFFFFFFFFFFFUL;
        if (key == 0)
            return null;
        var language = ClientState.ClientLanguage;
        if (!weaponDisplayCaches.TryGetValue(language, out var cache))
        {
            var candidates = new List<(ulong Model, string Name, uint Icon)>();
            foreach (var item in DataManager.GetExcelSheet<Item>(language))
            {
                if (item.Name.IsEmpty || !item.EquipSlotCategory.IsValid)
                    continue;
                var slots = item.EquipSlotCategory.Value;
                if (slots.MainHand != 1 && slots.OffHand != 1)
                    continue;
                if (item.ModelMain != 0)
                    candidates.Add((item.ModelMain & 0xFFFFFFFFFFFFUL, item.Name.ToString(), (uint)item.Icon));
                if (item.ModelSub != 0)
                    candidates.Add((item.ModelSub & 0xFFFFFFFFFFFFUL, item.Name.ToString(), (uint)item.Icon));
            }
            cache = candidates.GroupBy(static item => item.Model).ToDictionary(
                static group => group.Key,
                group => new EquipmentItemDisplay(
                    string.Join(" / ", group.Select(static item => item.Name).Distinct(GameTextComparison.GetComparer(language))),
                    group.Select(static item => item.Icon).FirstOrDefault(static icon => icon != 0)));
            weaponDisplayCaches[language] = cache;
        }
        return cache.GetValueOrDefault(key);
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
        if (!TryGetCurrentTarget(out var snapshot, out message)
            || !appearanceApplyService.TryApply(snapshot, model.ModelAppearance, out operationId, out message))
            return false;
        modelSearchOperations.Add(operationId);
        return true;
    }

    private bool TryGetCurrentTarget(out ActorSnapshot snapshot, out string message)
    {
        snapshot = null!;
        var target = ClientState.IsGPosing ? TargetManager.GPoseTarget : TargetManager.Target;
        if (target is null)
        {
            message = Localizer.Get(TextKey.NoTargetSelected);
            return false;
        }
        snapshot = RegistryActorResolver.FindTarget(actorRegistry.Entries,
            target.ObjectIndex, target.GameObjectId, target.EntityId, ClientState.TerritoryType)!;
        if (snapshot is null)
        {
            message = Localizer.Get(TextKey.TargetActorUnavailable);
            return false;
        }
        message = string.Empty;
        return true;
    }

    public bool TryApplyModel(LogicalActorKey actor, ModelSearchEntry model, out Guid operationId, out string message)
    {
        restoreStatus = string.Empty;
        if (!appearanceApplyService.TryApply(actor, model.ModelAppearance, out operationId, out message))
            return false;
        modelSearchOperations.Add(operationId);
        return true;
    }

    public bool TryRestoreActor(LogicalActorKey actor, out string message)
        => TryRestoreActor(actor, out message, restoreWithinBatch: false);

    private bool TryRestoreActor(LogicalActorKey actor, out string message, bool restoreWithinBatch)
    {
        restoreStatus = string.Empty;
        pendingCustomizeEdits.Remove(actor);
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
                return CompleteActorRestore(actor, out message, outfitRestored: true);
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

    private bool CompleteActorRestore(LogicalActorKey actor, out string message, bool outfitRestored = false)
    {
        // Restore an applied local NPC model through the game redraw even if an
        // outfit was also restored. A pin alone does not require a model redraw.
        var outfitOnly = false;
        var weaponColorsConfirmed = true;
        if (!appearancePersistence.HasLocalModelSearchAppearance(actor)
            && actorResolver.TryResolve(actor, out var restored)
            && actorRegistry.CaptureCurrentAppearance(restored) is { } rendered
            && appearanceMemory.TryCapture(restored, out var gameAppearance)
            && PinnedOutfitStore.CanMaintainWithOutfit(rendered, gameAppearance)
            && (outfitRestored || ((appearancePersistence.HasWeaponOverride(actor) || appearancePersistence.GetModel(actor) is not null)
                && outfitMemory.TryCapture(restored, out var gameOutfit)
                && OutfitDataValueComparer.AreEqual(EquipmentDisplayFormatting.CreateHumanOutfit(rendered), gameOutfit))))
        {
            // Restore weapon stains/materials before removing their retained settings.
            if (!ApplyMatchingWeaponDyes(restored, rendered, gameAppearance, out weaponColorsConfirmed))
            {
                message = Localizer[TextKey.TargetActorUnavailable];
                return false;
            }
            outfitOnly = true;
        }
        actorRegistry.ClearManagedAppearance(actor);
        actorRegistry.TryGet(actor, out var pinTarget);
        appearancePersistence.Restore(actor, pinTarget);
        mainWindow.ClearCustomizeDraft(actor);
        UnpinActor(actor);
        if (outfitOnly)
        {
            message = weaponColorsConfirmed ? "Original outfit restored." : Localizer[TextKey.DyeRestoreUnconfirmed];
            restoreStatus = message;
            return true;
        }
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
            || IsOutfitModified(actor) || appearancePersistence.HasWeaponOverride(actor);

    public bool TryGetOutfitOverride(LogicalActorKey actor, out OutfitOverrideState state)
        => bulkOutfitService.Store.TryGet(actor, out state!);

    public bool IsOutfitPinned(LogicalActorKey actor)
        => actorRegistry.TryGet(actor, out var entry) && pinnedOutfitStore.IsPinned(entry);

    public HashSet<LogicalActorKey> GetPinnedActorKeysForDisplay(IReadOnlyList<ActorEntry> actors)
        => pinnedOutfitStore.GetPinnedActorKeysForDisplay(actors);

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

    internal int CountModifiedPinActors(Func<LogicalActorKey, bool> isModified)
    {
        var current = actorRegistry.Entries;
        var modified = current.Where(actor => isModified(actor.Key)).ToArray();
        return modified.Length + appearancePersistence.GetAbsentModifiedActors(current, modified).Count;
    }

    public string PinModifiedActors()
    {
        var result = PinModifiedActors(pinnedOutfitStore, appearancePersistence, actorRegistry.Entries,
            IsActorModified, CapturePinAppearance);
        return Localizer.Get(TextKey.AppearancePinsSaved, result.Pinned, result.Unavailable);
    }

    internal static (int Pinned, int Unavailable) PinModifiedActors(PinnedOutfitStore pins,
        ActorAppearancePersistence persistence, IReadOnlyList<ActorEntry> present,
        Func<LogicalActorKey, bool> isModified, Func<ActorEntry, AppearanceData?> capture)
    {
        var modified = present.Where(actor => isModified(actor.Key)).ToArray();
        var absent = persistence.GetAbsentModifiedActors(present, modified);
        var retained = absent.ToDictionary(actor => actor.Key, actor =>
        {
            var appearance = persistence.CaptureAbsentPinAppearance(actor);
            return appearance is null ? null : pins.ApplyCurrentNpcSharingToRetainedPin(actor, appearance) ?? appearance;
        });
        return pins.PinCurrent(absent.Concat(modified),
            actor => retained.TryGetValue(actor.Key, out var appearance) ? appearance : capture(actor));
    }

    public string UnpinAllActors()
    {
        Log.Information("[PinPersistence] UnpinAllRequested PinCount={PinCount}", pinnedOutfitStore.Count);
        var removed = UnpinAllActors(pinnedOutfitStore, appearancePersistence);
        failedPinnedOutfitReapplyStates.Clear();
        failedPinnedAppearanceStates.Clear();
        return Localizer.Get(TextKey.AppearancePinsRemoved, removed);
    }

    internal static int UnpinAllActors(PinnedOutfitStore pins, ActorAppearancePersistence persistence)
    {
        persistence.RememberSharedAppearanceForBulkPin(pins);
        return pins.UnpinAll();
    }

    private AppearanceData? CapturePinAppearance(ActorEntry actor)
    {
        if (!actorResolver.TryResolve(actor.Key, out var current))
            return null;
        var appearance = actorRegistry.CaptureCurrentAppearance(current);
        if (appearance is not null)
        {
            appearancePersistence.RecordModel(current, appearance, modelSearchWeaponJob: null, equipmentEdited: true);
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
        Guid operationId,
        LogicalActorKey actor,
        ActorRepresentationKey representation,
        AppearanceData applied,
        bool succeeded)
    {
        var maintenance = pinnedAppearanceOperationActor == actor;
        if (maintenance)
        {
            if (succeeded)
                failedPinnedAppearanceStates.Remove(actor);
            else if (pinnedAppearanceOperationObservedState is { } failedState)
                failedPinnedAppearanceStates[actor] = failedState;
            pinnedAppearanceOperationActor = null;
            pinnedAppearanceOperationObservedState = null;
            nextPinnedOutfitScanTick = Environment.TickCount64 + 500;
        }
        if (HandleEquipmentOperationCompleted(operationId, actor, representation, applied, succeeded, maintenance))
            return;
        var modelSearchApply = modelSearchOperations.Remove(operationId);
        var customizeEdit = customizeOperations.Remove(operationId, out var requestId);
        try
        {
            if (actorResolver.TryResolve(actor, representation, out var current)
                && CanPublishAppearanceCompletion(succeeded, representation, current))
            {
                var weaponJob = modelSearchApply && current.IsLocalPlayer
                    ? (byte?)(ObjectTable.LocalPlayer?.ClassJob.RowId ?? current.ClassJob)
                    : null;
                var appearance = CreatePublishedAppearance(appearancePersistence.ResolveCustomizeEquipment(current, applied));
                if (PublishAppliedAppearance(current, appearance, customizeEdit, weaponJob)
                    && modelSearchApply)
                {
                    var pending = pendingCustomizeEdits.GetLatest(actor, representation);
                    appearancePersistence.RebaseOriginalCustomize(current,
                        pending is not null && CustomizeCopyData.TryCreate(applied, out var original) ? original : null);
                    mainWindow.RebaseCustomizeDraft(current, applied, pending);
                }
            }
        }
        finally
        {
            if (customizeEdit)
                CustomizeOperationCompleted?.Invoke(requestId, succeeded);
        }
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
        var maintenance = pinnedOutfitOperationActor == actor;
        var shared = !maintenance && succeeded && type is BulkOperationType.ApplyOutfit or BulkOperationType.UnequipAll
            && bulkOutfitService.TryResolveOperationActor(actor, out var sharedSource)
            && desired is not null && ShareNpcEquipment(sharedSource, EquipmentCopyData.FromOutfit(desired));
        if (pendingHeadgearSetting == actor)
        {
            pendingHeadgearSetting = null;
            if (!shared && type == BulkOperationType.ApplyOutfit && succeeded && desired is not null
                && actorRegistry.TryGet(actor, out var selectedActor))
                pinnedOutfitStore.UpdateHeadgearSettings(selectedActor, desired);
        }
        if (pendingEquipmentSelection is { } selection && selection.Actor == actor)
        {
            pendingEquipmentSelection = null;
            if (!shared && type == BulkOperationType.ApplyOutfit && succeeded && desired is not null
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

            if (!CompleteActorRestore(actor, out var redrawMessage, outfitRestored: true))
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
            && bulkOutfitService.TryResolveOperationActor(actor, out var current))
        {
            appearancePersistence.RecordOutfit(current, desired);
            drawObjectInjector.EnablePersistentAppearance(current);
            if (appearancePersistence.GetRetainedAppearance(current) is { } updated)
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

    private void OnLocalPlayerClassJobChanged(uint _) => FollowLocalPlayerHeadSettings();

    private void OnLocalPlayerInventoryChanged(IReadOnlyCollection<InventoryEventArgs> events)
    {
        if (events.Any(IsEquippedAppearanceChange))
            FollowLocalPlayerHeadSettings();
    }

    private static bool IsEquippedAppearanceChange(InventoryEventArgs change)
    {
        if (change.Item.ContainerType != GameInventoryType.EquippedItems)
            return false;
        if (change is not InventoryItemChangedArgs changed)
            return change.Type is GameInventoryEvent.Added or GameInventoryEvent.Removed;

        ref readonly var oldItem = ref changed.OldItemState;
        ref readonly var newItem = ref changed.Item;
        return oldItem.ItemId != newItem.ItemId || oldItem.GlamourId != newItem.GlamourId
            || !oldItem.Stains.SequenceEqual(newItem.Stains);
    }

    private void FollowLocalPlayerHeadSettings()
    {
        var local = actorRegistry.Entries.FirstOrDefault(static actor => actor.IsLocalPlayer);
        if (local is null || ObjectTable.LocalPlayer?.GameObjectId != local.GameObjectId
            || pinnedOutfitStore.IsPinned(local)
            || !appearancePersistence.ReleaseModelSearchHeadSettings(local.Key, out var appearance))
            return;

        var current = local.Current;
        if (current.CurrentAppearance is { } rendered)
            actorRegistry.RecordAppliedAppearance(local.Key, current.RepresentationKey,
                rendered with { UsesGameHeadSettings = true });
        outfitMemory.ApplyGameHeadSettings(current, appearance);
    }

    private void OnPluginFrameworkUpdate(IFramework framework)
    {
        var owner = PlateOwner;
        if (Configuration.MigrateLegacyPins(owner))
            SaveConfiguration("CharacterPinMigration");
        ObservePinState("Framework");
        ProcessPendingCustomizeEdits();
        ProcessWeaponSelections();
        UpdateGlamourPlateCache();
        ProcessGlamourPlateImport();
        EnsureCommandsRegistered();
        var now = Environment.TickCount64;
        if (pinnedOutfitOwner != owner || pinnedOutfitTerritory != ClientState.TerritoryType || !ClientState.IsLoggedIn)
        {
            pinnedOutfitOwner = owner;
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
        if ((pinnedOutfitStore.Count == 0 && !pinnedOutfitStore.HasSharedNpcEquipment)
            || pinnedOutfitOperationActor is not null
            || pinnedAppearanceOperationActor is not null
            || bulkOutfitService.CurrentOperation is not null)
            return false;

        var actors = actorRegistry.Entries;
        foreach (var actor in pinnedActorDiscovery.GetCandidates(actors,
                     actor => pinnedOutfitStore.IsPinned(actor) || pinnedOutfitStore.IsNpcEquipmentSharingCandidate(actor),
                     now, ref nextPinnedOutfitScanTick))
        {
            OutfitData desired;
            OutfitData current;
            var shareEquipment = pinnedOutfitStore.TryGetSharedNpcEquipment(actor, out var sharedEquipment);
            var shareCustomize = pinnedOutfitStore.TryGetSharedNpcCustomize(actor, out var sharedCustomize);
            var hasPinnedAppearance = pinnedOutfitStore.TryGetAppearanceForCurrentJob(actor, out var pinnedAppearance);
            if (hasPinnedAppearance || shareEquipment || shareCustomize)
            {
                if (!actorResolver.TryResolve(actor.Key, out var representation)
                    || (shareCustomize ? actorRegistry.CaptureCustomizeAppearance(representation)
                        : actorRegistry.CaptureCurrentAppearance(representation)) is not { } rendered
                    || appearanceApplyService.IsPending(actor.Key))
                    continue;
                var individualOutfit = !hasPinnedAppearance && pinnedOutfitStore.TryGet(actor, out var storedOutfit)
                    ? storedOutfit : null;
                pinnedAppearance = ComposeSharedNpcAppearance(rendered, pinnedAppearance, individualOutfit,
                    shareEquipment ? sharedEquipment : null, shareCustomize ? sharedCustomize : null);
                if ((shareEquipment || shareCustomize) && !pinnedOutfitStore.WasSharedNpcChangeAttempted(actor))
                {
                    // A completion for an older request must not block this new edit.
                    failedPinnedAppearanceStates.Remove(actor.Key);
                    failedPinnedOutfitReapplyStates.Remove(actor.Key);
                }
                if (failedPinnedAppearanceStates.TryGetValue(actor.Key, out var failed)
                    && PinnedOutfitStore.AppearanceEquals(rendered, failed))
                    continue;
                if (shareCustomize && CanApplySharedCustomizeDirectly(rendered, pinnedAppearance))
                {
                    pinnedOutfitStore.MarkSharedNpcEquipmentAttempted(actor);
                    if (!TryApplySharedCustomizeDirectly(representation, rendered, sharedCustomize, out var applied))
                    {
                        failedPinnedAppearanceStates[actor.Key] = rendered;
                        continue;
                    }
                    rendered = applied;
                }
                if (PinnedOutfitStore.AppearanceEquals(rendered, pinnedAppearance))
                {
                    pinnedOutfitStore.MarkSharedNpcEquipmentAttempted(actor);
                    continue;
                }
                if (!PinnedOutfitStore.CanMaintainWithOutfit(rendered, pinnedAppearance)
                    || EquipmentDisplayFormatting.CreateHumanOutfit(rendered) is not { } renderedOutfit
                    || !(shareEquipment ? AssignSharedOutfit(sharedEquipment, out desired)
                        : pinnedOutfitStore.TryGet(actor, out desired)))
                {
                    pinnedOutfitStore.MarkSharedNpcEquipmentAttempted(actor);
                    var started = !hasPinnedAppearance && shareEquipment && !shareCustomize
                        ? TryPasteEquipment(representation, sharedEquipment, out _, out _)
                        : appearanceApplyService.TryApply(actor.Key, pinnedAppearance, out _);
                    if (!started)
                        continue;
                    pinnedAppearanceOperationActor = actor.Key;
                    pinnedAppearanceOperationObservedState = rendered;
                    return true;
                }
                // An equipment-only difference uses the existing slot operation and its
                // completion/failure handling, preserving the cutscene's animated model.
                pinnedOutfitStore.MarkSharedNpcEquipmentAttempted(actor);
                if (!ApplyMatchingWeaponDyes(representation, rendered, pinnedAppearance, out _))
                {
                    failedPinnedAppearanceStates[actor.Key] = rendered;
                    continue;
                }
                current = renderedOutfit;
            }
            else if (actor.Current.Race is null
                || !pinnedOutfitStore.TryGet(actor, out desired)
                || !bulkOutfitService.TryCaptureOutfit(actor.Key, out current))
                continue;

            desired = desired.WithCurrentEarVisibility(current);
            if (failedPinnedOutfitReapplyStates.TryGetValue(actor.Key, out var failedState))
            {
                if (OutfitDataValueComparer.AreEqual(current, failedState))
                    continue;
                failedPinnedOutfitReapplyStates.Remove(actor.Key);
            }
            if (OutfitDataValueComparer.AreEqual(current, desired))
            {
                pinnedOutfitStore.MarkSharedNpcEquipmentAttempted(actor);
                continue;
            }

            pinnedOutfitStore.MarkSharedNpcEquipmentAttempted(actor);
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
            // Tracking is set above before the normal service can synchronously report completion.
            // This is only the operation just queued here, never an unrelated user batch.
            bulkOutfitService.ProcessNextFrame();
            return true;
        }
        return false;
    }

    private bool ApplyMatchingWeaponDyes(ActorSnapshot actor, AppearanceData current, AppearanceData desired,
        out bool confirmed)
    {
        confirmed = true;
        for (var hand = 0; hand < 2; ++hand)
        {
            var offhand = hand == 1;
            var weapon = offhand ? desired.Offhand : desired.Mainhand;
            var oldWeapon = offhand ? current.Offhand : current.Mainhand;
            var dyes = offhand ? desired.OffhandDyes : desired.MainhandDyes;
            var oldDyes = offhand ? current.OffhandDyes : current.MainhandDyes;
            if (weapon is null || (weapon == oldWeapon && dyes == oldDyes))
                continue;
            if (!outfitMemory.TryApplyPinnedWeaponDyes(actor, offhand, weapon.Value, dyes, out var result))
                return false;
            confirmed &= result.Applied;
            appearancePersistence.RecordWeapon(actor, offhand, weapon.Value, dyes);
            drawObjectInjector.EnablePersistentAppearance(actor);
        }
        return true;
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

    internal static string CreateAppearanceSignature(AppearanceData appearance)
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
