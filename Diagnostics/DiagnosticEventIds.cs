namespace ActorMorpher.Diagnostics;

public static class DiagnosticEventIds
{
    public const string PluginStarted = "AM0001";
    public const string PluginStopping = "AM0002";
    public const string PluginStopped = "AM0003";
    public const string ConfigurationLoaded = "AM0010";
    public const string ConfigurationSaved = "AM0011";
    public const string DiagnosticModeChanged = "AM0012";
    public const string UserActionRequested = "AM1001";
    public const string UserActionRejected = "AM1002";
    public const string ActorRegistryChanged = "AM2001";
    public const string ActorResolved = "AM2101";
    public const string ActorIdentityMismatch = "AM2102";
    public const string ActorUnavailable = "AM2103";
    public const string ActorValidationPassed = "AM2104";
    public const string ActorValidationFailed = "AM2105";
    public const string MorphOperationStarted = "AM3001";
    public const string MorphSnapshotCaptured = "AM3002";
    public const string MorphDesiredUpdated = "AM3003";
    public const string MorphApplied = "AM3004";
    public const string MorphRestored = "AM3005";
    public const string MorphOperationFailed = "AM3099";
    public const string RedrawOperationStarted = "AM4001";
    public const string RedrawStateChanged = "AM4002";
    public const string RedrawCompleted = "AM4003";
    public const string RedrawCancelled = "AM4004";
    public const string DrawObjectCreateInjected = "AM4005";
    public const string RedrawFailed = "AM4099";
    public const string GPoseEntered = "AM5001";
    public const string GPoseExited = "AM5002";
    public const string GPoseMappingResolved = "AM5003";
    public const string GPoseMappingAmbiguous = "AM5004";
    public const string GPoseOperationFailed = "AM5099";
    public const string BulkBatchStarted = "AM6001";
    public const string BulkTargetResolved = "AM6002";
    public const string OutfitSnapshotCaptured = "AM6003";
    public const string OutfitApplied = "AM6004";
    public const string OutfitSkipped = "AM6005";
    public const string OutfitRolledBack = "AM6006";
    public const string BulkBatchCompleted = "AM6007";
    public const string BulkBatchCancelled = "AM6008";
    public const string BulkActorFailed = "AM6099";
    public const string UnequipBatchStarted = "AM6101";
    public const string RestoreBatchStarted = "AM6201";
    public const string TroubleshootingCaptureStarted = "AM7001";
    public const string TroubleshootingCaptureEnded = "AM7002";
    public const string DiagnosticSnapshotCreated = "AM7003";
    public const string PreviewAssetsResolved = "AM7101";
    public const string PreviewSelectionRequested = "AM7102";
    public const string PreviewStateChanged = "AM7103";
    public const string SlowOperation = "AM8001";
    public const string LogEventsDropped = "AM8002";
    public const string DiagnosticMarker = "AM8003";
    public const string RepeatedEventsSuppressed = "AM8004";
    public const string HandledException = "AM9001";
    public const string FileLoggingUnavailable = "AM9002";
    public const string DiagnosticExportFailed = "AM9003";
    public const string UnexpectedPluginException = "AM9099";

    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>
    {
        [PluginStarted] = "PluginStarted", [PluginStopping] = "PluginStopping", [PluginStopped] = "PluginStopped",
        [ConfigurationLoaded] = "ConfigurationLoaded", [ConfigurationSaved] = "ConfigurationSaved",
        [DiagnosticModeChanged] = "DiagnosticModeChanged", [UserActionRequested] = "UserActionRequested",
        [UserActionRejected] = "UserActionRejected", [ActorRegistryChanged] = "ActorRegistryChanged",
        [ActorResolved] = "ActorResolved", [ActorIdentityMismatch] = "ActorIdentityMismatch",
        [ActorUnavailable] = "ActorUnavailable", [ActorValidationPassed] = "ActorValidationPassed",
        [ActorValidationFailed] = "ActorValidationFailed", [MorphOperationStarted] = "MorphOperationStarted",
        [MorphSnapshotCaptured] = "MorphSnapshotCaptured", [MorphDesiredUpdated] = "MorphDesiredUpdated",
        [MorphApplied] = "MorphApplied", [MorphRestored] = "MorphRestored",
        [MorphOperationFailed] = "MorphOperationFailed", [RedrawOperationStarted] = "RedrawOperationStarted",
        [RedrawStateChanged] = "RedrawStateChanged", [RedrawCompleted] = "RedrawCompleted",
        [RedrawCancelled] = "RedrawCancelled", [DrawObjectCreateInjected] = "DrawObjectCreateInjected",
        [RedrawFailed] = "RedrawFailed", [GPoseEntered] = "GPoseEntered",
        [GPoseExited] = "GPoseExited", [GPoseMappingResolved] = "GPoseMappingResolved",
        [GPoseMappingAmbiguous] = "GPoseMappingAmbiguous", [GPoseOperationFailed] = "GPoseOperationFailed",
        [BulkBatchStarted] = "BulkBatchStarted", [BulkTargetResolved] = "BulkTargetResolved",
        [OutfitSnapshotCaptured] = "OutfitSnapshotCaptured", [OutfitApplied] = "OutfitApplied",
        [OutfitSkipped] = "OutfitSkipped", [OutfitRolledBack] = "OutfitRolledBack", [BulkBatchCompleted] = "BulkBatchCompleted",
        [BulkBatchCancelled] = "BulkBatchCancelled", [BulkActorFailed] = "BulkActorFailed",
        [UnequipBatchStarted] = "UnequipBatchStarted", [RestoreBatchStarted] = "RestoreBatchStarted",
        [TroubleshootingCaptureStarted] = "TroubleshootingCaptureStarted",
        [TroubleshootingCaptureEnded] = "TroubleshootingCaptureEnded",
        [DiagnosticSnapshotCreated] = "DiagnosticSnapshotCreated", [SlowOperation] = "SlowOperation",
        [PreviewAssetsResolved] = "PreviewAssetsResolved", [PreviewSelectionRequested] = "PreviewSelectionRequested",
        [PreviewStateChanged] = "PreviewStateChanged",
        [LogEventsDropped] = "LogEventsDropped", [DiagnosticMarker] = "DiagnosticMarker",
        [RepeatedEventsSuppressed] = "RepeatedEventsSuppressed", [HandledException] = "HandledException",
        [FileLoggingUnavailable] = "FileLoggingUnavailable", [DiagnosticExportFailed] = "DiagnosticExportFailed",
        [UnexpectedPluginException] = "UnexpectedPluginException",
    };

    public static string GetName(string eventId)
        => Names.TryGetValue(eventId, out var name) ? name : "UnknownEvent";
}
