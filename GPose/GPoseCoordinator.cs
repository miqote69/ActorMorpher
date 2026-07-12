using Dalamud.Plugin.Services;

namespace ActorMorpher.GPose;

public sealed class GPoseCoordinator : IDisposable
{
    private const int RepresentationWaitFrames = 15;
    private const int TimeoutFrames = 180;

    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly ActorRegistry registry;
    private readonly IDiagnosticLog diagnostics;
    private readonly GPoseMappingResolver mappingResolver = new();
    private IReadOnlyList<ActorEntry> normalActors = Array.Empty<ActorEntry>();
    private int stateFrames;

    public GPoseCoordinator(IFramework framework, IClientState clientState, ActorRegistry registry, IDiagnosticLog? diagnostics = null)
    {
        this.framework = framework;
        this.clientState = clientState;
        this.registry = registry;
        this.diagnostics = diagnostics ?? NullDiagnosticLog.Instance;
        State = clientState.IsGPosing ? GPoseState.Entering : GPoseState.Outside;
        framework.Update += OnFrameworkUpdate;
    }

    public GPoseState State { get; private set; }
    public int MappingCount { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public event Action? MappingsReady;
    public event Action? Exited;

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
        registry.ClearGPoseMappings();
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!clientState.IsGPosing)
        {
            if (State != GPoseState.Outside)
            {
                diagnostics.Write(new DiagnosticLogEntry
                {
                    EventId = DiagnosticEventIds.GPoseExited,
                    Category = DiagnosticCategory.GPose,
                    Message = "GPose exited.",
                    Properties = new Dictionary<string, object?> { ["mappingCount"] = MappingCount },
                });
                State = GPoseState.Exiting;
                registry.ClearGPoseMappings();
                MappingCount = 0;
                Exited?.Invoke();
            }

            normalActors = registry.Entries;
            State = GPoseState.Outside;
            stateFrames = 0;
            return;
        }

        stateFrames++;
        if (stateFrames > TimeoutFrames && State != GPoseState.Ready)
        {
            registry.ClearGPoseMappings();
            State = GPoseState.TimedOut;
            Status = "GPose representation mapping timed out.";
            diagnostics.Write(new DiagnosticLogEntry
            {
                Level = DiagnosticLogLevel.Error,
                EventId = DiagnosticEventIds.GPoseOperationFailed,
                Category = DiagnosticCategory.GPose,
                Message = Status,
                Outcome = "Failed",
                Properties = new Dictionary<string, object?> { ["timeoutFrames"] = TimeoutFrames, ["mappingCount"] = MappingCount },
            });
            return;
        }

        switch (State)
        {
            case GPoseState.Outside:
                diagnostics.Write(new DiagnosticLogEntry
                {
                    EventId = DiagnosticEventIds.GPoseEntered,
                    Category = DiagnosticCategory.GPose,
                    Message = "GPose entered.",
                });
                State = GPoseState.Entering;
                stateFrames = 0;
                break;
            case GPoseState.Entering:
                State = GPoseState.WaitingForRepresentations;
                break;
            case GPoseState.WaitingForRepresentations when stateFrames >= RepresentationWaitFrames:
                State = GPoseState.ResolvingMappings;
                break;
            case GPoseState.ResolvingMappings:
                var mappings = mappingResolver.Resolve(normalActors, registry.Entries).ToDictionary();
                var localSource = normalActors.FirstOrDefault(static actor => actor.IsLocalPlayer);
                ActorSnapshot? directLocalPlayer = null;
                var directLocalPlayerFound = localSource is not null
                    && registry.TryGetGPoseLocalPlayer(localSource.Key, out directLocalPlayer);
                if (directLocalPlayerFound)
                    mappings[directLocalPlayer!.RepresentationKey] = localSource!.Key;
                registry.SetGPoseMappings(mappings);
                MappingCount = mappings.Count;
                diagnostics.Write(new DiagnosticLogEntry
                {
                    EventId = DiagnosticEventIds.GPoseMappingResolved,
                    Category = DiagnosticCategory.GPose,
                    Message = "GPose representation mapping resolved.",
                    Properties = new Dictionary<string, object?>
                    {
                        ["mappingCount"] = MappingCount,
                        ["normalActorCount"] = normalActors.Count,
                        ["currentActorCount"] = registry.Entries.Count,
                        ["currentRepresentationCount"] = registry.Entries.Sum(static actor => actor.Representations.Count),
                        ["directLocalPlayerFound"] = directLocalPlayerFound,
                        ["directLocalPlayerIndex"] = directLocalPlayerFound
                            ? directLocalPlayer!.RepresentationKey.ObjectIndex
                            : null,
                    },
                });
                State = GPoseState.ApplyingOverrides;
                break;
            case GPoseState.ApplyingOverrides:
                Status = $"Mapped {MappingCount} GPose representations.";
                State = GPoseState.Ready;
                MappingsReady?.Invoke();
                break;
        }
    }
}
