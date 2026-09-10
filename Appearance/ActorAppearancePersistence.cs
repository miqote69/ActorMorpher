namespace ActorMorpher.Appearance;

public sealed class ActorAppearancePersistence
{
    private readonly Dictionary<ActorContinuityKey, LogicalActorKey> identities = new();
    private readonly Dictionary<LogicalActorKey, AppearanceData> models = new();
    private readonly Dictionary<LogicalActorKey, (ulong? Mainhand, ulong? Offhand)> weapons = new();
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
        weapons.Remove(actor.LogicalKey);
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

    internal bool HasWeaponOverride(LogicalActorKey actor) => weapons.ContainsKey(actor);

    internal void RecordWeapon(ActorSnapshot actor, bool offhand, ulong weapon)
    {
        Register(actor);
        if (models.TryGetValue(actor.LogicalKey, out var model))
        {
            models[actor.LogicalKey] = offhand ? model with { Offhand = weapon } : model with { Mainhand = weapon };
            return;
        }
        var current = weapons.GetValueOrDefault(actor.LogicalKey);
        weapons[actor.LogicalKey] = offhand ? (current.Mainhand, weapon) : (weapon, current.Offhand);
    }

    public AppearanceData? GetRetainedAppearance(ActorSnapshot snapshot)
        => GetModel(snapshot.LogicalKey);

    public AppearanceData? GetCreateAppearance(LogicalActorKey actor, uint currentModelId, out bool outfitOnly)
    {
        outfitOnly = false;
        if (models.TryGetValue(actor, out var model))
            return model;
        var hasOutfit = Outfits.TryGet(actor, out var outfit);
        var hasWeapons = weapons.TryGetValue(actor, out var selectedWeapons);
        if (!hasOutfit && !hasWeapons)
            return null;
        outfitOnly = true;
        // The existing partial Create path keeps the game's model/customize and applies only populated fields.
        var appearance = AppearanceData.Create(currentModelId, ModelCategory.Human, 0,
            AppearanceCompleteness.ModelOnly, Array.Empty<byte>(), Array.Empty<ulong>(),
            mainhand: selectedWeapons.Mainhand, offhand: selectedWeapons.Offhand);
        return hasOutfit ? WithOutfit(appearance, outfit.Desired) : appearance;
    }

    public void Restore(LogicalActorKey actor)
    {
        models.Remove(actor);
        weapons.Remove(actor);
        colorOutfits.Remove(actor);
        Outfits.CompleteRestore(actor);
    }

    private static AppearanceData WithOutfit(AppearanceData model, OutfitData outfit)
        => model.WithOutfit(outfit.Equipment.Select(ActorRegistry.ToEquipmentModelValue),
            outfit.VisorToggled, outfit.Facewear.IsAvailable ? outfit.Facewear.ModelId : model.FacewearModelId,
            outfit.HatVisible) with { ColoredEquipment = outfit.Equipment };
}
