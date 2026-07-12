using Dalamud.Plugin.Services;

namespace ActorMorpher.Preview;

public sealed class ModelPreviewController : IModelPreviewBackend
{
    public static readonly TimeSpan SelectionDebounce = TimeSpan.FromMilliseconds(200);
    public static readonly TimeSpan VisibilityTimeout = TimeSpan.FromMilliseconds(750);

    private readonly IModelPreviewBackend backend;
    private readonly IClientContext context;
    private readonly IDiagnosticLog diagnostics;
    private readonly Func<DateTimeOffset> getNow;
    private readonly IFramework? framework;
    private ModelSearchEntry? requestedModel;
    private ModelPreviewSelectionKey? requestedKey;
    private DateTimeOffset dispatchAfter = DateTimeOffset.MaxValue;
    private DateTimeOffset lastVisibleAt = DateTimeOffset.MinValue;
    private uint lastTerritory;
    private bool wasLoggedIn;
    private bool active;
    private bool dispatched;
    private bool disposed;
    private long generation;

    public ModelPreviewController(
        IFramework framework,
        IModelPreviewBackend backend,
        IClientContext context,
        IDiagnosticLog? diagnostics = null)
        : this(backend, context, diagnostics)
    {
        this.framework = framework;
        framework.Update += OnFrameworkUpdate;
    }

    public ModelPreviewController(
        IModelPreviewBackend backend,
        IClientContext context,
        IDiagnosticLog? diagnostics = null,
        Func<DateTimeOffset>? getNow = null)
    {
        this.backend = backend;
        this.context = context;
        this.diagnostics = diagnostics ?? NullDiagnosticLog.Instance;
        this.getNow = getNow ?? (() => DateTimeOffset.UtcNow);
        lastTerritory = context.TerritoryId;
        wasLoggedIn = context.IsLoggedIn;
    }

    public ModelPreviewSnapshot Snapshot { get; private set; }
        = new(0, ModelPreviewState.Idle, null, "Select a model to inspect its preview status.");

    public void SetActive(bool value)
    {
        if (disposed)
            return;

        var now = getNow();
        if (value)
            lastVisibleAt = now;
        if (active == value)
            return;

        active = value;
        if (!active)
        {
            Suspend("The model preview is suspended while its UI is not visible.", "UiInactive");
            return;
        }

        if (requestedModel is not null)
            Schedule(now, "UiActivated");
    }

    public void Select(ModelSearchEntry? model)
    {
        if (disposed)
            return;

        var nextKey = ModelPreviewSelectionKey.From(model);
        if (requestedKey == nextKey)
            return;

        requestedModel = model;
        requestedKey = nextKey;
        generation++;
        CancelBackend();

        diagnostics.Write(new DiagnosticLogEntry
        {
            EventId = DiagnosticEventIds.PreviewSelectionRequested,
            Category = DiagnosticCategory.ModelSearch,
            Message = model is null ? "Model preview selection cleared." : "Model preview selection requested.",
            Properties = PreviewProperties(model, "SelectionChanged"),
        });

        if (model is null)
        {
            Publish(ModelPreviewState.Idle, null, "Select a model to inspect its preview status.", "SelectionCleared");
            return;
        }

        if (!active || !context.IsLoggedIn)
        {
            Publish(ModelPreviewState.Suspended, model.ModelId,
                "The model preview is suspended until its UI and the game session are active.", "SelectionSuspended");
            return;
        }

        Schedule(getNow(), "SelectionChanged");
    }

    public void Process()
    {
        if (disposed)
            return;

        var now = getNow();
        if (active && now - lastVisibleAt > VisibilityTimeout)
        {
            active = false;
            Suspend("The model preview is suspended while its UI is not visible.", "UiHeartbeatExpired");
        }

        var loggedIn = context.IsLoggedIn;
        var territory = context.TerritoryId;
        var loginChanged = loggedIn != wasLoggedIn;
        var territoryChanged = territory != lastTerritory;
        wasLoggedIn = loggedIn;
        lastTerritory = territory;

        if (!loggedIn)
        {
            if (loginChanged || dispatched)
                Suspend("The model preview is suspended while logged out.", "LoggedOut");
            return;
        }

        if (loginChanged || territoryChanged)
        {
            Suspend("The model preview is waiting for the current territory to settle.",
                loginChanged ? "LoggedIn" : "TerritoryChanged");
            if (active && requestedModel is not null)
                Schedule(now, loginChanged ? "LoggedIn" : "TerritoryChanged");
            return;
        }

        if (!active || requestedModel is null)
            return;

        if (!dispatched)
        {
            if (now < dispatchAfter)
                return;
            Dispatch();
            return;
        }

        SyncBackendSnapshot();
    }

