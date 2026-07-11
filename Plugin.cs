using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using LuminaSupplemental.Excel.Model;
using LuminaSupplemental.Excel.Services;

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
    [PluginService] private static IClientState ClientState { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;

    private readonly MainWindow mainWindow;
    private readonly WindowSystem windowSystem = new("ActorMorpher");
    private readonly ActorRegistry actorRegistry;
    private readonly ActorIdentityService actorIdentity = new();
    private readonly RedrawCoordinator redrawCoordinator;
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
        actorRegistry = new ActorRegistry(ObjectTable, ClientState, Framework);
        redrawCoordinator = new RedrawCoordinator(
            Framework,
            new RegistryActorResolver(actorRegistry),
            new UnavailableAppearanceMemory(),
            new NativeRedrawBackend(ObjectTable),
            new DalamudClientContext(ClientState));
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
        redrawCoordinator.Dispose();
        actorRegistry.Dispose();
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
        => actorRegistry.Entries;

    public bool TryResolveActor(LogicalActorKey key, out ActorEntry actor)
        => actorIdentity.TryResolve(actorRegistry, key, out actor);

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

    public bool TryApplyModelToLocalPlayer(ModelSearchEntry model, out string message)
    {
        message = GetApplyUnavailableReason(model);
        return false;
    }

    public static string GetApplyUnavailableReason(ModelSearchEntry model)
        => model.Completeness switch
        {
            AppearanceCompleteness.Unsupported
                => "This model does not have a supported standalone appearance representation.",
            AppearanceCompleteness.ModelOnly when model.Category == ModelCategory.Monster
                => "Only the Monster model ID is available; the required redraw behavior has not been verified.",
            AppearanceCompleteness.ModelOnly when model.Category == ModelCategory.Demihuman
                => "This Demihuman is missing complete customize or equipment part data.",
            _ when model.Category == ModelCategory.Human && model.HumanAppearance is null
                => "This Human NPC does not have complete appearance data.",
            _ => "Standalone appearance apply is not available in this build.",
        };

    private IReadOnlyList<ModelSearchEntry> BuildModelSearchEntries()
    {
        var modelChara = DataManager.GetExcelSheet<ModelChara>()
            .ToDictionary(static row => row.RowId);
        var eNpcResidents = DataManager.GetExcelSheet<ENpcResident>();
        var bNpcNames = DataManager.GetExcelSheet<BNpcName>();
        var bNpcNameLinks = LoadBattleNpcNameLinks();
        var entries = new List<ModelSearchEntry>(modelChara.Count);

        foreach (var row in DataManager.GetExcelSheet<ENpcBase>())
        {
            var modelId = row.ModelChara.RowId;
            if (!modelChara.TryGetValue(modelId, out var model))
                continue;

            var name = eNpcResidents.TryGetRow(row.RowId, out var resident)
                ? resident.Singular.ToString()
                : string.Empty;
            if (string.IsNullOrWhiteSpace(name))
                continue;

            var appearance = model.Type == 1 ? CreateHumanAppearance(row) : null;
            if (model.Type == 1 && appearance is null)
                continue;
            var modelAppearance = model.Type == 2 ? CreateDemihumanAppearance(row) : null;

            entries.Add(CreateSearchEntry(
                model,
                ModelSource.EventNpc,
                row.RowId,
                name,
                (uint)row.Race.RowId,
                (byte)row.Gender,
                row.BodyType,
                appearance,
                modelAppearance));
        }

        foreach (var row in DataManager.GetExcelSheet<BNpcBase>())
        {
            var modelId = row.ModelChara.RowId;
            if (!modelChara.TryGetValue(modelId, out var model))
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

            var npcEquip = row.NpcEquip.ValueNullable;
            var appearance = model.Type == 1 && customize is { } humanCustomize && npcEquip is { } humanEquip
                ? CreateHumanAppearance(humanCustomize, humanEquip)
                : null;
            var modelAppearance = model.Type == 2 && customize is { } demihumanCustomize && npcEquip is { } demihumanEquip
                ? CreateDemihumanAppearance(row.RowId, modelId, demihumanCustomize, demihumanEquip)
                : null;
            if (model.Type == 1 && (appearance is null || names.Length == 0))
                continue;

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
                    bodyType,
                    appearance,
                    modelAppearance));
            }
        }

        var referencedModelIds = entries.Select(static entry => entry.ModelId).ToHashSet();
        foreach (var model in modelChara.Values)
        {
            if (model.Type == 1 || model.RowId == 0)
                continue;

            if (!referencedModelIds.Add(model.RowId))
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
            .DistinctBy(static entry => entry.HumanAppearance is { } appearance
                ? $"{entry.Name}\u001f{appearance.Signature}"
                : $"{entry.Name}\u001f{entry.ModelId}\u001f{entry.Source}\u001f{entry.SourceId}")
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
        byte bodyType,
        HumanAppearance? humanAppearance = null,
        AppearanceData? modelAppearance = null)
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
            bodyType,
            humanAppearance,
            model.Type switch
            {
                1 when humanAppearance is not null => AppearanceCompleteness.Complete,
                1 => AppearanceCompleteness.Unsupported,
                2 when modelAppearance is not null => AppearanceCompleteness.Complete,
                2 or 3 => AppearanceCompleteness.ModelOnly,
                _ => AppearanceCompleteness.Unsupported,
            },
            modelAppearance);
    }

    private static HumanAppearance? CreateHumanAppearance(ENpcBase row)
    {
        var customize = new byte[]
        {
            (byte)row.Race.RowId, (byte)row.Gender, row.BodyType, row.Height, (byte)row.Tribe.RowId,
            row.Face, row.HairStyle, row.HairHighlight, row.SkinColor, row.EyeHeterochromia,
            row.HairColor, row.HairHighlightColor, row.FacialFeature, row.FacialFeatureColor,
            row.Eyebrows, row.EyeColor, row.EyeShape, row.Nose, row.Jaw, row.Mouth,
            row.LipColor, row.BustOrTone1, row.ExtraFeature1, row.ExtraFeature2OrBust,
            row.FacePaint, row.FacePaintColor,
        };
        if (!IsValidHumanCustomize(customize))
            return null;

        var equipment = row.NpcEquip.RowId is not 0
            && row.NpcEquip.ValueNullable is { } npcEquip
            && row is { ModelBody: 0, ModelLegs: 0 }
                ? CreateEquipment(npcEquip)
                : CreateEquipment(row);
        var mainhand = row.NpcEquip.RowId is not 0
            && row.NpcEquip.ValueNullable is { } weaponEquip
            && row is { ModelBody: 0, ModelLegs: 0 }
                ? PackWeapon(weaponEquip.ModelMainHand, weaponEquip.DyeMainHand.RowId, weaponEquip.Dye2MainHand.RowId)
                : PackWeapon(row.ModelMainHand, row.DyeMainHand.RowId, row.Dye2MainHand.RowId);
        var offhand = row.NpcEquip.RowId is not 0
            && row.NpcEquip.ValueNullable is { } offhandEquip
            && row is { ModelBody: 0, ModelLegs: 0 }
                ? PackWeapon(offhandEquip.ModelOffHand, offhandEquip.DyeOffHand.RowId, offhandEquip.Dye2OffHand.RowId)
                : PackWeapon(row.ModelOffHand, row.DyeOffHand.RowId, row.Dye2OffHand.RowId);

        return new HumanAppearance(customize, equipment, mainhand, offhand, row.Visor);
    }

    private static AppearanceData CreateDemihumanAppearance(ENpcBase row)
    {
        var customize = new byte[]
        {
            (byte)row.Race.RowId, (byte)row.Gender, row.BodyType, row.Height, (byte)row.Tribe.RowId,
            row.Face, row.HairStyle, row.HairHighlight, row.SkinColor, row.EyeHeterochromia,
            row.HairColor, row.HairHighlightColor, row.FacialFeature, row.FacialFeatureColor,
            row.Eyebrows, row.EyeColor, row.EyeShape, row.Nose, row.Jaw, row.Mouth,
            row.LipColor, row.BustOrTone1, row.ExtraFeature1, row.ExtraFeature2OrBust,
            row.FacePaint, row.FacePaintColor,
        };
        var equipment = row.NpcEquip.RowId is not 0
            && row.NpcEquip.ValueNullable is { } npcEquip
            && row is { ModelBody: 0, ModelLegs: 0 }
                ? CreateEquipment(npcEquip)
                : CreateEquipment(row);

        return AppearanceData.Create(
            row.ModelChara.RowId,
            ModelCategory.Demihuman,
            row.RowId,
            AppearanceCompleteness.Complete,
            customize,
            equipment);
    }

    private static AppearanceData CreateDemihumanAppearance(
        uint sourceRowId,
        uint modelCharaId,
        BNpcCustomize customizeRow,
        NpcEquip equip)
    {
        var customize = new byte[]
        {
            (byte)customizeRow.Race.RowId, (byte)customizeRow.Gender, customizeRow.BodyType, customizeRow.Height,
            (byte)customizeRow.Tribe.RowId, customizeRow.Face, customizeRow.HairStyle, customizeRow.HairHighlight,
            customizeRow.SkinColor, customizeRow.EyeHeterochromia, customizeRow.HairColor, customizeRow.HairHighlightColor,
            customizeRow.FacialFeature, customizeRow.FacialFeatureColor, customizeRow.Eyebrows, customizeRow.EyeColor,
            customizeRow.EyeShape, customizeRow.Nose, customizeRow.Jaw, customizeRow.Mouth, customizeRow.LipColor,
            customizeRow.BustOrTone1, customizeRow.ExtraFeature1, customizeRow.ExtraFeature2OrBust,
            customizeRow.FacePaint, customizeRow.FacePaintColor,
        };

        return AppearanceData.Create(
            modelCharaId,
            ModelCategory.Demihuman,
            sourceRowId,
            AppearanceCompleteness.Complete,
            customize,
            CreateEquipment(equip));
    }

    private static HumanAppearance? CreateHumanAppearance(BNpcCustomize row, NpcEquip equip)
    {
        var customize = new byte[]
        {
            (byte)row.Race.RowId, (byte)row.Gender, row.BodyType, row.Height, (byte)row.Tribe.RowId,
            row.Face, row.HairStyle, row.HairHighlight, row.SkinColor, row.EyeHeterochromia,
            row.HairColor, row.HairHighlightColor, row.FacialFeature, row.FacialFeatureColor,
            row.Eyebrows, row.EyeColor, row.EyeShape, row.Nose, row.Jaw, row.Mouth,
            row.LipColor, row.BustOrTone1, row.ExtraFeature1, row.ExtraFeature2OrBust,
            row.FacePaint, row.FacePaintColor,
        };
        if (!IsValidHumanCustomize(customize))
            return null;

        return new HumanAppearance(
            customize,
            CreateEquipment(equip),
            PackWeapon(equip.ModelMainHand, equip.DyeMainHand.RowId, equip.Dye2MainHand.RowId),
            PackWeapon(equip.ModelOffHand, equip.DyeOffHand.RowId, equip.Dye2OffHand.RowId),
            equip.Visor);
    }

    private static bool IsValidHumanCustomize(IReadOnlyList<byte> customize)
    {
        var race = customize[0];
        var gender = customize[1];
        var tribe = customize[4];
        return race is >= 1 and <= 8
            && gender is 0 or 1
            && tribe is >= 1 and <= 16;
    }

    private static ulong[] CreateEquipment(ENpcBase row)
        =>
        [
            PackArmor(row.ModelHead, row.DyeHead.RowId, row.Dye2Head.RowId),
            PackArmor(row.ModelBody, row.DyeBody.RowId, row.Dye2Body.RowId),
            PackArmor(row.ModelHands, row.DyeHands.RowId, row.Dye2Hands.RowId),
            PackArmor(row.ModelLegs, row.DyeLegs.RowId, row.Dye2Legs.RowId),
            PackArmor(row.ModelFeet, row.DyeFeet.RowId, row.Dye2Feet.RowId),
            PackArmor(row.ModelEars, row.DyeEars.RowId, row.Dye2Ears.RowId),
            PackArmor(row.ModelNeck, row.DyeNeck.RowId, row.Dye2Neck.RowId),
            PackArmor(row.ModelWrists, row.DyeWrists.RowId, row.Dye2Wrists.RowId),
            PackArmor(row.ModelRightRing, row.DyeRightRing.RowId, row.Dye2RightRing.RowId),
            PackArmor(row.ModelLeftRing, row.DyeLeftRing.RowId, row.Dye2LeftRing.RowId),
        ];

    private static ulong[] CreateEquipment(NpcEquip row)
        =>
        [
            PackArmor(row.ModelHead, row.DyeHead.RowId, row.Dye2Head.RowId),
            PackArmor(row.ModelBody, row.DyeBody.RowId, row.Dye2Body.RowId),
            PackArmor(row.ModelHands, row.DyeHands.RowId, row.Dye2Hands.RowId),
            PackArmor(row.ModelLegs, row.DyeLegs.RowId, row.Dye2Legs.RowId),
            PackArmor(row.ModelFeet, row.DyeFeet.RowId, row.Dye2Feet.RowId),
            PackArmor(row.ModelEars, row.DyeEars.RowId, row.Dye2Ears.RowId),
            PackArmor(row.ModelNeck, row.DyeNeck.RowId, row.Dye2Neck.RowId),
            PackArmor(row.ModelWrists, row.DyeWrists.RowId, row.Dye2Wrists.RowId),
            PackArmor(row.ModelRightRing, row.DyeRightRing.RowId, row.Dye2RightRing.RowId),
            PackArmor(row.ModelLeftRing, row.DyeLeftRing.RowId, row.Dye2LeftRing.RowId),
        ];

    private static ulong PackArmor(ulong model, uint stain1, uint stain2)
        => model | ((ulong)stain1 << 24) | ((ulong)stain2 << 32);

    private static ulong PackWeapon(ulong model, uint stain1, uint stain2)
        => model | ((ulong)stain1 << 48) | ((ulong)stain2 << 56);

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

}

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

public sealed record HumanAppearance(
    byte[] Customize,
    ulong[] Equipment,
    ulong Mainhand,
    ulong Offhand,
    bool VisorToggled)
{
    public string Signature { get; } = string.Join(
        ':',
        Convert.ToHexString(Customize),
        string.Join(',', Equipment.Select(static value => value.ToString("X16"))),
        Mainhand.ToString("X16"),
        Offhand.ToString("X16"),
        VisorToggled ? "1" : "0");
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
    byte BodyType,
    HumanAppearance? HumanAppearance,
    AppearanceCompleteness Completeness,
    AppearanceData? ModelAppearance)
{
    public uint ModelId => RowId;

    public bool IsYoungNpc => Category == ModelCategory.Human && BodyType == (byte)NpcAge.Young;
}
