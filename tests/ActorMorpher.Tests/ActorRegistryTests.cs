using System;
using System.Collections.Generic;
using System.Linq;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using ActorMorpher.Interop;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class ActorRegistryTests
{
    [Theory]
    [InlineData(ObjectKind.Pc)]
    [InlineData(ObjectKind.EventNpc)]
    [InlineData(ObjectKind.BattleNpc)]
    [InlineData(ObjectKind.Companion)]
    public void CharacterKindsAreSupported(ObjectKind kind)
        => Assert.True(ActorRegistry.IsSupportedObjectKind(kind));

    [Theory]
    [InlineData(ObjectKind.None)]
    [InlineData(ObjectKind.Treasure)]
    public void UnrelatedObjectKindsAreRejected(ObjectKind kind)
        => Assert.False(ActorRegistry.IsSupportedObjectKind(kind));

    [Fact]
    public void DisplayOrderPrioritizesOnlyTheLocalPlayersOwnMinion()
    {
        var localPlayerChildObjectAddress = (nint)0x1000;
        var localPlayer = Entry(1, "Local Player", ObjectKind.Pc, isLocalPlayer: true);
        var ownMinion = Entry(
            2,
            "Own Minion",
            ObjectKind.Companion,
            isOwnMinion: ActorRegistry.IsOwnMinion(
                ObjectKind.Companion,
                localPlayerChildObjectAddress,
                localPlayerChildObjectAddress));
        var otherCompanion = Entry(
            3,
            "Other Companion",
            ObjectKind.Companion,
            isOwnMinion: ActorRegistry.IsOwnMinion(
                ObjectKind.Companion,
                (nint)0x2000,
                localPlayerChildObjectAddress));
        var battleNpcZulu = Entry(4, "Zulu", ObjectKind.BattleNpc);
        var battleNpcAlpha = Entry(5, "Alpha", ObjectKind.BattleNpc);

        var ordered = ActorRegistry.OrderForDisplay(
            [otherCompanion, battleNpcZulu, ownMinion, localPlayer, battleNpcAlpha],
            StringComparer.Ordinal);

        Assert.True(ownMinion.IsOwnMinion);
        Assert.False(otherCompanion.IsOwnMinion);
        Assert.False(ActorRegistry.IsOwnMinion(
            ObjectKind.EventNpc,
            localPlayerChildObjectAddress,
            localPlayerChildObjectAddress));
        Assert.Equal(
            new[] { localPlayer, ownMinion, battleNpcAlpha, battleNpcZulu, otherCompanion },
            ordered);
    }

    [Fact]
    public void PublishedAppearanceLookupUsesTheExactSourceRepresentation()
    {
        var fieldRepresentation = Representation(1, false, 30);
        var gposeRepresentation = Representation(201, true, 30);
        var fieldAppearance = Appearance(100, ModelCategory.Human);
        var gposeAppearance = Appearance(200, ModelCategory.Human);
        var actor = Entry(
            1,
            "Local Player",
            ObjectKind.Pc,
            isLocalPlayer: true,
            representations:
            [
                Snapshot(fieldRepresentation, fieldAppearance),
                Snapshot(gposeRepresentation, gposeAppearance),
            ]);

        Assert.Same(fieldAppearance, ActorRegistry.GetPublishedAppearance([actor], fieldRepresentation));
        Assert.Same(gposeAppearance, ActorRegistry.GetPublishedAppearance([actor], gposeRepresentation));
        Assert.Null(ActorRegistry.GetPublishedAppearance([actor], Representation(99, false, 30)));
    }

    [Fact]
    public void PublishedAppearanceLookupReturnsNullWhenTheSnapshotHasNoCurrentAppearance()
    {
        var representation = Representation(1, false, 30);
        var actor = Entry(
            1,
            "Local Player",
            ObjectKind.Pc,
            isLocalPlayer: true,
            representations: [Snapshot(representation, null)]);

        Assert.Null(ActorRegistry.GetPublishedAppearance([actor], representation));
    }

    [Fact]
    public void RefreshKeepsNpcOwnedFieldsAndIncorporatesLatestRenderedFields()
    {
        var representation = Representation(1, false, 30);
        var latestRendered = Appearance(100, ModelCategory.Human) with
        {
            FacewearModelId = 0,
            HatVisible = false,
            ModelScale = 1.16f,
            Mainhand = 301,
        };
        var selectedNpc = Appearance(200, ModelCategory.Human) with
        {
            FacewearModelId = 27,
            HatVisible = true,
            ModelScale = 0.84f,
        };
        var raw = Snapshot(representation, latestRendered);

        var retained = ActorRegistry.RetainManagedAppearance(
            raw,
            new Dictionary<(LogicalActorKey, ActorRepresentationKey), AppearanceData> { [(raw.LogicalKey, representation)] = selectedNpc },
            _ => null);

        var current = Assert.IsType<AppearanceData>(retained.CurrentAppearance);
        Assert.NotSame(selectedNpc, current);
        Assert.True(retained.IsAppearanceManaged);
        Assert.Equal(200u, retained.ModelCharaId);
        Assert.Equal((ushort)0, current.FacewearModelId);
        Assert.True(current.HatVisible);
        Assert.Equal(1.16f, current.ModelScale);
        Assert.Equal(301UL, current.Mainhand);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartialOrUnsupportedRefreshNeverFallsBackToOldCompleteC(bool isNull)
    {
        var representation = Representation(1, false, 30);
        var oldComplete = Appearance(200, ModelCategory.Human);
        var latest = isNull
            ? null
            : AppearanceData.Create(
                300,
                ModelCategory.Other,
                0,
                AppearanceCompleteness.Unsupported,
                [],
                [],
                null);
        var raw = Snapshot(representation, latest);

        var published = ActorRegistry.RetainManagedAppearance(
            raw,
            new Dictionary<(LogicalActorKey, ActorRepresentationKey), AppearanceData> { [(raw.LogicalKey, representation)] = oldComplete },
            _ => oldComplete);

        Assert.Same(latest, published.CurrentAppearance);
        Assert.False(published.IsAppearanceManaged);
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(published.CurrentAppearance));
    }

    [Fact]
    public void RepresentationTransitionPublishesTheSameAppliedAppearanceInstance()
    {
        var representation = Representation(201, true, 30);
        var selectedNpc = Appearance(200, ModelCategory.Human);

        var retained = ActorRegistry.RetainManagedAppearance(
            Snapshot(representation, Appearance(100, ModelCategory.Human)),
            new Dictionary<(LogicalActorKey, ActorRepresentationKey), AppearanceData>(),
            snapshot => snapshot.RepresentationKey == representation ? selectedNpc : null);

        Assert.Same(selectedNpc, retained.CurrentAppearance);
        Assert.True(retained.IsAppearanceManaged);
    }

    [Fact]
    public void CompleteCurrentAppearanceRequiresEachHumanFieldAndEachCategoryContract()
    {
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(null));
        Assert.True(ActorRegistry.IsCompleteCurrentAppearance(Human()));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Human(completeness: AppearanceCompleteness.ModelOnly)));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Human(customize: Enumerable.Repeat((byte)0, 25))));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Human(equipment: Enumerable.Repeat(0UL, 9))));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Human(modelScale: null)));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Human(mainhand: null)));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Human(offhand: null)));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Human(visorToggled: null)));
        Assert.True(ActorRegistry.IsCompleteCurrentAppearance(Human(facewearModelId: null)));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Human(hatVisible: null)));

        Assert.True(ActorRegistry.IsCompleteCurrentAppearance(Demihuman()));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Demihuman(completeness: AppearanceCompleteness.ModelOnly)));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Demihuman(equipment: Enumerable.Repeat(0UL, 9))));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Demihuman(modelScale: null)));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Demihuman() with { Customize = [1] }));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Demihuman() with { Mainhand = 0 }));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Demihuman() with { Offhand = 0 }));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Demihuman() with { VisorToggled = false }));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Demihuman() with { FacewearModelId = 0 }));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Demihuman() with { HatVisible = false }));

        Assert.True(ActorRegistry.IsCompleteCurrentAppearance(Monster()));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Monster(completeness: AppearanceCompleteness.Complete)));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Monster(modelScale: null)));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Monster() with { Customize = [1] }));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Monster() with { Equipment = [1] }));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Monster() with { Mainhand = 0 }));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Monster() with { Offhand = 0 }));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Monster() with { VisorToggled = false }));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Monster() with { FacewearModelId = 0 }));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(Monster() with { HatVisible = false }));
        Assert.False(ActorRegistry.IsCompleteCurrentAppearance(AppearanceData.Create(
            0,
            ModelCategory.Other,
            0,
            AppearanceCompleteness.Unsupported,
            [],
            [],
            0)));
    }

    [Fact]
    public void UnknownFacewearDoesNotSuppressManagedMerge()
    {
        var representation = Representation(201, true, 30);
        var raw = Snapshot(representation, Human(facewearModelId: null));
        var managed = Human() with { ModelCharaId = 200 };
        var retained = ActorRegistry.RetainManagedAppearance(raw,
            new Dictionary<(LogicalActorKey, ActorRepresentationKey), AppearanceData>
                { [(raw.LogicalKey, representation)] = managed }, _ => null);
        Assert.True(retained.IsAppearanceManaged);
        Assert.Equal(200u, retained.CurrentAppearance!.ModelCharaId);
        Assert.Null(retained.CurrentAppearance.FacewearModelId);
        Assert.Equal(raw.CurrentAppearance!.Equipment, retained.CurrentAppearance.Equipment);
    }

    [Fact]
    public void CompleteCurrentAppearanceTreatsZeroFalseAndAnyScaleValueAsPresent()
    {
        Assert.True(ActorRegistry.IsCompleteCurrentAppearance(Human(
            sourceRowId: 0,
            modelScale: float.NaN,
            equipment: Enumerable.Repeat(0UL, 10),
            mainhand: 0,
            offhand: 0,
            visorToggled: false,
            facewearModelId: 0,
            hatVisible: false)));
        Assert.True(ActorRegistry.IsCompleteCurrentAppearance(Human(sourceRowId: 999)));
        Assert.True(ActorRegistry.IsCompleteCurrentAppearance(Demihuman(
            modelScale: 0,
            equipment: Enumerable.Repeat(0UL, 10))));
        Assert.True(ActorRegistry.IsCompleteCurrentAppearance(Monster(modelCharaId: 0, modelScale: 0)));
    }

    [Theory]
    [InlineData(ModelCategory.Human, true, true)]
    [InlineData(ModelCategory.Demihuman, false, true)]
    [InlineData(ModelCategory.Monster, false, false)]
    [InlineData(ModelCategory.Other, false, false)]
    public void CategoryContractNeverCrossFillsUndefinedFields(
        ModelCategory category,
        bool keepsHumanFields,
        bool keepsEquipment)
    {
        var raw = Appearance(100, category);

        var published = ActorRegistry.ApplyCategoryContract(raw);

        Assert.Equal(keepsHumanFields ? raw.Customize : [], published.Customize);
        Assert.Equal(keepsEquipment ? raw.Equipment : [], published.Equipment);
        Assert.Equal(keepsHumanFields ? raw.Mainhand : null, published.Mainhand);
        Assert.Equal(keepsHumanFields ? raw.Offhand : null, published.Offhand);
        Assert.Equal(keepsHumanFields ? raw.VisorToggled : null, published.VisorToggled);
        Assert.Equal(keepsHumanFields ? raw.FacewearModelId : null, published.FacewearModelId);
        Assert.Equal(keepsHumanFields ? raw.HatVisible : null, published.HatVisible);
        Assert.Equal(raw.ModelCharaId, published.ModelCharaId);
        Assert.Equal(raw.ModelScale, published.ModelScale);
    }

    [Fact]
    public void FirstAppliedDiagnosticCandidateOccursOnceThenSourceRowZeroStopsIt()
    {
        var representation = Representation(1, false, 30);
        var desired = Human(sourceRowId: 1046813, hatVisible: true);
        var rawAppearance = Human(sourceRowId: 0, hatVisible: false);
        var retainedSnapshot = Snapshot(representation, desired) with { IsAppearanceManaged = true };
        var rawSnapshot = Snapshot(representation, rawAppearance);
        var previous = Entry(
            1,
            "Local Player",
            ObjectKind.Pc,
            isLocalPlayer: true,
            representations: [retainedSnapshot]);

        var first = Assert.Single(ActorRegistry.SelectFirstAppliedDiagnosticCandidates([previous], [rawSnapshot]));
        Assert.Equal(previous.Key, first.Actor);
        Assert.Equal(representation, first.Representation);
        Assert.Same(desired, first.Desired);
        Assert.Same(rawSnapshot, first.Raw);

        var published = ActorRegistry.RetainManagedAppearance(
            rawSnapshot,
            new Dictionary<(LogicalActorKey, ActorRepresentationKey), AppearanceData> { [(rawSnapshot.LogicalKey, representation)] = desired },
            _ => null);
        var current = Assert.IsType<AppearanceData>(published.CurrentAppearance);
        Assert.Equal(0u, current.SourceRowId);
        var next = Entry(
            1,
            "Local Player",
            ObjectKind.Pc,
            isLocalPlayer: true,
            representations: [published]);
        Assert.Empty(ActorRegistry.SelectFirstAppliedDiagnosticCandidates([next], [rawSnapshot]));
    }

    [Fact]
    public void FirstAppliedDiagnosticReportsMissingRawWithoutUsingAnotherRepresentation()
    {
        var representation = Representation(1, false, 30);
        var otherRepresentation = Representation(2, false, 30);
        var desired = Human(sourceRowId: 1046813);
        var retainedSnapshot = Snapshot(representation, desired) with { IsAppearanceManaged = true };
        var previous = Entry(
            1,
            "Local Player",
            ObjectKind.Pc,
            isLocalPlayer: true,
            representations: [retainedSnapshot]);
        var otherRaw = Snapshot(otherRepresentation, Human());

        var candidate = Assert.Single(
            ActorRegistry.SelectFirstAppliedDiagnosticCandidates([previous], [otherRaw]));
        Assert.Equal(representation, candidate.Representation);
        Assert.Null(candidate.Raw);

        var observation = ActorRegistry.CreateFirstAppliedDiagnosticObservation(
            candidate.Desired,
            candidate.Raw,
            out var effective);
        var properties = NativeDrawObjectInjector.BuildHumanDiagnosticProperties(candidate.Desired, observation);
        Assert.Null(effective);
        Assert.Equal("RawSnapshotUnavailable", observation.UnavailableReason);
        Assert.Equal("Unavailable", properties["payloadComparison"]);
    }

    [Fact]
    public void RegistryDiagnosticSeparatesRawBackingHatFromManagedEffectiveHat()
    {
        var representation = Representation(1, false, 30);
        var desired = Human(sourceRowId: 1046813, hatVisible: true, modelCharaId: 604);
        var raw = Snapshot(representation, Human(sourceRowId: 0, hatVisible: false, modelCharaId: 999));

        var observation = ActorRegistry.CreateFirstAppliedDiagnosticObservation(
            desired,
            raw,
            out var effective);
        var properties = NativeDrawObjectInjector.BuildHumanDiagnosticProperties(desired, observation);

        Assert.False(observation.HatVisibleBacking);
        Assert.True(observation.HatVisibleEffective);
        Assert.Null(observation.HatVisibleObserved);
        Assert.Equal((uint)999, observation.ModelCharaId);
        Assert.Equal((uint)604, Assert.IsType<AppearanceData>(effective).ModelCharaId);
        Assert.True(Assert.IsType<AppearanceData>(effective).HatVisible);
        Assert.Equal("Mismatch", properties["payloadComparison"]);
        Assert.Contains(
            "ModelCharaId",
            Assert.IsAssignableFrom<IEnumerable<string>>(properties["mismatchedFields"]));
        Assert.Null(properties["hatVisibleObserved"]);
    }

    private static ActorEntry Entry(
        ushort index,
        string name,
        ObjectKind kind,
        bool isLocalPlayer = false,
        bool isOwnMinion = false,
        IReadOnlyList<ActorSnapshot>? representations = null)
        => new(
            new LogicalActorKey(index, index, index, index, kind, 30),
            name,
            kind,
            isLocalPlayer,
            representations ?? Array.Empty<ActorSnapshot>(),
            isOwnMinion);

    private static ActorRepresentationKey Representation(ushort index, bool isGPose, uint territory)
        => new(index, index, index, isGPose, territory);

    private static ActorSnapshot Snapshot(
        ActorRepresentationKey representation,
        AppearanceData? appearance)
        => new(
            new LogicalActorKey(
                representation.ObjectIndex,
                representation.GameObjectId,
                representation.EntityId,
                1,
                ObjectKind.Pc,
                representation.TerritoryId),
            representation,
            "Local Player",
            ObjectKind.Pc,
            1,
            appearance?.ModelCharaId ?? 0,
            1,
            0,
            1,
            1,
            100,
            true,
            false,
            appearance);

    private static AppearanceData Appearance(uint modelId, ModelCategory category)
        => AppearanceData.Create(
            modelId,
            category,
            0,
            AppearanceCompleteness.Complete,
            Enumerable.Range(1, 26).Select(static value => (byte)value),
            Enumerable.Range(1, 10).Select(static value => (ulong)value),
            0.84f,
            101,
            102,
            true,
            103,
            false);

    private static AppearanceData Human(
        AppearanceCompleteness completeness = AppearanceCompleteness.Complete,
        IEnumerable<byte>? customize = null,
        IEnumerable<ulong>? equipment = null,
        float? modelScale = 0.84f,
        ulong? mainhand = 0,
        ulong? offhand = 0,
        bool? visorToggled = false,
        ushort? facewearModelId = 0,
        bool? hatVisible = false,
        uint sourceRowId = 0,
        uint modelCharaId = 0)
        => AppearanceData.Create(
            modelCharaId,
            ModelCategory.Human,
            sourceRowId,
            completeness,
            customize ?? Enumerable.Repeat((byte)0, 26),
            equipment ?? Enumerable.Repeat(0UL, 10),
            modelScale,
            mainhand,
            offhand,
            visorToggled,
            facewearModelId,
            hatVisible);

    private static AppearanceData Demihuman(
        AppearanceCompleteness completeness = AppearanceCompleteness.Complete,
        IEnumerable<ulong>? equipment = null,
        float? modelScale = 0.84f)
        => AppearanceData.Create(
            0,
            ModelCategory.Demihuman,
            0,
            completeness,
            [],
            equipment ?? Enumerable.Repeat(0UL, 10),
            modelScale);

    private static AppearanceData Monster(
        AppearanceCompleteness completeness = AppearanceCompleteness.ModelOnly,
        float? modelScale = 0.84f,
        uint modelCharaId = 0)
        => AppearanceData.Create(
            modelCharaId,
            ModelCategory.Monster,
            0,
            completeness,
            [],
            [],
            modelScale);
}
