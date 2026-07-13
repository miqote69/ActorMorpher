using Dalamud.Plugin.Services;

namespace ActorMorpher.Preview;

public sealed class ModelPreviewStainSource(IDataManager dataManager)
{
    private const string LegacyPath = "chara/base_material/stainingtemplate.stm";
    private const string DawntrailPath = "chara/base_material/stainingtemplate_gud.stm";
    private StmPreviewFile? legacy;
    private StmPreviewFile? dawntrail;
    private bool loaded;

    public MtrlPreviewData Apply(MtrlPreviewData material, ModelPreviewStains stains)
    {
        if (stains == default || material.DyeRows.Count == 0 || material.DiffuseRows.Count == 0)
            return material;
        EnsureLoaded();
        var stm = material.IsDawntrail ? dawntrail : legacy;
        return Apply(material, stains, stm);
    }

    public static MtrlPreviewData Apply(MtrlPreviewData material, ModelPreviewStains stains, StmPreviewFile? stm)
    {
        if (stains == default || material.DyeRows.Count == 0 || material.DiffuseRows.Count == 0 || stm is null)
            return material;
        var rows = material.DiffuseRows.ToArray();
        var changed = false;
        for (var index = 0; index < Math.Min(rows.Length, material.DyeRows.Count); ++index)
        {
            var dye = material.DyeRows[index];
            var stain = dye.Channel switch
            {
                0 => stains.Stain1,
                1 => stains.Stain2,
                _ => (byte)0,
            };
            if (!dye.DiffuseColor || stain == 0)
                continue;
            var found = stm.TryGetDiffuseColor(dye.Template, stain, out var color);
            if (!found && material.IsDawntrail && dye.Template < 1000)
                found = stm.TryGetDiffuseColor(checked((ushort)(dye.Template + 1000)), stain, out color);
            if (!found)
                continue;
            rows[index] = color;
            changed = true;
        }
        return changed ? material with { DiffuseRows = rows } : material;
    }

    private void EnsureLoaded()
    {
        if (loaded)
            return;
        loaded = true;
        legacy = Load(LegacyPath);
        dawntrail = Load(DawntrailPath);
    }

    private StmPreviewFile? Load(string path)
    {
        try
        {
            var bytes = dataManager.GetFile(path)?.Data;
            return bytes is null ? null : new StmPreviewFile(bytes);
        }
        catch
        {
            return null;
        }
    }
}

public readonly record struct ModelPreviewStains(byte Stain1, byte Stain2);
