namespace ActorMorpher.Diagnostics;

public sealed record DiagnosticLogSettings(
    FileDiagnosticMode Mode,
    bool IncludeActorNames,
    bool IncludeRawAddresses,
    bool MirrorBesideAssembly,
    int RetentionDays,
    int MaximumSessions,
    int MaximumFileSizeMb,
    int MaximumTotalSizeMb);
