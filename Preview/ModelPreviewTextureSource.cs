using Dalamud.Plugin.Services;
using Lumina.Data.Files;
using System.Numerics;

namespace ActorMorpher.Preview;

public sealed class ModelPreviewTextureSource(IDataManager dataManager)
{
    private const uint SamplerDiffuse = 0x115306BE;
    private const uint SamplerColorMap0 = 0x1E6FEF9C;
    private const uint SamplerColorMap1 = 0x6968DF0A;
    private const uint SamplerNormal = 0x0C5EC1F1;
    private const uint SamplerMask = 0x8A4E82B6;
    private const uint CharacterIndexSampler = 1449103320;
    private readonly MtrlPreviewParser parser = new();
    private readonly ModelPreviewStainSource stainSource = new(dataManager);
    private byte[]? humanCmp;
    private bool humanCmpLoaded;

    public ModelPreviewTextureContext CreateContext(ModelSearchEntry? model)
    {
        if (model is not ({ Category: ModelCategory.Human, HumanAppearance.Customize.Length: >= 26 }
            or { Category: ModelCategory.Demihuman, ModelAppearance.Customize.Length: >= 26 }))
            return ModelPreviewTextureContext.Default;
        if (!humanCmpLoaded)
        {
            humanCmpLoaded = true;
            humanCmp = dataManager.GetFile("chara/xls/charamake/human.cmp")?.Data;
        }
        return ModelPreviewTextureContext.FromModel(model, humanCmp);
    }

    public ModelPreviewTexturePayload? Load(string materialPath, ModelPreviewTextureContext context)
    {
        if (string.IsNullOrWhiteSpace(materialPath))
            return null;
        var materialData = dataManager.GetFile(materialPath)?.Data;
        if (materialData is null)
            return null;
        var material = parser.Parse(materialData);
        material = stainSource.Apply(material, context.StainsForMaterial(materialPath));
        if (string.Equals(material.ShaderPackage, "iris.shpk", StringComparison.OrdinalIgnoreCase))
        {
            var diffusePath = material.FindTexture(SamplerDiffuse, "_d.tex", "_base.tex");
            var maskPath = material.FindTexture(SamplerMask, "_m.tex", "_mask.tex");
            var diffuseTexture = diffusePath is null ? null : dataManager.GetFile<TexFile>(diffusePath);
            var maskTexture = maskPath is null ? null : dataManager.GetFile<TexFile>(maskPath);
            if (diffuseTexture is not null && maskTexture is not null)
                return ModelPreviewTextureComposer.ComposeIrisBaseColor(context, diffuseTexture, maskTexture);
        }
        if (string.Equals(material.ShaderPackage, "charactertattoo.shpk", StringComparison.OrdinalIgnoreCase))
        {
            var normalPath = material.FindTexture(SamplerNormal, "_n.tex", "_norm.tex");
            var normalTexture = normalPath is null ? null : dataManager.GetFile<TexFile>(normalPath);
            if (normalTexture is not null)
                return ModelPreviewTextureComposer.ComposeCharacterTattooBaseColor(context, normalTexture);
        }
        if (string.Equals(material.ShaderPackage, "characterocclusion.shpk", StringComparison.OrdinalIgnoreCase)
            && material.TexturePaths.FirstOrDefault() is { } occlusionPath)
            return ModelPreviewTexturePayload.FromGame(occlusionPath);
        if (string.Equals(material.ShaderPackage, "hair.shpk", StringComparison.OrdinalIgnoreCase))
        {
            var normalPath = material.FindTexture(SamplerNormal, "_n.tex", "_norm.tex");
            var maskPath = material.FindTexture(SamplerMask, "_m.tex", "_mask.tex", "_s.tex");
            var normal = normalPath is null ? null : dataManager.GetFile<TexFile>(normalPath);
            var mask = maskPath is null ? null : dataManager.GetFile<TexFile>(maskPath);
            if (normal is not null && mask is not null)
            {
                var hairContext = context.ForHairMaterial(material);
                return ModelPreviewTextureComposer.ComposeHairBaseColor(hairContext, normal, mask);
            }
        }
        if (string.Equals(material.ShaderPackage, "skin.shpk", StringComparison.OrdinalIgnoreCase))
        {
            var diffusePath = material.FindTexture(SamplerDiffuse, "_d.tex", "_base.tex");
            var normalPath = material.FindTexture(SamplerNormal, "_n.tex", "_norm.tex");
            var skinDiffuse = diffusePath is null ? null : dataManager.GetFile<TexFile>(diffusePath);
            var normal = normalPath is null ? null : dataManager.GetFile<TexFile>(normalPath);
            if (skinDiffuse is not null && normal is not null)
                return ModelPreviewTextureComposer.ComposeSkinBaseColor(context, skinDiffuse, normal);
        }
        var diffuse = material.FindTexture(
            SamplerDiffuse,
            "_d.tex",
            "_base.tex",
            "_diff.tex")
            ?? material.FindTexture(SamplerColorMap0)
            ?? material.FindTexture(SamplerColorMap1);
        var index = material.FindTexture(CharacterIndexSampler, "_id.tex");
        if (index is not null && material.DiffuseRows.Count > 0)
        {
            var indexTexture = dataManager.GetFile<TexFile>(index);
            var diffuseTexture = diffuse is null ? null : dataManager.GetFile<TexFile>(diffuse);
            var normalPath = material.FindTexture(SamplerNormal, "_n.tex", "_norm.tex");
            var normalTexture = normalPath is null ? null : dataManager.GetFile<TexFile>(normalPath);
            var generated = indexTexture is null
                ? null
                : ModelPreviewTextureComposer.ComposeCharacterBaseColor(
                material,
                indexTexture,
                diffuseTexture,
                normalTexture);
            if (generated is not null)
                return generated;
        }
        return diffuse is null ? null : ModelPreviewTexturePayload.FromGame(diffuse);
    }

}

