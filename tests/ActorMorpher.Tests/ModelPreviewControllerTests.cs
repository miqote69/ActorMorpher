using ActorMorpher.Appearance;
using ActorMorpher.Diagnostics;
using ActorMorpher.Interop;
using ActorMorpher.Preview;
using System;
using System.Collections.Generic;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class ModelPreviewControllerTests
{
    [Fact]
    public void DebouncesSelectionBeforeDispatchingToBackend()
    {
        var clock = new TestClock();
        var backend = new FakeBackend();
        using var controller = CreateController(clock, backend);

        controller.SetActive(true);
        controller.Select(Entry(100, 10));

        Assert.Equal(ModelPreviewState.Loading, controller.Snapshot.State);
        Assert.Empty(backend.SelectedModels);

        clock.Advance(ModelPreviewController.SelectionDebounce - TimeSpan.FromMilliseconds(1));
        controller.Process();
        Assert.Empty(backend.SelectedModels);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        controller.Process();
        Assert.Single(backend.SelectedModels);
        Assert.Equal((uint)100, backend.SelectedModels[0].RowId);
        Assert.Equal(ModelPreviewState.Ready, controller.Snapshot.State);
    }

    [Fact]
    public void ReplacingSelectionDuringDebounceDispatchesOnlyNewestModel()
    {
        var clock = new TestClock();
        var backend = new FakeBackend();
        using var controller = CreateController(clock, backend);

        controller.SetActive(true);
        controller.Select(Entry(100, 10));
        clock.Advance(TimeSpan.FromMilliseconds(100));
        controller.Select(Entry(200, 20));
        clock.Advance(ModelPreviewController.SelectionDebounce);
        controller.Process();

        var selected = Assert.Single(backend.SelectedModels);
        Assert.Equal((uint)200, selected.RowId);
        Assert.Equal((uint)200, controller.Snapshot.ModelId);
    }

    [Fact]
    public void SameModelIdFromDifferentSourceIsANewSelection()
    {
        var clock = new TestClock();
        var backend = new FakeBackend();
        using var controller = CreateController(clock, backend);
        controller.SetActive(true);

        controller.Select(Entry(100, 10));
        clock.Advance(ModelPreviewController.SelectionDebounce);
        controller.Process();
        controller.Select(Entry(100, 11));
        clock.Advance(ModelPreviewController.SelectionDebounce);
        controller.Process();

        Assert.Equal(2, backend.SelectedModels.Count);
        Assert.Equal((uint)10, backend.SelectedModels[0].SourceId);
        Assert.Equal((uint)11, backend.SelectedModels[1].SourceId);
    }

    [Fact]
    public void InactivePreviewReleasesBackendAndResumesLatestSelection()
    {
        var clock = new TestClock();
        var backend = new FakeBackend();
        using var controller = CreateController(clock, backend);
        controller.SetActive(true);
        controller.Select(Entry(100, 10));
        clock.Advance(ModelPreviewController.SelectionDebounce);
        controller.Process();

        controller.SetActive(false);
        Assert.Equal(ModelPreviewState.Suspended, controller.Snapshot.State);
        Assert.True(backend.ReleaseCount > 0);

        controller.SetActive(true);
        clock.Advance(ModelPreviewController.SelectionDebounce);
        controller.Process();

        Assert.Equal(2, backend.SelectedModels.Count);
        Assert.Equal(ModelPreviewState.Ready, controller.Snapshot.State);
    }

    [Fact]
    public void TerritoryChangeReleasesAndRedispatchesAfterDebounce()
    {
        var clock = new TestClock();
        var backend = new FakeBackend();
        var context = new FakeContext();
        using var controller = CreateController(clock, backend, context);
        controller.SetActive(true);
        controller.Select(Entry(100, 10));
        clock.Advance(ModelPreviewController.SelectionDebounce);
        controller.Process();

        context.TerritoryId = 2;
        controller.Process();
        Assert.Equal(ModelPreviewState.Loading, controller.Snapshot.State);

        clock.Advance(ModelPreviewController.SelectionDebounce);
        controller.SetActive(true);
        controller.Process();
        Assert.Equal(2, backend.SelectedModels.Count);
    }

    [Fact]
    public void MissingVisibilityHeartbeatSuspendsAndReleasesPreview()
    {
        var clock = new TestClock();
        var backend = new FakeBackend();
        using var controller = CreateController(clock, backend);
        controller.SetActive(true);
        controller.Select(Entry(100, 10));
        clock.Advance(ModelPreviewController.SelectionDebounce);
        controller.Process();

        clock.Advance(ModelPreviewController.VisibilityTimeout + TimeSpan.FromMilliseconds(1));
        controller.Process();

        Assert.Equal(ModelPreviewState.Suspended, controller.Snapshot.State);
        Assert.True(backend.ReleaseCount > 0);
    }

    [Fact]
    public void LogoutReleasesPreviewAndLoginReschedulesLatestSelection()
    {
        var clock = new TestClock();
        var backend = new FakeBackend();
        var context = new FakeContext();
        using var controller = CreateController(clock, backend, context);
        controller.SetActive(true);
        controller.Select(Entry(100, 10));
        clock.Advance(ModelPreviewController.SelectionDebounce);
        controller.Process();

        context.IsLoggedIn = false;
        controller.Process();
        Assert.Equal(ModelPreviewState.Suspended, controller.Snapshot.State);

        context.IsLoggedIn = true;
        controller.SetActive(true);
        controller.Process();
        clock.Advance(ModelPreviewController.SelectionDebounce);
        controller.SetActive(true);
        controller.Process();

        Assert.Equal(2, backend.SelectedModels.Count);
        Assert.Equal(ModelPreviewState.Ready, controller.Snapshot.State);
    }

    [Fact]
    public void DisposeReleasesAndDisposesBackend()
    {
        var clock = new TestClock();
        var backend = new FakeBackend();
        var controller = CreateController(clock, backend);
        controller.SetActive(true);
        controller.Select(Entry(100, 10));

        controller.Dispose();

        Assert.True(backend.ReleaseCount > 0);
        Assert.True(backend.IsDisposed);
    }

    private static ModelPreviewController CreateController(
        TestClock clock,
        FakeBackend backend,
        FakeContext? context = null)
        => new(backend, context ?? new FakeContext(), NullDiagnosticLog.Instance, clock.GetNow);

    private static ModelSearchEntry Entry(uint rowId, uint sourceId)
        => new(rowId, ModelCategory.Human, ModelSource.BattleNpc, sourceId, $"Model {sourceId}",
            1, 1, 1, 1, 1, 0, 1, null, AppearanceCompleteness.Unsupported, null);

    private sealed class TestClock
    {
        private DateTimeOffset now = DateTimeOffset.UnixEpoch;
        public DateTimeOffset GetNow() => now;
        public void Advance(TimeSpan amount) => now += amount;
    }

    private sealed class FakeContext : IClientContext
    {
        public uint TerritoryId { get; set; } = 1;
        public bool IsLoggedIn { get; set; } = true;
        public bool IsGPosing => false;
    }

    private sealed class FakeBackend : IModelPreviewBackend
    {
        private long generation;
        public List<ModelSearchEntry> SelectedModels { get; } = [];
        public int ReleaseCount { get; private set; }
        public bool IsDisposed { get; private set; }
        public ModelPreviewSnapshot Snapshot { get; private set; }
            = new(0, ModelPreviewState.Idle, null, string.Empty);

        public void Select(ModelSearchEntry? model)
        {
            generation++;
            if (model is null)
            {
                ReleaseCount++;
                Snapshot = new(generation, ModelPreviewState.Idle, null, string.Empty);
                return;
            }

            SelectedModels.Add(model);
            Snapshot = new(generation, ModelPreviewState.Ready, model.ModelId, "Ready");
        }

        public void ResetCamera()
        {
        }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }
}
