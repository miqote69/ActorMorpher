using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using Glamourer.Api.Enums;
using Glamourer.Api.IpcSubscribers;
using Lumina.Excel.Sheets;
using LuminaSupplemental.Excel.Model;
using LuminaSupplemental.Excel.Services;
using Penumbra.Api.Enums;
using Penumbra.Api.IpcSubscribers;
using Newtonsoft.Json.Linq;
using PenumbraRedrawObject = Penumbra.Api.IpcSubscribers.RedrawObject;

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
    private readonly PenumbraRedrawObject penumbraRedraw;
    private readonly ApplyState glamourerApplyState;
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
        penumbraRedraw = new PenumbraRedrawObject(PluginInterface);
        glamourerApplyState = new ApplyState(PluginInterface);

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

    public bool TryApplyModelToLocalPlayer(ModelSearchEntry model, out string message)
    {
        if (model.Category == ModelCategory.Human)
        {
            if (model.HumanAppearance is not { } appearance)
            {
                message = "The selected Human NPC does not have complete appearance data.";
                return false;
            }

            try
            {
                var result = glamourerApplyState.Invoke(
                    CreateGlamourerState(appearance),
                    0,
                    0,
                    ApplyFlag.Once | ApplyFlag.Equipment | ApplyFlag.Customization);
                if (result is not GlamourerApiEc.Success and not GlamourerApiEc.NothingDone)
                {
                    message = $"Glamourer rejected the NPC appearance: {result}.";
                    return false;
                }

                message = $"Applied {model.Name} through Glamourer.";
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply Human NPC {Name} through Glamourer.", model.Name);
                message = "Human apply requires Glamourer to be installed and enabled.";
                return false;
            }
        }

        var localPlayer = ObjectTable.LocalPlayer;
        if (localPlayer is null || localPlayer.Address == nint.Zero)
        {
            message = "Local player is not available.";
            return false;
        }

        try
        {
            var character = (Character*)localPlayer.Address;
            character->ModelContainer.ModelCharaId = checked((int)model.ModelId);
            penumbraRedraw.Invoke(0, RedrawType.Redraw);

            message = $"Applied {model.Name} (Model ID {model.ModelId}) to yourself.";
            return true;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply Model ID {ModelId} to the local player.", model.ModelId);
            message = $"Failed to apply Model ID {model.ModelId}. Penumbra is required; see /xllog for details.";
            return false;
        }
    }

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

            entries.Add(CreateSearchEntry(
                model,
                ModelSource.EventNpc,
                row.RowId,
                name,
                (uint)row.Race.RowId,
                (byte)row.Gender,
                row.BodyType,
                appearance));
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

            var appearance = model.Type == 1 && customize is { } humanCustomize
                ? CreateHumanAppearance(humanCustomize, row.NpcEquip.Value)
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
                    appearance));
            }
        }

        foreach (var model in modelChara.Values)
        {
            if (model.Type == 1 || model.RowId == 0)
                continue;

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
        HumanAppearance? humanAppearance = null)
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
            humanAppearance);
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
            && row.NpcEquip.Value is { } npcEquip
            && row is { ModelBody: 0, ModelLegs: 0 }
                ? CreateEquipment(npcEquip)
                : CreateEquipment(row);
        var mainhand = row.NpcEquip.RowId is not 0
            && row.NpcEquip.Value is { } weaponEquip
            && row is { ModelBody: 0, ModelLegs: 0 }
                ? PackWeapon(weaponEquip.ModelMainHand, weaponEquip.DyeMainHand.RowId, weaponEquip.Dye2MainHand.RowId)
                : PackWeapon(row.ModelMainHand, row.DyeMainHand.RowId, row.Dye2MainHand.RowId);
        var offhand = row.NpcEquip.RowId is not 0
            && row.NpcEquip.Value is { } offhandEquip
            && row is { ModelBody: 0, ModelLegs: 0 }
                ? PackWeapon(offhandEquip.ModelOffHand, offhandEquip.DyeOffHand.RowId, offhandEquip.Dye2OffHand.RowId)
                : PackWeapon(row.ModelOffHand, row.DyeOffHand.RowId, row.Dye2OffHand.RowId);

        return new HumanAppearance(customize, equipment, mainhand, offhand, row.Visor);
    }

    private static JObject CreateGlamourerState(HumanAppearance appearance)
    {
        var equipment = new JObject();
        var slotNames = new[] { "Head", "Body", "Hands", "Legs", "Feet", "Ears", "Neck", "Wrists", "RFinger", "LFinger" };
        var equipTypes = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 9 };
        for (var i = 0; i < appearance.Equipment.Length; ++i)
        {
            var armor = appearance.Equipment[i];
            equipment[slotNames[i]] = CreateEquipmentToken(
                CreateCustomItemId((ushort)armor, 0, (byte)(armor >> 16), equipTypes[i]),
                (byte)(armor >> 24),
                (byte)(armor >> 32));
        }

        equipment["MainHand"] = CreateWeaponToken(appearance.Mainhand);
        equipment["OffHand"] = CreateWeaponToken(appearance.Offhand);
        equipment["Hat"] = CreateToggleToken(true, "Show");
        equipment["VieraEars"] = CreateToggleToken(true, "Show");
        equipment["Weapon"] = CreateToggleToken(true, "Show");
        equipment["Visor"] = CreateToggleToken(appearance.VisorToggled, "IsToggled");

        var customize = new JObject
        {
            ["ModelId"] = 0,
            ["Wetness"] = CreateToggleToken(false, "Value"),
        };
        var values = EnumerateCustomizeValues(appearance.Customize);
        foreach (var (name, value) in values)
        {
            customize[name] = new JObject
            {
                ["Value"] = value,
                ["Apply"] = true,
            };
        }

        return new JObject
        {
            ["FileVersion"] = 1,
            ["Equipment"] = equipment,
            ["Bonus"] = new JObject(),
            ["Customize"] = customize,
            ["Parameters"] = new JObject(),
            ["Materials"] = new JObject(),
        };
    }

    private static JObject CreateEquipmentToken(ulong itemId, byte stain1, byte stain2)
        => new()
        {
            ["ItemId"] = itemId,
            ["Crest"] = false,
            ["Apply"] = true,
            ["ApplyStain"] = true,
            ["ApplyCrest"] = false,
            ["Stain"] = stain1,
            ["Stain2"] = stain2,
        };

    private static JObject CreateWeaponToken(ulong weapon)
        => CreateEquipmentToken(
            CreateCustomItemId((ushort)weapon, (ushort)(weapon >> 16), (byte)(weapon >> 32), 0),
            (byte)(weapon >> 48),
            (byte)(weapon >> 56));

    private static JObject CreateToggleToken(bool value, string valueName)
        => new()
        {
            [valueName] = value,
            ["Apply"] = true,
        };

    private static ulong CreateCustomItemId(ushort primary, ushort secondary, byte variant, byte equipType)
        => primary
            | ((ulong)secondary << 16)
            | ((ulong)variant << 32)
            | ((ulong)equipType << 40)
            | (1UL << 48);

    private static IEnumerable<(string Name, byte Value)> EnumerateCustomizeValues(byte[] data)
    {
        yield return ("Race", data[0]);
        yield return ("Gender", data[1]);
        yield return ("BodyType", data[2]);
        yield return ("Height", data[3]);
        yield return ("Clan", data[4]);
        yield return ("Face", data[5]);
        yield return ("Hairstyle", data[6]);
        yield return ("Highlights", (byte)(data[7] >> 7));
        yield return ("SkinColor", data[8]);
        yield return ("EyeColorRight", data[9]);
        yield return ("HairColor", data[10]);
        yield return ("HighlightsColor", data[11]);
        for (var i = 0; i < 7; ++i)
            yield return ($"FacialFeature{i + 1}", (byte)((data[12] >> i) & 1));
        yield return ("LegacyTattoo", (byte)(data[12] >> 7));
        yield return ("TattooColor", data[13]);
        yield return ("Eyebrows", data[14]);
        yield return ("EyeColorLeft", data[15]);
        yield return ("EyeShape", (byte)(data[16] & 0x7F));
        yield return ("SmallIris", (byte)(data[16] >> 7));
        yield return ("Nose", data[17]);
        yield return ("Jaw", data[18]);
        yield return ("Mouth", (byte)(data[19] & 0x7F));
        yield return ("Lipstick", (byte)(data[19] >> 7));
        yield return ("LipColor", data[20]);
        yield return ("MuscleMass", data[21]);
        yield return ("TailShape", data[22]);
        yield return ("BustSize", data[23]);
        yield return ("FacePaint", (byte)(data[24] & 0x7F));
        yield return ("FacePaintReversed", (byte)(data[24] >> 7));
        yield return ("FacePaintColor", data[25]);
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
    HumanAppearance? HumanAppearance)
{
    public uint ModelId => RowId;

    public bool IsYoungNpc => Category == ModelCategory.Human && BodyType == (byte)NpcAge.Young;
}
