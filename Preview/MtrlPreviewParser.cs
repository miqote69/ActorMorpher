using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace ActorMorpher.Preview;

public sealed class MtrlPreviewParser
{
    private const int HeaderSize = 16;
    private const int AttributeSetSize = 4;
    private const int ShaderHeaderSize = 12;
    private const int ShaderKeySize = 8;
    private const int ConstantSize = 8;
    private const int SamplerSize = 12;

    public MtrlPreviewData Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length < HeaderSize)
            throw new InvalidDataException("MTRL header is truncated.");

        var reader = new Reader(data);
        var version = reader.UInt32();
        var packedSizes = reader.UInt32();
        var dataSetSize = checked((int)(packedSizes >> 16));
        var stringTableSize = reader.UInt16();
        var shaderPackageOffset = reader.UInt16();
        var textureCount = reader.Byte();
        var uvSetCount = reader.Byte();
        var colorSetCount = reader.Byte();
        var additionalDataSize = reader.Byte();

        var textureOffsets = new ushort[textureCount];
        for (var index = 0; index < textureOffsets.Length; ++index)
            textureOffsets[index] = checked((ushort)(reader.UInt32() & 0xFFFF));
        reader.Skip(checked((uvSetCount + colorSetCount) * AttributeSetSize));
        var strings = reader.Bytes(stringTableSize);
        var texturePaths = textureOffsets.Select(offset => ReadString(strings, offset)).ToArray();
        var shaderPackage = ReadString(strings, shaderPackageOffset);
        reader.Skip(additionalDataSize);
        var dataSet = reader.Bytes(dataSetSize);

        var samplers = new List<MtrlPreviewSampler>();
        if (reader.Remaining >= ShaderHeaderSize)
        {
            var shaderValueListSize = reader.UInt16();
            var shaderKeyCount = reader.UInt16();
            var constantCount = reader.UInt16();
            var samplerCount = reader.UInt16();
            reader.Skip(4);
            reader.Skip(checked(shaderKeyCount * ShaderKeySize));
            reader.Skip(checked(constantCount * ConstantSize));
            for (var index = 0; index < samplerCount; ++index)
            {
                var samplerId = reader.UInt32();
                reader.Skip(4);
                var textureIndex = reader.Byte();
                reader.Skip(3);
                if (textureIndex < texturePaths.Length)
                    samplers.Add(new MtrlPreviewSampler(samplerId, texturePaths[textureIndex]));
            }
            reader.Skip(shaderValueListSize);
        }

        return new MtrlPreviewData(
            version,
            shaderPackage,
            texturePaths,
            samplers,
            ReadDiffuseRows(dataSet));
    }

    private static IReadOnlyList<Vector3> ReadDiffuseRows(byte[] dataSet)
    {
        var (rowCount, rowSize) = dataSet.Length switch
        {
            >= 2048 => (32, 64),
            >= 512 => (16, 32),
            _ => (0, 0),
        };
        if (rowCount == 0)
            return Array.Empty<Vector3>();

        var rows = new Vector3[rowCount];
        for (var row = 0; row < rowCount; ++row)
        {
            var offset = row * rowSize;
            rows[row] = new Vector3(
                ReadHalf(dataSet.AsSpan(offset)),
                ReadHalf(dataSet.AsSpan(offset + 2)),
                ReadHalf(dataSet.AsSpan(offset + 4)));
        }
        return rows;
    }

    private static float ReadHalf(ReadOnlySpan<byte> data)
        => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(data));

    private static string ReadString(byte[] strings, int offset)
    {
        if (offset < 0 || offset >= strings.Length)
            return string.Empty;
        var end = Array.IndexOf(strings, (byte)0, offset);
        var length = end < 0 ? strings.Length - offset : end - offset;
        return Encoding.UTF8.GetString(strings, offset, length);
    }

    private sealed class Reader(byte[] data)
    {
        private int position;
        public int Remaining => data.Length - position;
        public byte Byte() => Span(1)[0];
        public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Span(2));
        public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Span(4));
        public byte[] Bytes(int count) => Span(count).ToArray();
        public void Skip(int count) => Span(count);
        private ReadOnlySpan<byte> Span(int count)
        {
            if (count < 0 || position > data.Length - count)
                throw new EndOfStreamException("MTRL data is truncated.");
            var result = data.AsSpan(position, count);
            position += count;
            return result;
        }
    }
}

public sealed record MtrlPreviewData(
    uint Version,
    string ShaderPackage,
    IReadOnlyList<string> TexturePaths,
    IReadOnlyList<MtrlPreviewSampler> Samplers,
    IReadOnlyList<Vector3> DiffuseRows)
{
    public string? FindTexture(uint samplerId, params string[] suffixes)
        => Samplers.FirstOrDefault(sampler => sampler.SamplerId == samplerId)?.TexturePath
            ?? TexturePaths.FirstOrDefault(path => suffixes.Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)));
}

public sealed record MtrlPreviewSampler(uint SamplerId, string TexturePath);
