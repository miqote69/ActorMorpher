using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Services;
using ActorMorpher.Localization;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using NativeGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace ActorMorpher.Actors;

public sealed unsafe class ActorRegistry : IDisposable
{
    internal const int GPoseLocalPlayerSlot = 201;

    private readonly IObjectTable objectTable;
    private readonly IClientState clientState;
    private readonly IFramework framework;
    private readonly IHumanModelClassifier humanModelClassifier;
    private readonly IDiagnosticLog diagnostics;
    private readonly Func<ActorSnapshot, AppearanceData?> getRetainedAppearance;
    private readonly ActorAppearancePersistence? persistence;
    private readonly NativeActorContinuity continuity;
    internal Func<nint, LogicalActorKey?>? ResolveCopyActor { get; set; }
    private readonly object syncRoot = new();
    private IReadOnlyList<ActorEntry> entries = Array.Empty<ActorEntry>();
    private long publicationVersion;
    private IReadOnlyDictionary<ActorRepresentationKey, LogicalActorKey> gposeMappings
        = new Dictionary<ActorRepresentationKey, LogicalActorKey>();
    private uint lastTerritoryId;

    public ActorRegistry(
        IObjectTable objectTable,
        IClientState clientState,
        IFramework framework,
        IHumanModelClassifier humanModelClassifier,
        IDiagnosticLog? diagnostics = null,
        Func<ActorSnapshot, AppearanceData?>? getRetainedAppearance = null,
        ActorAppearancePersistence? persistence = null,
        NativeActorContinuity? continuity = null)
    {
        this.objectTable = objectTable;
        this.clientState = clientState;
        this.framework = framework;
        this.humanModelClassifier = humanModelClassifier;
        this.diagnostics = diagnostics ?? NullDiagnosticLog.Instance;
        this.getRetainedAppearance = getRetainedAppearance ?? (_ => null);
        this.persistence = persistence;
        this.continuity = continuity ?? new NativeActorContinuity();
        framework.Update += OnFrameworkUpdate;
    }

    public IReadOnlyList<ActorEntry> Entries
    {
        get
        {
            lock (syncRoot)
                return entries;
        }
    }

    public void Dispose()
        => framework.Update -= OnFrameworkUpdate;

    public bool TryGet(LogicalActorKey key, out ActorEntry actor)
    {
        lock (syncRoot)
        {
            actor = entries.FirstOrDefault(candidate => candidate.Key == key)!;
            return actor is not null;
        }
    }

    internal AppearanceData? GetPublishedAppearance(ActorRepresentationKey representation)
    {
        lock (syncRoot)
            return GetPublishedAppearance(entries, representation);
    }

    internal Func<LogicalActorKey, OutfitData?>? GetColorOutfit { get; set; }
    internal Func<ushort, byte, FacewearAppearance>? ResolveFacewear { get; set; }

    internal AppearanceData? CaptureCurrentAppearance(ActorSnapshot expected)
    {
        var key = expected.RepresentationKey;
        var obj = objectTable[key.ObjectIndex];
        if (obj is null || obj.GameObjectId != key.GameObjectId || obj.EntityId != key.EntityId)
            return null;
        var fresh = CreateSnapshot(obj, clientState.TerritoryType, null, nint.Zero);
        return DescribeRenderedAppearance(expected, fresh);
    }

    internal static AppearanceData? DescribeRenderedAppearance(ActorSnapshot expected, ActorSnapshot? fresh)
    {
        if (fresh is null || fresh.LogicalKey != expected.LogicalKey)
            return null;
        var raw = fresh.CurrentAppearance;
        return raw is not null && expected.IsAppearanceManaged
            && expected.CurrentAppearance is { } managed && managed.Category == raw.Category
                // ModelCharaId and IsHatHidden belong to restored game backing, not
                // rendered readback. Use the same descriptors as managed publication;
                // equipment (including the head), weapons and other fields stay fresh.
                ? raw with
                {
                    ModelCharaId = managed.ModelCharaId,
                    HatVisible = raw.Category == ModelCategory.Human ? managed.HatVisible : raw.HatVisible,
                }
                : raw;
    }

