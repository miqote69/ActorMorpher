namespace ActorMorpher.Appearance;

public sealed class ActorAppearancePersistence
{
    private readonly Func<ulong> getOwner;
    private readonly Dictionary<ulong, CharacterState> characters = new();
    private sealed class CharacterState
    {
        public readonly Dictionary<ActorContinuityKey, LogicalActorKey> Identities = new();
        public readonly Dictionary<LogicalActorKey, AppearanceData> Models = new();
        public readonly Dictionary<LogicalActorKey, WeaponOverride> Weapons = new();
        public readonly Dictionary<ulong, LogicalActorKey> LivingActors = new();
        public readonly Dictionary<LogicalActorKey, OutfitData> ColorOutfits = new();
    }
    private readonly record struct WeaponOverride(ulong? Mainhand, ulong? Offhand,
        WeaponDyes MainhandDyes, WeaponDyes OffhandDyes);
    public ActorAppearancePersistence(Func<ulong>? getOwner = null)
    {
        this.getOwner = getOwner ?? (() => 0);
        Outfits = new(this.getOwner);
    }

    private CharacterState GetState(ulong owner)
    {
        if (!characters.TryGetValue(owner, out var state))
            characters.Add(owner, state = new());
        return state;
    }

    internal OutfitData? GetColorOutfit(LogicalActorKey actor)
        => GetState(getOwner()).ColorOutfits.GetValueOrDefault(actor);
    internal void SetColorOutfit(LogicalActorKey actor, OutfitData outfit)
        => GetState(getOwner()).ColorOutfits[actor] = outfit;
    public OutfitOverrideStore Outfits { get; }

    public LogicalActorKey Resolve(ActorContinuityKey identity, LogicalActorKey current)
    {
        var state = GetState(getOwner());
        if (current.Lifetime != 0 && state.LivingActors.TryGetValue(current.Lifetime, out var living))
        {
            BindIdentity(state, identity, living);
            return living;
        }
        if (identity.Source != 5 && state.Identities.TryGetValue(identity, out var retained))
        {
            if (current.Lifetime != 0)
                state.LivingActors[current.Lifetime] = retained;
            return retained;
        }
        return current;
    }

    public void BindIdentity(ActorContinuityKey identity, LogicalActorKey actor)
        => BindIdentity(GetState(getOwner()), identity, actor);

    private static void BindIdentity(CharacterState state, ActorContinuityKey identity, LogicalActorKey actor)
    {
        if (identity.Source != 5)
            state.Identities[identity] = actor;
        if (actor.Lifetime != 0)
            state.LivingActors[actor.Lifetime] = actor;
    }

    public void Register(ActorSnapshot actor)
        => Register(GetState(getOwner()), actor);

    private static void Register(CharacterState state, ActorSnapshot actor)
    {
        if (actor.ContinuityKey is { } identity)
            BindIdentity(state, identity, actor.LogicalKey);
    }

    public void RecordModel(ActorSnapshot actor, AppearanceData appearance)
    {
        var state = GetState(getOwner());
        Register(state, actor);
        state.Weapons.Remove(actor.LogicalKey);
        state.Models[actor.LogicalKey] = appearance;
        if (EquipmentDisplayFormatting.CreateHumanOutfit(appearance) is { } outfit)
            state.ColorOutfits[actor.LogicalKey] = outfit;
        else
            state.ColorOutfits.Remove(actor.LogicalKey);
    }

    public void RecordOutfit(ActorSnapshot actor, OutfitData outfit)
    {
        var state = GetState(getOwner());
        Register(state, actor);
        if (state.Models.TryGetValue(actor.LogicalKey, out var model))
            state.Models[actor.LogicalKey] = WithOutfit(model, outfit);
    }

    public AppearanceData? GetModel(LogicalActorKey actor) => GetState(getOwner()).Models.GetValueOrDefault(actor);

    internal OutfitData? GetRetainedOutfit(LogicalActorKey actor)
    {
        var owner = getOwner();
        return GetState(owner).Models.ContainsKey(actor) ? null
            : Outfits.TryGet(owner, actor, out var outfit) ? outfit.Desired : null;
    }

