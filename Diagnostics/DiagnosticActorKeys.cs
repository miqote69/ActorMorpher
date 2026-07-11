namespace ActorMorpher.Diagnostics;

public static class DiagnosticActorKeys
{
    public static string Format(IDiagnosticLog diagnostics, LogicalActorKey key)
        => new DiagnosticRedactor(false, false, diagnostics.SessionId).ActorKey(key);
}