    internal (long PublicationVersion, AppearanceData? Appearance) GetPublishedAppearanceState(
        ActorRepresentationKey representation)
    {
        lock (syncRoot)
            return (publicationVersion, GetPublishedAppearance(entries, representation));
    }

    internal bool RecordAppliedAppearance(
        LogicalActorKey actor,
        ActorRepresentationKey representation,
        AppearanceData appearance)
    {
        lock (syncRoot)
        {
            var found = false;
            entries = entries.Select(entry =>
            {
                if (entry.Key != actor)
                    return entry;

                var representations = entry.Representations.Select(snapshot =>
                {
                    if (snapshot.RepresentationKey != representation)
                        return snapshot;
                    found = true;
                    return ApplyManagedAppearance(snapshot, appearance);
                }).ToArray();
                return found ? entry with { Representations = representations } : entry;
            }).ToArray();
            if (found)
                publicationVersion++;
            return found;
        }
    }

    internal bool ClearManagedAppearance(LogicalActorKey actor)
    {
        lock (syncRoot)
        {
            var found = false;
            entries = entries.Select(entry =>
            {
                if (entry.Key != actor)
                    return entry;
                var representations = entry.Representations.Select(snapshot =>
                {
                    if (!snapshot.IsAppearanceManaged)
                        return snapshot;
                    found = true;
                    return snapshot with { IsAppearanceManaged = false };
                }).ToArray();
                return found ? entry with { Representations = representations } : entry;
            }).ToArray();
            if (found)
                publicationVersion++;
            return found;
        }
    }

    internal static AppearanceData? GetPublishedAppearance(
        IEnumerable<ActorEntry> publishedEntries,
        ActorRepresentationKey representation)
    {
        foreach (var snapshot in publishedEntries.SelectMany(static actor => actor.Representations))
        {
            if (snapshot.RepresentationKey == representation)
                return snapshot.CurrentAppearance;
        }

        return null;
    }

    public bool TryGetGPoseLocalPlayer(LogicalActorKey logicalKey, out ActorSnapshot snapshot)
    {
        snapshot = null!;
        if (!clientState.IsGPosing || logicalKey.ObjectKind != ObjectKind.Pc)
            return false;

        var candidate = objectTable[GPoseLocalPlayerSlot];
        if (candidate is null
            || candidate.Address == nint.Zero
            || candidate.ObjectKind != ObjectKind.Pc)
            return false;

        var created = CreateSnapshot(candidate, clientState.TerritoryType, null, nint.Zero);
        if (created is null)
            return false;

        var mapped = created with
        {
            LogicalKey = logicalKey,
            RepresentationKey = new ActorRepresentationKey(
                GPoseLocalPlayerSlot,
                candidate.GameObjectId,
                candidate.EntityId,
                true,
                clientState.TerritoryType),
            IsLocalPlayer = true,
        };
        IReadOnlyDictionary<(LogicalActorKey, ActorRepresentationKey), AppearanceData> retainedAppearances;
        lock (syncRoot)
        {
            retainedAppearances = entries
                .SelectMany(static entry => entry.Representations)
                .Where(static representation => representation.IsAppearanceManaged
                    && representation.CurrentAppearance is not null)
                .ToDictionary(
                    static representation => (representation.LogicalKey, representation.RepresentationKey),
                    static representation => representation.CurrentAppearance!);
        }
        snapshot = RetainManagedAppearance(mapped, retainedAppearances, getRetainedAppearance);
        return true;
    }

    public void SetGPoseMappings(IReadOnlyDictionary<ActorRepresentationKey, LogicalActorKey> mappings)
    {
        lock (syncRoot)
            gposeMappings = new Dictionary<ActorRepresentationKey, LogicalActorKey>(mappings);
    }

    public void ClearGPoseMappings()
        => SetGPoseMappings(new Dictionary<ActorRepresentationKey, LogicalActorKey>());

