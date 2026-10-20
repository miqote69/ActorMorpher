using System.Numerics;
using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;

namespace ActorMorpher.Customization;

public readonly record struct CustomizeOption(byte Value, uint IconId, string? Label = null);
public readonly record struct CustomizeColor(byte Value, Vector4 Color);
public readonly record struct CustomizeNamedValue(byte Value, string Name);
public readonly record struct CustomizeOriginOption(byte Race, byte Tribe, string RaceName, string TribeName);

/// <summary>Game-provided display choices, not a validator for Customize bytes.</summary>
public sealed class CustomizeCatalog
{
    private readonly ExcelSheet<Race> races;
    private readonly ExcelSheet<Tribe> tribes;
    private readonly ExcelSheet<CharaMakeType> charaMake;
    private readonly ExcelSheet<HairMakeType> hairMake;
    private readonly ExcelSheet<RawRow> hairMakeRaw;
    private readonly ExcelSheet<CharaMakeCustomize> customizeSheet;
    private readonly ExcelSheet<Lobby> lobby;
    private readonly ExcelSheet<ENpcBase> eventNpcs;
    private readonly ExcelSheet<BNpcBase> battleNpcs;
    private readonly Func<byte[]?> loadColors;
    private readonly Func<string, bool> fileExists;
    private HashSet<(byte Race, byte Tribe, byte Sex)>? oldNpcOrigins;
    private readonly Dictionary<(byte Race, byte Tribe, byte Sex), IReadOnlyList<byte>> bodyTypes = [];
    private readonly Dictionary<(byte Race, byte Tribe, byte Sex, byte Face, int Index, byte Body), IReadOnlyList<CustomizeOption>> options = [];
    private readonly Dictionary<(byte Race, byte Tribe, byte Sex, int Index), IReadOnlyList<CustomizeColor>> colors = [];
    private byte[]? humanCmp;

    public CustomizeCatalog(IDataManager dataManager)
        : this(dataManager.GetExcelSheet<Race>(), dataManager.GetExcelSheet<Tribe>(),
            dataManager.GetExcelSheet<CharaMakeType>(), dataManager.GetExcelSheet<HairMakeType>(),
            dataManager.GetExcelSheet<RawRow>(name: "HairMakeType"),
            dataManager.GetExcelSheet<CharaMakeCustomize>(), dataManager.GetExcelSheet<Lobby>(),
            dataManager.GetExcelSheet<ENpcBase>(), dataManager.GetExcelSheet<BNpcBase>(),
            () => dataManager.GetFile("chara/xls/charamake/human.cmp")?.Data, dataManager.FileExists) { }

    internal CustomizeCatalog(Lumina.GameData gameData)
        : this(gameData.GetExcelSheet<Race>()!, gameData.GetExcelSheet<Tribe>()!,
            gameData.GetExcelSheet<CharaMakeType>()!, gameData.GetExcelSheet<HairMakeType>()!,
            gameData.GetExcelSheet<RawRow>(name: "HairMakeType")!,
            gameData.GetExcelSheet<CharaMakeCustomize>()!, gameData.GetExcelSheet<Lobby>()!,
            gameData.GetExcelSheet<ENpcBase>()!, gameData.GetExcelSheet<BNpcBase>()!,
            () => gameData.GetFile("chara/xls/charamake/human.cmp")?.Data, gameData.FileExists) { }

    private CustomizeCatalog(ExcelSheet<Race> races, ExcelSheet<Tribe> tribes,
        ExcelSheet<CharaMakeType> charaMake, ExcelSheet<HairMakeType> hairMake,
        ExcelSheet<RawRow> hairMakeRaw,
        ExcelSheet<CharaMakeCustomize> customizeSheet, ExcelSheet<Lobby> lobby,
        ExcelSheet<ENpcBase> eventNpcs, ExcelSheet<BNpcBase> battleNpcs,
        Func<byte[]?> loadColors, Func<string, bool> fileExists)
        => (this.races, this.tribes, this.charaMake, this.hairMake, this.hairMakeRaw, this.customizeSheet,
            this.lobby, this.eventNpcs, this.battleNpcs, this.loadColors, this.fileExists)
            = (races, tribes, charaMake, hairMake, hairMakeRaw, customizeSheet, lobby, eventNpcs, battleNpcs, loadColors, fileExists);

    public bool HasFaceModel(ReadOnlySpan<byte> customize)
    {
        var tribe = customize[4];
        var body = customize[2];
        var family = tribe switch
        {
            1 => 1, 2 => 3, 3 or 4 => 5, 5 or 6 => 11, 7 or 8 => 7,
            9 or 10 => 9, 11 or 12 => 13, 13 or 14 => 15, 15 or 16 => 17, _ => 1,
        };
        var code = (family + customize[1]) * 100 + (body is 2 or 4 or 5 ? body : 1);
        if (customize[0] == 1 && (customize[22] & 0x80) != 0) code += 9000;
        var face = FaceId(customize[5], tribe, body);
        // ResolveMdlPath's face slot removes the clan offset for these model families.
        if ((tribe is 12 or 14 or 16 || tribe == 8 && body == 4) && face <= 200) face -= 100;
        return fileExists($"chara/human/c{code:D4}/obj/face/f{face:D4}/model/c{code:D4}f{face:D4}_fac.mdl");
    }

