using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace ActorMorpher.Preview;

public sealed class HumanPbdDeformer
{
    private const int MaximumEntries = 128;
    private const int MaximumBones = 512;
    private readonly Entry[] entries;
    private readonly TreeEntry[] tree;
    private readonly Dictionary<ushort, int> entriesByCode;

    public HumanPbdDeformer(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var reader = new Reader(data);
        var count = reader.Int32();
        if (count is <= 0 or > MaximumEntries)
            throw new InvalidDataException("Human PBD entry count is invalid.");

        entries = new Entry[count];
        entriesByCode = new Dictionary<ushort, int>(count);
        for (var index = 0; index < count; ++index)
        {
            var code = reader.UInt16();
            var treeIndex = reader.Int16();
            var deformerOffset = reader.Int32();
            reader.Skip(4); // Scale value is not part of vertex deformation.
            if (treeIndex < 0 || treeIndex >= count || !entriesByCode.TryAdd(code, index))
                throw new InvalidDataException("Human PBD entry metadata is invalid.");
            entries[index] = new Entry(code, treeIndex, ReadMatrices(data, deformerOffset));
        }

        tree = new TreeEntry[count];
        for (var index = 0; index < count; ++index)
            tree[index] = new TreeEntry(reader.Int16(), reader.Int16(), reader.Int16(), reader.Int16());
    }

    public bool TryDeform(
        ushort targetCode,
        ushort modelCode,
        IReadOnlyList<ModelPreviewSourceMesh> sources,
        out IReadOnlyList<ModelPreviewSourceMesh> deformed)
    {
        deformed = sources;
        if (targetCode == 0 || modelCode == 0 || targetCode == modelCode)
            return true;
        if (!TryBuildMatrices(targetCode, modelCode, out var matrices))
            return false;

        deformed = sources.Select(source => Deform(source, matrices)).ToArray();
        return true;
    }

    private bool TryBuildMatrices(
        ushort targetCode,
        ushort modelCode,
        out IReadOnlyDictionary<string, Transform> matrices)
    {
        matrices = new Dictionary<string, Transform>();
        if (!entriesByCode.TryGetValue(targetCode, out var currentIndex)
            || !entriesByCode.ContainsKey(modelCode))
            return false;

        var result = new Dictionary<string, Transform>(entries[currentIndex].Matrices, StringComparer.Ordinal);
        for (var depth = 0; depth < entries.Length; ++depth)
        {
            var current = entries[currentIndex];
            var node = tree[current.TreeIndex];
            if (node.ParentIndex < 0 || node.ParentIndex >= tree.Length)
                return false;
            var parentNode = tree[node.ParentIndex];
            if (parentNode.DeformerIndex < 0 || parentNode.DeformerIndex >= entries.Length)
                return false;
            var parentIndex = parentNode.DeformerIndex;
            var parent = entries[parentIndex];
            if (parent.Code == modelCode)
            {
                matrices = result;
                return true;
            }

            foreach (var bone in result.Keys.ToArray())
            {
                if (parent.Matrices.TryGetValue(bone, out var parentMatrix))
                    result[bone] = Transform.Multiply(parentMatrix, result[bone]);
            }
            currentIndex = parentIndex;
        }
        return false;
    }

    private static ModelPreviewSourceMesh Deform(
        ModelPreviewSourceMesh source,
        IReadOnlyDictionary<string, Transform> matrices)
    {
        if (source.Bones is not { Count: > 0 })
            return source;
        var vertices = new ModelPreviewSourceVertex[source.Vertices.Length];
        for (var index = 0; index < vertices.Length; ++index)
        {
            var vertex = source.Vertices[index];
            vertices[index] = vertex with
            {
                Position = DeformPosition(vertex.Position, vertex.BoneWeights, vertex.BoneIndices, source.Bones, matrices),
            };
        }
        return source with { Vertices = vertices };
    }

    private static Vector4? DeformPosition(
        Vector4? position,
        Vector4? weights,
        ModelPreviewBoneIndices? indices,
        IReadOnlyList<string> bones,
        IReadOnlyDictionary<string, Transform> matrices)
    {
        if (position is not { } source || weights is not { } boneWeights || indices is not { } boneIndices)
            return position;

        var original = new Vector3(source.X, source.Y, source.Z);
        var result = Vector3.Zero;
        var totalWeight = 0.0f;
        for (var influence = 0; influence < 4; ++influence)
        {
            var weight = boneWeights[influence];
            if (!float.IsFinite(weight) || weight <= 0)
                continue;
            var boneIndex = boneIndices[influence];
            var transformed = boneIndex < bones.Count
                && matrices.TryGetValue(bones[boneIndex], out var matrix)
                    ? matrix.Apply(original)
                    : original;
            result += transformed * weight;
            totalWeight += weight;
        }
        if (totalWeight <= 0)
            return position;
        if (totalWeight < 1.0f)
            result += original * (1.0f - totalWeight);
        else if (totalWeight > 1.0001f)
            result /= totalWeight;
        return new Vector4(result, source.W);
    }

