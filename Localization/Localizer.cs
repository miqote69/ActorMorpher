using Dalamud.Game;
using Dalamud.Plugin.Services;
using System.Globalization;

namespace ActorMorpher.Localization;

public sealed class Localizer
{
    private readonly Configuration configuration;
    private readonly IClientState clientState;

    public Localizer(Configuration configuration, IClientState clientState)
    {
        this.configuration = configuration;
        this.clientState = clientState;
    }

    public UiLanguage EffectiveLanguage => configuration.UiLanguage == UiLanguage.Automatic
        ? FromClientLanguage(clientState.ClientLanguage)
        : configuration.UiLanguage;

    public string this[TextKey key] => Get(key);

    public string Get(TextKey key, params object[] arguments)
    {
        var value = Strings.TryGetValue(EffectiveLanguage, out var language)
            && language.TryGetValue(key, out var localized)
                ? localized
                : English[key];
        return arguments.Length == 0 ? value : string.Format(CultureInfo.CurrentCulture, value, arguments);
    }

    public static UiLanguage FromClientLanguage(ClientLanguage language) => language switch
    {
        ClientLanguage.Japanese => UiLanguage.Japanese,
        ClientLanguage.German => UiLanguage.German,
        ClientLanguage.French => UiLanguage.French,
        _ => UiLanguage.English,
    };