    private void OnFrameworkUpdate(IFramework _)
        => Refresh();

    private void Refresh()
    {
        var territoryId = clientState.TerritoryType;
        if (lastTerritoryId != 0 && lastTerritoryId != territoryId)
        {
            ClearGPoseMappings();
            diagnostics.Write(new DiagnosticLogEntry
            {
                EventId = DiagnosticEventIds.ActorRegistryChanged,
                Category = DiagnosticCategory.ActorRegistry,
                Message = "Actor registry territory changed.",
                Properties = new Dictionary<string, object?> { ["previousTerritoryId"] = lastTerritoryId, ["territoryId"] = territoryId },
            });
        }
        lastTerritoryId = territoryId;
        var localPlayer = objectTable.LocalPlayer;
        var localPlayerId = localPlayer?.GameObjectId;
        var localPlayerChildObjectAddress = localPlayer is null || localPlayer.Address == nint.Zero
            ? nint.Zero
            : (nint)(((Character*)localPlayer.Address)->ChildObject);
        var snapshots = objectTable
            .Where(static obj => obj is not null
                && obj.Address != nint.Zero
                && obj.IsValid()
                && IsSupportedObjectKind(obj.ObjectKind))
            .Select(obj => CreateSnapshot(obj, territoryId, localPlayerId, localPlayerChildObjectAddress))
            .Where(static snapshot => snapshot is not null)
            .Select(static snapshot => snapshot!)
            .ToArray();

        IReadOnlyDictionary<ActorRepresentationKey, LogicalActorKey> mappings;
        IReadOnlyDictionary<(LogicalActorKey, ActorRepresentationKey), AppearanceData> retainedAppearances;
        IReadOnlyList<ActorEntry> previousEntries;
        lock (syncRoot)
        {
            previousEntries = entries;
            mappings = gposeMappings;
            retainedAppearances = entries
                .SelectMany(static entry => entry.Representations)
                .Where(static snapshot => snapshot.IsAppearanceManaged && snapshot.CurrentAppearance is not null)
                .ToDictionary(static snapshot => (snapshot.LogicalKey, snapshot.RepresentationKey), static snapshot => snapshot.CurrentAppearance!);
        }

        TryWriteFirstAppliedSnapshotDiagnostics(previousEntries, snapshots);

        var mappedSnapshots = snapshots.Select(snapshot =>
        {
            if (!mappings.TryGetValue(snapshot.RepresentationKey, out var logicalKey))
                return RetainManagedAppearance(snapshot, retainedAppearances, getRetainedAppearance);

            var mapped = snapshot with
            {
                LogicalKey = logicalKey,
                RepresentationKey = snapshot.RepresentationKey with { IsGPoseRepresentation = true },
            };
            return RetainManagedAppearance(mapped, retainedAppearances, getRetainedAppearance);
        });

        var next = OrderForDisplay(
            mappedSnapshots
                .GroupBy(static snapshot => snapshot.LogicalKey)
                .Select(static group => new ActorEntry(
                    group.Key,
                    group.First().Name,
                    group.First().ObjectKind,
                    group.Any(static representation => representation.IsLocalPlayer),
                    group.OrderBy(static representation => representation.RepresentationKey.IsGPoseRepresentation).ToArray(),
                    group.Any(static representation => representation.IsOwnMinion))),
            GameTextComparison.GetComparer(clientState.ClientLanguage));

        int previousCount;
        lock (syncRoot)
        {
            previousCount = entries.Count;
            entries = next;
            publicationVersion++;
        }
        if (previousCount != next.Count)
            diagnostics.Write(new DiagnosticLogEntry
            {
                EventId = DiagnosticEventIds.ActorRegistryChanged,
                Category = DiagnosticCategory.ActorRegistry,
                Message = "Logical actor count changed.",
                Properties = new Dictionary<string, object?> { ["previousCount"] = previousCount, ["currentCount"] = next.Count },
            });
    }

