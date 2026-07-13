using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace ActorMorpher.Preview;

public sealed class MdlPreviewParser
{
    private const uint Version5 = 0x01000005;
    private const uint Version6 = 0x01000006;
    private const int FileHeaderSize = 0x44;
    private const int VertexDeclarationElementCount = 17;
    private const int ModelHeaderSize = 0x38;
    private const int ElementIdSize = 0x20;
    private const int LodSize = 0x3C;
    private const int ExtraLodSize = 0x28;
    private const int MeshSize = 0x24;

    public MdlPreviewParseResult Parse(
        byte[] data,
        byte? facialFeatures = null,
        ushort? imcAttributeMask = null,
        bool? hasTail = null)
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
        var fileMaterialCount = reader.ReadUInt16();
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
        var strings = reader.ReadBytes(reader.ReadBoundedUInt32("MDL string table is too large."));
        var modelHeader = reader.ReadSpan(ModelHeaderSize);
        var meshCount = BinaryPrimitives.ReadUInt16LittleEndian(modelHeader[4..]);
        var attributeCount = BinaryPrimitives.ReadUInt16LittleEndian(modelHeader[6..]);
        var submeshCount = BinaryPrimitives.ReadUInt16LittleEndian(modelHeader[8..]);
        var materialCount = BinaryPrimitives.ReadUInt16LittleEndian(modelHeader[10..]);
        var boneCount = BinaryPrimitives.ReadUInt16LittleEndian(modelHeader[12..]);
        var boneTableCount = BinaryPrimitives.ReadUInt16LittleEndian(modelHeader[14..]);
        var elementIdCount = BinaryPrimitives.ReadUInt16LittleEndian(modelHeader[24..]);
        var terrainShadowMeshCount = modelHeader[26];
        var hasExtraLod = (modelHeader[27] & 0x10) != 0;
        var terrainShadowSubmeshCount = BinaryPrimitives.ReadUInt16LittleEndian(modelHeader[38..]);
        if (meshCount == 0 || meshCount > ModelPreviewMeshBuilder.MaximumMeshCount || meshCount > declarationCount)
            throw new InvalidDataException("MDL mesh count is invalid.");
        if (materialCount > fileMaterialCount)
            throw new InvalidDataException("MDL material count is inconsistent.");

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
            var materialIndex = reader.ReadUInt16();
            var submeshIndex = reader.ReadUInt16();
            var meshSubmeshCount = reader.ReadUInt16();
            var boneTableIndex = reader.ReadUInt16();
            var startIndex = reader.ReadUInt32();
            var streamOffsets = reader.ReadUInt32Array(3);
            var strides = reader.ReadBytes(3);
            var streamCount = (byte)(reader.ReadByte() & 0x03);
            if (reader.Position - start != MeshSize)
                throw new InvalidDataException("MDL mesh layout is invalid.");
            meshes[meshIndex] = new Mesh(
                vertexCount,
                indexCount,
                materialIndex,
                submeshIndex,
                meshSubmeshCount,
                boneTableIndex,
                startIndex,
                streamOffsets,
                strides,
                streamCount);
        }

        var attributeOffsets = reader.ReadUInt32Array(attributeCount);
        var attributes = attributeOffsets.Select(offset => ReadString(strings, offset)).ToArray();
        reader.Skip(checked(terrainShadowMeshCount * 20));
        var submeshes = new Submesh[submeshCount];
        for (var index = 0; index < submeshes.Length; ++index)
        {
            submeshes[index] = new Submesh(reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32());
            reader.Skip(4);
        }
        reader.Skip(checked(terrainShadowSubmeshCount * 12));
        var materialOffsets = reader.ReadUInt32Array(materialCount);
        var materials = materialOffsets.Select(offset => ReadString(strings, offset)).ToArray();
        var boneOffsets = reader.ReadUInt32Array(boneCount);
        var boneNames = boneOffsets.Select(offset => ReadString(strings, offset)).ToArray();
        var boneTables = ReadBoneTables(reader, version, boneTableCount);

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
            if (position == default || !CanRead(mesh, position))
                continue;

            var vertices = ReadVertices(data, vertexOffsets[0], vertexBufferSizes[0], mesh, declarations[meshIndex]);
            var indices = ReadIndices(
                data,
                indexOffsets[0],
                indexBufferSizes[0],
                mesh,
                submeshes,
                attributes,
                facialFeatures,
                imcAttributeMask,
                hasTail);
            if (indices.Length == 0)
                continue;
            var material = mesh.MaterialIndex < materials.Length ? materials[mesh.MaterialIndex] : string.Empty;
            var bones = mesh.BoneTableIndex < boneTables.Length
                ? boneTables[mesh.BoneTableIndex]
                    .Select(index => index < boneNames.Length ? boneNames[index] : string.Empty)
                    .ToArray()
                : Array.Empty<string>();
            sources.Add(new ModelPreviewSourceMesh(meshIndex, material, vertices, indices, bones));
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
        IReadOnlyList<VertexElement> elements)
    {
        var vertices = new ModelPreviewSourceVertex[mesh.VertexCount];
        var streamEnd = checked((long)bufferOffset + bufferSize);
        foreach (var element in elements.Where(element => element.Usage is 0 or 1 or 2 or 4 or 7 && CanRead(mesh, element)))
        {
            var streamStart = checked((long)bufferOffset + mesh.StreamOffsets[element.Stream]);
            var requiredEnd = checked(streamStart
                + (long)Math.Max(0, mesh.VertexCount - 1) * mesh.Strides[element.Stream]
                + element.Offset
                + VertexValueSize(element.Type));
            if (streamStart < bufferOffset || requiredEnd > streamEnd || requiredEnd > data.Length)
                throw new InvalidDataException("MDL vertex stream is outside its declared buffer.");
        }

        for (var vertexIndex = 0; vertexIndex < vertices.Length; ++vertexIndex)
        {
            Vector4? position = null;
            Vector4? uv = null;
            Vector4? color = null;
            Vector4? boneWeights = null;
            ModelPreviewBoneIndices? boneIndices = null;
            foreach (var element in elements)
            {
                if (element.Usage is not (0 or 1 or 2 or 4 or 7) || !CanRead(mesh, element))
                    continue;
                var offset = checked((int)((long)bufferOffset
                    + mesh.StreamOffsets[element.Stream]
                    + (long)vertexIndex * mesh.Strides[element.Stream]
                    + element.Offset));
                if (element.Usage == 2)
                {
                    boneIndices ??= ReadBoneIndices(data.AsSpan(offset), element.Type);
                    continue;
                }
                var value = ReadVector(data.AsSpan(offset), element.Type);
                switch (element.Usage)
                {
                    case 0 when position is null: position = value; break;
                    case 1 when boneWeights is null: boneWeights = value; break;
                    case 4 when uv is null: uv = value; break;
                    case 7 when color is null: color = value; break;
                }
            }
            vertices[vertexIndex] = new ModelPreviewSourceVertex(position, null, uv, color, boneWeights, boneIndices);
        }
        return vertices;
    }

    private static bool CanRead(Mesh mesh, VertexElement element)
    {
        if (element.Stream >= 3 || element.Stream >= mesh.StreamCount)
            return false;
        var stride = mesh.Strides[element.Stream];
        var valueSize = TryVertexValueSize(element.Type);
        if (valueSize == 0)
            return false;
        return stride > 0 && valueSize <= stride && element.Offset <= stride - valueSize;
    }

    private static ushort[] ReadIndices(
        byte[] data,
        uint bufferOffset,
        uint bufferSize,
        Mesh mesh,
        IReadOnlyList<Submesh> submeshes,
        IReadOnlyList<string> attributes,
        byte? facialFeatures,
        ushort? imcAttributeMask,
        bool? hasTail)
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
        if ((facialFeatures is null && imcAttributeMask is null && hasTail is null) || mesh.SubmeshCount == 0)
            return indices;
        if (mesh.SubmeshIndex > submeshes.Count - mesh.SubmeshCount)
            throw new InvalidDataException("MDL mesh submesh range is invalid.");

        var selected = new List<ushort>(indices.Length);
        var endSubmesh = mesh.SubmeshIndex + mesh.SubmeshCount;
        for (var submeshIndex = mesh.SubmeshIndex; submeshIndex < endSubmesh; ++submeshIndex)
        {
            var submesh = submeshes[submeshIndex];
            if (!IncludesEnabledAttributes(
                    submesh.AttributeMask,
                    attributes,
                    facialFeatures,
                    imcAttributeMask,
                    hasTail))
                continue;
            if (submesh.IndexOffset < mesh.StartIndex)
                throw new InvalidDataException("MDL submesh index range precedes its mesh.");
            var relativeOffset = checked((int)(submesh.IndexOffset - mesh.StartIndex));
            var submeshIndexCount = checked((int)submesh.IndexCount);
            if (relativeOffset > indices.Length - submeshIndexCount)
                throw new InvalidDataException("MDL submesh index range is outside its mesh.");
            selected.AddRange(indices.AsSpan(relativeOffset, submeshIndexCount).ToArray());
        }
        return selected.ToArray();
    }

    private static bool IncludesEnabledAttributes(
        uint attributeMask,
        IReadOnlyList<string> attributes,
        byte? facialFeatures,
        ushort? imcAttributeMask,
        bool? hasTail)
    {
        var hasFacialVariant = false;
        var includesFacialVariant = false;
        for (var index = 0; index < Math.Min(attributes.Count, 32); ++index)
        {
            if ((attributeMask & (1u << index)) == 0)
                continue;
            var attribute = attributes[index];
            if (facialFeatures is not null
                && attribute.Length == 8
                && attribute.StartsWith("atr_fv_", StringComparison.Ordinal)
                && attribute[7] is >= 'a' and <= 'g')
            {
                hasFacialVariant = true;
                var feature = 1 << (attribute[7] - 'a');
                includesFacialVariant |= (facialFeatures.Value & feature) != 0;
                continue;
            }
            if (imcAttributeMask is not null
                && TryGetImcAttributeBit(attribute, out var imcBit)
                && (imcAttributeMask.Value & (1 << imcBit)) == 0)
                return false;
            if (hasTail is not null
                && (attribute == "atr_tls" && !hasTail.Value
                    || attribute == "atr_tlh" && hasTail.Value))
                return false;
        }
        return !hasFacialVariant || includesFacialVariant;
    }

    private static bool TryGetImcAttributeBit(string attribute, out int bit)
    {
        bit = 0;
        if (!attribute.StartsWith("atr_", StringComparison.Ordinal)
            || attribute.Length < 7
            || attribute[^2] != '_'
            || attribute[^1] is < 'a' or > 'j')
            return false;
        var category = attribute[4..^2];
        if (category is not ("mv" or "tv" or "gv" or "dv" or "sv"
            or "ev" or "nv" or "wv" or "rv" or "bv" or "parts" or "hv"))
            return false;
        bit = attribute[^1] - 'a';
        return true;
    }

    private static Vector4 ReadVector(ReadOnlySpan<byte> source, byte type)
        => type switch
        {
            0 => new(ReadSingle(source), 0, 0, 1),
            1 => new(ReadSingle(source), ReadSingle(source[4..]), 0, 1),
            2 => new(ReadSingle(source), ReadSingle(source[4..]), ReadSingle(source[8..]), 1),
            3 => new(ReadSingle(source), ReadSingle(source[4..]), ReadSingle(source[8..]), ReadSingle(source[12..])),
            4 => new(source[2] / 255f, source[1] / 255f, source[0] / 255f, source[3] / 255f),
            5 => new(source[0] / 255f, source[1] / 255f, source[2] / 255f, source[3] / 255f),
            8 => new(source[0] / 255f, source[1] / 255f, source[2] / 255f, source[3] / 255f),
            13 => new(ReadHalf(source), ReadHalf(source[2..]), 0, 1),
            14 => new(ReadHalf(source), ReadHalf(source[2..]), ReadHalf(source[4..]), ReadHalf(source[6..])),
            16 => new(
                BinaryPrimitives.ReadUInt16LittleEndian(source),
                BinaryPrimitives.ReadUInt16LittleEndian(source[2..]),
                0,
                1),
            17 => new(
                BinaryPrimitives.ReadUInt16LittleEndian(source),
                BinaryPrimitives.ReadUInt16LittleEndian(source[2..]),
                BinaryPrimitives.ReadUInt16LittleEndian(source[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(source[6..])),
            _ => throw new InvalidDataException($"Unsupported MDL position type {type}."),
        };

    private static ModelPreviewBoneIndices ReadBoneIndices(ReadOnlySpan<byte> source, byte type)
        => type switch
        {
            5 or 8 => new(source[0], source[1], source[2], source[3]),
            16 => new(
                BinaryPrimitives.ReadUInt16LittleEndian(source),
                BinaryPrimitives.ReadUInt16LittleEndian(source[2..]),
                0,
                0),
            17 => new(
                BinaryPrimitives.ReadUInt16LittleEndian(source),
                BinaryPrimitives.ReadUInt16LittleEndian(source[2..]),
                BinaryPrimitives.ReadUInt16LittleEndian(source[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(source[6..])),
            _ => throw new InvalidDataException($"Unsupported MDL blend-index type {type}."),
        };

    private static float ReadSingle(ReadOnlySpan<byte> source)
        => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source));

    private static float ReadHalf(ReadOnlySpan<byte> source)
        => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(source));

    private static int VertexValueSize(byte type)
    {
        var size = TryVertexValueSize(type);
        return size == 0
            ? throw new InvalidDataException($"Unsupported MDL vertex type {type}.")
            : size;
    }

    private static int TryVertexValueSize(byte type)
        => type switch
        {
            0 => 4,
            1 => 8,
            2 => 12,
            3 => 16,
            4 or 5 or 8 or 13 => 4,
            14 or 17 => 8,
            16 => 4,
            _ => 0,
        };

    private static ushort[][] ReadBoneTables(CheckedReader reader, uint version, ushort count)
    {
        if (count == 0)
            return Array.Empty<ushort[]>();
        var tables = new ushort[count][];
        if (version == Version5)
        {
            for (var table = 0; table < count; ++table)
            {
                var indices = Enumerable.Range(0, 64).Select(_ => reader.ReadUInt16()).ToArray();
                var boneCount = reader.ReadUInt32();
                if (boneCount > indices.Length)
                    throw new InvalidDataException("MDL V5 bone table count is invalid.");
                tables[table] = indices[..(int)boneCount];
            }
            return tables;
        }

        var tableStart = reader.Position;
        for (var table = 0; table < count; ++table)
        {
            reader.Position = tableStart + table * 4;
            var offset = reader.ReadUInt16();
            var boneCount = reader.ReadUInt16();
            if (boneCount > 256)
                throw new InvalidDataException("MDL V6 bone table count is invalid.");
            var indexPosition = checked(tableStart + table * 4 + offset * 4);
            reader.Position = indexPosition;
            tables[table] = Enumerable.Range(0, boneCount).Select(_ => reader.ReadUInt16()).ToArray();
        }
        reader.Position = tableStart + count * 4;
        return tables;
    }

    private static string ReadString(byte[] strings, uint offset)
    {
        if (offset >= strings.Length)
            return string.Empty;
        var start = checked((int)offset);
        var length = Array.IndexOf(strings, (byte)0, start);
        length = length < 0 ? strings.Length - start : length - start;
        return Encoding.UTF8.GetString(strings, start, length);
    }

    private static void ValidateBuffer(byte[] data, uint offset, uint size, string name)
    {
        if (size == 0 || offset > data.Length || size > data.Length - offset)
            throw new InvalidDataException($"MDL {name} buffer is outside the file.");
    }

    private readonly record struct VertexElement(byte Stream, byte Offset, byte Type, byte Usage);
    private readonly record struct Mesh(
        ushort VertexCount,
        uint IndexCount,
        ushort MaterialIndex,
        ushort SubmeshIndex,
        ushort SubmeshCount,
        ushort BoneTableIndex,
        uint StartIndex,
        uint[] StreamOffsets,
        byte[] Strides,
        byte StreamCount);
    private readonly record struct Submesh(uint IndexOffset, uint IndexCount, uint AttributeMask);

    private sealed class CheckedReader(byte[] data)
    {
        public int Position { get; set; }

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
