using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ActorMorpher.Actors;
using ActorMorpher.Appearance;
using Dalamud.Game.ClientState.Objects.Enums;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class CustomizeLiveApplyTests
{
    [Fact]
    public void ContinuousEditsDispatchFirstAndLatestWithoutRepeatingAtIdle()
    {
        var queue = new CustomizeLiveApplyQueue();
        var dispatched = new List<CustomizeLiveRequest>();
        var pending = false;
        bool Dispatch(CustomizeLiveRequest request) { dispatched.Add(request); pending = true; return true; }
        var first = Request(1);
        var middle = Request(2);
        var latest = Request(3);
        queue.Process(_ => pending, Dispatch);
        Assert.Empty(dispatched);
        Assert.True(queue.Submit(first, _ => pending, Dispatch));
        Assert.True(queue.Submit(middle, _ => pending, Dispatch));
        Assert.True(queue.Submit(latest, _ => pending, Dispatch));
        queue.Process(_ => pending, Dispatch);
        Assert.Equal([first], dispatched);
        pending = false;
        queue.Process(_ => pending, Dispatch);
        Assert.Equal([first, latest], dispatched);
        pending = false;
        queue.Process(_ => pending, Dispatch);
        queue.Process(_ => pending, Dispatch);
        Assert.Equal([first, latest], dispatched);
    }

    [Fact]
    public void ANewEditAfterCompletionReplacesTheOlderUnsentEdit()
    {
        var queue = new CustomizeLiveApplyQueue();
        var dispatched = new List<CustomizeLiveRequest>();
        bool Dispatch(CustomizeLiveRequest request) { dispatched.Add(request); return true; }
        queue.Submit(Request(1), _ => true, Dispatch);
        var newest = Request(2);
        queue.Submit(newest, _ => false, Dispatch);
        queue.Process(_ => false, Dispatch);
        Assert.Equal([newest], dispatched);
    }

    [Fact]
    public void FailedDispatchDoesNotRetryWithoutAnotherUserEdit()
    {
        var queue = new CustomizeLiveApplyQueue();
        var attempts = 0;
        bool Dispatch(CustomizeLiveRequest request) { attempts++; return false; }
        queue.Submit(Request(1), _ => true, Dispatch);
        queue.Process(_ => false, Dispatch);
        queue.Process(_ => false, Dispatch);
        Assert.Equal(1, attempts);
        Assert.False(queue.Submit(Request(2), _ => false, Dispatch));
        queue.Process(_ => false, Dispatch);
        Assert.Equal(2, attempts);
    }

    [Fact]
    public void ExplicitRestoreAndDisposeRemoveUnsentEditsWithoutRetargeting()
    {
        var queue = new CustomizeLiveApplyQueue();
        var dispatched = new List<CustomizeLiveRequest>();
        bool Dispatch(CustomizeLiveRequest request) { dispatched.Add(request); return true; }
        var first = Request(1);
        var other = Request(2) with
        {
            Actor = first.Actor with { Lifetime = 2 },
            Representation = first.Representation with { ObjectIndex = 202, IsGPoseRepresentation = true },
        };
        queue.Submit(first, _ => true, Dispatch);
        queue.Submit(other, _ => true, Dispatch);
        queue.Remove(first.Actor);
        queue.Process(_ => false, Dispatch);
        Assert.Equal([other], dispatched);
        Assert.Equal(other.Representation, dispatched[0].Representation);
        queue.Submit(first, _ => true, Dispatch);
        queue.Clear();
        queue.Process(_ => false, Dispatch);
        Assert.Single(dispatched);
    }

    [Fact]
    public void OnlyRgbUpdatesWithoutRemovedOverridesUseDirectApplication()
    {
        var request = Request(1);
        var current = AppearanceData.Create(0, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
            request.Customize, Enumerable.Repeat(0UL, 10)) with { CustomizeColors = [new(8, 1, 0, 0)] };
        var rgb = request with { Colors = [new(8, 0, 1, 0), new(10, 0, 0, 1)] };
        Assert.True(rgb.CanApplyColorsDirectly(current));
        Assert.False((rgb with { Customize = rgb.Customize.SetItem(6, 2) }).CanApplyColorsDirectly(current));
        Assert.False((rgb with { Colors = [new(10, 0, 0, 1)] }).CanApplyColorsDirectly(current));
        Assert.False((rgb with { Colors = [] }).CanApplyColorsDirectly(current));
    }

    [Fact]
    public void BodySlidersAndOtherEditableFieldsUseTheExistingHuman()
    {
        var request = Request(1);
        var current = AppearanceData.Create(0, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
            request.Customize, Enumerable.Repeat(123UL, 10), 1.25f, 456, 789, true, 12, false);
        foreach (var index in Enumerable.Range(0, 26).Except([0, 1, 2, 4, 5]))
        {
            var edit = request with { Customize = request.Customize.SetItem(index, 77) };
            var desired = edit.CreateAppearance(current);
            Assert.False(edit.CanApplyColorsDirectly(current, desired));
            Assert.True(CustomizeLiveRequest.CanApplyCustomizeDirectly(current, desired));
            Assert.Equal(current, desired with { Customize = current.Customize });
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(4)]
    [InlineData(5)]
    public void SkeletonAndFaceChangesKeepTheRequiredRedrawPath(int index)
    {
        var request = Request(1);
        var current = AppearanceData.Create(0, ModelCategory.Human, 0, AppearanceCompleteness.Complete,
            request.Customize, Enumerable.Repeat(0UL, 10));
        var edit = request with { Customize = request.Customize.SetItem(index, 2) };
        var desired = edit.CreateAppearance(current);
        Assert.False(edit.CanApplyColorsDirectly(current, desired));
        Assert.False(CustomizeLiveRequest.CanApplyCustomizeDirectly(current, desired));
    }

    [Fact]
    public void ContinuousDirectEditsDispatchOncePerUserInputAndNeverAgainAtIdle()
    {
        var queue = new CustomizeLiveApplyQueue();
        var dispatched = new List<CustomizeLiveRequest>();
        bool Dispatch(CustomizeLiveRequest request) { dispatched.Add(request); return true; }
        var edits = new[] { Request(77), Request(88), Request(99) };
        foreach (var edit in edits) Assert.True(queue.Submit(edit, _ => false, Dispatch));
        queue.Process(_ => false, Dispatch);
        queue.Process(_ => false, Dispatch);
        Assert.Equal(edits, dispatched);
    }

    private static CustomizeLiveRequest Request(byte value)
        => new(Guid.NewGuid(), new(1, 1, 1, 0, ObjectKind.Pc, 30), new(1, 1, 1, false, 30),
            Enumerable.Repeat(value, 26).ToImmutableArray(), []);
}