    private ActorSnapshot? CreateSnapshot(
        IGameObject obj,
        uint territoryId,
        ulong? localPlayerId,
        nint localPlayerChildObjectAddress)
    {
        if (obj.Address == nint.Zero)
            return null;

        var native = (Character*)obj.Address;
        var objectIndex = checked((ushort)obj.ObjectIndex);
        var modelCharaId = checked((uint)native->ModelContainer.ModelCharaId);
        var isHuman = humanModelClassifier.IsHuman(modelCharaId);
        var name = obj.Name.ToString();
        if (string.IsNullOrWhiteSpace(name))
            name = $"{obj.ObjectKind} {obj.BaseId}";

        var representation = new ActorRepresentationKey(
            objectIndex,
            obj.GameObjectId,
            obj.EntityId,
            false,
            territoryId);
        var logical = new LogicalActorKey(
            objectIndex,
            obj.GameObjectId,
            obj.EntityId,
            obj.BaseId,
            obj.ObjectKind,
            territoryId);

        var observedIdentity = continuity.Read((NativeGameObject*)native, territoryId);
        var identity = observedIdentity.Key;
        logical = logical with { Continuity = identity, Lifetime = observedIdentity.Lifetime };
        var copyActor = ResolveCopyActor?.Invoke(obj.Address);
        logical = copyActor ?? persistence?.Resolve(identity, logical) ?? logical;
        var snapshot = new ActorSnapshot(
            logical,
            representation,
            name,
            obj.ObjectKind,
            obj.BaseId,
            modelCharaId,
            isHuman ? native->DrawData.CustomizeData.Race : null,
            isHuman ? native->DrawData.CustomizeData.Sex : null,
            isHuman ? native->DrawData.CustomizeData.BodyType : null,
            native->CharacterData.ClassJob,
            native->CharacterData.Level,
            obj.GameObjectId == localPlayerId,
            IsOwnMinion(obj.ObjectKind, obj.Address, localPlayerChildObjectAddress))
        {
            ContinuityKey = copyActor is null ? identity : null,
        };

        var characterBase = ((NativeGameObject*)native)->GetCharacterBase();
        if (characterBase is null)
            return snapshot;

        var category = characterBase->GetModelType() switch
        {
            CharacterBase.ModelType.Human => ModelCategory.Human,
            CharacterBase.ModelType.DemiHuman => ModelCategory.Demihuman,
            CharacterBase.ModelType.Monster => ModelCategory.Monster,
            _ => ModelCategory.Other,
        };
        var customize = Array.Empty<byte>();
        var equipment = Array.Empty<ulong>();
        ulong? mainhand = null;
        ulong? offhand = null;
        bool? visorToggled = null;
        ushort? facewearModelId = null;
        bool? hatVisible = null;

        if (category == ModelCategory.Human)
        {
            var human = (Human*)characterBase;
            customize = human->Customize.Data.ToArray();
            var outfit = NativeOutfitMemory.CaptureRendered(native, human, ResolveFacewear);
            equipment = outfit.Equipment.Select(ToEquipmentModelValue).ToArray();
            mainhand = NativeAppearanceMemory.CaptureRenderedWeapon(native, DrawDataContainer.WeaponSlot.MainHand);
            offhand = NativeAppearanceMemory.CaptureRenderedWeapon(native, DrawDataContainer.WeaponSlot.OffHand);
            visorToggled = outfit.VisorToggled;
            facewearModelId = outfit.Facewear.IsAvailable ? outfit.Facewear.ModelId : null;
            hatVisible = outfit.HatVisible;
        }
        else if (category == ModelCategory.Demihuman)
        {
            equipment = CaptureRenderedEquipment(characterBase);
        }

        var currentAppearance = ApplyCategoryContract(AppearanceData.Create(
            modelCharaId,
            category,
            0,
            category switch
            {
                ModelCategory.Human or ModelCategory.Demihuman => AppearanceCompleteness.Complete,
                ModelCategory.Monster => AppearanceCompleteness.ModelOnly,
                _ => AppearanceCompleteness.Unsupported,
            },
            customize,
            equipment,
            NativeModelScale.CaptureRendered(native),
            mainhand,
            offhand,
            visorToggled,
            facewearModelId,
            hatVisible));
        if (category == ModelCategory.Human && GetColorOutfit?.Invoke(snapshot.LogicalKey) is { } colors
            && EquipmentDisplayFormatting.CreateHumanOutfit(currentAppearance) is { } renderedOutfit)
            currentAppearance = currentAppearance with
            {
                ColoredEquipment = NativeOutfitMemory.WithColors(renderedOutfit, colors).Equipment,
            };
        snapshot = snapshot with { CurrentAppearance = currentAppearance };
        if (category != ModelCategory.Human)
            return snapshot;

        var visibleCustomize = ((Human*)characterBase)->Customize;
        return snapshot.WithVisibleHumanCustomize(
            visibleCustomize.Race,
            visibleCustomize.Sex,
            visibleCustomize.BodyType);
    }

