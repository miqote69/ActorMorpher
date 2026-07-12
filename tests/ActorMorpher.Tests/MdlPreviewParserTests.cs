using ActorMorpher.Preview;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class MdlPreviewParserTests
{
    [Fact]
    public void ParsesVersion6HighLodTriangleWithoutReadingBoneTables()
    {
        var parsed = new MdlPreviewParser().Parse(CreateTriangleMdl());
        var model = new ModelPreviewMeshBuilder().Build(parsed.Meshes);

        Assert.Equal(1, parsed.LodCount);
        var mesh = Assert.Single(model.Meshes);
        Assert.Equal(3, mesh.Vertices.Length);
        Assert.Equal(new ushort[] { 0, 1, 2 }, mesh.Indices);
        Assert.Equal(new Vector3(1, 0, 0), mesh.Vertices[1].Position);
        Assert.True(model.Bounds.IsValid);
    }

    [Fact]
    public void RejectsTruncatedVertexBuffer()
    {
        var data = CreateTriangleMdl();
        Array.Resize(ref data, data.Length - 1);

        Assert.Throws<InvalidDataException>(() => new MdlPreviewParser().Parse(data));
    }

    [Fact]
    public void KeepsPrimaryVectorPositionWhenDeclarationContainsScalarPosition()
    {
        var parsed = new MdlPreviewParser().Parse(CreateTriangleMdl(true));
        var model = new ModelPreviewMeshBuilder().Build(parsed.Meshes);

        Assert.Equal(new Vector3(1, 0, 0), Assert.Single(model.Meshes).Vertices[1].Position);
    }

    private static byte[] CreateTriangleMdl(bool addScalarPosition = false)
    {
        const int declarationOffset = 0x44;
        const int declarationSize = 17 * 8;
        const int stringHeaderSize = 8;
        const int modelHeaderSize = 0x38;
        const int lodsSize = 3 * 0x3C;
        const int meshSize = 0x24;
        const int vertexSize = 3 * 12;
        const int indexSize = 3 * 2;
        const int vertexOffset = declarationOffset + declarationSize + stringHeaderSize + modelHeaderSize + lodsSize + meshSize;
        const int indexOffset = vertexOffset + vertexSize;
        var data = new byte[indexOffset + indexSize];
        var writer = new SpanWriter(data);

        writer.UInt32(0x01000006);
        writer.UInt32(0);
        writer.UInt32(0);
        writer.UInt16(1);
        writer.UInt16(0);
        writer.UInt32s(vertexOffset, 0, 0);
        writer.UInt32s(indexOffset, 0, 0);
        writer.UInt32s(vertexSize, 0, 0);
        writer.UInt32s(indexSize, 0, 0);
        writer.Byte(1);
        writer.Skip(3);

        writer.Byte(0);
        writer.Byte(0);
        writer.Byte(2);
        writer.Byte(0);
        writer.Skip(4);
        var firstUnusedElement = 1;
        if (addScalarPosition)
        {
            writer.Byte(0);
            writer.Byte(0);
            writer.Byte(0);
            writer.Byte(0);
            writer.Skip(4);
            firstUnusedElement = 2;
        }
        for (var element = firstUnusedElement; element < 17; ++element)
        {
            writer.Byte(byte.MaxValue);
            writer.Skip(7);
        }

        writer.Skip(4);
        writer.UInt32(0);
        writer.Skip(4);
        writer.UInt16(1);
        writer.Skip(modelHeaderSize - 6);

        writer.UInt16(0);
        writer.UInt16(1);
        writer.Skip(0x3C - 4);
        writer.Skip(2 * 0x3C);

        writer.UInt16(3);
        writer.Skip(2);
        writer.UInt32(3);
        writer.Skip(8);
        writer.UInt32(0);
        writer.UInt32s(0, 0, 0);
        writer.Byte(12);
        writer.Byte(0);
        writer.Byte(0);
        writer.Byte(1);

        writer.Single(0); writer.Single(0); writer.Single(0);
        writer.Single(1); writer.Single(0); writer.Single(0);
        writer.Single(0); writer.Single(1); writer.Single(0);
        writer.UInt16(0); writer.UInt16(1); writer.UInt16(2);
        return data;
    }

    private sealed class SpanWriter(byte[] data)
    {
        private int position;
        public void Byte(byte value) => data[position++] = value;
        public void UInt16(int value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(position), checked((ushort)value));
            position += 2;
        }
        public void UInt32(int value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(position), checked((uint)value));
            position += 4;
        }
        public void UInt32s(params int[] values)
        {
            foreach (var value in values)
                UInt32(value);
        }
        public void Single(float value) => UInt32(BitConverter.SingleToInt32Bits(value));
        public void Skip(int count) => position += count;
    }
}
