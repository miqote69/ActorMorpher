using System;
using System.IO;
using System.Linq;
using System.Numerics;
using ActorMorpher.Appearance;
using ActorMorpher.Preview;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class SoftwareModelPreviewTests
{
    [Fact]
    public void StaticProjectionReusesTrianglesWithoutEnumeratingTheSceneAgain()
    {
        var original = new SoftwareModelPreviewSceneBuilder().Build([Model([0, 1, 2])]);
        var counted = new CountingTriangles(original.Triangles);
        var scene = original with { Triangles = counted };
        var view = new SoftwareModelPreviewView(scene, 0.55f, -0.12f, 1);
        var projector = new SoftwareModelPreviewProjector();
        var position = new Vector2(10, 20);
        var size = new Vector2(300, 280);
        var first = projector.GetProjection(view, position, size);

        for (var frame = 0; frame < 120; ++frame)
            Assert.Same(first, projector.GetProjection(view, position, size));
        Assert.Equal(1, counted.Enumerations);
        Assert.Equal(SoftwareModelPreviewProjector.Project(view, position, size).ToArray(), first.ToArray());

        projector.Clear();
        Assert.NotSame(first, projector.GetProjection(view, position, size));
        Assert.Equal(3, counted.Enumerations); // Initial, uncached oracle, and after Clear.
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void ProjectionCacheInvalidatesEveryViewInput(int changedInput)
    {
        var scene = new SoftwareModelPreviewSceneBuilder().Build([Model([0, 1, 2])]);
        var view = new SoftwareModelPreviewView(scene, 0.55f, -0.12f, 1);
        var projector = new SoftwareModelPreviewProjector();
        var position = Vector2.Zero;
        var size = new Vector2(300, 300);
        var first = projector.GetProjection(view, position, size);
        switch (changedInput)
        {
            case 0: view = view with { Yaw = 0.8f }; break;
            case 1: view = view with { Pitch = 0.4f }; break;
            case 2: view = view with { Zoom = 2 }; break;
            case 3: position = new Vector2(30, 40); break;
            case 4: size = new Vector2(420, 360); break;
            case 5: view = view with { Scene = scene with { } }; break;
        }
        var changed = projector.GetProjection(view, position, size);
        Assert.NotSame(first, changed);
        Assert.Equal(SoftwareModelPreviewProjector.Project(view, position, size).ToArray(), changed.ToArray());
        Assert.Same(changed, projector.GetProjection(view, position, size));
    }

    private sealed class CountingTriangles(
        System.Collections.Generic.IReadOnlyList<SoftwareModelPreviewTriangle> triangles)
        : System.Collections.Generic.IReadOnlyList<SoftwareModelPreviewTriangle>
    {
        public int Enumerations { get; private set; }
        public int Count => triangles.Count;
        public SoftwareModelPreviewTriangle this[int index] => triangles[index];
        public System.Collections.Generic.IEnumerator<SoftwareModelPreviewTriangle> GetEnumerator()
        {
            ++Enumerations;
            return triangles.GetEnumerator();
        }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void SceneBuilderPreservesEveryTriangle()
    {
        const int triangleCount = 10_001;
        var indices = Enumerable.Range(0, triangleCount)
            .SelectMany(static _ => new ushort[] { 0, 1, 2 })
            .ToArray();
        var model = Model(indices);

        var scene = new SoftwareModelPreviewSceneBuilder().Build([model]);

        Assert.Equal(triangleCount, scene.Triangles.Count);
        Assert.Equal(triangleCount, scene.SourceTriangleCount);
    }

    [Fact]
    public void SceneBuilderRejectsInsteadOfCreatingHolesBeyondLimit()
    {
        var indices = Enumerable.Range(0, SoftwareModelPreviewSceneBuilder.MaximumTriangleCount + 1)
            .SelectMany(static _ => new ushort[] { 0, 1, 2 })
            .ToArray();

        Assert.Throws<InvalidDataException>(() => new SoftwareModelPreviewSceneBuilder().Build([Model(indices)]));
    }

    [Fact]
    public void SceneBuilderKeepsSmallFaceAndHairTriangles()
    {
        var vertices = new[]
        {
            Vertex(0, 0, 0),
            Vertex(0.0001f, 0, 0),
            Vertex(0, 0.0001f, 0),
        };
        var bounds = new ModelPreviewBounds(new Vector3(-1), new Vector3(1));
        var mesh = new ModelPreviewCpuMesh(0, "face", vertices, [0, 1, 2], bounds);
        var model = new ModelPreviewCpuModel([mesh], Array.Empty<ModelPreviewMeshIssue>(), bounds);

        var scene = new SoftwareModelPreviewSceneBuilder().Build([model]);

        Assert.Single(scene.Triangles);
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
    public void ProjectorCullsBackfacesForOneSidedMaterials()
    {
        var scene = new SoftwareModelPreviewSceneBuilder(_ => false).Build([Model([0, 2, 1])]);

        var projected = SoftwareModelPreviewProjector.Project(
            new(scene, 0, 0, 1),
            Vector2.Zero,
            new Vector2(300, 300));

        Assert.Empty(projected);
    }

    [Fact]
    public void ProjectorDrawsTwoSidedBackfacesBeforeFrontFaces()
    {
        var scene = new SoftwareModelPreviewSceneBuilder(_ => true).Build([Model([0, 1, 2, 0, 2, 1])]);

        var projected = SoftwareModelPreviewProjector.Project(
            new(scene, 0, 0, 1),
            Vector2.Zero,
            new Vector2(300, 300));

        Assert.Equal(2, projected.Count);
        Assert.True(projected[0].IsBackFacing);
        Assert.False(projected[1].IsBackFacing);
        Assert.Equal(projected[0].FirstTextureTint, projected[1].FirstTextureTint);
    }

    [Fact]
    public void ProjectorUsesVertexNormalsForSmoothLighting()
    {
        var vertices = new[]
        {
            Vertex(-1, -1, 0),
            Vertex(1, -1, 0),
            Vertex(1, 1, 0),
            Vertex(-1, 1, 1),
        };
        var bounds = new ModelPreviewBounds(new Vector3(-1, -1, 0), new Vector3(1, 1, 1));
        var mesh = new ModelPreviewCpuMesh(0, "cloth", vertices, [0, 1, 2, 0, 2, 3], bounds);
        var scene = new SoftwareModelPreviewSceneBuilder(_ => false).Build(
            [new ModelPreviewCpuModel([mesh], Array.Empty<ModelPreviewMeshIssue>(), bounds)]);

        var projected = SoftwareModelPreviewProjector.Project(
            new(scene, 0, 0, 1),
            Vector2.Zero,
            new Vector2(300, 300));

        var tints = projected.SelectMany(static triangle => new[]
        {
            triangle.FirstTextureTint,
            triangle.SecondTextureTint,
            triangle.ThirdTextureTint,
        });
        Assert.Single(tints.Distinct());
    }

    [Fact]
    public void ProjectorOffsetsBodySkinBehindNearbyClothing()
    {
        var skinVertices = new[]
        {
            Vertex(-1, -1, 0.01f),
            Vertex(1, -1, 0.01f),
            Vertex(1, 1, 0.01f),
        };
        var clothVertices = new[]
        {
            Vertex(-1, -1, 0),
            Vertex(1, -1, 0),
            Vertex(1, 1, 0),
        };
        var bounds = new ModelPreviewBounds(new Vector3(-1, -1, 0), new Vector3(1, 1, 0.01f));
        var skin = new ModelPreviewCpuMesh(0, "body-skin", skinVertices, [0, 1, 2], bounds);
        var cloth = new ModelPreviewCpuMesh(1, "cloth", clothVertices, [0, 1, 2], bounds);
        var scene = new SoftwareModelPreviewSceneBuilder(
            _ => false,
            path => path == "body-skin").Build(
            [new ModelPreviewCpuModel([skin, cloth], Array.Empty<ModelPreviewMeshIssue>(), bounds)]);

        var projected = SoftwareModelPreviewProjector.Project(
            new(scene, 0, 0, 1),
            Vector2.Zero,
            new Vector2(300, 300));

        Assert.Equal(2, projected.Count);
        Assert.True(projected[0].IsBodySkin);
        Assert.False(projected[1].IsBodySkin);
    }

    [Fact]
    public void ProjectorOrdersSkinThenLowerBodyThenOuterClothing()
    {
        static ModelPreviewCpuMesh Mesh(int index, string material, float z)
        {
            var vertices = new[]
            {
                Vertex(-1, -1, z),
                Vertex(1, -1, z),
                Vertex(1, 1, z),
            };
            var bounds = new ModelPreviewBounds(new Vector3(-1, -1, 0), new Vector3(1, 1, 0.02f));
            return new ModelPreviewCpuMesh(index, material, vertices, [0, 1, 2], bounds);
        }

        var bounds = new ModelPreviewBounds(new Vector3(-1, -1, 0), new Vector3(1, 1, 0.02f));
        var scene = new SoftwareModelPreviewSceneBuilder(
            _ => false,
            path => path == "body-skin",
            path => path == "lower-body").Build(
            [new ModelPreviewCpuModel(
                [Mesh(0, "body-skin", 0.02f), Mesh(1, "lower-body", 0.01f), Mesh(2, "outer", 0)],
                Array.Empty<ModelPreviewMeshIssue>(),
                bounds)]);

        var projected = SoftwareModelPreviewProjector.Project(
            new(scene, 0, 0, 1),
            Vector2.Zero,
            new Vector2(300, 300));

        Assert.Equal(3, projected.Count);
        Assert.True(projected[0].IsBodySkin);
        Assert.True(projected[1].IsLowerBodyEquipment);
        Assert.Equal("outer", projected[2].MaterialPath);
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
        using var backend = new SoftwareModelPreviewBackend(_ => assets, (_, _, _, _) => Model([0, 1, 2]));

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
        using var backend = new SoftwareModelPreviewBackend(_ => assets, (_, _, _, _) => Model([0, 1, 2]));

        backend.Select(entry);

        Assert.Equal(ModelPreviewState.Ready, backend.Snapshot.State);
        Assert.NotNull(backend.GetView());
    }

    [Fact]
    public void BackendDoesNotPublishFloatingOptionalPartsWhenRequiredBodyFails()
    {
        var entry = AssetEntry() with { Category = ModelCategory.Human };
        var assets = new ModelPreviewAssetReport(
            entry.ModelId,
            entry.Category,
            ModelPreviewReadiness.AssetsComplete,
            [
                new(ModelPreviewAssetKind.Model, "Body", "body.mdl", true),
                new(ModelPreviewAssetKind.Model, "Hair", "hair.mdl", true, false),
            ]);
        using var backend = new SoftwareModelPreviewBackend(
            _ => assets,
            (path, _, _, _) => path == "body.mdl" ? throw new InvalidDataException("PBD failed") : Model([0, 1, 2]));

        backend.Select(entry);

        Assert.Equal(ModelPreviewState.Failed, backend.Snapshot.State);
        Assert.Null(backend.GetView());
    }

    [Fact]
    public void BackendPassesSelectedHumanFacialFeaturesToFaceParser()
    {
        var customize = new byte[26];
        customize[12] = 0x10;
        var entry = AssetEntry() with
        {
            Category = ModelCategory.Human,
            HumanAppearance = new HumanAppearance(customize, new ulong[10], 0, 0, false),
        };
        var assets = new ModelPreviewAssetReport(
            entry.ModelId,
            entry.Category,
            ModelPreviewReadiness.AssetsComplete,
            [new(ModelPreviewAssetKind.Model, "Face", "face.mdl", true)],
            1201);
        byte passedFeatures = 0;
        using var backend = new SoftwareModelPreviewBackend(
            _ => assets,
            (_, _, _, features) =>
            {
                passedFeatures = features;
                return Model([0, 1, 2]);
            });

        backend.Select(entry);

        Assert.Equal((byte)0x10, passedFeatures);
        Assert.Equal(ModelPreviewState.Ready, backend.Snapshot.State);
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
