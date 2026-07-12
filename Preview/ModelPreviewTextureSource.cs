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
    private const uint CharacterIndexSampler = 1449103320;
    private readonly MtrlPreviewParser parser = new();
    private byte[]? humanCmp;
    private bool humanCmpLoaded;

    public ModelPreviewTextureContext CreateContext(ModelSearchEntry? model)
    {
        if (model is not { Category: ModelCategory.Human, HumanAppearance.Customize.Length: >= 12 } human)
            return ModelPreviewTextureContext.Default;
        if (!humanCmpLoaded)
        {
            humanCmpLoaded = true;
            humanCmp = dataManager.GetFile("chara/xls/charamake/human.cmp")?.Data;
        }
        var customize = human.HumanAppearance.Customize;
        if (humanCmp is null
            || !HumanCmpPreviewPalette.TryGetHairColors(
                humanCmp,
                customize[4],
                customize[1],
                customize[10],
                customize[11],
                out var hair,
                out var highlight))
            return ModelPreviewTextureContext.Default;
        if (customize[7] == 0)
            highlight = hair;
        return new ModelPreviewTextureContext(hair, highlight);
    }

    public ModelPreviewTexturePayload? Load(string materialPath, ModelPreviewTextureContext context)
    {
        if (string.IsNullOrWhiteSpace(materialPath))
            return null;
        var materialData = dataManager.GetFile(materialPath)?.Data;
        if (materialData is null)
            return null;
        var material = parser.Parse(materialData);
        if (string.Equals(material.ShaderPackage, "hair.shpk", StringComparison.OrdinalIgnoreCase))
        {
            var normalPath = material.FindTexture(SamplerNormal, "_n.tex", "_norm.tex");
            var maskPath = material.FindTexture(0, "_m.tex", "_mask.tex");
            var normal = normalPath is null ? null : dataManager.GetFile<TexFile>(normalPath);
            var mask = maskPath is null ? null : dataManager.GetFile<TexFile>(maskPath);
            if (normal is not null && mask is not null)
                return ModelPreviewTextureComposer.ComposeHairBaseColor(context, normal, mask);
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

public sealed record ModelPreviewTextureContext(Vector4 HairColor, Vector4 HairHighlightColor)
{
    public static ModelPreviewTextureContext Default { get; } = new(
        new Vector4(130, 64, 13, 255) / 255.0f,
        new Vector4(77, 126, 240, 255) / 255.0f);
}
