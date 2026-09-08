namespace ActorMorpher.BulkOutfit;

internal enum PlateImportProgress { Idle, Waiting, Ready, ContextChanged }

internal sealed class GlamourPlateImportOperation
{
    private int index;
    private ulong characterId;
    private uint territory;
    public bool IsPending { get; private set; }

    public bool Start(int plateIndex, ulong owner, uint currentTerritory, Func<bool> request)
    {
        if (IsPending || !request())
            return false;
        index = plateIndex;
        characterId = owner;
        territory = currentTerritory;
        IsPending = true;
        return true;
    }

    public PlateImportProgress Poll(ulong owner, uint currentTerritory, bool loaded, out int plateIndex)
    {
        plateIndex = index;
        if (!IsPending)
            return PlateImportProgress.Idle;
        if (owner == 0 || owner != characterId || currentTerritory != territory)
        {
            Cancel();
            return PlateImportProgress.ContextChanged;
        }
        if (!loaded)
            return PlateImportProgress.Waiting;
        IsPending = false;
        return PlateImportProgress.Ready;
    }

    public void Cancel() => IsPending = false;
}
