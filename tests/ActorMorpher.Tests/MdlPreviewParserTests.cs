using ActorMorpher.Preview;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Numerics;
using System.Text;
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

    [Fact]
    public void ParsesVersion6BoneTableWeightsAndIndices()
    {
        var parsed = new MdlPreviewParser().Parse(CreateSkinnedTriangleMdl());
        var mesh = Assert.Single(parsed.Meshes);

        Assert.Equal("j_head", Assert.Single(mesh.Bones!));
        Assert.Equal(Vector4.UnitX, mesh.Vertices[0].BoneWeights);
        Assert.Equal(new ModelPreviewBoneIndices(0, 0, 0, 0), mesh.Vertices[0].BoneIndices);
    }

    [Fact]
    public void FiltersFaceVariantSubmeshesByFacialFeatureBits()
    {
        var data = CreateFaceVariantMdl();

        var unfiltered = Assert.Single(new MdlPreviewParser().Parse(data).Meshes);
        var filtered = Assert.Single(new MdlPreviewParser().Parse(data, 0x10).Meshes);

        Assert.Equal(new ushort[] { 0, 1, 2, 0, 2, 3 }, unfiltered.Indices);
        Assert.Equal(new ushort[] { 0, 2, 3 }, filtered.Indices);
    }

    [Fact]
    public void FiltersEquipmentVariantSubmeshesByImcAttributeMask()
    {
        var data = CreateAttributedMdl(["atr_tv_a", "atr_tv_b"], [1u, 2u]);

        var variantB = Assert.Single(new MdlPreviewParser().Parse(
            data,
            imcAttributeMask: 0x0002).Meshes);

        Assert.Equal(new ushort[] { 0, 2, 3 }, variantB.Indices);
    }

    [Fact]
    public void FiltersTailSpecificSubmeshesByTargetRace()
    {
        var data = CreateAttributedMdl(["atr_tlh", "atr_tls", "atr_kod"], [1u, 6u]);

        var noTail = Assert.Single(new MdlPreviewParser().Parse(data, hasTail: false).Meshes);
        var withTail = Assert.Single(new MdlPreviewParser().Parse(data, hasTail: true).Meshes);

        Assert.Equal(new ushort[] { 0, 1, 2 }, noTail.Indices);
        Assert.Equal(new ushort[] { 0, 2, 3 }, withTail.Indices);
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

    private static byte[] CreateSkinnedTriangleMdl()
    {
        const int declarationOffset = 0x44;
        const int declarationSize = 17 * 8;
        const int stringHeaderSize = 8;
        const int stringSize = 7;
        const int modelHeaderSize = 0x38;
        const int lodsSize = 3 * 0x3C;
        const int meshSize = 0x24;
        const int boneNameOffsetsSize = 4;
        const int boneTableSize = 8;
        const int vertexSize = 3 * 20;
        const int indexSize = 3 * 2;
        const int vertexOffset = declarationOffset + declarationSize + stringHeaderSize + stringSize
            + modelHeaderSize + lodsSize + meshSize + boneNameOffsetsSize + boneTableSize;
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

        WriteElement(writer, 0, 0, 2, 0);
        WriteElement(writer, 0, 12, 8, 1);
        WriteElement(writer, 0, 16, 5, 2);
        for (var element = 3; element < 17; ++element)
        {
            writer.Byte(byte.MaxValue);
            writer.Skip(7);
        }

        writer.UInt16(1);
        writer.Skip(2);
        writer.UInt32(stringSize);
        writer.Bytes(Encoding.UTF8.GetBytes("j_head\0"));

        writer.Skip(4);
        writer.UInt16(1);
        writer.UInt16(0);
        writer.UInt16(0);
        writer.UInt16(0);
        writer.UInt16(1);
        writer.UInt16(1);
        writer.UInt16(0);
        writer.UInt16(0);
        writer.UInt16(0);
        writer.Byte(1);
        writer.Byte(0);
        writer.UInt16(0);
        writer.Byte(0);
        writer.Byte(0);
        writer.Skip(28);

        writer.UInt16(0);
        writer.UInt16(1);
        writer.Skip(0x3C - 4);
        writer.Skip(2 * 0x3C);

        writer.UInt16(3);
        writer.Skip(2);
        writer.UInt32(3);
        writer.UInt16(0);
        writer.UInt16(0);
        writer.UInt16(0);
        writer.UInt16(0);
        writer.UInt32(0);
        writer.UInt32s(0, 0, 0);
        writer.Byte(20);
        writer.Byte(0);
        writer.Byte(0);
        writer.Byte(1);

        writer.UInt32(0);
        writer.UInt16(1);
        writer.UInt16(1);
        writer.UInt16(0);
        writer.UInt16(0);

        WriteSkinnedVertex(writer, 0, 0, 0);
        WriteSkinnedVertex(writer, 1, 0, 0);
        WriteSkinnedVertex(writer, 0, 1, 0);
        writer.UInt16(0);
        writer.UInt16(1);
        writer.UInt16(2);
        return data;
    }

    private static byte[] CreateFaceVariantMdl()
        => CreateAttributedMdl(["atr_fv_a", "atr_fv_e"], [1u, 2u]);

    private static byte[] CreateAttributedMdl(string[] attributes, uint[] submeshMasks)
    {
        Assert.Equal(2, submeshMasks.Length);
        var strings = Encoding.UTF8.GetBytes(string.Join('\0', attributes) + '\0');
        const int declarationOffset = 0x44;
        const int declarationSize = 17 * 8;
        const int stringHeaderSize = 8;
        const int modelHeaderSize = 0x38;
        const int lodsSize = 3 * 0x3C;
        const int meshSize = 0x24;
        var attributeOffsetsSize = attributes.Length * 4;
        const int submeshSize = 2 * 16;
        const int vertexSize = 4 * 12;
        const int indexSize = 6 * 2;
        var vertexOffset = declarationOffset + declarationSize + stringHeaderSize + strings.Length
            + modelHeaderSize + lodsSize + meshSize + attributeOffsetsSize + submeshSize;
        var indexOffset = vertexOffset + vertexSize;
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

        WriteElement(writer, 0, 0, 2, 0);
        for (var element = 1; element < 17; ++element)
        {
            writer.Byte(byte.MaxValue);
            writer.Skip(7);
        }

        writer.UInt16(attributes.Length);
        writer.Skip(2);
        writer.UInt32(strings.Length);
        writer.Bytes(strings);

        writer.Skip(4);
        writer.UInt16(1);
        writer.UInt16(attributes.Length);
        writer.UInt16(2);
        writer.Skip(modelHeaderSize - 10);

        writer.UInt16(0);
        writer.UInt16(1);
        writer.Skip(0x3C - 4);
        writer.Skip(2 * 0x3C);

        writer.UInt16(4);
        writer.Skip(2);
        writer.UInt32(6);
        writer.UInt16(0);
        writer.UInt16(0);
        writer.UInt16(2);
        writer.UInt16(0);
        writer.UInt32(0);
        writer.UInt32s(0, 0, 0);
        writer.Byte(12);
        writer.Byte(0);
        writer.Byte(0);
        writer.Byte(1);

        var stringOffset = 0;
        foreach (var attribute in attributes)
        {
            writer.UInt32(stringOffset);
            stringOffset += Encoding.UTF8.GetByteCount(attribute) + 1;
        }
        writer.UInt32(0);
        writer.UInt32(3);
        writer.UInt32(checked((int)submeshMasks[0]));
        writer.Skip(4);
        writer.UInt32(3);
        writer.UInt32(3);
        writer.UInt32(checked((int)submeshMasks[1]));
        writer.Skip(4);

        writer.Single(0); writer.Single(0); writer.Single(0);
        writer.Single(1); writer.Single(0); writer.Single(0);
        writer.Single(1); writer.Single(1); writer.Single(0);
        writer.Single(0); writer.Single(1); writer.Single(0);
        writer.UInt16(0); writer.UInt16(1); writer.UInt16(2);
        writer.UInt16(0); writer.UInt16(2); writer.UInt16(3);
        return data;
    }

    private static void WriteElement(SpanWriter writer, byte stream, byte offset, byte type, byte usage)
    {
        writer.Byte(stream);
        writer.Byte(offset);
        writer.Byte(type);
        writer.Byte(usage);
        writer.Skip(4);
    }

    private static void WriteSkinnedVertex(SpanWriter writer, float x, float y, float z)
    {
        writer.Single(x);
        writer.Single(y);
        writer.Single(z);
        writer.Bytes([255, 0, 0, 0]);
        writer.Bytes([0, 0, 0, 0]);
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
        public void Bytes(ReadOnlySpan<byte> value)
        {
            value.CopyTo(data.AsSpan(position));
            position += value.Length;
        }
        public void Skip(int count) => position += count;
    }
}
