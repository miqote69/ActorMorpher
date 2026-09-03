using Dalamud.Configuration;
using ActorMorpher.BulkOutfit;
using ActorMorpher.Localization;

namespace ActorMorpher;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    private const int BulkRaceFilterMaximum = 8;
    private const int BulkGenderFilterMaximum = 2;

    public int Version { get; set; } = 6;
    public UiLanguage UiLanguage { get; set; } = UiLanguage.Automatic;
    public bool Enable3DPreview { get; set; } = true;
    public FileDiagnosticMode FileDiagnosticMode { get; set; }
    public bool IncludeActorNamesInDiagnostics { get; set; }
    public bool IncludeRawAddressesInDiagnostics { get; set; }
    public bool MirrorDiagnosticsBesidePluginAssembly { get; set; }
    public int DiagnosticRetentionDays { get; set; } = 14;
    public int DiagnosticMaximumSessions { get; set; } = 10;
    public int DiagnosticMaximumFileSizeMb { get; set; } = 10;
    public int DiagnosticMaximumTotalSizeMb { get; set; } = 100;
    public bool BulkIncludeYourself { get; set; }
    public int BulkActorType { get; set; }
    public int BulkRace { get; set; }
    public int BulkGender { get; set; }
    public int BulkAge { get; set; }
    public string BulkNameFilter { get; set; } = string.Empty;
    public bool BulkExclusionEnabled { get; set; }
    public int BulkExcludeActorType { get; set; }
    public int BulkExcludeRace { get; set; }
    public int BulkExcludeGender { get; set; }
    public int BulkExcludeAge { get; set; }
    public string BulkExcludeNameFilter { get; set; } = string.Empty;
    public List<PinnedOutfitConfiguration> PinnedOutfits { get; set; } = [];

    public static Configuration Create(bool isDev)
        => new()
        {
            FileDiagnosticMode = isDev ? FileDiagnosticMode.Full : FileDiagnosticMode.Off,
            MirrorDiagnosticsBesidePluginAssembly = isDev,
        };

    public void MigrateAndValidate(bool isDev)
    {
        if (Version < 2)
        {
            FileDiagnosticMode = isDev ? FileDiagnosticMode.Full : FileDiagnosticMode.Off;
            MirrorDiagnosticsBesidePluginAssembly = isDev;
            Version = 2;
        }
        if (Version < 3)
        {
            UiLanguage = UiLanguage.Automatic;
            Version = 3;
        }
        if (Version < 4)
            Version = 4;
        if (Version < 5)
        {
            Enable3DPreview = true;
            Version = 5;
        }
        if (Version < 6)
        {
            BulkIncludeYourself = false;
            BulkActorType = 0;
            BulkRace = 0;
            BulkGender = 0;
            BulkAge = 0;
            BulkNameFilter = string.Empty;
            BulkExclusionEnabled = false;
            BulkExcludeActorType = 0;
            BulkExcludeRace = 0;
            BulkExcludeGender = 0;
            BulkExcludeAge = 0;
            BulkExcludeNameFilter = string.Empty;
            Version = 6;
        }

        if (!Enum.IsDefined(UiLanguage))
            UiLanguage = UiLanguage.Automatic;
        if (!Enum.IsDefined(FileDiagnosticMode))
            FileDiagnosticMode = FileDiagnosticMode.Off;
        DiagnosticRetentionDays = Math.Clamp(DiagnosticRetentionDays, 1, 365);
        DiagnosticMaximumSessions = Math.Clamp(DiagnosticMaximumSessions, 1, 100);
        DiagnosticMaximumFileSizeMb = Math.Clamp(DiagnosticMaximumFileSizeMb, 1, 100);
        DiagnosticMaximumTotalSizeMb = Math.Clamp(DiagnosticMaximumTotalSizeMb, 10, 1000);
        BulkActorType = Math.Clamp(BulkActorType, 0, (int)ActorTargetType.Npcs);
        BulkRace = Math.Clamp(BulkRace, 0, BulkRaceFilterMaximum);
        BulkGender = Math.Clamp(BulkGender, 0, BulkGenderFilterMaximum);
        BulkAge = Math.Clamp(BulkAge, 0, (int)BulkOutfitAge.Child);
        BulkNameFilter ??= string.Empty;
        BulkExcludeActorType = Math.Clamp(BulkExcludeActorType, 0, (int)ActorTargetType.Npcs);
        BulkExcludeRace = Math.Clamp(BulkExcludeRace, 0, BulkRaceFilterMaximum);
        BulkExcludeGender = Math.Clamp(BulkExcludeGender, 0, BulkGenderFilterMaximum);
        BulkExcludeAge = Math.Clamp(BulkExcludeAge, 0, (int)BulkOutfitAge.Child);
        BulkExcludeNameFilter ??= string.Empty;
        PinnedOutfitStore.Normalize(this);
    }
}