    internal static bool IsCompleteCurrentAppearance(AppearanceData? current)
    {
        if (current is null)
            return false;

        return current.Category switch
        {
            ModelCategory.Human => current.Completeness == AppearanceCompleteness.Complete
                && current.Customize.Length == 26
                && current.Equipment.Length == 10
                && current.ModelScale is not null
                && current.Mainhand is not null
                && current.Offhand is not null
                && current.VisorToggled is not null
                && current.HatVisible is not null,
            ModelCategory.Demihuman => current.Completeness == AppearanceCompleteness.Complete
                && current.Customize.IsEmpty
                && current.Equipment.Length == 10
                && current.ModelScale is not null
                && current.Mainhand is null
                && current.Offhand is null
                && current.VisorToggled is null
                && current.FacewearModelId is null
                && current.HatVisible is null,
            ModelCategory.Monster => current.Completeness == AppearanceCompleteness.ModelOnly
                && current.Customize.IsEmpty
                && current.Equipment.IsEmpty
                && current.ModelScale is not null
                && current.Mainhand is null
                && current.Offhand is null
                && current.VisorToggled is null
                && current.FacewearModelId is null
                && current.HatVisible is null,
            _ => false,
        };
    }

    internal static ActorSnapshot ApplyManagedAppearance(
        ActorSnapshot snapshot,
        AppearanceData appearance)
    {
        var isHuman = appearance.Category == ModelCategory.Human && appearance.Customize.Length >= 3;
        return snapshot with
        {
            ModelCharaId = appearance.ModelCharaId,
            Race = isHuman ? appearance.Customize[0] : null,
            Gender = isHuman ? appearance.Customize[1] : null,
            BodyType = isHuman ? appearance.Customize[2] : null,
            CurrentAppearance = appearance,
            IsAppearanceManaged = true,
        };
    }

    internal static ActorSnapshot RetainManagedAppearance(
        ActorSnapshot snapshot,
        IReadOnlyDictionary<(LogicalActorKey, ActorRepresentationKey), AppearanceData> retainedAppearances,
        Func<ActorSnapshot, AppearanceData?> getTransitionAppearance)
    {
        if (retainedAppearances.TryGetValue((snapshot.LogicalKey, snapshot.RepresentationKey), out var retained))
            return MergeManagedAppearance(snapshot, retained);

        var current = snapshot.CurrentAppearance;
        var transitioned = getTransitionAppearance(snapshot);
        return transitioned is null
            || current is null
            || !IsCompleteCurrentAppearance(current)
            || current.Category != transitioned.Category
                ? snapshot
                : ApplyManagedAppearance(snapshot, transitioned);
    }

    internal static ActorSnapshot MergeManagedAppearance(
        ActorSnapshot snapshot,
        AppearanceData retained)
    {
        if (snapshot.CurrentAppearance is not { } current
            || !IsCompleteCurrentAppearance(current)
            || current.Category != retained.Category)
            return snapshot;

        var merged = current with
        {
            ModelCharaId = retained.ModelCharaId,
            HatVisible = current.Category == ModelCategory.Human
                ? retained.HatVisible
                : current.HatVisible,
        };
        return ApplyManagedAppearance(snapshot, merged);
    }