    private static readonly IReadOnlyDictionary<TextKey, string> English = new Dictionary<TextKey, string>
    {
        [TextKey.Automatic] = "Automatic", [TextKey.English] = "English", [TextKey.Japanese] = "Japanese", [TextKey.German] = "German", [TextKey.French] = "French",
        [TextKey.Settings] = "Settings", [TextKey.UiLanguage] = "UI Language", [TextKey.GameDataLanguage] = "Game data language: {0}",
        [TextKey.Actors] = "Actors", [TextKey.ModelSearch] = "Model Search", [TextKey.BulkOutfit] = "Bulk Outfit", [TextKey.Diagnostics] = "Diagnostics",
        [TextKey.Human] = "Human", [TextKey.Demihuman] = "Demihuman", [TextKey.Monster] = "Monster", [TextKey.All] = "All", [TextKey.Players] = "Players", [TextKey.Npcs] = "NPCs",
        [TextKey.AnyRace] = "Any race", [TextKey.AnyGender] = "Any gender", [TextKey.Male] = "Male", [TextKey.Female] = "Female", [TextKey.Adult] = "Adult", [TextKey.Old] = "Old", [TextKey.YoungNpc] = "Young NPC", [TextKey.Unknown] = "Unknown ({0})",
        [TextKey.Yes] = "Yes", [TextKey.No] = "No", [TextKey.None] = "None", [TextKey.SelectActor] = "Select an actor from the list.", [TextKey.SelectCharacter] = "Select a character from the list.",
        [TextKey.VisibleActors] = "Visible actors ({0})", [TextKey.Results] = "Results ({0})", [TextKey.FilterByName] = "Filter by name", [TextKey.Category] = "Category", [TextKey.Name] = "Name", [TextKey.ModelId] = "Model ID", [TextKey.SearchHint] = "Search by NPC, monster, or model name",
        [TextKey.Race] = "Race", [TextKey.Gender] = "Gender", [TextKey.Age] = "Age", [TextKey.BodyType] = "Body Type", [TextKey.Characters] = "Characters", [TextKey.Details] = "Details", [TextKey.Field] = "Field", [TextKey.Value] = "Value",
        [TextKey.ActorType] = "Actor Type", [TextKey.ObjectKind] = "ObjectKind", [TextKey.Representation] = "Representation", [TextKey.ObjectIndex] = "Object Index", [TextKey.OriginalObjectIndex] = "Original Object Index", [TextKey.GameObjectId] = "GameObject ID", [TextKey.EntityId] = "Entity ID", [TextKey.BaseId] = "Base ID", [TextKey.ModelCharaId] = "ModelChara ID", [TextKey.ClassJob] = "Class Job", [TextKey.Level] = "Level", [TextKey.IsLocalPlayer] = "Is Local Player", [TextKey.CurrentMorph] = "Current Morph", [TextKey.BulkOutfitModified] = "Bulk Outfit Modified", [TextKey.SnapshotAvailable] = "Snapshot Available", [TextKey.GPoseRepresentation] = "GPose Representation", [TextKey.NonHumanUnknown] = "Non-Human / Unknown",
        [TextKey.RestoreOriginalState] = "Restore Original State", [TextKey.ApplyToYourself] = "Apply to Yourself", [TextKey.ApplyToSelectedActor] = "Apply to Selected Actor",
        [TextKey.Source] = "Source", [TextKey.Type] = "Type", [TextKey.Model] = "Model", [TextKey.Base] = "Base", [TextKey.Variant] = "Variant", [TextKey.Data] = "Data", [TextKey.Equipment] = "Equipment", [TextKey.Slot] = "Slot", [TextKey.Set] = "Set", [TextKey.ItemName] = "Name", [TextKey.Stain1] = "Stain 1", [TextKey.Stain2] = "Stain 2", [TextKey.Facewear] = "Facewear", [TextKey.Hat] = "Hat", [TextKey.Visor] = "Visor", [TextKey.Visible] = "Visible", [TextKey.Hidden] = "Hidden", [TextKey.Toggled] = "Toggled", [TextKey.Normal] = "Normal", [TextKey.Unavailable] = "Unavailable", [TextKey.NoEquipment] = "None",
        [TextKey.SourceOutfit] = "Source Outfit: Current Player Appearance", [TextKey.RefreshSourcePreview] = "Refresh Source Preview", [TextKey.TargetFilters] = "Target Filters", [TextKey.IncludeYourself] = "Include Yourself", [TextKey.MatchingActors] = "Matching logical actors: {0}", [TextKey.EligibleHumanActors] = "Eligible human actors: {0}", [TextKey.SkippedNonHumanActors] = "Skipped non-human actors: {0}", [TextKey.UnavailableActors] = "Unavailable actors: {0}", [TextKey.ApplyMatchingActors] = "Apply to Matching Actors", [TextKey.UnequipAll] = "Unequip All", [TextKey.RestoreModifiedActors] = "Restore Modified Actors", [TextKey.CancelPendingOperation] = "Cancel Pending Operation", [TextKey.Processing] = "Processing {0} / {1}", [TextKey.Succeeded] = "Succeeded", [TextKey.Skipped] = "Skipped", [TextKey.Failed] = "Failed",
        [TextKey.Preview3D]="3D Preview", [TextKey.ResetCamera]="Reset Camera", [TextKey.DiagnosticFileLogging]="Diagnostic File Logging", [TextKey.Off]="Off", [TextKey.ErrorsOnly]="Errors Only", [TextKey.FullTroubleshooting]="Full Troubleshooting", [TextKey.NoDiagnosticFiles]="No Actor Morpher diagnostic files will be created.", [TextKey.CriticalErrorsStandardLog]="Critical errors may still appear in the standard Dalamud log.", [TextKey.IncludeActorNames]="Include Actor Names", [TextKey.IncludeRawAddresses]="Include Raw Memory Addresses", [TextKey.MirrorLogs]="Mirror Logs Beside Plugin Assembly", [TextKey.RetentionDays]="Retention Days", [TextKey.MaximumSessions]="Maximum Sessions", [TextKey.MaximumFileSize]="Maximum File Size (MB)", [TextKey.MaximumTotalSize]="Maximum Total Size (MB)", [TextKey.ApplyDiagnosticSettings]="Apply Diagnostic Settings", [TextKey.SessionId]="Session ID", [TextKey.CurrentMode]="Current Mode", [TextKey.CurrentLogFile]="Current Log File", [TextKey.StandardLogDirectory]="Standard Log Directory", [TextKey.MirrorLogDirectory]="Mirror Log Directory", [TextKey.QueuedEvents]="Queued Events", [TextKey.DroppedEvents]="Dropped Events", [TextKey.CurrentFileSize]="Current File Size", [TextKey.LastFileLoggingError]="Last File Logging Error", [TextKey.TroubleshootingCapture]="Troubleshooting Capture", [TextKey.Active]="Active", [TextKey.Inactive]="Inactive", [TextKey.OpenLogFolder]="Open Log Folder", [TextKey.CopyLogPath]="Copy Log Path", [TextKey.OptionalMarkerNote]="Optional marker note", [TextKey.AddDiagnosticMarker]="Add Diagnostic Marker", [TextKey.BeginTroubleshootingCapture]="Begin Troubleshooting Capture", [TextKey.EndTroubleshootingCapture]="End Troubleshooting Capture", [TextKey.CreateDiagnosticSnapshot]="Create Diagnostic Snapshot", [TextKey.ClearOldLogs]="Clear Old Logs", [TextKey.LatestSnapshot]="Latest Snapshot: {0}",
    };