    internal bool HasWeaponOverride(LogicalActorKey actor) => GetState(getOwner()).Weapons.ContainsKey(actor);

    internal void RecordWeapon(ActorSnapshot actor, bool offhand, ulong weapon, WeaponDyes? dyes = null)
    {
        var state = GetState(getOwner());
        Register(state, actor);
        if (state.Models.TryGetValue(actor.LogicalKey, out var model))
        {
            state.Models[actor.LogicalKey] = offhand
                ? model with { Offhand = weapon, OffhandDyes = dyes ?? model.OffhandDyes }
                : model with { Mainhand = weapon, MainhandDyes = dyes ?? model.MainhandDyes };
            return;
        }
        var current = state.Weapons.GetValueOrDefault(actor.LogicalKey);
        state.Weapons[actor.LogicalKey] = offhand
            ? current with { Offhand = weapon, OffhandDyes = dyes ?? current.OffhandDyes }
            : current with { Mainhand = weapon, MainhandDyes = dyes ?? current.MainhandDyes };
    }

    internal WeaponDyes GetWeaponDyes(LogicalActorKey actor, bool offhand, ulong? rendered)
    {
        var state = GetState(getOwner());
        ulong? selected;
        WeaponDyes dyes;
        if (state.Models.TryGetValue(actor, out var model))
        {
            selected = offhand ? model.Offhand : model.Mainhand;
            dyes = offhand ? model.OffhandDyes : model.MainhandDyes;
        }
        else
        {
            var weapon = state.Weapons.GetValueOrDefault(actor);
            selected = offhand ? weapon.Offhand : weapon.Mainhand;
            dyes = offhand ? weapon.OffhandDyes : weapon.MainhandDyes;
        }
        return selected.HasValue && rendered.HasValue
            && (selected.Value & WeaponSelection.ModelMask) == (rendered.Value & WeaponSelection.ModelMask)
            ? dyes : default;
    }

    public AppearanceData? GetRetainedAppearance(ActorSnapshot snapshot)
        => GetModel(snapshot.LogicalKey);

    public AppearanceData? GetCreateAppearance(LogicalActorKey actor, uint currentModelId, out bool outfitOnly)
    {
        var owner = getOwner();
        var state = GetState(owner);
        outfitOnly = false;
        if (state.Models.TryGetValue(actor, out var model))
            return model;
        var hasOutfit = Outfits.TryGet(owner, actor, out var outfit);
        var hasWeapons = state.Weapons.TryGetValue(actor, out var selectedWeapons);
        if (!hasOutfit && !hasWeapons)
            return null;
        outfitOnly = true;
        // The existing partial Create path keeps the game's model/customize and applies only populated fields.
        var appearance = AppearanceData.Create(currentModelId, ModelCategory.Human, 0,
            AppearanceCompleteness.ModelOnly, Array.Empty<byte>(), Array.Empty<ulong>(),
            mainhand: selectedWeapons.Mainhand, offhand: selectedWeapons.Offhand)
            with { MainhandDyes = selectedWeapons.MainhandDyes, OffhandDyes = selectedWeapons.OffhandDyes };
        return hasOutfit ? WithOutfit(appearance, outfit.Desired) : appearance;
    }

    public void Restore(LogicalActorKey actor)
    {
        var owner = getOwner();
        var state = GetState(owner);
        state.Models.Remove(actor);
        state.Weapons.Remove(actor);
        state.ColorOutfits.Remove(actor);
        Outfits.CompleteRestore(owner, actor);
    }

    private static AppearanceData WithOutfit(AppearanceData model, OutfitData outfit)
        => model.WithOutfit(outfit.Equipment.Select(ActorRegistry.ToEquipmentModelValue),
            outfit.VisorToggled, outfit.Facewear.IsAvailable ? outfit.Facewear.ModelId : model.FacewearModelId,
            outfit.HatVisible) with
        {
            ColoredEquipment = outfit.Equipment,
            VieraEarsHidden = outfit.VieraEarsHidden ?? model.VieraEarsHidden,
        };
}