    public IReadOnlyList<byte> GetBodyTypes(ReadOnlySpan<byte> customize)
    {
        if (customize.Length < 6) return [];
        var key = (Race: customize[0], Tribe: customize[4], Sex: customize[1]);
        if (bodyTypes.TryGetValue(key, out var cached)) return cached;
        if (!charaMake.Any(row => row.Race.RowId == key.Race && row.Tribe.RowId == key.Tribe && row.Gender == key.Sex))
            return bodyTypes[key] = [];

        var family = key.Tribe switch
        {
            1 => 1, 2 => 3, 3 or 4 => 5, 5 or 6 => 11, 7 or 8 => 7,
            9 or 10 => 9, 11 or 12 => 13, 13 or 14 => 15, 15 or 16 => 17, _ => 0,
        } + key.Sex;
        var result = new List<byte> { 1 };
        oldNpcOrigins ??= LoadOldNpcOrigins();
        // Body 3 uses the adult family with an aged face, rather than a cXX03 model.
        var adult = $"c{family:D2}01";
        var hasOldFace = GetOptions(customize, 5).Any(face => face.Value <= 100
            && fileExists($"chara/human/{adult}/obj/face/f{face.Value + ((key.Tribe - 1) & 1) * 100 + 90:D4}/model/{adult}f{face.Value + ((key.Tribe - 1) & 1) * 100 + 90:D4}_fac.mdl"));
        if (oldNpcOrigins.Contains(key) || hasOldFace) result.Add(3);
        // A fallback to another race's child model does not establish a dedicated body type.
        var young = $"c{family:D2}04";
        if (fileExists($"chara/human/{young}/skeleton/base/b0001/skl_{young}b0001.sklb")) result.Add(4);
        return bodyTypes[key] = result.ToArray();
    }

    private HashSet<(byte Race, byte Tribe, byte Sex)> LoadOldNpcOrigins()
    {
        var result = new HashSet<(byte, byte, byte)>();
        foreach (var row in eventNpcs)
            if (row.BodyType == 3 && row.ModelChara.ValueNullable is { Type: 1 })
                result.Add(((byte)row.Race.RowId, (byte)row.Tribe.RowId, row.Gender));
        foreach (var row in battleNpcs)
            if (row.ModelChara.ValueNullable is { Type: 1 } && row.BNpcCustomize.ValueNullable is { BodyType: 3 } customize)
                result.Add(((byte)customize.Race.RowId, (byte)customize.Tribe.RowId, customize.Gender));
        return result;
    }

    public IReadOnlyList<CustomizeOriginOption> GetOrigins()
        => charaMake.Select(row => new CustomizeOriginOption(
                checked((byte)row.Race.RowId), checked((byte)row.Tribe.RowId),
                row.Race.Value.Masculine.ToString(), row.Tribe.Value.Masculine.ToString()))
            .Distinct().ToArray();

    public string? GetRaceName(byte race)
        => races.TryGetRow(race, out var row) ? row.Masculine.ToString() : null;

    public string? GetTribeName(byte tribe)
        => tribes.TryGetRow(tribe, out var row) ? row.Masculine.ToString() : null;

    public IReadOnlyList<CustomizeNamedValue> GetRaces()
        => races
            .Where(row => row.RowId is > 0 and <= byte.MaxValue)
            .Select(row => new CustomizeNamedValue((byte)row.RowId, row.Masculine.ToString()))
            .ToArray();

    public IReadOnlyList<CustomizeNamedValue> GetTribes(byte race)
    {
        var ids = charaMake
            .Where(row => row.Race.RowId == race).Select(row => row.Tribe.RowId).ToHashSet();
        return tribes
            .Where(row => ids.Contains(row.RowId) && row.RowId <= byte.MaxValue)
            .Select(row => new CustomizeNamedValue((byte)row.RowId, row.Masculine.ToString()))
            .ToArray();
    }