    internal static IReadOnlyList<(
        LogicalActorKey Actor,
        ActorRepresentationKey Representation,
        AppearanceData Desired,
        ActorSnapshot? Raw)> SelectFirstAppliedDiagnosticCandidates(
        IEnumerable<ActorEntry> publishedEntries,
        IReadOnlyList<ActorSnapshot> rawSnapshots)
    {
        var rawByRepresentation = rawSnapshots.ToDictionary(static snapshot => snapshot.RepresentationKey);
        return publishedEntries
            .SelectMany(entry => entry.Representations
                .Where(static snapshot => snapshot.IsAppearanceManaged
                    && snapshot.CurrentAppearance is { SourceRowId: not 0 })
                .Select(snapshot => (
                    Actor: entry.Key,
                    Representation: snapshot.RepresentationKey,
                    Desired: snapshot.CurrentAppearance!,
                    Raw: rawByRepresentation.GetValueOrDefault(snapshot.RepresentationKey))))
            .ToArray();
    }

    private void WriteFirstAppliedSnapshotDiagnostics(
        IReadOnlyList<(
            LogicalActorKey Actor,
            ActorRepresentationKey Representation,
            AppearanceData Desired,
            ActorSnapshot? Raw)> candidates)
    {
        foreach (var candidate in candidates)
        {
            try
            {
                var rawAppearance = candidate.Raw?.CurrentAppearance;
                var observation = CreateFirstAppliedDiagnosticObservation(
                    candidate.Desired,
                    candidate.Raw,
                    out var effective);
                var properties = new Dictionary<string, object?>(
                    NativeDrawObjectInjector.BuildHumanDiagnosticProperties(candidate.Desired, observation))
                {
                    ["sourceRowId"] = candidate.Desired.SourceRowId,
                    ["rawModelCharaId"] = rawAppearance?.ModelCharaId,
                    ["effectiveModelCharaId"] = effective?.ModelCharaId,
                    ["rawSourceRowId"] = rawAppearance?.SourceRowId,
                };
                diagnostics.Write(new DiagnosticLogEntry
                {
                    EventId = DiagnosticEventIds.MorphSnapshotCaptured,
                    Category = DiagnosticCategory.Snapshot,
                    Message = candidate.Raw is null
                        ? "First post-Apply raw ActorRegistry snapshot was unavailable."
                        : "First post-Apply raw ActorRegistry snapshot captured before managed merge.",
                    ActorKey = DiagnosticActorKeys.Format(diagnostics, candidate.Actor),
                    RepresentationKey = candidate.Representation.ToString(),
                    Phase = "FirstRegistrySnapshotAfterApply",
                    Outcome = properties["payloadComparison"]?.ToString(),
                    Properties = properties,
                });
            }
            catch (Exception exception)
            {
                try
                {
                    diagnostics.Error(
                        DiagnosticEventIds.HandledException,
                        DiagnosticCategory.Snapshot,
                        "First post-Apply ActorRegistry diagnostic observation failed without changing publication.",
                        exception,
                        new Dictionary<string, object?>
                        {
                            ["sourceRowId"] = candidate.Desired.SourceRowId,
                            ["representationKey"] = candidate.Representation.ToString(),
                        });
                }
                catch
                {
                    // Diagnostics must not change ActorRegistry publication.
                }
            }
        }
    }

