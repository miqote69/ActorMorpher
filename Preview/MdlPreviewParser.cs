using System.Buffers.Binary;
using System.Numerics;

namespace ActorMorpher.Preview;

public sealed class MdlPreviewParser
{
    private const uint Version5 = 0x01000005;
    private const uint Version6 = 0x01000006;
    private const int FileHeaderSize = 0x44;
    private const int VertexDeclarationElementCount = 17;
    private const int VertexDeclarationElementSize = 8;
    private const int ModelHeaderSize = 0x38;
    private const int ElementIdSize = 0x20;
    private const int LodSize = 0x3C;
    private const int ExtraLodSize = 0x28;
    private const int MeshSize = 0x24;

    public MdlPreviewParseResult Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < FileHeaderSize)
            throw new InvalidDataException("MDL file header is truncated.");

        var reader = new CheckedReader(data);
        var version = reader.ReadUInt32();
        if (version is not (Version5 or Version6))
            throw new InvalidDataException($"Unsupported MDL version 0x{version:X8}.");

        reader.Skip(8); // Stack and runtime sizes.
        var declarationCount = reader.ReadUInt16();
        reader.Skip(2); // Material count.
        var vertexOffsets = reader.ReadUInt32Array(3);
        var indexOffsets = reader.ReadUInt32Array(3);
        var vertexBufferSizes = reader.ReadUInt32Array(3);
        var indexBufferSizes = reader.ReadUInt32Array(3);
        var lodCount = reader.ReadByte();
        reader.Skip(3);

        if (declarationCount == 0 || declarationCount > ModelPreviewMeshBuilder.MaximumMeshCount)
            throw new InvalidDataException("MDL vertex declaration count is invalid.");
        if (lodCount is < 1 or > 3)
            throw new InvalidDataException("MDL LOD count is invalid.");

        var declarations = new VertexElement[declarationCount][];
        for (var declarationIndex = 0; declarationIndex < declarations.Length; ++declarationIndex)
        {
            var elements = new List<VertexElement>(VertexDeclarationElementCount);
            for (var elementIndex = 0; elementIndex < VertexDeclarationElementCount; ++elementIndex)
            {
                var stream = reader.ReadByte();
                var offset = reader.ReadByte();
                var type = reader.ReadByte();
                var usage = reader.ReadByte();
                reader.Skip(4);
                if (stream != byte.MaxValue)
                    elements.Add(new VertexElement(stream, offset, type, usage));
            }
            declarations[declarationIndex] = elements.ToArray();
        }

        reader.Skip(4); // String count and padding.
        reader.Skip(reader.ReadBoundedUInt32("MDL string table is too large."));
        var modelHeader = reader.ReadSpan(ModelHeaderSize);
        var meshCount = BinaryPrimitives.ReadUInt16LittleEndian(modelHeader[4..]);
        var elementIdCount = BinaryPrimitives.ReadUInt16LittleEndian(modelHeader[24..]);
        var hasExtraLod = (modelHeader[27] & 0x10) != 0;
        if (meshCount == 0 || meshCount > ModelPreviewMeshBuilder.MaximumMeshCount || meshCount > declarationCount)
            throw new InvalidDataException("MDL mesh count is invalid.");

        reader.Skip(checked(elementIdCount * ElementIdSize));
        var highLodMeshIndex = reader.ReadUInt16();
        var highLodMeshCount = reader.ReadUInt16();
        reader.Skip(LodSize - 4);
        reader.Skip(2 * LodSize);
        if (hasExtraLod)
            reader.Skip(3 * ExtraLodSize);
        if (highLodMeshCount == 0 || highLodMeshIndex > meshCount - highLodMeshCount)
            throw new InvalidDataException("MDL High LOD mesh range is invalid.");

        var meshes = new Mesh[meshCount];
        for (var meshIndex = 0; meshIndex < meshes.Length; ++meshIndex)
        {
            var start = reader.Position;
            var vertexCount = reader.ReadUInt16();
            reader.Skip(2);
            var indexCount = reader.ReadUInt32();
            reader.Skip(8);
            var startIndex = reader.ReadUInt32();
            var streamOffsets = reader.ReadUInt32Array(3);
            var strides = reader.ReadBytes(3);
            var streamCount = (byte)(reader.ReadByte() & 0x03);
            if (reader.Position - start != MeshSize)
                throw new InvalidDataException("MDL mesh layout is invalid.");
            meshes[meshIndex] = new Mesh(vertexCount, indexCount, startIndex, streamOffsets, strides, streamCount);
        }

        ValidateBuffer(data, vertexOffsets[0], vertexBufferSizes[0], "vertex");
        ValidateBuffer(data, indexOffsets[0], indexBufferSizes[0], "index");

        long totalVertices = 0;
        long totalIndices = 0;
        var sources = new List<ModelPreviewSourceMesh>(highLodMeshCount);
        var meshEnd = highLodMeshIndex + highLodMeshCount;
        for (var meshIndex = highLodMeshIndex; meshIndex < meshEnd; ++meshIndex)
        {
            var mesh = meshes[meshIndex];
            totalVertices += mesh.VertexCount;
            totalIndices += mesh.IndexCount;
            if (totalVertices > ModelPreviewMeshBuilder.MaximumVerticesPerModel
                || totalIndices > ModelPreviewMeshBuilder.MaximumIndicesPerModel)
                throw new InvalidDataException("MDL High LOD geometry exceeds the preview limits.");

            var position = declarations[meshIndex].FirstOrDefault(static element => element.Usage == 0);
            if (position == default || position.Stream >= 3 || position.Stream >= mesh.StreamCount)
                continue;
            var stride = mesh.Strides[position.Stream];
            var valueSize = VertexValueSize(position.Type);
            if (stride == 0 || position.Offset > stride - valueSize)
                continue;

            var vertices = ReadVertices(data, vertexOffsets[0], vertexBufferSizes[0], mesh, position, stride);
            var indices = ReadIndices(data, indexOffsets[0], indexBufferSizes[0], mesh);
            sources.Add(new ModelPreviewSourceMesh(meshIndex, $"Mesh {meshIndex}", vertices, indices));
        }

        if (sources.Count == 0)
            throw new InvalidDataException("MDL contains no supported High LOD position streams.");
        return new MdlPreviewParseResult(sources, lodCount);
    }

    private static ModelPreviewSourceVertex[] ReadVertices(
        byte[] data,
        uint bufferOffset,
        uint bufferSize,
        Mesh mesh,
        VertexElement position,
        byte stride)
    {
        var vertices = new ModelPreviewSourceVertex[mesh.VertexCount];
        var streamStart = checked((long)bufferOffset + mesh.StreamOffsets[position.Stream]);
        var streamEnd = checked((long)bufferOffset + bufferSize);
        var requiredEnd = checked(streamStart + (long)Math.Max(0, mesh.VertexCount - 1) * stride + position.Offset + VertexValueSize(position.Type));
        if (streamStart < bufferOffset || requiredEnd > streamEnd || requiredEnd > data.Length)
            throw new InvalidDataException("MDL vertex stream is outside its declared buffer.");

        for (var vertexIndex = 0; vertexIndex < vertices.Length; ++vertexIndex)
        {
            var offset = checked((int)(streamStart + (long)vertexIndex * stride + position.Offset));
            vertices[vertexIndex] = new ModelPreviewSourceVertex(
                ReadVector(data.AsSpan(offset), position.Type),
                null,
                null,
                null);
        }
        return vertices;
    }

    private static ushort[] ReadIndices(byte[] data, uint bufferOffset, uint bufferSize, Mesh mesh)
    {
        var count = checked((int)mesh.IndexCount);
        var start = checked((long)bufferOffset + (long)mesh.StartIndex * sizeof(ushort));
        var end = checked(start + (long)count * sizeof(ushort));
        var bufferEnd = checked((long)bufferOffset + bufferSize);
        if (start < bufferOffset || end > bufferEnd || end > data.Length)
            throw new InvalidDataException("MDL index stream is outside its declared buffer.");

        var indices = new ushort[count];
        for (var index = 0; index < count; ++index)
            indices[index] = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(checked((int)start + index * 2)));
        return indices;
    }

    private static Vector4 ReadVector(ReadOnlySpan<byte> source, byte type)
        => type switch
        {
            2 => new(ReadSingle(source), ReadSingle(source[4..]), ReadSingle(source[8..]), 1),
            3 => new(ReadSingle(source), ReadSingle(source[4..]), ReadSingle(source[8..]), ReadSingle(source[12..])),
            8 => new(source[0] / 255f, source[1] / 255f, source[2] / 255f, source[3] / 255f),
            13 => new(ReadHalf(source), ReadHalf(source[2..]), 0, 1),
            14 => new(ReadHalf(source), ReadHalf(source[2..]), ReadHalf(source[4..]), ReadHalf(source[6..])),
            _ => throw new InvalidDataException($"Unsupported MDL position type {type}."),
        };

    private static float ReadSingle(ReadOnlySpan<byte> source)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source));

    private static float ReadHalf(ReadOnlySpan<byte> source)
        => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(source));

    private static int VertexValueSize(byte type)
        => type switch
        {
            2 => 12,
            3 => 16,
            8 or 13 => 4,
            14 => 8,
            _ => throw new InvalidDataException($"Unsupported MDL position type {type}."),
        };

    private static void ValidateBuffer(byte[] data, uint offset, uint size, string name)
    {
        if (size == 0 || offset > data.Length || size > data.Length - offset)
            throw new InvalidDataException($"MDL {name} buffer is outside the file.");
    }

    private readonly record struct VertexElement(byte Stream, byte Offset, byte Type, byte Usage);
    private readonly record struct Mesh(
        ushort VertexCount,
        uint IndexCount,
        uint StartIndex,
        uint[] StreamOffsets,
        byte[] Strides,
        byte StreamCount);

    private sealed class CheckedReader(byte[] data)
    {
        public int Position { get; private set; }

        public byte ReadByte() => ReadSpan(1)[0];
        public ushort ReadUInt16() => BinaryPrimitives.ReadUInt16LittleEndian(ReadSpan(2));
        public uint ReadUInt32() => BinaryPrimitives.ReadUInt32LittleEndian(ReadSpan(4));
        public uint[] ReadUInt32Array(int count) => Enumerable.Range(0, count).Select(_ => ReadUInt32()).ToArray();
        public byte[] ReadBytes(int count) => ReadSpan(count).ToArray();
        public int ReadBoundedUInt32(string message)
        {
            var value = ReadUInt32();
            if (value > int.MaxValue)
                throw new InvalidDataException(message);
            return (int)value;
        }
        public ReadOnlySpan<byte> ReadSpan(int count)
        {
            if (count < 0 || Position > data.Length - count)
                throw new EndOfStreamException("MDL data is truncated.");
            var result = data.AsSpan(Position, count);
            Position += count;
            return result;
        }
        public void Skip(int count) => ReadSpan(count);
    }
}

public sealed record MdlPreviewParseResult(IReadOnlyList<ModelPreviewSourceMesh> Meshes, int LodCount);
