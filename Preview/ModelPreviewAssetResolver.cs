namespace ActorMorpher.Preview;

public sealed class ModelPreviewAssetResolver
{
    private const ushort MaximumPathId = 9999;
    private static readonly (string Suffix, string Label)[] DemihumanParts =
    [
        ("met", "Head"),
        ("top", "Body"),
        ("glv", "Hands"),
        ("dwn", "Legs"),
        ("sho", "Feet"),
    ];
    private static readonly (OutfitSlot Slot, string Suffix, string Label)[] HumanEquipmentParts =
    [
        (OutfitSlot.Head, "met", "Head"),
        (OutfitSlot.Body, "top", "Body"),
        (OutfitSlot.Hands, "glv", "Hands"),
        (OutfitSlot.Legs, "dwn", "Legs"),
        (OutfitSlot.Feet, "sho", "Feet"),
        (OutfitSlot.Ears, "ear", "Ears"),
        (OutfitSlot.Neck, "nek", "Neck"),
        (OutfitSlot.Wrists, "wri", "Wrists"),
        (OutfitSlot.RightRing, "rir", "Right Ring"),
        (OutfitSlot.LeftRing, "ril", "Left Ring"),
    ];

    private readonly Func<string, bool> fileExists;
    private readonly HumanPreviewDataBuilder humanBuilder;

    public ModelPreviewAssetResolver(Func<string, bool> fileExists, HumanPreviewDataBuilder? humanBuilder = null)
    {
        this.fileExists = fileExists;
        this.humanBuilder = humanBuilder ?? new HumanPreviewDataBuilder();
    }

    public ModelPreviewAssetReport Resolve(ModelSearchEntry model)
        => model.Category switch
        {
            ModelCategory.Human => ResolveHuman(model),
            ModelCategory.Demihuman => ResolveDemihuman(model),
            ModelCategory.Monster => ResolveMonster(model),
            _ => Invalid(model),
        };

    private ModelPreviewAssetReport ResolveHuman(ModelSearchEntry model)
    {
        if (!humanBuilder.TryBuild(model, out var human, out _))
            return Invalid(model);

        var family = HumanModelFamily(human.Race, human.Customize[4], human.Sex);
        if (family == 0)
            return Invalid(model);
        var bodyType = human.BodyType is (byte)NpcAge.Old or (byte)NpcAge.Young ? human.BodyType : (byte)NpcAge.Normal;
        var specificCode = bodyType == (byte)NpcAge.Old
            ? human.Sex == 0 ? "c0103" : "c0203"
            : $"c{family:D2}{bodyType:D2}";
        var adultCode = $"c{family:D2}01";
        var fallbackCode = human.Sex == 0 ? "c0101" : "c0201";
        var modelCodes = ModelCodes(specificCode, adultCode, fallbackCode, bodyType);
        var faceId = human.Customize[5];
        var hairId = human.Customize[6];
        var assets = new List<ModelPreviewAsset>
        {
            new(ModelPreviewAssetKind.HumanAppearance, "Customize + Equipment", null, true, false),
            FirstPresent(
                ModelPreviewAssetKind.Model,
                "Face",
                modelCodes.Select(code =>
                    $"chara/human/{code}/obj/face/f{faceId:D4}/model/{code}f{faceId:D4}_fac.mdl"),
                true,
                1),
        };

        if (hairId > 0)
        {
            assets.Add(FirstPresent(
                ModelPreviewAssetKind.Model,
                "Hair",
                modelCodes.Select(code =>
                    $"chara/human/{code}/obj/hair/h{hairId:D4}/model/{code}h{hairId:D4}_hir.mdl"),
                false,
                1));
        }

        foreach (var part in HumanEquipmentParts)
        {
            var packed = human.Equipment[(int)part.Slot];
            var set = checked((ushort)(packed & 0xFFFF));
            var variant = checked((byte)((packed >> 16) & 0xFF));
            var candidates = set == 0
                ? BaseBodyCandidates(part.Slot, part.Suffix, modelCodes)
                : EquipmentCandidates(part.Slot, part.Suffix, set, modelCodes)
                    .Concat(BaseBodyCandidates(part.Slot, part.Suffix, modelCodes));
            if (!candidates.Any())
                continue;
            assets.Add(FirstPresent(
                ModelPreviewAssetKind.Model,
                part.Label,
                candidates,
                part.Slot == OutfitSlot.Body,
                variant == 0 ? (byte)1 : variant));
        }

        assets.Add(FirstPresent(
            ModelPreviewAssetKind.Skeleton,
            "Skeleton",
            modelCodes.Select(code =>
                $"chara/human/{code}/skeleton/base/b0001/skl_{code}b0001.sklb"),
            false,
            1));

        var requiredModels = assets.Where(static asset => asset.Kind == ModelPreviewAssetKind.Model && asset.IsRequired).ToArray();
        var anyModel = assets.Any(static asset => asset.Kind == ModelPreviewAssetKind.Model && asset.IsPresent);
        var readiness = requiredModels.Length > 0 && requiredModels.All(static asset => asset.IsPresent)
            ? ModelPreviewReadiness.AssetsComplete
            : anyModel
                ? ModelPreviewReadiness.AssetsPartial
                : ModelPreviewReadiness.AssetsMissing;
        return new ModelPreviewAssetReport(
            model.ModelId,
            model.Category,
            readiness,
            assets,
            ushort.Parse(specificCode.AsSpan(1)));
    }

    private static IEnumerable<string> BaseBodyCandidates(
        OutfitSlot slot,
        string suffix,
        IReadOnlyList<string> modelCodes)
    {
        if (slot is not (OutfitSlot.Body or OutfitSlot.Hands or OutfitSlot.Legs or OutfitSlot.Feet))
            return Array.Empty<string>();
        return modelCodes.Select(code =>
            $"chara/equipment/e0000/model/{code}e0000_{suffix}.mdl");
    }

