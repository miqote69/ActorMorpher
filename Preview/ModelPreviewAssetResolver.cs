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

    private readonly Func<string, bool> fileExists;

    public ModelPreviewAssetResolver(Func<string, bool> fileExists)
        => this.fileExists = fileExists;

    public ModelPreviewAssetReport Resolve(ModelSearchEntry model)
        => model.Category switch
        {
            ModelCategory.Human => ResolveHuman(model),
            ModelCategory.Demihuman => ResolveDemihuman(model),
            ModelCategory.Monster => ResolveMonster(model),
            _ => Invalid(model),
        };

    private static ModelPreviewAssetReport ResolveHuman(ModelSearchEntry model)
    {
        var ready = model.HumanAppearance is not null
            && model.ModelAppearance is { Completeness: AppearanceCompleteness.Complete };
        return new ModelPreviewAssetReport(
            model.ModelId,
            model.Category,
            ready ? ModelPreviewReadiness.HumanDataReady : ModelPreviewReadiness.InvalidModelData,
            [new ModelPreviewAsset(ModelPreviewAssetKind.HumanAppearance, "Customize + Equipment", null, ready)]);
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
            Asset(ModelPreviewAssetKind.Imc, "IMC", $"{root}/{body}.imc"),
            Asset(ModelPreviewAssetKind.Model, "Body", $"{root}/model/{monster}{body}.mdl"),
            Asset(ModelPreviewAssetKind.Skeleton, "Skeleton", $"chara/monster/{monster}/skeleton/base/{body}/skl_{monster}{body}.sklb"),
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
            Asset(ModelPreviewAssetKind.Imc, "IMC", $"{root}/{equipment}.imc"),
        };
        assets.AddRange(DemihumanParts.Select(part => Asset(
            ModelPreviewAssetKind.Model,
            part.Label,
            $"{root}/model/{demihuman}{equipment}_{part.Suffix}.mdl")));
        assets.Add(Asset(
            ModelPreviewAssetKind.Skeleton,
            "Skeleton",
            $"chara/demihuman/{demihuman}/skeleton/base/b0001/skl_{demihuman}b0001.sklb"));

        var modelAssets = assets.Where(static asset => asset.Kind == ModelPreviewAssetKind.Model).ToArray();
        return Report(model, assets, modelAssets.All(static asset => asset.IsPresent), assets[^1].IsPresent);
    }

    private ModelPreviewAsset Asset(ModelPreviewAssetKind kind, string label, string path)
    {
        try
        {
            return new ModelPreviewAsset(kind, label, path, fileExists(path));
        }
        catch
        {
            return new ModelPreviewAsset(kind, label, path, false);
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
