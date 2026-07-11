using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;

namespace ActorMorpher.Interop;

public sealed class HumanModelClassifier : IHumanModelClassifier
{
    private readonly Lazy<HashSet<uint>> humanModelIds;

    public HumanModelClassifier(IDataManager dataManager)
    {
        humanModelIds = new Lazy<HashSet<uint>>(
            () => dataManager.GetExcelSheet<ModelChara>()
                .Where(static row => row.Type == 1)
                .Select(static row => row.RowId)
                .Append(0u)
                .ToHashSet(),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public bool IsHuman(uint modelCharaId)
        => humanModelIds.Value.Contains(modelCharaId);
}
