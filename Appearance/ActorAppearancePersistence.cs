namespace ActorMorpher.Appearance;

public sealed class ActorAppearancePersistence
{
    private readonly Func<ulong> getOwner;
    private readonly Func<ActorSnapshot, WeaponPinScope?>? getWeaponPinScope;
    private readonly Dictionary<ulong, CharacterState> characters = new();
    private sealed class CharacterState
    {
        public readonly Dictionary<ActorContinuityKey, LogicalActorKey> Identities = new();
        public readonly Dictionary<LogicalActorKey, RetainedModel> Models = new();
        public readonly Dictionary<LogicalActorKey, WeaponOverride> Weapons = new();
        public readonly Dictionary<ulong, LogicalActorKey> LivingActors = new();
        public readonly Dictionary<LogicalActorKey, OutfitData> ColorOutfits = new();
        public readonly Dictionary<(LogicalActorKey Actor, ActorRepresentationKey Representation), CustomizeCopyData> OriginalCustomize = new();
        public readonly Dictionary<LogicalActorKey, ModifiedActor> ModifiedActors = new();
    }

    private sealed record ModifiedActor(ActorEntry Entry, PinnedOutfitConfiguration Identity,
        AppearanceData? AppearanceBeforeClearingSharedPins = null);

    private sealed record RetainedModel(AppearanceData Appearance, byte? MainhandJob, byte? OffhandJob)
    {
        internal bool AllowsWeapon(bool offhand, byte job)
            => (offhand ? OffhandJob : MainhandJob) is not { } weaponJob || weaponJob == job;
    }

    private readonly record struct WeaponOverride(ulong? Mainhand, ulong? Offhand,
        WeaponDyes MainhandDyes, WeaponDyes OffhandDyes);
    public ActorAppearancePersistence(Func<ulong>? getOwner = null)
        : this(getOwner, null) { }

