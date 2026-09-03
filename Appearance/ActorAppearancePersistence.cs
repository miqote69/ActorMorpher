namespace ActorMorpher.Appearance;

public sealed class ActorAppearancePersistence
{
    private readonly Dictionary<ActorContinuityKey, LogicalActorKey> identities = new();
    private readonly Dictionary<LogicalActorKey, AppearanceData> models = new();
    private readonly Dictionary<ulong, LogicalActorKey> livingActors = new();
    private readonly Dictionary<LogicalActorKey, OutfitData> colorOutfits = new();
    internal OutfitData? GetColorOutfit(LogicalActorKey actor) => colorOutfits.GetValueOrDefault(actor);
    internal void SetColorOutfit(LogicalActorKey actor, OutfitData outfit) => colorOutfits[actor] = outfit;
    public OutfitOverrideStore Outfits { get; } = new();

    public LogicalActorKey Resolve(ActorContinuityKey identity, LogicalActorKey current)
    {
        if (current.Lifetime != 0 && livingActors.TryGetValue(current.Lifetime, out var living))
        {
            BindIdentity(identity, living);
            return living;
        }
        if (identity.Source != 5 && identities.TryGetValue(identity, out var retained))
        {
            if (current.Lifetime != 0)
                livingActors[current.Lifetime] = retained;
            return retained;
        }
        return current;
    }

    public void BindIdentity(ActorContinuityKey identity, LogicalActorKey actor)
    {
        if (identity.Source != 5)
            identities[identity] = actor;
        if (actor.Lifetime != 0)
            livingActors[actor.Lifetime] = actor;
    }

    public void Register(ActorSnapshot actor)
    {
        if (actor.ContinuityKey is { } identity)
            BindIdentity(identity, actor.LogicalKey);
    }

    public void RecordModel(ActorSnapshot actor, AppearanceData appearance)
    {
        Register(actor);
        models[actor.LogicalKey] = appearance;
        if (EquipmentDisplayFormatting.CreateHumanOutfit(appearance) is { } outfit)
            colorOutfits[actor.LogicalKey] = outfit;
        else
            colorOutfits.Remove(actor.LogicalKey);
    }

    public void RecordOutfit(ActorSnapshot actor, OutfitData outfit)
    {
        Register(actor);
        if (models.TryGetValue(actor.LogicalKey, out var model))
            models[actor.LogicalKey] = WithOutfit(model, outfit);
    }

    public AppearanceData? GetModel(LogicalActorKey actor) => models.GetValueOrDefault(actor);

    public AppearanceData? GetRetainedAppearance(ActorSnapshot snapshot)
        => GetModel(snapshot.LogicalKey);

    public AppearanceData? GetCreateAppearance(LogicalActorKey actor, uint currentModelId, out bool outfitOnly)
    {
        outfitOnly = false;
        if (models.TryGetValue(actor, out var model))
            return model;
        if (!Outfits.TryGet(actor, out var outfit))
            return null;
        outfitOnly = true;
        return WithOutfit(AppearanceData.Create(currentModelId, ModelCategory.Human, 0,
            AppearanceCompleteness.ModelOnly, Array.Empty<byte>(), Array.Empty<ulong>()), outfit.Desired);
    }

    public void Restore(LogicalActorKey actor)
    {
        models.Remove(actor);
        colorOutfits.Remove(actor);
        Outfits.CompleteRestore(actor);
    }

    private static AppearanceData WithOutfit(AppearanceData model, OutfitData outfit)
        => model.WithOutfit(outfit.Equipment.Select(ActorRegistry.ToEquipmentModelValue),
            outfit.VisorToggled, outfit.Facewear.IsAvailable ? outfit.Facewear.ModelId : model.FacewearModelId,
            outfit.HatVisible) with { ColoredEquipment = outfit.Equipment };
}
