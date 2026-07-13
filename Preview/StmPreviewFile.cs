using System.Buffers.Binary;
using System.Numerics;

namespace ActorMorpher.Preview;

public sealed class StmPreviewFile
{
    private const int MaximumEntries = 8192;
    private const int StainCount = 254;
    private const int HalfColorSize = 6;
    private readonly byte[] data;
    private readonly Dictionary<uint, Entry> entries;
    private readonly int columnCount;

    public StmPreviewFile(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        this.data = data;
        var reader = new Reader(data);
        if (reader.UInt16() != 0x534D)
            throw new InvalidDataException("STM magic is invalid.");
        var version = reader.UInt16();
        var entryCount = reader.UInt16();
        var colorCount = reader.Byte();
        var scalarCount = reader.Byte();
        if (entryCount is 0 or > MaximumEntries)
            throw new InvalidDataException("STM entry count is invalid.");
        if (version == 0x0101)
        {
            if (colorCount != 0 || scalarCount != 0)
                throw new InvalidDataException("STM legacy column counts are invalid.");
            colorCount = 3;
            scalarCount = 2;
        }
        else if (version is not (0x0200 or 0x0201))
        {
            throw new InvalidDataException("STM version is unsupported.");
        }
        if (colorCount == 0 || colorCount + scalarCount > 32)
            throw new InvalidDataException("STM column counts are invalid.");
        columnCount = colorCount + scalarCount;

        var keys = new uint[entryCount];
        var offsets = new uint[entryCount];
        for (var index = 0; index < entryCount; ++index)
            keys[index] = reader.UInt32();
        for (var index = 0; index < entryCount; ++index)
            offsets[index] = reader.UInt32();
        var dataStart = reader.Position;
        entries = new Dictionary<uint, Entry>(entryCount);
        for (var index = 0; index < entryCount; ++index)
        {
            var start = checked(dataStart + checked((int)offsets[index] * 2));
            var end = index + 1 < entryCount
                ? checked(dataStart + checked((int)offsets[index + 1] * 2))
                : data.Length;
            if (start < dataStart || end < start || end > data.Length || !entries.TryAdd(keys[index], new Entry(start, end - start)))
                throw new InvalidDataException("STM entry offsets are invalid.");
        }
    }

    public bool TryGetDiffuseColor(ushort template, byte stainId, out Vector3 color)
    {
        color = default;
        if (stainId is 0 or > StainCount || !entries.TryGetValue(template, out var entry))
            return false;
        var headerSize = checked(columnCount * sizeof(ushort));
        if (entry.Length < headerSize)
            return false;
        var firstColumnSize = checked(BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(entry.Offset)) * 2);
        if (firstColumnSize > entry.Length - headerSize)
            return false;
        return TryReadColor(data.AsSpan(entry.Offset + headerSize, firstColumnSize), stainId, out color);
    }

    private static bool TryReadColor(ReadOnlySpan<byte> column, byte stainId, out Vector3 color)
    {
        color = default;
        ReadOnlySpan<byte> value = default;
        if (column.Length == 0)
            return true;
        if (column.Length == HalfColorSize)
        {
            value = column;
        }
        else if (column.Length == StainCount * HalfColorSize)
        {
            value = column.Slice((stainId - 1) * HalfColorSize, HalfColorSize);
        }
        else if (column.Length >= StainCount && (column.Length - StainCount) % HalfColorSize == 0)
        {
            var valueCount = (column.Length - StainCount) / HalfColorSize;
            var indexOffset = valueCount * HalfColorSize + stainId;
            var valueIndex = indexOffset < column.Length ? column[indexOffset] : 0;
            if (valueIndex == 0)
                return true;
            if (valueIndex > valueCount)
                return false;
            value = column.Slice((valueIndex - 1) * HalfColorSize, HalfColorSize);
        }
        else
        {
            return false;
        }

        color = new Vector3(
            ReadHalf(value),
            ReadHalf(value[2..]),
            ReadHalf(value[4..]));
        return float.IsFinite(color.X) && float.IsFinite(color.Y) && float.IsFinite(color.Z);
    }

    private static float ReadHalf(ReadOnlySpan<byte> data)
        => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(data));

    private readonly record struct Entry(int Offset, int Length);

    private sealed class Reader(byte[] data)
    {
        private int position;
        public int Position => position;
        public byte Byte() => Span(1)[0];
        public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Span(2));
        public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Span(4));
        private ReadOnlySpan<byte> Span(int count)
        {
            if (count < 0 || position > data.Length - count)
                throw new EndOfStreamException("STM data is truncated.");
            var result = data.AsSpan(position, count);
            position += count;
            return result;
        }
    }
}
