using System.Numerics;
using System.IO;
using System.Text;
using ActorMorpher.Preview;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class HumanPbdDeformerTests
{
    [Fact]
    public void AppliesTargetBoneMatrixUsingVertexWeights()
    {
        var deformer = new HumanPbdDeformer(CreatePbd());
        var vertex = new ModelPreviewSourceVertex(
            new Vector4(1, 1, 1, 1),
            null,
            null,
            null,
            Vector4.UnitX,
            new ModelPreviewBoneIndices(0, 0, 0, 0));
        var source = new ModelPreviewSourceMesh(0, "material", [vertex], [0, 0, 0], ["j_head"]);

        var result = deformer.TryDeform(201, 101, [source], out var meshes);

        Assert.True(result);
        Assert.Equal(new Vector4(1, 3, 1, 1), Assert.Single(meshes).Vertices[0].Position);
    }

    [Fact]
    public void LeavesMatchingModelAndTargetCodeUntouched()
    {
        var deformer = new HumanPbdDeformer(CreatePbd());
        var sources = new[]
        {
            new ModelPreviewSourceMesh(0, "material", [], []),
        };

        Assert.True(deformer.TryDeform(101, 101, sources, out var result));
        Assert.Same(sources, result);
    }

    [Fact]
    public void RejectsUnknownDeformationPath()
    {
        var deformer = new HumanPbdDeformer(CreatePbd());

        Assert.False(deformer.TryDeform(9999, 101, [], out _));
    }

    [Fact]
    public void ReportsOnlyReachablePbdPathsAsCompatible()
    {
        var deformer = new HumanPbdDeformer(CreatePbd());

        Assert.True(deformer.CanDeform(201, 101));
        Assert.True(deformer.CanDeform(201, 201));
        Assert.False(deformer.CanDeform(101, 201));
        Assert.False(deformer.CanDeform(9999, 101));
    }

    private static byte[] CreatePbd()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(2);
        writer.Write((ushort)101);
        writer.Write((short)0);
        writer.Write(0);
        writer.Write(1.0f);
        writer.Write((ushort)201);
        writer.Write((short)1);
        writer.Write(44);
        writer.Write(1.0f);

        writer.Write((short)-1);
        writer.Write((short)1);
        writer.Write((short)-1);
        writer.Write((short)0);
        writer.Write((short)0);
        writer.Write((short)-1);
        writer.Write((short)-1);
        writer.Write((short)1);

        writer.Write(1);
        writer.Write((ushort)56);
        writer.Write((ushort)0);
        foreach (var value in new float[]
                 {
                     1, 0, 0, 0,
                     0, 1, 0, 2,
                     0, 0, 1, 0,
                 })
            writer.Write(value);
        writer.Write(Encoding.UTF8.GetBytes("j_head\0"));
        writer.Flush();
        return stream.ToArray();
    }
}
