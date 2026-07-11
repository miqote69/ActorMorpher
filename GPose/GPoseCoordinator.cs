using Dalamud.Plugin.Services;

namespace ActorMorpher.GPose;

public sealed class GPoseCoordinator : IDisposable
{
    private const int RepresentationWaitFrames = 15;
    private const int TimeoutFrames = 180;

    private readonly IFramework framework;
    private readonly IClientState clientState;
    private readonly ActorRegistry registry;
    private readonly GPoseMappingResolver mappingResolver = new();
    private IReadOnlyList<ActorEntry> normalActors = Array.Empty<ActorEntry>();
    private int stateFrames;

    public GPoseCoordinator(IFramework framework, IClientState clientState, ActorRegistry registry)
    {
        this.framework = framework;
        this.clientState = clientState;
        this.registry = registry;
        State = clientState.IsGPosing ? GPoseState.Entering : GPoseState.Outside;
        framework.Update += OnFrameworkUpdate;
    }

    public GPoseState State { get; private set; }
    public int MappingCount { get; private set; }
    public string Status { get; private set; } = string.Empty;

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
                State = GPoseState.Exiting;
                registry.ClearGPoseMappings();
                MappingCount = 0;
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
            return;
        }

        switch (State)
        {
            case GPoseState.Outside:
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
                var mappings = mappingResolver.Resolve(normalActors, registry.Entries);
                registry.SetGPoseMappings(mappings);
                MappingCount = mappings.Count;
                State = GPoseState.ApplyingOverrides;
                break;
            case GPoseState.ApplyingOverrides:
                Status = $"Mapped {MappingCount} GPose representations.";
                State = GPoseState.Ready;
                break;
        }
    }
}
