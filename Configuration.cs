using Dalamud.Configuration;
using ActorMorpher.Localization;

namespace ActorMorpher;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 4;
    public UiLanguage UiLanguage { get; set; } = UiLanguage.Automatic;
    public FileDiagnosticMode FileDiagnosticMode { get; set; }
    public bool IncludeActorNamesInDiagnostics { get; set; }
    public bool IncludeRawAddressesInDiagnostics { get; set; }
    public bool MirrorDiagnosticsBesidePluginAssembly { get; set; }
    public int DiagnosticRetentionDays { get; set; } = 14;
    public int DiagnosticMaximumSessions { get; set; } = 10;
    public int DiagnosticMaximumFileSizeMb { get; set; } = 10;
    public int DiagnosticMaximumTotalSizeMb { get; set; } = 100;
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

        if (!Enum.IsDefined(UiLanguage))
            UiLanguage = UiLanguage.Automatic;
        if (!Enum.IsDefined(FileDiagnosticMode))
            FileDiagnosticMode = FileDiagnosticMode.Off;
        DiagnosticRetentionDays = Math.Clamp(DiagnosticRetentionDays, 1, 365);
        DiagnosticMaximumSessions = Math.Clamp(DiagnosticMaximumSessions, 1, 100);
        DiagnosticMaximumFileSizeMb = Math.Clamp(DiagnosticMaximumFileSizeMb, 1, 100);
        DiagnosticMaximumTotalSizeMb = Math.Clamp(DiagnosticMaximumTotalSizeMb, 10, 1000);
        PinnedOutfitStore.Normalize(this);
    }
}
