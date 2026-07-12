using ActorMorpher.Preview;
using System;
using System.IO;
using System.Numerics;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class ModelPreviewMeshBuilderTests
{
    [Fact]
    public void BuildsValidatedCpuMeshAndBounds()
    {
        var source = Triangle(
            new Vector3(2, -1, 3),
            new Vector3(4, -1, 3),
            new Vector3(2, 2, 3),
            Vector3.UnitZ);

        var model = new ModelPreviewMeshBuilder().Build([source]);

        var mesh = Assert.Single(model.Meshes);
        Assert.Empty(model.Issues);
        Assert.Equal(new Vector3(2, -1, 3), model.Bounds.Min);
        Assert.Equal(new Vector3(4, 2, 3), model.Bounds.Max);
        Assert.Equal(Vector3.UnitZ, mesh.Vertices[0].Normal);
        Assert.Equal(new Vector2(0.25f, 0.75f), mesh.Vertices[0].UV);
        Assert.Equal(new Vector4(0.1f, 0.2f, 0.3f, 1), mesh.Vertices[0].Color);
        Assert.NotSame(source.Indices, mesh.Indices);
    }

    [Fact]
    public void GeneratesNormalsFromTriangleWhenMissing()
    {
        var source = Triangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, null);

        var model = new ModelPreviewMeshBuilder().Build([source]);

        Assert.All(model.Meshes[0].Vertices, vertex => Assert.Equal(Vector3.UnitZ, vertex.Normal));
    }

    [Fact]
    public void UsesStableFallbackNormalForDegenerateTriangle()
    {
        var source = Triangle(Vector3.Zero, Vector3.Zero, Vector3.Zero, null);

        var model = new ModelPreviewMeshBuilder().Build([source]);

        Assert.All(model.Meshes[0].Vertices, vertex => Assert.Equal(Vector3.UnitY, vertex.Normal));
    }

    [Theory]
    [InlineData(ModelPreviewMeshIssueKind.MissingPosition, true, false)]
    [InlineData(ModelPreviewMeshIssueKind.NonFinitePosition, false, true)]
    public void SkipsInvalidMeshAndKeepsValidMesh(
        ModelPreviewMeshIssueKind expected,
        bool missingPosition,
        bool nonFinitePosition)
    {
        var invalidPosition = missingPosition
            ? (Vector4?)null
            : new Vector4(nonFinitePosition ? float.NaN : 0, 0, 0, 1);
        var invalid = new ModelPreviewSourceMesh(
            9,
            "invalid.mtrl",
            [
                new(invalidPosition, null, null, null),
                new(new Vector4(1, 0, 0, 1), null, null, null),
                new(new Vector4(0, 1, 0, 1), null, null, null),
            ],
            [0, 1, 2]);

        var model = new ModelPreviewMeshBuilder().Build([
            invalid,
            Triangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ),
        ]);

        Assert.Single(model.Meshes);
        var issue = Assert.Single(model.Issues);
        Assert.Equal(9, issue.SourceIndex);
        Assert.Equal(expected, issue.Kind);
    }

    [Fact]
    public void SkipsMeshWithOutOfRangeIndex()
    {
        var invalid = Triangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ) with
        {
            SourceIndex = 4,
            Indices = [0, 1, 3],
        };

        var model = new ModelPreviewMeshBuilder().Build([
            invalid,
            Triangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ),
        ]);

        Assert.Equal(ModelPreviewMeshIssueKind.IndexOutOfRange, Assert.Single(model.Issues).Kind);
    }

    [Fact]
    public void RejectsModelWhenEveryMeshIsInvalid()
    {
        var invalid = Triangle(Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ) with
        {
            Indices = [0, 1],
        };

        Assert.Throws<InvalidDataException>(() => new ModelPreviewMeshBuilder().Build([invalid]));
    }

    private static ModelPreviewSourceMesh Triangle(Vector3 first, Vector3 second, Vector3 third, Vector3? normal)
    {
        var uv = new Vector4(0.25f, 0.75f, 0, 0);
        var color = new Vector4(0.1f, 0.2f, 0.3f, 1);
        return new ModelPreviewSourceMesh(
            0,
            "test.mtrl",
            [
                new(new Vector4(first, 1), normal, uv, color),
                new(new Vector4(second, 1), normal, uv, color),
                new(new Vector4(third, 1), normal, uv, color),
            ],
            [0, 1, 2]);
    }
}
