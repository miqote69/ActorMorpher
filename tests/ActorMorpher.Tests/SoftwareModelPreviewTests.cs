using System;
using System.Linq;
using System.Numerics;
using ActorMorpher.Appearance;
using ActorMorpher.Preview;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class SoftwareModelPreviewTests
{
    [Fact]
    public void SceneBuilderBoundsSampledTriangleCount()
    {
        var indices = Enumerable.Range(0, SoftwareModelPreviewSceneBuilder.MaximumTriangleCount + 1)
            .SelectMany(static _ => new ushort[] { 0, 1, 2 })
            .ToArray();
        var model = Model(indices);

        var scene = new SoftwareModelPreviewSceneBuilder().Build([model]);

        Assert.InRange(scene.Triangles.Count, 1, SoftwareModelPreviewSceneBuilder.MaximumTriangleCount);
        Assert.Equal(SoftwareModelPreviewSceneBuilder.MaximumTriangleCount + 1, scene.SourceTriangleCount);
    }

    [Fact]
    public void ProjectorProducesFiniteDepthSortedCoordinates()
    {
        var scene = new SoftwareModelPreviewSceneBuilder().Build([Model([0, 1, 2, 0, 2, 3])]);
        var view = new SoftwareModelPreviewView(scene, 0.4f, -0.2f, 1.0f);

        var projected = SoftwareModelPreviewProjector.Project(view, new Vector2(10, 20), new Vector2(300, 300));

        Assert.Equal(2, projected.Count);
        Assert.All(projected, static triangle =>
        {
            Assert.True(IsFinite(triangle.First));
            Assert.True(IsFinite(triangle.Second));
            Assert.True(IsFinite(triangle.Third));
            Assert.True(float.IsFinite(triangle.Depth));
        });
        Assert.True(projected.Zip(projected.Skip(1), static (left, right) => left.Depth <= right.Depth).All(static value => value));
    }

    [Fact]
    public void ZoomIncreasesProjectedSize()
    {
        var scene = new SoftwareModelPreviewSceneBuilder().Build([Model([0, 1, 2])]);
        var viewport = new Vector2(300, 300);
        var normal = SoftwareModelPreviewProjector.Project(new(scene, 0, 0, 1), Vector2.Zero, viewport)[0];
        var zoomed = SoftwareModelPreviewProjector.Project(new(scene, 0, 0, 2), Vector2.Zero, viewport)[0];

        Assert.True(Vector2.Distance(zoomed.First, zoomed.Second) > Vector2.Distance(normal.First, normal.Second));
    }

    [Fact]
    public void BackendPublishesReadySceneAndUpdatesCamera()
    {
        var entry = AssetEntry();
        var assets = new ModelPreviewAssetReport(
            entry.ModelId,
            entry.Category,
            ModelPreviewReadiness.AssetsPartial,
            [new(ModelPreviewAssetKind.Model, "Body", "body.mdl", true)]);
        using var backend = new SoftwareModelPreviewBackend(_ => assets, _ => Model([0, 1, 2]));

        backend.Select(entry);
        var initial = Assert.IsType<SoftwareModelPreviewView>(backend.GetView());
        backend.Orbit(10, -5);
        backend.AdjustZoom(2);
        var adjusted = Assert.IsType<SoftwareModelPreviewView>(backend.GetView());

        Assert.Equal(ModelPreviewState.Ready, backend.Snapshot.State);
        Assert.NotEqual(initial.Yaw, adjusted.Yaw);
        Assert.NotEqual(initial.Pitch, adjusted.Pitch);
        Assert.True(adjusted.Zoom > initial.Zoom);
    }

    [Fact]
    public void BackendUsesSoftwareGeometryForHumanModels()
    {
        var entry = AssetEntry() with { Category = ModelCategory.Human };
        var assets = new ModelPreviewAssetReport(
            entry.ModelId,
            entry.Category,
            ModelPreviewReadiness.AssetsComplete,
            [new(ModelPreviewAssetKind.Model, "Body", "body.mdl", true)]);
        using var backend = new SoftwareModelPreviewBackend(_ => assets, _ => Model([0, 1, 2]));

        backend.Select(entry);

        Assert.Equal(ModelPreviewState.Ready, backend.Snapshot.State);
        Assert.NotNull(backend.GetView());
    }

    private static ModelPreviewCpuModel Model(ushort[] indices)
    {
        var vertices = new[]
        {
            Vertex(-1, -1, 0),
            Vertex(1, -1, 0),
            Vertex(1, 1, 0),
            Vertex(-1, 1, 0),
        };
        var bounds = new ModelPreviewBounds(new Vector3(-1, -1, 0), new Vector3(1, 1, 0.1f));
        var mesh = new ModelPreviewCpuMesh(0, "material", vertices, indices, bounds);
        return new ModelPreviewCpuModel([mesh], Array.Empty<ModelPreviewMeshIssue>(), bounds);
    }

    private static ModelPreviewVertex Vertex(float x, float y, float z)
        => new(new Vector3(x, y, z), Vector3.UnitZ, Vector2.Zero, Vector4.One);

    private static ModelSearchEntry AssetEntry()
        => new(
            100,
            ModelCategory.Monster,
            ModelSource.ModelChara,
            100,
            "Monster",
            3,
            1,
            1,
            1,
            0,
            0,
            0,
            null,
            AppearanceCompleteness.ModelOnly,
            null);

    private static bool IsFinite(Vector2 value)
        => float.IsFinite(value.X) && float.IsFinite(value.Y);
}