    internal static NativeDrawObjectInjector.HumanDiagnosticObservation CreateFirstAppliedDiagnosticObservation(
        AppearanceData desired,
        ActorSnapshot? raw,
        out AppearanceData? effective)
    {
        var rawAppearance = raw?.CurrentAppearance;
        effective = raw is null
            ? null
            : MergeManagedAppearance(raw, desired).CurrentAppearance;
        var rawIsHuman = rawAppearance?.Category == ModelCategory.Human;
        return new NativeDrawObjectInjector.HumanDiagnosticObservation(
            rawAppearance?.ModelCharaId,
            rawAppearance is null ? "Unavailable" : "RawActorRegistrySnapshot",
            rawAppearance?.Category,
            rawIsHuman ? rawAppearance!.Customize : null,
            rawIsHuman ? rawAppearance!.Equipment : null,
            rawIsHuman ? rawAppearance!.ModelScale : null,
            rawIsHuman ? rawAppearance!.Mainhand : null,
            rawIsHuman ? rawAppearance!.Offhand : null,
            rawIsHuman ? rawAppearance!.FacewearModelId : null,
            rawIsHuman ? rawAppearance!.HatVisible : null,
            effective?.HatVisible,
            null,
            rawIsHuman ? rawAppearance!.VisorToggled : null,
            "NotApplicable",
            rawIsHuman ? "Available" : "Unavailable",
            raw is null
                ? "RawSnapshotUnavailable"
                : rawIsHuman
                    ? null
                    : "RawSnapshotNonHuman",
            "NotApplicableAtRegistrySnapshot");
    }

    private void TryWriteFirstAppliedSnapshotDiagnostics(
        IReadOnlyList<ActorEntry> publishedEntries,
        IReadOnlyList<ActorSnapshot> rawSnapshots)
    {
        try
        {
            WriteFirstAppliedSnapshotDiagnostics(
                SelectFirstAppliedDiagnosticCandidates(publishedEntries, rawSnapshots));
        }
        catch (Exception exception)
        {
            try
            {
                diagnostics.Error(
                    DiagnosticEventIds.HandledException,
                    DiagnosticCategory.Snapshot,
                    "Post-Apply ActorRegistry diagnostic selection failed without changing publication.",
                    exception);
            }
            catch
            {
                // Diagnostics must not change ActorRegistry publication.
            }
        }
    }

    internal static AppearanceData ApplyCategoryContract(AppearanceData current)
        => current.Category switch
        {
            ModelCategory.Human => current,
            ModelCategory.Demihuman => current with
            {
                Customize = [],
                Mainhand = null,
                Offhand = null,
                VisorToggled = null,
                FacewearModelId = null,
                HatVisible = null,
            },
            ModelCategory.Monster or ModelCategory.Other => current with
            {
                Customize = [],
                Equipment = [],
                Mainhand = null,
                Offhand = null,
                VisorToggled = null,
                FacewearModelId = null,
                HatVisible = null,
            },
            _ => current,
        };

    private static ulong[] CaptureRenderedEquipment(CharacterBase* characterBase)
    {
        const int equipmentSlotCount = 10;
        var equipment = new ulong[equipmentSlotCount];
        for (var index = 0; index < equipmentSlotCount; ++index)
        {
            var model = default(EquipmentModelId);
            characterBase->GetEquipmentSlotModel(&model, (uint)index);
            equipment[index] = model.Value;
        }
        return equipment;
    }

    internal static ulong ToEquipmentModelValue(ArmorAppearance source)
        => new EquipmentModelId
        {
            Id = source.Set,
            Variant = source.Variant,
            Stain0 = source.Stain1,
            Stain1 = source.Stain2,
        }.Value;

    public static bool IsOwnMinion(ObjectKind kind, nint address, nint localPlayerChildObjectAddress)
        => kind == ObjectKind.Companion
        && localPlayerChildObjectAddress != nint.Zero
        && address == localPlayerChildObjectAddress;

    public static IReadOnlyList<ActorEntry> OrderForDisplay(
        IEnumerable<ActorEntry> actors,
        IComparer<string> nameComparer)
        => actors
            .OrderBy(static actor => actor.IsLocalPlayer ? 0 : actor.IsOwnMinion ? 1 : 2)
            .ThenBy(static actor => actor.Kind)
            .ThenBy(static actor => actor.Name, nameComparer)
            .ToArray();

    public static bool IsSupportedObjectKind(ObjectKind kind)
        => kind is ObjectKind.Pc or ObjectKind.EventNpc or ObjectKind.BattleNpc or ObjectKind.Companion;
}
