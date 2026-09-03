using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class LocalPlayerAppearancePersistenceTests
{
    [Fact]
    public void TerritoryChangeRetainsCurrentRepresentationOwnership()
    {
        var persistence = new LocalPlayerAppearancePersistence();
        var originalActor = Key(10);
        var originalRepresentation = Representation(10);
        persistence.UpdateContext(10, true);
        persistence.RecordApplied(originalActor, originalRepresentation, 1);

        Assert.True(persistence.UpdateContext(11, true));
        Assert.Equal(originalActor, persistence.Actor);
        Assert.Equal(originalRepresentation, persistence.CurrentRepresentation);
    }

    [Fact]
    public void TransferClaimsEachNewRepresentationOnceWithTheSameAppliedAppearance()
    {
        var persistence = new LocalPlayerAppearancePersistence();
        var source = Representation(10);
        var appearance = HumanAppearance();
        persistence.UpdateContext(10, true);
        persistence.RecordApplied(Key(10), source, 1, appearance);
        persistence.UpdatePublishedAppearance(source, true, 2);

        var next = Representation(11);
        Assert.True(persistence.TryBeginTransfer(source, next, 2, out var actor));
        Assert.Equal(Key(10), actor);
        Assert.Same(appearance, persistence.GetRetainedAppearance(next));
        Assert.False(persistence.TryBeginTransfer(next, next, 2, out _));
        Assert.False(persistence.TryBeginTransfer(next, Representation(12), 2, out _));
        persistence.UpdatePublishedAppearance(next, true, 3);
        Assert.True(persistence.TryBeginTransfer(next, Representation(12), 3, out _));
    }

    [Fact]
    public void TransitionOwnerRetainsTheSameImmutableAppearanceInstance()
    {
        var persistence = new LocalPlayerAppearancePersistence();
        var appearance = HumanAppearance();
        var representation = Representation(10);

        persistence.RecordApplied(Key(10), representation, 1, appearance);

        Assert.Same(appearance, persistence.CurrentAppearance);
        Assert.Same(appearance, persistence.GetRetainedAppearance(representation));
        Assert.Null(persistence.GetRetainedAppearance(Representation(11)));
    }

    [Fact]
    public void SameRepresentationDoesNotStartATransfer()
    {
        var persistence = new LocalPlayerAppearancePersistence();
        var representation = Representation(10);
        persistence.UpdateContext(10, true);
        persistence.RecordApplied(Key(10), representation, 1);
        persistence.UpdatePublishedAppearance(representation, true, 2);

        Assert.False(persistence.TryBeginTransfer(representation, representation, 2, out _));
        Assert.False(persistence.CanBeginTransfer(representation));
    }

    [Fact]
    public void CandidateCheckDoesNotConsumeANewRepresentation()
    {
        var persistence = new LocalPlayerAppearancePersistence();
        var source = Representation(10);
        persistence.RecordApplied(Key(10), source, 1);
        persistence.UpdatePublishedAppearance(source, true, 2);

        Assert.True(persistence.CanBeginTransfer(Representation(11)));
        Assert.True(persistence.CanBeginTransfer(Representation(11)));
        Assert.True(persistence.TryBeginTransfer(source, Representation(11), 2, out _));
        Assert.False(persistence.CanBeginTransfer(Representation(11)));
    }

    [Fact]
    public void ApplyStartsUnarmedAndOnlyItsCurrentSourcePublicationArms()
    {
        var persistence = new LocalPlayerAppearancePersistence();
        var source = Representation(10);
        persistence.RecordApplied(Key(10), source, 5);

        Assert.False(persistence.IsArmed);
        Assert.False(persistence.TryGetArmedSource(out _, out _));
        persistence.UpdatePublishedAppearance(Representation(11), true, 6);
        Assert.False(persistence.IsArmed);
        persistence.UpdatePublishedAppearance(source, true, 5);
        Assert.False(persistence.IsArmed);

        persistence.UpdatePublishedAppearance(source, true, 6);

        Assert.True(persistence.IsArmed);
        Assert.True(persistence.TryGetArmedSource(out var actor, out var armedSource));
        Assert.Equal(Key(10), actor);
        Assert.Equal(source, armedSource);
    }

    [Fact]
    public void PartialPublicationUnarmsAndLaterCompletePublicationRearms()
    {
        var persistence = new LocalPlayerAppearancePersistence();
        var source = Representation(10);
        persistence.RecordApplied(Key(10), source, 1);
        persistence.UpdatePublishedAppearance(source, true, 2);
        Assert.True(persistence.IsArmed);

        persistence.UpdatePublishedAppearance(source, false, 3);

        Assert.False(persistence.IsArmed);
        Assert.Null(persistence.CurrentAppearance);
        Assert.False(persistence.TryGetArmedSource(out _, out _));
        persistence.UpdatePublishedAppearance(source, true, 4);
        Assert.True(persistence.IsArmed);
    }

    [Fact]
    public void EarlyUnarmedRepresentationIsObservedWithoutReplacingTheApprovedSource()
    {
        var persistence = new LocalPlayerAppearancePersistence();
        var source = Representation(10);
        var early = Representation(11);
        var later = Representation(12);
        persistence.RecordApplied(Key(10), source, 1);

        persistence.ObserveRepresentation(early);

        Assert.Equal(source, persistence.CurrentRepresentation);
        Assert.False(persistence.IsArmed);
        persistence.UpdatePublishedAppearance(source, true, 2);
        Assert.False(persistence.TryBeginTransfer(source, early, 2, out _));
        Assert.True(persistence.TryBeginTransfer(source, later, 2, out _));
        Assert.False(persistence.IsArmed);
    }

    [Fact]
    public void FieldCutsceneAndGPoseKeysTransferOnceAfterEachCurrentPublication()
    {
        var persistence = new LocalPlayerAppearancePersistence();
        var field = new ActorRepresentationKey(0, 100, 200, false, 10);
        var cutscene = new ActorRepresentationKey(1, 101, 201, false, 10);
        var gpose = new ActorRepresentationKey(201, 102, 202, true, 10);
        var returnedField = new ActorRepresentationKey(0, 100, 200, false, 11);
        persistence.RecordApplied(Key(10), field, 1);
        persistence.UpdatePublishedAppearance(field, true, 2);

        Assert.True(persistence.TryBeginTransfer(field, cutscene, 2, out _));
        persistence.UpdatePublishedAppearance(cutscene, true, 3);
        Assert.True(persistence.TryBeginTransfer(cutscene, gpose, 3, out _));
        persistence.UpdatePublishedAppearance(gpose, true, 4);
        Assert.True(persistence.TryBeginTransfer(gpose, returnedField, 4, out _));
        Assert.False(persistence.TryBeginTransfer(returnedField, returnedField, 4, out _));
    }

    [Fact]
    public void TerritoryMakesReusedObjectIdentityANewRepresentation()
    {
        var persistence = new LocalPlayerAppearancePersistence();
        var first = new ActorRepresentationKey(0, 100, 200, false, 10);
        var next = new ActorRepresentationKey(0, 100, 200, false, 11);
        persistence.RecordApplied(Key(10), first, 1);
        persistence.UpdatePublishedAppearance(first, true, 2);

        Assert.True(persistence.TryBeginTransfer(first, next, 2, out _));
        Assert.False(persistence.TryBeginTransfer(next, next, 2, out _));
    }

    [Fact]
    public void RestoreAndLogoutBothClearTransitionState()
    {
        var persistence = new LocalPlayerAppearancePersistence();
        persistence.UpdateContext(10, true);
        persistence.RecordApplied(Key(10), Representation(10), 1);
        persistence.RecordRestored();
        Assert.False(persistence.IsActive);

        persistence.RecordApplied(Key(11), Representation(11), 2);
        persistence.UpdateContext(11, false);
        persistence.UpdateContext(12, true);
        Assert.Null(persistence.Actor);
        Assert.False(persistence.TryBeginTransfer(Representation(11), Representation(12), 3, out _));
    }

    private static LogicalActorKey Key(uint territory)
        => new(0, 100, 200, 300, ObjectKind.Pc, territory);

    private static ActorRepresentationKey Representation(uint value)
        => new((ushort)value, value, value, false);

    private static AppearanceData HumanAppearance()
        => AppearanceData.Create(
            101,
            ModelCategory.Human,
            202,
            AppearanceCompleteness.Complete,
            new byte[26],
            new ulong[10],
            0.84f,
            0,
            0,
            false,
            27,
            true);
}