    public void ResetCamera()
    {
        if (disposed || Snapshot.State != ModelPreviewState.Ready)
            return;

        try
        {
            backend.ResetCamera();
        }
        catch (Exception exception)
        {
            diagnostics.Error(DiagnosticEventIds.PreviewStateChanged, DiagnosticCategory.ModelSearch,
                "Model preview camera reset failed.", exception, PreviewProperties(requestedModel, "ResetCamera"));
            Publish(ModelPreviewState.Failed, requestedModel?.ModelId,
                "The model preview camera could not be reset.", "ResetCameraFailed");
        }
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        if (framework is not null)
            framework.Update -= OnFrameworkUpdate;
        CancelBackend();
        try
        {
            backend.Dispose();
        }
        catch (Exception exception)
        {
            diagnostics.Error(DiagnosticEventIds.PreviewStateChanged, DiagnosticCategory.ModelSearch,
                "Model preview backend disposal failed.", exception,
                PreviewProperties(requestedModel, "DisposeFailed"));
        }
    }

    private void OnFrameworkUpdate(IFramework _)
        => Process();

    private void Schedule(DateTimeOffset now, string reason)
    {
        CancelBackend();
        dispatchAfter = now + SelectionDebounce;
        Publish(ModelPreviewState.Loading, requestedModel?.ModelId,
            "Preparing the selected model preview...", reason);
    }

    private void Dispatch()
    {
        var dispatchGeneration = generation;
        var model = requestedModel;
        try
        {
            backend.Select(model);
            if (dispatchGeneration != generation || requestedKey != ModelPreviewSelectionKey.From(model))
            {
                backend.Select(null);
                return;
            }
            dispatched = true;
            dispatchAfter = DateTimeOffset.MaxValue;
            SyncBackendSnapshot();
        }
        catch (Exception exception)
        {
            dispatched = false;
            diagnostics.Error(DiagnosticEventIds.PreviewStateChanged, DiagnosticCategory.ModelSearch,
                "Model preview backend rejected the selected model.", exception,
                PreviewProperties(model, "DispatchFailed"));
            Publish(ModelPreviewState.Failed, model?.ModelId,
                "The selected model preview could not be created.", "DispatchFailed");
        }
    }

    private void SyncBackendSnapshot()
    {
        var backendSnapshot = backend.Snapshot;
        Publish(backendSnapshot.State, requestedModel?.ModelId, backendSnapshot.Status, "BackendState");
    }

    private void Suspend(string status, string reason)
    {
        CancelBackend();
        dispatchAfter = DateTimeOffset.MaxValue;
        if (requestedModel is null)
            Publish(ModelPreviewState.Idle, null, "Select a model to inspect its preview status.", reason);
        else
            Publish(ModelPreviewState.Suspended, requestedModel.ModelId, status, reason);
    }

    private void CancelBackend()
    {
        dispatched = false;
        try
        {
            backend.Select(null);
        }
        catch (Exception exception)
        {
            diagnostics.Error(DiagnosticEventIds.PreviewStateChanged, DiagnosticCategory.ModelSearch,
                "Model preview backend release failed.", exception,
                PreviewProperties(requestedModel, "ReleaseFailed"));
        }
    }

    private void Publish(ModelPreviewState state, uint? modelId, string status, string reason)
    {
        if (Snapshot.Generation == generation
            && Snapshot.State == state
            && Snapshot.ModelId == modelId
            && string.Equals(Snapshot.Status, status, StringComparison.Ordinal))
            return;

        Snapshot = new(generation, state, modelId, status);
        diagnostics.Write(new DiagnosticLogEntry
        {
            EventId = DiagnosticEventIds.PreviewStateChanged,
            Category = DiagnosticCategory.ModelSearch,
            Message = "Model preview state changed.",
            Outcome = state.ToString(),
            Properties = PreviewProperties(requestedModel, reason),
        });
    }

    private IReadOnlyDictionary<string, object?> PreviewProperties(ModelSearchEntry? model, string reason)
        => new Dictionary<string, object?>
        {
            ["generation"] = generation,
            ["reason"] = reason,
            ["modelCharaId"] = model?.ModelId,
            ["category"] = model?.Category,
            ["source"] = model?.Source,
            ["sourceRowId"] = model?.SourceId,
            ["active"] = active,
            ["loggedIn"] = context.IsLoggedIn,
            ["territoryId"] = context.TerritoryId,
        };
}