    internal ActorAppearancePersistence(Func<ulong>? getOwner,
        Func<ActorSnapshot, WeaponPinScope?>? getWeaponPinScope)
    {
        this.getOwner = getOwner ?? (() => 0);
        this.getWeaponPinScope = getWeaponPinScope;
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

    private static void RememberModifiedActor(CharacterState state, ActorSnapshot actor)
    {
        Register(state, actor);
        if (state.ModifiedActors.TryGetValue(actor.LogicalKey, out var previous))
            actor = actor with { CurrentAppearance = actor.CurrentAppearance ?? previous.Entry.Current.CurrentAppearance };
        var entry = new ActorEntry(actor.LogicalKey, actor.Name, actor.ObjectKind, actor.IsLocalPlayer, [actor], actor.IsOwnMinion);
        if (previous is null)
            ForgetModifiedPinTarget(state, entry);
        state.ModifiedActors[actor.LogicalKey] = new(entry, PinnedOutfitConfiguration.CreateAppearanceIdentity(entry));
    }

    private static void ForgetModifiedPinTarget(CharacterState state, ActorEntry entry)
    {
        foreach (var (key, previous) in state.ModifiedActors)
            if (previous.Identity.Matches(entry))
                state.ModifiedActors.Remove(key);
    }

    public void RecordModel(ActorSnapshot actor, AppearanceData appearance)
        => RecordModel(actor, appearance, modelSearchWeaponJob: null);

    internal void RecordModel(ActorSnapshot actor, AppearanceData appearance, byte? modelSearchWeaponJob,
        bool equipmentEdited = false, bool weaponsEdited = false)
    {
        if (appearance.PinnedWeaponScope is not null || appearance.UsesRetainedPlayerEquipment)
            appearance = appearance with { PinnedWeaponScope = null, UsesRetainedPlayerEquipment = false };
        var state = GetState(getOwner());
        RememberModifiedActor(state, actor with { CurrentAppearance = appearance });
        state.Weapons.Remove(actor.LogicalKey);
        var previous = state.Models.GetValueOrDefault(actor.LogicalKey);
        state.Models[actor.LogicalKey] = RetainModel(previous, appearance,
            weaponsEdited && previous?.MainhandJob is not null ? actor.ClassJob : modelSearchWeaponJob,
            equipmentEdited);
        if (EquipmentDisplayFormatting.CreateHumanOutfit(appearance) is { } outfit)
            state.ColorOutfits[actor.LogicalKey] = outfit;
        else
            state.ColorOutfits.Remove(actor.LogicalKey);
    }

    private static RetainedModel RetainModel(RetainedModel? previous, AppearanceData appearance, byte? weaponJob,
        bool equipmentEdited)
    {
        if (appearance.Category != ModelCategory.Human)
            return new(appearance, null, null);
        if (weaponJob is { } job)
            return new(appearance with { UsesGameHeadSettings = false }, job, job);
        if (previous?.MainhandJob is null)
            return new(appearance, null, null);

        // Publishing a body/customize edit must not adopt another job's rendered weapons.
        var saved = previous.Appearance;
        return previous with
        {
            Appearance = appearance with
            {
                Mainhand = saved.Mainhand,
                Offhand = saved.Offhand,
                MainhandDyes = saved.MainhandDyes,
                OffhandDyes = saved.OffhandDyes,
                UsesGameHeadSettings = !equipmentEdited && saved.UsesGameHeadSettings,
            },
        };
    }

    public void RecordOutfit(ActorSnapshot actor, OutfitData outfit)
    {
        var state = GetState(getOwner());
        RememberModifiedActor(state, actor);
        if (state.Models.TryGetValue(actor.LogicalKey, out var model))
            state.Models[actor.LogicalKey] = model with { Appearance = WithOutfit(model.Appearance, outfit) };
    }

    public AppearanceData? GetModel(LogicalActorKey actor) => GetState(getOwner()).Models.GetValueOrDefault(actor)?.Appearance;

    internal CustomizeEditState CreateCustomizeDraft(ActorSnapshot actor, AppearanceData rendered)
        => new(actor, rendered.Customize, rendered.CustomizeColors,
            GetState(getOwner()).OriginalCustomize.GetValueOrDefault((actor.LogicalKey, actor.RepresentationKey)));

    internal void RememberOriginalCustomize(CustomizeEditState draft)
        => GetState(getOwner()).OriginalCustomize.TryAdd((draft.Actor, draft.Representation), draft.GetOriginal());

    internal void RebaseOriginalCustomize(ActorSnapshot actor, CustomizeCopyData? original)
    {
        var originals = GetState(getOwner()).OriginalCustomize;
        var key = (actor.LogicalKey, actor.RepresentationKey);
        if (original is null)
            originals.Remove(key);
        else
            originals[key] = original;
    }

    internal bool HasLocalModelSearchAppearance(LogicalActorKey actor)
        => GetState(getOwner()).Models.GetValueOrDefault(actor)?.MainhandJob is not null;

    internal bool ReleaseModelSearchHeadSettings(LogicalActorKey actor, out AppearanceData appearance)
    {
        appearance = null!;
        var models = GetState(getOwner()).Models;
        if (!models.TryGetValue(actor, out var model) || model.MainhandJob is null
            || model.Appearance.UsesGameHeadSettings)
            return false;

        appearance = model.Appearance with { UsesGameHeadSettings = true };
        models[actor] = model with { Appearance = appearance };
        return true;
    }

    internal AppearanceData ResolveCustomizeEquipment(ActorSnapshot actor, AppearanceData desired)
    {
        if (!desired.UsesRetainedPlayerEquipment
            || GetState(getOwner()).Models.GetValueOrDefault(actor.LogicalKey) is not { MainhandJob: not null } model)
            return desired;

        var equipment = ForCurrentWeaponJob(actor, model.Appearance);
        return desired with
        {
            Mainhand = equipment.Mainhand,
            Offhand = equipment.Offhand,
            MainhandDyes = equipment.MainhandDyes,
            OffhandDyes = equipment.OffhandDyes,
            UsesGameHeadSettings = equipment.UsesGameHeadSettings,
            UsesRetainedPlayerEquipment = false,
        };
    }

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
        RememberModifiedActor(state, actor);
        if (state.Models.TryGetValue(actor.LogicalKey, out var model))
        {
            var appearance = model.Appearance;
            state.Models[actor.LogicalKey] = offhand
                ? model with
                {
                    Appearance = appearance with { Offhand = weapon, OffhandDyes = dyes ?? appearance.OffhandDyes },
                    OffhandJob = model.OffhandJob is null ? null : actor.ClassJob,
                }
                : model with
                {
                    Appearance = appearance with { Mainhand = weapon, MainhandDyes = dyes ?? appearance.MainhandDyes },
                    MainhandJob = model.MainhandJob is null ? null : actor.ClassJob,
                };
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
            selected = offhand ? model.Appearance.Offhand : model.Appearance.Mainhand;
            dyes = offhand ? model.Appearance.OffhandDyes : model.Appearance.MainhandDyes;
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

    internal WeaponDyes GetWeaponDyes(ActorSnapshot actor, bool offhand, ulong? rendered)
    {
        var dyes = GetWeaponDyes(actor.LogicalKey, offhand, rendered);
        if (dyes == default)
            return default;
        if (getWeaponPinScope?.Invoke(actor) is { } scope)
            return scope.AppliesTo(actor.ClassJob) ? dyes : default;
        var model = GetState(getOwner()).Models.GetValueOrDefault(actor.LogicalKey);
        return model?.AllowsWeapon(offhand, actor.ClassJob) == false ? default : dyes;
    }

    public AppearanceData? GetRetainedAppearance(ActorSnapshot snapshot)
        => GetModel(snapshot.LogicalKey) is { } model ? ForCurrentWeaponJob(snapshot, model) : null;

    private AppearanceData ForCurrentWeaponJob(ActorSnapshot actor, AppearanceData appearance)
    {
        if (getWeaponPinScope?.Invoke(actor) is { } scope)
            return (appearance with { PinnedWeaponScope = scope }).ForWeaponJob(actor.ClassJob, actor.CurrentAppearance);
        if (!GetState(getOwner()).Models.TryGetValue(actor.LogicalKey, out var model))
            return appearance;

        var useMainhand = model.AllowsWeapon(false, actor.ClassJob);
        var useOffhand = model.AllowsWeapon(true, actor.ClassJob);
        return useMainhand && useOffhand ? appearance : appearance with
        {
            Mainhand = useMainhand ? appearance.Mainhand : actor.CurrentAppearance?.Mainhand,
            Offhand = useOffhand ? appearance.Offhand : actor.CurrentAppearance?.Offhand,
            MainhandDyes = useMainhand ? appearance.MainhandDyes : actor.CurrentAppearance?.MainhandDyes ?? default,
            OffhandDyes = useOffhand ? appearance.OffhandDyes : actor.CurrentAppearance?.OffhandDyes ?? default,
        };
    }

    internal AppearanceData? GetCreateAppearance(ActorSnapshot actor, uint currentModelId, out bool outfitOnly)
    {
        var appearance = GetCreateAppearance(actor.LogicalKey, currentModelId, out outfitOnly);
        if (appearance is null)
            return null;
        if (appearance.UsesGameHeadSettings)
            appearance = appearance with { HatVisible = null, FacewearModelId = null, VisorToggled = null };
        return ForCurrentWeaponJob(actor with { CurrentAppearance = null }, appearance);
    }

    public AppearanceData? GetCreateAppearance(LogicalActorKey actor, uint currentModelId, out bool outfitOnly)
    {
        var owner = getOwner();
        var state = GetState(owner);
        outfitOnly = false;
        if (state.Models.TryGetValue(actor, out var model))
            return model.Appearance;
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

    internal IReadOnlyList<ActorEntry> GetAbsentModifiedActors(IReadOnlyList<ActorEntry> present,
        IReadOnlyList<ActorEntry> modifiedPresent)
    {
        var owner = getOwner();
        var state = GetState(owner);
        if (state.ModifiedActors.Count == 0)
            return [];
        var presentKeys = present.Select(static actor => actor.Key).ToHashSet();
        return state.ModifiedActors.Values
            .Where(actor => !presentKeys.Contains(actor.Entry.Key)
                && HasRetainedEdit(owner, state, actor.Entry)
                && !modifiedPresent.Any(actor.Identity.Matches))
            .Select(actor => actor.Entry)
            .ToArray();
    }

    private bool HasRetainedEdit(ulong owner, CharacterState state, ActorEntry actor)
        => state.Models.ContainsKey(actor.Key) || (actor.Current.CurrentAppearance is not null
            && (state.Weapons.ContainsKey(actor.Key) || HasChangedOutfit(owner, actor.Key)));

    private bool HasChangedOutfit(ulong owner, LogicalActorKey actor)
        => Outfits.TryGet(owner, actor, out var outfit)
            && !OutfitDataValueComparer.AreEqual(outfit.Original, outfit.Desired);

    internal AppearanceData? CaptureAbsentPinAppearance(ActorEntry actor)
    {
        var owner = getOwner();
        var state = GetState(owner);
        return state.ModifiedActors[actor.Key].AppearanceBeforeClearingSharedPins
            ?? ComposeRetainedPinAppearance(owner, state, actor.Current);
    }

    internal void RememberSharedAppearanceForBulkPin(PinnedOutfitStore pins)
    {
        var owner = getOwner();
        var state = GetState(owner);
        foreach (var actor in state.ModifiedActors.Values.ToArray())
        {
            var appearance = actor.AppearanceBeforeClearingSharedPins
                ?? ComposeRetainedPinAppearance(owner, state, actor.Entry.Current);
            if (appearance is not null && pins.ApplyCurrentNpcSharingToRetainedPin(actor.Entry, appearance) is { } shared)
                state.ModifiedActors[actor.Entry.Key] = actor with { AppearanceBeforeClearingSharedPins = shared };
        }
    }

    private AppearanceData? ComposeRetainedPinAppearance(ulong owner, CharacterState state, ActorSnapshot actor)
    {
        if (GetRetainedAppearance(actor) is { } model)
            return model;
        var appearance = actor.CurrentAppearance;
        if (appearance is null)
            return null;
        if (Outfits.TryGet(owner, actor.LogicalKey, out var outfit) && appearance.Category == ModelCategory.Human)
            appearance = WithOutfit(appearance, outfit.Desired);
        if (state.Weapons.TryGetValue(actor.LogicalKey, out var weapons))
            appearance = appearance with
            {
                Mainhand = weapons.Mainhand ?? appearance.Mainhand,
                Offhand = weapons.Offhand ?? appearance.Offhand,
                MainhandDyes = weapons.Mainhand is null ? appearance.MainhandDyes : weapons.MainhandDyes,
                OffhandDyes = weapons.Offhand is null ? appearance.OffhandDyes : weapons.OffhandDyes,
            };
        return appearance;
    }

    public void Restore(LogicalActorKey actor) => Restore(actor, null);

    internal void Restore(LogicalActorKey actor, ActorEntry? pinTarget)
    {
        var owner = getOwner();
        var state = GetState(owner);
        state.Models.Remove(actor);
        state.Weapons.Remove(actor);
        state.ColorOutfits.Remove(actor);
        state.ModifiedActors.Remove(actor);
        if (pinTarget is not null)
            ForgetModifiedPinTarget(state, pinTarget);
        foreach (var key in state.OriginalCustomize.Keys.Where(key => key.Actor == actor).ToArray())
            state.OriginalCustomize.Remove(key);
        Outfits.CompleteRestore(owner, actor);
    }

    private static AppearanceData WithOutfit(AppearanceData model, OutfitData outfit)
        => model.WithOutfit(outfit.Equipment.Select(ActorRegistry.ToEquipmentModelValue),
            outfit.VisorToggled, outfit.Facewear.IsAvailable ? outfit.Facewear.ModelId : model.FacewearModelId,
            outfit.HatVisible) with
        {
            ColoredEquipment = outfit.Equipment,
            VieraEarsHidden = outfit.VieraEarsHidden ?? model.VieraEarsHidden,
            UsesGameHeadSettings = false,
        };
}