public sealed record ModelPreviewTextureContext(
    Vector4 HairColor,
    Vector4 HairHighlightColor,
    Vector4 SkinColor,
    Vector4 EyeColor,
    Vector4 HeterochromiaColor,
    Vector4 FacialFeatureColor,
    Vector4 FacePaintColor,
    byte FacePaint,
    bool UseMaterialHairColor,
    IReadOnlyList<ModelPreviewStains> EquipmentStains)
{
    private const uint DiffuseColorConstant = 0x2C2A34DD;

    public static ModelPreviewTextureContext Default { get; } = new(
        new Vector4(130, 64, 13, 255) / 255.0f,
        new Vector4(77, 126, 240, 255) / 255.0f,
        Vector4.One,
        new Vector4(75, 55, 40, 255) / 255.0f,
        new Vector4(75, 55, 40, 255) / 255.0f,
        new Vector4(40, 25, 20, 255) / 255.0f,
        new Vector4(80, 45, 45, 255) / 255.0f,
        0,
        false,
        Array.Empty<ModelPreviewStains>());

    public static ModelPreviewTextureContext FromModel(ModelSearchEntry? model, byte[]? humanCmp)
    {
        IReadOnlyList<byte>? customize = model switch
        {
            { Category: ModelCategory.Human, HumanAppearance.Customize.Length: >= 26 } human
                => human.HumanAppearance.Customize,
            { Category: ModelCategory.Demihuman, ModelAppearance.Customize.Length: >= 26 } demihuman
                => demihuman.ModelAppearance.Customize,
            _ => null,
        };
        IReadOnlyList<ulong>? equipment = model switch
        {
            { Category: ModelCategory.Human, HumanAppearance.Equipment.Length: 10 } human
                => human.HumanAppearance.Equipment,
            { Category: ModelCategory.Demihuman, ModelAppearance.Equipment.Length: 10 } demihuman
                => demihuman.ModelAppearance.Equipment,
            _ => null,
        };
        var equipmentStains = equipment is null
            ? Array.Empty<ModelPreviewStains>()
            : equipment.Select(static packed => new ModelPreviewStains(
                checked((byte)((packed >> 24) & 0xFF)),
                checked((byte)((packed >> 32) & 0xFF)))).ToArray();
        var useMaterialHairColor = model?.Category == ModelCategory.Demihuman;
        var fallback = Default with
        {
            UseMaterialHairColor = useMaterialHairColor,
            EquipmentStains = equipmentStains,
        };
        if (customize is null || humanCmp is null)
            return fallback;

        var hair = fallback.HairColor;
        var highlight = fallback.HairHighlightColor;
        var skin = fallback.SkinColor;
        var eye = fallback.EyeColor;
        var heterochromia = fallback.HeterochromiaColor;
        var facialFeature = fallback.FacialFeatureColor;
        var facePaint = fallback.FacePaintColor;
        if (HumanCmpPreviewPalette.TryGetHairColors(
            humanCmp,
            customize[4],
            customize[1],
            customize[10],
            customize[11],
            out var resolvedHair,
            out var resolvedHighlight))
        {
            hair = resolvedHair;
            highlight = resolvedHighlight;
        }
        if (customize[7] == 0)
            highlight = hair;
        HumanCmpPreviewPalette.TryGetSkinColor(
            humanCmp,
            customize[4],
            customize[1],
            customize[8],
            out skin);
        if (skin == default)
            skin = fallback.SkinColor;
        if (HumanCmpPreviewPalette.TryGetEyeColor(humanCmp, customize[15], out var resolvedEye))
            eye = resolvedEye;
        heterochromia = customize[9] == 0
            ? eye
            : HumanCmpPreviewPalette.TryGetEyeColor(humanCmp, customize[9], out var resolvedHeterochromia)
                ? resolvedHeterochromia
                : eye;
        if (HumanCmpPreviewPalette.TryGetFacialFeatureColor(humanCmp, customize[13], out var resolvedFeature))
            facialFeature = resolvedFeature;
        if (HumanCmpPreviewPalette.TryGetFacePaintColor(humanCmp, customize[25], out var resolvedFacePaint))
            facePaint = resolvedFacePaint;
        return new ModelPreviewTextureContext(
            hair,
            highlight,
            skin,
            eye,
            heterochromia,
            facialFeature,
            facePaint,
            customize[24],
            useMaterialHairColor,
            equipmentStains);
    }

    public ModelPreviewTextureContext ForHairMaterial(MtrlPreviewData material)
    {
        if (!UseMaterialHairColor
            || !material.TryGetConstantVector3(DiffuseColorConstant, out var diffuseColor))
            return this;
        var materialColor = new Vector4(diffuseColor, 1.0f);
        return this with
        {
            HairColor = materialColor,
            HairHighlightColor = materialColor,
        };
    }

    public ModelPreviewStains StainsForMaterial(string materialPath)
    {
        if (EquipmentStains.Count != 10)
            return default;
        foreach (var (token, slot) in MaterialSlots)
        {
            if (materialPath.Contains(token, StringComparison.OrdinalIgnoreCase))
                return EquipmentStains[(int)slot];
        }
        return default;
    }

    private static readonly (string Token, OutfitSlot Slot)[] MaterialSlots =
    [
        ("_met_", OutfitSlot.Head),
        ("_top_", OutfitSlot.Body),
        ("_glv_", OutfitSlot.Hands),
        ("_dwn_", OutfitSlot.Legs),
        ("_sho_", OutfitSlot.Feet),
        ("_ear_", OutfitSlot.Ears),
        ("_nek_", OutfitSlot.Neck),
        ("_wri_", OutfitSlot.Wrists),
        ("_rir_", OutfitSlot.RightRing),
        ("_ril_", OutfitSlot.LeftRing),
    ];
}