    public IReadOnlyList<CustomizeOption> GetOptions(ReadOnlySpan<byte> customize, int index)
    {
        if (customize.Length < 6) return [];
        var key = (customize[0], customize[4], customize[1], customize[5], index, customize[2]);
        if (options.TryGetValue(key, out var cached)) return cached;
        var result = new List<CustomizeOption>();

        // Hair and paint menus reference CharaMakeCustomize rows, including unlockable choices.
        if (index is 6 or 24)
        {
            foreach (var row in hairMake)
            {
                if (row.Race.RowId != key.Item1 || row.Tribe.RowId != key.Item2 || row.Gender != key.Item3) continue;
                var raw = hairMakeRaw.GetRow(row.RowId);
                for (var menuIndex = 0; menuIndex < row.CharaMakeStruct.Count; ++menuIndex)
                {
                    var menu = row.CharaMakeStruct[menuIndex];
                    if (menu.Customize != index || menu.SubMenuType != 1) continue;
                    // The current generated sheet exposes only 100 params, but the game has
                    // more hairstyles. Read the same interleaved EXD columns for the full menu.
                    for (var i = 0; i < menu.SubMenuNum; ++i)
                        if (customizeSheet.TryGetRow(raw.ReadUInt32Column(66 + i * 9 + menuIndex), out var choice))
                            result.Add(new CustomizeOption(choice.FeatureID, choice.Icon));
                }
            }
        }
        else
        {
            // Face, tail/ear and fur-pattern menus contain icon IDs and their corresponding byte values.
            foreach (var row in charaMake)
            {
                if (row.Race.RowId != key.Item1 || row.Tribe.RowId != key.Item2 || row.Gender != key.Item3) continue;
                if (index == 12)
                {
                    foreach (var faceMenu in row.CharaMakeStruct)
                    {
                        if (faceMenu.Customize != 5) continue;
                        for (var face = 0; face < faceMenu.SubMenuNum; ++face)
                        {
                            if (!IsFaceSelected(customize, faceMenu.SubMenuGraphic[face])) continue;
                            var features = row.FacialFeatureOption[face];
                            int[] icons = [features.Option1, features.Option2, features.Option3, features.Option4,
                                features.Option5, features.Option6, features.Option7];
                            for (var bit = 0; bit < icons.Length; ++bit)
                                result.Add(new CustomizeOption((byte)(1 << bit), (uint)icons[bit]));
                        }
                    }
                    continue;
                }
                foreach (var menu in row.CharaMakeStruct)
                {
                    if (menu.Customize != index) continue;
                    if (menu.SubMenuType == 1)
                    {
                        for (var i = 0; i < menu.SubMenuNum; ++i)
                            result.Add(new CustomizeOption(menu.SubMenuGraphic[i], menu.SubMenuParam[i]));
                    }
                    else if (menu.SubMenuType == 0 && index is 14 or 16 or 17 or 18 or 19 or 22)
                    {
                        for (var i = 0; i < menu.SubMenuNum; ++i)
                            result.Add(new CustomizeOption(menu.SubMenuGraphic[i], 0,
                                lobby.GetRow(menu.SubMenuParam[i]).Text.ToString()));
                    }
                    else if (menu.SubMenuType == 5 && index is 3 or 21 or 23)
                    {
                        for (var i = menu.SubMenuParam[2]; i <= menu.SubMenuParam[3] && i <= byte.MaxValue; ++i)
                            result.Add(new CustomizeOption((byte)i, 0));
                    }
                }
            }
        }
        return options[key] = result.Distinct().ToArray();
    }

    // Compare the model's face IDs without changing either Customize byte representation.
    internal static bool IsFaceSelected(ReadOnlySpan<byte> customize, byte choice)
        => customize.Length >= 6 && FaceId(customize[5], customize[4], customize[2])
            == FaceId(choice, customize[4], customize[2]);

    private static int FaceId(byte face, byte tribe, byte body)
        => face > 100 ? face : face + ((tribe - 1) & 1) * 100 + (body == 3 ? 90 : 0);

    public IReadOnlyList<CustomizeColor> GetColors(ReadOnlySpan<byte> customize, int index)
    {
        if (customize.Length < 5) return [];
        var key = (customize[0], customize[4], customize[1], index);
        if (colors.TryGetValue(key, out var cached)) return cached;
        if (index == 20 && GetOptions(customize, index).Count != 0) return [];
        humanCmp ??= loadColors();
        if (humanCmp is null) return [];
        var result = new List<CustomizeColor>(256);
        for (var value = 0; value <= byte.MaxValue; ++value)
            if (TryGetColor(humanCmp, key.Item2, key.Item3, index, (byte)value, out var color))
                result.Add(new CustomizeColor((byte)value, color));
        return colors[key] = result.ToArray();
    }

    // human.cmp contains separate shader and UI swatches. Use UI colors for the picker,
    // and the two shader tone tables for lips/paint. All byte IDs remain addressable.
    // Layout: https://github.com/Ottermandias/Penumbra.GameData/blob/main/Files/CmpData.cs
    internal static bool TryGetColor(byte[] data, byte tribe, byte sex, int index, byte value, out Vector4 color)
    {
        const int sharedBlockSize = 9216;
        const int tribeBlockSize = 5120;
        color = default;
        int offset;
        if (index is 8 or 10)
        {
            if (tribe is < 1 or > 16 || sex > 1) return false;
            offset = sharedBlockSize * 2 + ((tribe - 1) * 2 + sex) * tribeBlockSize
                + (index == 8 ? 3072 : 4096) + value * 4;
        }
        else
        {
            var start = index switch
            {
                9 or 15 => sharedBlockSize,
                11 => sharedBlockSize + 1024,
                13 => sharedBlockSize + 3072,
                20 => (value & 128) == 0 ? 2048 : 4096,
                25 => (value & 128) == 0 ? 2560 : 4608,
                _ => -1,
            };
            if (start < 0) return false;
            offset = start + (index is 20 or 25 ? value & 127 : value) * 4;
        }
        if (offset > data.Length - 4) return false;
        color = new Vector4(data[offset], data[offset + 1], data[offset + 2], data[offset + 3]) / 255f;
        return true;
    }
}
