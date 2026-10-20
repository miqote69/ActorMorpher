namespace ActorMorpher;

public sealed partial class Plugin
{
    private readonly Dictionary<Guid, (EquipmentCopyData Data, OutfitData? Original)> equipmentCopyOperations = new();
    internal event Action<Guid, bool>? EquipmentOperationCompleted;

    public void PasteEquipmentToSource(EquipmentCopyData data)
        => bulkOutfitService.SetSourceOutfit(data.Outfit);

    public bool TryCopyEquipment(ActorSnapshot source, out EquipmentCopyData data, out string message)
    {
        data = null!;
        if (!actorResolver.TryResolve(source.LogicalKey, source.RepresentationKey, out var current)
            || !EquipmentCopyData.TryCreate(actorRegistry.CaptureCustomizeAppearance(current), out data))
        {
            message = "The selected actor's equipment is not available.";
            return false;
        }
        message = string.Empty;
        return true;
    }

    public bool TryPasteEquipment(ActorSnapshot target, EquipmentCopyData data,
        out Guid operationId, out string message)
    {
        operationId = Guid.Empty;
        if (!actorResolver.TryResolve(target.LogicalKey, target.RepresentationKey, out var current)
            || actorRegistry.CaptureCustomizeAppearance(current) is not { } appearance
            || !EquipmentCopyData.CanCopy(appearance))
        {
            message = "The selected actor's equipment is not available.";
            return false;
        }
        var desired = data.ApplyTo(appearance);
        OutfitData? original = null;
        if (appearancePersistence.Outfits.TryGet(current.LogicalKey, out var retainedOutfit))
            original = retainedOutfit.Original;
        else if (appearancePersistence.GetModel(current.LogicalKey) is null
            && !outfitMemory.TryCapture(current, out original))
        {
            message = "The selected actor's original equipment is not available.";
            return false;
        }
        if (!appearanceApplyService.TryApply(current, desired, out operationId, out message))
            return false;
        equipmentCopyOperations.Add(operationId, (data, original));
        restoreStatus = string.Empty;
        return true;
    }

    // Called before the general appearance completion handler so publication and
    // existing-pin updates happen once, before the equipment UI receives completion.
    private bool HandleEquipmentOperationCompleted(Guid operationId, LogicalActorKey actor,
        ActorRepresentationKey representation, AppearanceData applied, bool succeeded, bool maintenance)
    {
        if (!equipmentCopyOperations.Remove(operationId, out var request)) return false;
        var published = false;
        if (actorResolver.TryResolve(actor, representation, out var current)
            && CanPublishAppearanceCompletion(succeeded, representation, current))
        {
            var appearance = CreatePublishedAppearance(applied);
            published = appearancePersistence.GetModel(actor) is null
                || actorRegistry.RecordAppliedAppearance(actor, representation, appearance);
            if (published)
            {
                RecordEquipmentCopy(appearancePersistence, current, request.Data, request.Original);
                drawObjectInjector.EnablePersistentAppearance(current);
            }
            if (published && !maintenance && !ShareNpcEquipment(current, request.Data)
                && actorRegistry.TryGet(actor, out var entry))
                pinnedOutfitStore.UpdateEquipment(entry, request.Data);
        }
        EquipmentOperationCompleted?.Invoke(operationId, published);
        return true;
    }

    internal static void RecordEquipmentCopy(ActorAppearancePersistence persistence, ActorSnapshot actor,
        EquipmentCopyData data, OutfitData? original)
    {
        var outfit = data.Outfit;
        if (original is not null)
            persistence.Outfits.SetDesired(actor.LogicalKey, original, outfit);
        if (persistence.GetModel(actor.LogicalKey) is { } existingModel)
            persistence.RecordModel(actor, data.ApplyTo(existingModel));
        else
        {
            persistence.RecordOutfit(actor, outfit);
            if (data.IncludesWeapons && data.Mainhand is { } mainhand)
                persistence.RecordWeapon(actor, false, mainhand, data.MainhandDyes);
            if (data.IncludesWeapons && data.Offhand is { } offhand)
                persistence.RecordWeapon(actor, true, offhand, data.OffhandDyes);
        }
        persistence.SetColorOutfit(actor.LogicalKey, outfit);
    }
}