    private static IReadOnlyDictionary<string, Transform> ReadMatrices(byte[] data, int offset)
    {
        if (offset == 0)
            return new Dictionary<string, Transform>(StringComparer.Ordinal);
        if (offset < 0 || offset > data.Length - 4)
            throw new InvalidDataException("Human PBD deformer offset is invalid.");

        var count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
        if (count is < 0 or > MaximumBones)
            throw new InvalidDataException("Human PBD bone count is invalid.");
        var namesStart = checked(offset + 4);
        var matrixStart = checked(namesStart + count * 2 + ((count & 1) == 0 ? 0 : 2));
        if (matrixStart > data.Length - count * 48)
            throw new InvalidDataException("Human PBD matrix table is truncated.");

        var result = new Dictionary<string, Transform>(count, StringComparer.Ordinal);
        for (var bone = 0; bone < count; ++bone)
        {
            var nameOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(namesStart + bone * 2));
            var name = ReadString(data, checked(offset + nameOffset));
            if (string.IsNullOrEmpty(name))
                continue;
            result[name] = Transform.Read(data.AsSpan(matrixStart + bone * 48, 48));
        }
        return result;
    }

    private static string ReadString(byte[] data, int offset)
    {
        if (offset < 0 || offset >= data.Length)
            throw new InvalidDataException("Human PBD bone name offset is invalid.");
        var end = Array.IndexOf(data, (byte)0, offset);
        if (end < 0)
            throw new InvalidDataException("Human PBD bone name is unterminated.");
        return Encoding.UTF8.GetString(data, offset, end - offset);
    }

    private readonly record struct Entry(
        ushort Code,
        short TreeIndex,
        IReadOnlyDictionary<string, Transform> Matrices);

    private readonly record struct TreeEntry(
        short ParentIndex,
        short FirstChildIndex,
        short NextSiblingIndex,
        short DeformerIndex);

    private readonly record struct Transform(Vector4 X, Vector4 Y, Vector4 Z)
    {
        public Vector3 Apply(Vector3 value)
        {
            var homogeneous = new Vector4(value, 1.0f);
            return new Vector3(
                Vector4.Dot(X, homogeneous),
                Vector4.Dot(Y, homogeneous),
                Vector4.Dot(Z, homogeneous));
        }

        public static Transform Read(ReadOnlySpan<byte> data)
            => new(ReadRow(data), ReadRow(data[16..]), ReadRow(data[32..]));

        public static Transform Multiply(Transform left, Transform right)
        {
            var rightX = new Vector4(right.X.X, right.Y.X, right.Z.X, 0);
            var rightY = new Vector4(right.X.Y, right.Y.Y, right.Z.Y, 0);
            var rightZ = new Vector4(right.X.Z, right.Y.Z, right.Z.Z, 0);
            var rightTranslation = new Vector4(right.X.W, right.Y.W, right.Z.W, 1);
            return new Transform(
                new Vector4(
                    Vector4.Dot(left.X, rightX),
                    Vector4.Dot(left.X, rightY),
                    Vector4.Dot(left.X, rightZ),
                    Vector4.Dot(left.X, rightTranslation)),
                new Vector4(
                    Vector4.Dot(left.Y, rightX),
                    Vector4.Dot(left.Y, rightY),
                    Vector4.Dot(left.Y, rightZ),
                    Vector4.Dot(left.Y, rightTranslation)),
                new Vector4(
                    Vector4.Dot(left.Z, rightX),
                    Vector4.Dot(left.Z, rightY),
                    Vector4.Dot(left.Z, rightZ),
                    Vector4.Dot(left.Z, rightTranslation)));
        }

        private static Vector4 ReadRow(ReadOnlySpan<byte> data)
            => new(ReadSingle(data), ReadSingle(data[4..]), ReadSingle(data[8..]), ReadSingle(data[12..]));

        private static float ReadSingle(ReadOnlySpan<byte> data)
            => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(data));
    }

    private sealed class Reader(byte[] data)
    {
        private int position;
        public short Int16() => BinaryPrimitives.ReadInt16LittleEndian(Span(2));
        public ushort UInt16() => BinaryPrimitives.ReadUInt16LittleEndian(Span(2));
        public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Span(4));
        public void Skip(int count) => Span(count);
        private ReadOnlySpan<byte> Span(int count)
        {
            if (count < 0 || position > data.Length - count)
                throw new EndOfStreamException("Human PBD data is truncated.");
            var result = data.AsSpan(position, count);
            position += count;
            return result;
        }
    }
}