    private static IEnumerable<string> EquipmentCandidates(
        OutfitSlot slot,
        string suffix,
        ushort set,
        IReadOnlyList<string> modelCodes)
    {
        var prefix = slot >= OutfitSlot.Ears ? 'a' : 'e';
        return modelCodes.Select(code =>
            $"chara/{(prefix == 'a' ? "accessory" : "equipment")}/{prefix}{set:D4}/model/{code}{prefix}{set:D4}_{suffix}.mdl");
    }

    private ModelPreviewAsset FirstPresent(
        ModelPreviewAssetKind kind,
        string label,
        IEnumerable<string> candidates,
        bool required,
        byte materialVariant)
    {
        string? first = null;
        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            first ??= candidate;
            var asset = Asset(kind, label, candidate, required, materialVariant);
            if (asset.IsPresent)
                return asset;
        }
        return new ModelPreviewAsset(kind, label, first, false, required, materialVariant);
    }

    private static IEnumerable<string> Codes(params string[] codes)
        => codes.Distinct(StringComparer.Ordinal);

    private static IReadOnlyList<string> ModelCodes(
        string specificCode,
        string adultCode,
        string fallbackCode,
        byte bodyType)
        => bodyType switch
        {
            (byte)NpcAge.Young => Codes(specificCode, "c0104", "c0101").ToArray(),
            (byte)NpcAge.Old => Codes(specificCode, "c0103", "c0101").ToArray(),
            _ => Codes(adultCode, fallbackCode).ToArray(),
        };

    private static int HumanModelFamily(byte race, byte tribe, byte sex)
    {
        if (sex > 1)
            return 0;
        var maleFamily = race switch
        {
            1 => tribe == 2 ? 3 : 1,
            2 => 5,
            3 => 11,
            4 => 7,
            5 => 9,
            6 => 13,
            7 => 15,
            8 => 17,
            _ => 0,
        };
        return maleFamily == 0 ? 0 : maleFamily + sex;
    }

    private ModelPreviewAssetReport ResolveMonster(ModelSearchEntry model)
    {
        if (!HasValidPathIds(model))
            return Invalid(model);

        var monster = $"m{model.Model:D4}";
        var body = $"b{model.Base:D4}";
        var root = $"chara/monster/{monster}/obj/body/{body}";
        var assets = new[]
        {
            Asset(ModelPreviewAssetKind.Imc, "IMC", $"{root}/{body}.imc", false),
            Asset(ModelPreviewAssetKind.Model, "Body", $"{root}/model/{monster}{body}.mdl", true, model.Variant),
            Asset(ModelPreviewAssetKind.Skeleton, "Skeleton", $"chara/monster/{monster}/skeleton/base/b0001/skl_{monster}b0001.sklb"),
        };
        return Report(model, assets, assets[1].IsPresent, assets[2].IsPresent);
    }

    private ModelPreviewAssetReport ResolveDemihuman(ModelSearchEntry model)
    {
        if (!HasValidPathIds(model))
            return Invalid(model);

        var demihuman = $"d{model.Model:D4}";
        var equipment = $"e{model.Base:D4}";
        var root = $"chara/demihuman/{demihuman}/obj/equipment/{equipment}";
        var assets = new List<ModelPreviewAsset>
        {
            Asset(ModelPreviewAssetKind.Imc, "IMC", $"{root}/{equipment}.imc", false),
        };
        assets.AddRange(DemihumanParts.Select(part => Asset(
            ModelPreviewAssetKind.Model,
            part.Label,
            $"{root}/model/{demihuman}{equipment}_{part.Suffix}.mdl",
            false,
            model.Variant)));
        assets.Add(Asset(
            ModelPreviewAssetKind.Skeleton,
            "Skeleton",
            $"chara/demihuman/{demihuman}/skeleton/base/b0001/skl_{demihuman}b0001.sklb"));

        var modelAssets = assets.Where(static asset => asset.Kind == ModelPreviewAssetKind.Model).ToArray();
        return Report(model, assets, modelAssets.Any(static asset => asset.IsPresent), assets[^1].IsPresent);
    }

    private ModelPreviewAsset Asset(
        ModelPreviewAssetKind kind,
        string label,
        string path,
        bool required = true,
        byte materialVariant = 1)
    {
        try
        {
            return new ModelPreviewAsset(kind, label, path, fileExists(path), required, materialVariant);
        }
        catch
        {
            return new ModelPreviewAsset(kind, label, path, false, required, materialVariant);
        }
    }

    private static ModelPreviewAssetReport Report(
        ModelSearchEntry model,
        IReadOnlyList<ModelPreviewAsset> assets,
        bool requiredModelsPresent,
        bool skeletonPresent)
    {
        var anyModel = assets.Any(static asset => asset.Kind == ModelPreviewAssetKind.Model && asset.IsPresent);
        var readiness = requiredModelsPresent && skeletonPresent
            ? ModelPreviewReadiness.AssetsComplete
            : anyModel
                ? ModelPreviewReadiness.AssetsPartial
                : ModelPreviewReadiness.AssetsMissing;
        return new ModelPreviewAssetReport(model.ModelId, model.Category, readiness, assets);
    }

    private static bool HasValidPathIds(ModelSearchEntry model)
        => model.Model is > 0 and <= MaximumPathId
        && model.Base is > 0 and <= MaximumPathId;

    private static ModelPreviewAssetReport Invalid(ModelSearchEntry model)
        => new(model.ModelId, model.Category, ModelPreviewReadiness.InvalidModelData, Array.Empty<ModelPreviewAsset>());
}
