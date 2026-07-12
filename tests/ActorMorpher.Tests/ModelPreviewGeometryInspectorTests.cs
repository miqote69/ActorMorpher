using ActorMorpher.Preview;
using System;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class ModelPreviewGeometryInspectorTests
{
    [Fact]
    public void CombinesGeometryAndBoundsAcrossDemihumanParts()
    {
        var geometry = new Dictionary<string, ModelFileGeometry>
        {
            ["head.mdl"] = new(2, 100, 300, 3, new(new Vector3(-1, 0, -1), new Vector3(1, 2, 1))),
            ["body.mdl"] = new(4, 500, 1500, 3, new(new Vector3(-2, -3, -1), new Vector3(2, 1, 2))),
        };
        var inspector = new ModelPreviewGeometryInspector(path => geometry.GetValueOrDefault(path));

        var report = inspector.Inspect(Assets(("Head", "head.mdl"), ("Body", "body.mdl")));

        Assert.Equal(ModelPreviewGeometryState.Ready, report.State);
        Assert.Equal(6, report.MeshCount);
        Assert.Equal(600, report.VertexCount);
        Assert.Equal(1800, report.IndexCount);
        Assert.Equal(600, report.TriangleCount);
        Assert.Equal(new Vector3(-2, -3, -1), report.Bounds?.Min);
        Assert.Equal(new Vector3(2, 2, 2), report.Bounds?.Max);
        Assert.NotNull(report.AutoFrame);
    }

    [Fact]
    public void MarksGeometryPartialWhenAFileSkippedInvalidMeshes()
    {
        var inspector = new ModelPreviewGeometryInspector(_ => new ModelFileGeometry(
            1,
            3,
            3,
            1,
            new ModelPreviewBounds(Vector3.Zero, Vector3.One),
            2));

        var report = inspector.Inspect(Assets(("Body", "body.mdl")));

        Assert.Equal(ModelPreviewGeometryState.Partial, report.State);
        Assert.Equal(2, report.SkippedMeshCount);
    }

    [Fact]
    public void ContainsOnePartFailureAndKeepsUsableGeometry()
    {
        var inspector = new ModelPreviewGeometryInspector(path => path == "broken.mdl"
            ? throw new InvalidOperationException("broken")
            : new ModelFileGeometry(1, 10, 30, 1, new(new Vector3(-1), new Vector3(1))));

        var report = inspector.Inspect(Assets(("Body", "body.mdl"), ("Head", "broken.mdl")));

        Assert.Equal(ModelPreviewGeometryState.Partial, report.State);
        Assert.Equal(1, report.ReadyPartCount);
        Assert.Equal(1, report.FailedPartCount);
        Assert.Equal("InvalidOperationException", report.Parts[1].Failure);
    }

    [Fact]
    public void RejectsNonFiniteOrCollapsedBounds()
    {
        var inspector = new ModelPreviewGeometryInspector(_ => new ModelFileGeometry(
            1,
            3,
            3,
            1,
            new ModelPreviewBounds(Vector3.Zero, Vector3.Zero)));

        var report = inspector.Inspect(Assets(("Body", "body.mdl")));

        Assert.Equal(ModelPreviewGeometryState.Failed, report.State);
        Assert.Equal(ModelPreviewGeometryPartState.InvalidBounds, report.Parts[0].State);
        Assert.Null(report.Bounds);
    }

    [Fact]
    public void AutoFrameFitsBoundsAndProducesValidClipPlanes()
    {
        var bounds = new ModelPreviewBounds(new Vector3(-2, -4, -1), new Vector3(2, 4, 1));

        var result = ModelPreviewCameraFraming.TryCalculate(bounds, 1.0f, out var frame);

        Assert.True(result);
        Assert.Equal(Vector3.Zero, frame.Target);
        Assert.True(frame.Distance > 4);
        Assert.True(frame.NearPlane > 0);
        Assert.True(frame.FarPlane > frame.NearPlane);
    }

    [Fact]
    public void AutoFrameRejectsInvalidAspectRatio()
        => Assert.False(ModelPreviewCameraFraming.TryCalculate(
            new ModelPreviewBounds(new Vector3(-1), new Vector3(1)),
            0,
            out _));

    private static ModelPreviewAssetReport Assets(params (string Label, string Path)[] models)
        => new(
            100,
            ModelCategory.Demihuman,
            ModelPreviewReadiness.AssetsComplete,
            Array.ConvertAll(models, static model => new ModelPreviewAsset(
                ModelPreviewAssetKind.Model,
                model.Label,
                model.Path,
                true,
                false)));
}
