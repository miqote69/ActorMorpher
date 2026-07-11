namespace ActorMorpher.Actors;

public sealed class ActorIdentityService
{
    private readonly IDiagnosticLog diagnostics;

    public ActorIdentityService(IDiagnosticLog? diagnostics = null)
        => this.diagnostics = diagnostics ?? NullDiagnosticLog.Instance;

    public bool Matches(LogicalActorKey expected, ActorSnapshot current)
        => expected == current.LogicalKey
        && expected.GameObjectId == current.RepresentationKey.GameObjectId
        && expected.EntityId == current.RepresentationKey.EntityId;

    public bool TryResolve(ActorRegistry registry, LogicalActorKey key, out ActorEntry actor)
    {
        var resolved = registry.TryGet(key, out actor);
        if (!resolved)
            diagnostics.Write(new DiagnosticLogEntry
            {
                Level = DiagnosticLogLevel.Warning,
                EventId = DiagnosticEventIds.ActorUnavailable,
                Category = DiagnosticCategory.ActorIdentity,
                Message = "Logical actor could not be resolved.",
                ActorKey = DiagnosticActorKeys.Format(diagnostics, key),
            });
        return resolved;
    }
}
