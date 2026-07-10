using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace ActorMorpher;

public sealed class Plugin : IDalamudPlugin
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
    private IReadOnlyList<ModelCharaEntry>? modelCharaCache;

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
        return ObjectTable.CharacterManagerObjects
            .Where(static obj => obj.Address != nint.Zero && obj.IsValid())
            .Select(CreateActorEntry)
            .OrderBy(static actor => actor.Kind)
            .ThenBy(static actor => actor.Name)
            .ToArray();
    }

    public IReadOnlyList<ModelCharaEntry> GetModelCharaEntries()
    {
        if (modelCharaCache is { } cache)
            return cache;

        try
        {
            modelCharaCache = DataManager.GetExcelSheet<ModelChara>()
                .Where(static row => row.RowId != 0)
                .Select(static row => new ModelCharaEntry(
                    row.RowId,
                    row.Type,
                    row.Model,
                    row.Base,
                    row.Variant))
                .OrderBy(static row => row.Type)
                .ThenBy(static row => row.RowId)
                .ToArray();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load ModelChara sheet.");
            modelCharaCache = Array.Empty<ModelCharaEntry>();
        }

        return modelCharaCache;
    }

    private static ActorEntry CreateActorEntry(IGameObject obj)
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
            obj.IsTargetable);
    }
}

public sealed record ActorEntry(
    ulong GameObjectId,
    ulong EntityId,
    uint BaseId,
    ObjectKind Kind,
    string Name,
    bool IsTargetable);

public sealed record ModelCharaEntry(
    uint RowId,
    byte Type,
    ushort Model,
    ushort Base,
    byte Variant);