    private static readonly IReadOnlyDictionary<UiLanguage, IReadOnlyDictionary<TextKey, string>> Strings = new Dictionary<UiLanguage, IReadOnlyDictionary<TextKey, string>>
    {
        [UiLanguage.English] = English,
        [UiLanguage.Japanese] = Translate(new Dictionary<TextKey, string>
        {
            [TextKey.Automatic]="自動", [TextKey.English]="英語", [TextKey.Japanese]="日本語", [TextKey.German]="ドイツ語", [TextKey.French]="フランス語", [TextKey.Settings]="設定", [TextKey.UiLanguage]="UI言語", [TextKey.GameDataLanguage]="ゲームデータ言語: {0}", [TextKey.Actors]="Actor一覧", [TextKey.ModelSearch]="モデル検索", [TextKey.BulkOutfit]="一括装備", [TextKey.Diagnostics]="診断", [TextKey.Human]="ヒューマン", [TextKey.Demihuman]="デミヒューマン", [TextKey.Monster]="モンスター", [TextKey.All]="すべて", [TextKey.Players]="プレイヤー", [TextKey.Npcs]="NPC", [TextKey.AnyRace]="すべての種族", [TextKey.AnyGender]="すべての性別", [TextKey.Male]="男性", [TextKey.Female]="女性", [TextKey.Adult]="大人", [TextKey.Old]="老人", [TextKey.YoungNpc]="Young NPC", [TextKey.Yes]="はい", [TextKey.No]="いいえ", [TextKey.None]="なし", [TextKey.SelectActor]="左の一覧からActorを選択してください。", [TextKey.SelectCharacter]="左の一覧からキャラクターを選択してください。", [TextKey.VisibleActors]="表示中のActor ({0})", [TextKey.Results]="検索結果 ({0})", [TextKey.FilterByName]="名前で絞り込み", [TextKey.Category]="カテゴリ", [TextKey.Name]="名称", [TextKey.ModelId]="Model ID", [TextKey.SearchHint]="NPC・モンスター・モデル名で検索", [TextKey.Race]="種族", [TextKey.Gender]="性別", [TextKey.Age]="年齢", [TextKey.BodyType]="ボディタイプ", [TextKey.Characters]="キャラクター", [TextKey.Details]="詳細", [TextKey.Field]="項目", [TextKey.Value]="値", [TextKey.ActorType]="Actor種別", [TextKey.RestoreOriginalState]="元の状態に復元", [TextKey.ApplyToYourself]="自分に適用", [TextKey.ApplyToSelectedActor]="選択したActorに適用", [TextKey.Equipment]="装備", [TextKey.Slot]="部位", [TextKey.Set]="番号", [TextKey.ItemName]="名称", [TextKey.Stain1]="染色1", [TextKey.Stain2]="染色2", [TextKey.NoEquipment]="装備なし", [TextKey.SourceOutfit]="コピー元装備: 現在のプレイヤー外見", [TextKey.RefreshSourcePreview]="コピー元を更新", [TextKey.TargetFilters]="対象フィルター", [TextKey.IncludeYourself]="自分を含める", [TextKey.MatchingActors]="一致するActor: {0}", [TextKey.EligibleHumanActors]="適用可能なヒューマン: {0}", [TextKey.SkippedNonHumanActors]="対象外の非ヒューマン: {0}", [TextKey.UnavailableActors]="利用不可: {0}", [TextKey.ApplyMatchingActors]="一致するActorへ適用", [TextKey.UnequipAll]="すべて外す", [TextKey.RestoreModifiedActors]="変更したActorを復元", [TextKey.CancelPendingOperation]="処理をキャンセル", [TextKey.Processing]="処理中 {0} / {1}", [TextKey.Succeeded]="成功", [TextKey.Skipped]="スキップ", [TextKey.Failed]="失敗", [TextKey.Preview3D]="3Dプレビュー", [TextKey.ResetCamera]="カメラをリセット", [TextKey.DiagnosticFileLogging]="診断ファイル出力", [TextKey.Off]="オフ", [TextKey.ErrorsOnly]="エラーのみ", [TextKey.FullTroubleshooting]="詳細トラブルシューティング", [TextKey.NoDiagnosticFiles]="Actor Morpherの診断ファイルは作成されません。", [TextKey.CriticalErrorsStandardLog]="重大なエラーはDalamud標準ログに記録される場合があります。", [TextKey.IncludeActorNames]="Actor名を含める", [TextKey.IncludeRawAddresses]="メモリアドレスを含める", [TextKey.MirrorLogs]="プラグインの隣にもログを保存", [TextKey.RetentionDays]="保存日数", [TextKey.MaximumSessions]="最大セッション数", [TextKey.MaximumFileSize]="最大ファイルサイズ (MB)", [TextKey.MaximumTotalSize]="合計最大サイズ (MB)", [TextKey.ApplyDiagnosticSettings]="診断設定を適用", [TextKey.SessionId]="セッションID", [TextKey.CurrentMode]="現在のモード", [TextKey.CurrentLogFile]="現在のログファイル", [TextKey.StandardLogDirectory]="標準ログフォルダー", [TextKey.MirrorLogDirectory]="ミラーログフォルダー", [TextKey.QueuedEvents]="待機中イベント", [TextKey.DroppedEvents]="破棄イベント", [TextKey.CurrentFileSize]="現在のファイルサイズ", [TextKey.LastFileLoggingError]="最後のログエラー", [TextKey.TroubleshootingCapture]="トラブルシューティング記録", [TextKey.Active]="有効", [TextKey.Inactive]="無効", [TextKey.OpenLogFolder]="ログフォルダーを開く", [TextKey.CopyLogPath]="ログパスをコピー", [TextKey.OptionalMarkerNote]="任意のマーカーメモ", [TextKey.AddDiagnosticMarker]="診断マーカーを追加", [TextKey.BeginTroubleshootingCapture]="記録を開始", [TextKey.EndTroubleshootingCapture]="記録を終了", [TextKey.CreateDiagnosticSnapshot]="診断スナップショットを作成", [TextKey.ClearOldLogs]="古いログを削除", [TextKey.LatestSnapshot]="最新スナップショット: {0}",
        }),
        [UiLanguage.German] = Translate(new Dictionary<TextKey, string> { [TextKey.Settings]="Einstellungen", [TextKey.UiLanguage]="UI-Sprache", [TextKey.Actors]="Akteure", [TextKey.ModelSearch]="Modellsuche", [TextKey.BulkOutfit]="Massen-Outfit", [TextKey.Diagnostics]="Diagnose", [TextKey.ApplyToYourself]="Auf dich anwenden", [TextKey.RestoreOriginalState]="Originalzustand wiederherstellen", [TextKey.IncludeYourself]="Dich selbst einschliessen", [TextKey.UnequipAll]="Alles ablegen" }),
        [UiLanguage.French] = Translate(new Dictionary<TextKey, string> { [TextKey.Settings]="Parametres", [TextKey.UiLanguage]="Langue de l'interface", [TextKey.Actors]="Acteurs", [TextKey.ModelSearch]="Recherche de modele", [TextKey.BulkOutfit]="Tenues groupees", [TextKey.Diagnostics]="Diagnostic", [TextKey.ApplyToYourself]="Appliquer a vous-meme", [TextKey.RestoreOriginalState]="Restaurer l'etat d'origine", [TextKey.IncludeYourself]="Vous inclure", [TextKey.UnequipAll]="Tout desequiper" }),
    };

    private static IReadOnlyDictionary<TextKey, string> Translate(IReadOnlyDictionary<TextKey, string> overrides)
        => English.ToDictionary(pair => pair.Key, pair => overrides.TryGetValue(pair.Key, out var value) ? value : pair.Value);
}
