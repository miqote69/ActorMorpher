using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Lumina.Excel.Sheets;
using LuminaSupplemental.Excel.Model;
using LuminaSupplemental.Excel.Services;

namespace ActorMorpher;

public sealed unsafe class Plugin : IDalamudPlugin
{
    public const string DisplayName = "Actor Morpher";

    private const string CommandName = "/actormorpher";
    private const string CommandAlias = "/amorph";

    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] private static IObjectTable ObjectTable { get; set; } = null!;
    [PluginService] private static IDataManager DataManager { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;

    private readonly MainWindow mainWindow;
    private readonly WindowSystem windowSystem = new("ActorMorpher");
    private IReadOnlyList<ModelSearchEntry>? modelSearchCache;

    public Configuration Configuration { get; }

    public static string DisplayVersion =>
        typeof(Plugin).Assembly
            .GetCustomAttributes(false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()
            ?.InformationalVersion
            .Split('+')[0]
        ?? typeof(Plugin).Assembly.GetName().Version?.ToString(3)
        ?? "0.0.0";

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        mainWindow = new MainWindow(this);
        windowSystem.AddWindow(mainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Actor Morpher.",
        });
        CommandManager.AddHandler(CommandAlias, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Actor Morpher.",
        });

        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUi;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUi;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        CommandManager.RemoveHandler(CommandName);
        CommandManager.RemoveHandler(CommandAlias);

        windowSystem.RemoveAllWindows();
        mainWindow.Dispose();
    }

    public void Save()
    {
        PluginInterface.SavePluginConfig(Configuration);
    }

    public void ToggleMainUi()
    {
        mainWindow.Toggle();
    }

    private void OnCommand(string command, string args)
    {
        ToggleMainUi();
    }

    public IReadOnlyList<ActorEntry> GetVisibleActors()
    {
        var localPlayerId = ObjectTable.LocalPlayer?.GameObjectId;
        return ObjectTable.CharacterManagerObjects
            .Where(static obj => obj.Address != nint.Zero && obj.IsValid())
            .Select(obj => CreateActorEntry(obj, localPlayerId))
            .OrderByDescending(static actor => actor.IsLocalPlayer)
            .ThenBy(static actor => actor.Kind)
            .ThenBy(static actor => actor.Name)
            .ToArray();
    }

    public IReadOnlyList<ModelSearchEntry> GetModelSearchEntries()
    {
        if (modelSearchCache is { } cache)
            return cache;

        try
        {
            modelSearchCache = BuildModelSearchEntries();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load model search data.");
            modelSearchCache = Array.Empty<ModelSearchEntry>();
        }

        return modelSearchCache;
    }

    public bool TryApplyModelToLocalPlayer(uint modelId, out string message)
    {
        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer is null || localPlayer.Address == nint.Zero)
        {
            message = "Local player is not available.";
            return false;
        }

        try
        {
            var character = (Character*)localPlayer.Address;
            character->ModelContainer.ModelCharaId = checked((int)modelId);

            var originalKind = character->GameObject.ObjectKind;
            character->GameObject.DisableDraw();
            try
            {
                character->GameObject.ObjectKind = FFXIVClientStructs.FFXIV.Client.Game.Object.ObjectKind.BattleNpc;
                character->GameObject.EnableDraw();
            }
            finally
            {
                character->GameObject.ObjectKind = originalKind;
            }

            message = $"Applied Model ID {modelId} to yourself.";
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply Model ID {ModelId} to the local player.", modelId);
            message = $"Failed to apply Model ID {modelId}. See /xllog for details.";
            return false;
        }
    }

    private IReadOnlyList<ModelSearchEntry> BuildModelSearchEntries()
    {
        var modelChara = DataManager.GetExcelSheet<ModelChara>()
            .Where(static row => row.RowId != 0)
            .ToDictionary(static row => row.RowId);
        var eNpcResidents = DataManager.GetExcelSheet<ENpcResident>();
        var bNpcNames = DataManager.GetExcelSheet<BNpcName>();
        var bNpcNameLinks = LoadBattleNpcNameLinks();
        var entries = new List<ModelSearchEntry>(modelChara.Count);

        foreach (var row in DataManager.GetExcelSheet<ENpcBase>())
        {
            var modelId = row.ModelChara.RowId;
            if (modelId == 0 || !modelChara.TryGetValue(modelId, out var model))
                continue;

            var name = eNpcResidents.TryGetRow(row.RowId, out var resident)
                ? resident.Singular.ToString()
                : $"Event NPC {row.RowId}";
            if (string.IsNullOrWhiteSpace(name))
                name = $"Event NPC {row.RowId}";
            entries.Add(CreateSearchEntry(
                model,
                ModelSource.EventNpc,
                row.RowId,
                name,
                (uint)row.Race.RowId,
                (byte)row.Gender,
                row.BodyType));
        }

        foreach (var row in DataManager.GetExcelSheet<BNpcBase>())
        {
            var modelId = row.ModelChara.RowId;
            if (modelId == 0 || !modelChara.TryGetValue(modelId, out var model))
                continue;

            var customize = row.BNpcCustomize.ValueNullable;
            var gender = (byte)(customize?.Gender ?? 0);
            var bodyType = customize?.BodyType ?? 0;
            var names = bNpcNameLinks.TryGetValue(row.RowId, out var nameIds)
                ? nameIds.Select(id => bNpcNames.TryGetRow(id, out var nameRow) ? nameRow.Singular.ToString() : string.Empty)
                    .Where(static name => !string.IsNullOrWhiteSpace(name))
                    .Distinct(StringComparer.CurrentCulture)
                    .ToArray()
                : Array.Empty<string>();

            if (names.Length == 0)
                names = [CreateBattleNpcFallbackName(row.RowId, gender, bodyType)];

            foreach (var name in names)
            {
                entries.Add(CreateSearchEntry(
                    model,
                    ModelSource.BattleNpc,
                    row.RowId,
                    name,
                    customize?.Race.RowId ?? 0,
                    gender,
                    bodyType));
            }
        }

        foreach (var model in modelChara.Values)
        {
            if (entries.Any(entry => entry.ModelId == model.RowId))
                continue;

            entries.Add(CreateSearchEntry(
                model,
                ModelSource.ModelChara,
                model.RowId,
                $"ModelChara {model.RowId}",
                0,
                0,
                0));
        }

        return entries
            .OrderBy(static row => row.Category)
            .ThenBy(static row => row.Name)
            .ThenBy(static row => row.ModelId)
            .ToArray();
    }

    private static ModelSearchEntry CreateSearchEntry(
        ModelChara model,
        ModelSource source,
        uint sourceId,
        string name,
        uint race,
        byte gender,
        byte bodyType)
    {
        return new ModelSearchEntry(
            model.RowId,
            model.Type switch
            {
                1 => ModelCategory.Human,
                2 => ModelCategory.Demihuman,
                3 => ModelCategory.Monster,
                _ => ModelCategory.Other,
            },
            source,
            sourceId,
            name,
            model.Type,
            model.Model,
            model.Base,
            model.Variant,
            race,
            gender,
            bodyType);
    }

    private static string CreateBattleNpcFallbackName(uint rowId, byte gender, byte bodyType)
    {
        var description = (bodyType, gender) switch
        {
            ((byte)NpcAge.Young, 0) => "少年 / Young Boy",
            ((byte)NpcAge.Young, 1) => "少女 / Young Girl",
            ((byte)NpcAge.Old, 0) => "老人 / Old Man",
            ((byte)NpcAge.Old, 1) => "老婆 / Old Woman",
            _ => "Battle NPC",
        };

        return $"{description} {rowId}";
    }

    private static IReadOnlyDictionary<uint, uint[]> LoadBattleNpcNameLinks()
    {
        var links = CsvLoader.LoadResource<BNpcLink>(
            CsvLoader.BNpcLinkResourceName,
            true,
            out var failedLines,
            out var exceptions);

        if (failedLines.Count > 0 || exceptions.Count > 0)
            Log.Warning("Failed to read {FailedCount} battle NPC name links.", failedLines.Count);

        return links
            .GroupBy(static link => link.BNpcBaseId)
            .ToDictionary(
                static group => group.Key,
                static group => group.Select(static link => link.BNpcNameId).Distinct().ToArray());
    }

    private static ActorEntry CreateActorEntry(IGameObject obj, ulong? localPlayerId)
    {
        var name = obj.Name.ToString();
        if (string.IsNullOrWhiteSpace(name))
            name = $"{obj.ObjectKind} {obj.BaseId}";

        return new ActorEntry(
            obj.GameObjectId,
            obj.EntityId,
            obj.BaseId,
            obj.ObjectKind,
            name,
            obj.IsTargetable,
            obj.GameObjectId == localPlayerId);
    }
}

public sealed record ActorEntry(
    ulong GameObjectId,
    ulong EntityId,
    uint BaseId,
    ObjectKind Kind,
    string Name,
    bool IsTargetable,
    bool IsLocalPlayer);

public enum NpcAge : byte
{
    Normal = 1,
    Old = 3,
    Young = 4,
}

public enum ModelCategory
{
    Human,
    Demihuman,
    Monster,
    Other,
}

public enum ModelSource
{
    ModelChara,
    EventNpc,
    BattleNpc,
}

public sealed record ModelSearchEntry(
    uint RowId,
    ModelCategory Category,
    ModelSource Source,
    uint SourceId,
    string Name,
    byte Type,
    ushort Model,
    ushort Base,
    byte Variant,
    uint Race,
    byte Gender,
    byte BodyType)
{
    public uint ModelId => RowId;

    public bool IsYoungNpc => Category == ModelCategory.Human && BodyType == (byte)NpcAge.Young;
}
