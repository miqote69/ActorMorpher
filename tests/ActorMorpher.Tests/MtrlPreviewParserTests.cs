using ActorMorpher.Preview;
using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class MtrlPreviewParserTests
{
    [Fact]
    public void ParsesTextureSamplersAndLegacyDiffuseRows()
    {
        var parsed = new MtrlPreviewParser().Parse(CreateMaterial());

        Assert.Equal("character.shpk", parsed.ShaderPackage);
        Assert.Equal("diffuse.tex", parsed.FindTexture(0x115306BE));
        Assert.Equal("index_id.tex", parsed.FindTexture(1449103320));
        Assert.Equal(16, parsed.DiffuseRows.Count);
        Assert.Equal(1.0f, parsed.DiffuseRows[0].X);
        Assert.Equal(1.0f, parsed.DiffuseRows[1].Y);
        Assert.True(parsed.TryGetConstantVector3(0x2C2A34DD, out var diffuseColor));
        Assert.Equal(new System.Numerics.Vector3(0.2415f, 0.2595833f, 0.35f), diffuseColor);
    }

    [Fact]
    public void RejectsTruncatedDataSet()
    {
        var data = CreateMaterial();
        Array.Resize(ref data, 100);

        Assert.Throws<EndOfStreamException>(() => new MtrlPreviewParser().Parse(data));
    }

    [Fact]
    public void ResolvesHairMaskFromSamplerMaskAndLegacySTextureSuffix()
    {
        var material = new MtrlPreviewData(
            1,
            "hair.shpk",
            ["gaia_s.tex"],
            [new MtrlPreviewSampler(0x8A4E82B6, "gaia_s.tex")],
            [],
            []);

        Assert.Equal("gaia_s.tex", material.FindTexture(0x8A4E82B6, "_m.tex", "_mask.tex", "_s.tex"));
    }

    [Fact]
    public void DemihumanHairUsesMaterialDiffuseColorForMainAndHighlight()
    {
        var material = new MtrlPreviewData(
            1,
            "hair.shpk",
            [],
            [],
            [new MtrlPreviewConstant(0x2C2A34DD, [0.2415f, 0.2595833f, 0.35f])],
            []);
        var context = ModelPreviewTextureContext.Default with { UseMaterialHairColor = true };

        var resolved = context.ForHairMaterial(material);

        var expected = new System.Numerics.Vector4(0.2415f, 0.2595833f, 0.35f, 1.0f);
        Assert.Equal(expected, resolved.HairColor);
        Assert.Equal(expected, resolved.HairHighlightColor);
    }

    private static byte[] CreateMaterial()
    {
        var strings = Encoding.UTF8.GetBytes("diffuse.tex\0index_id.tex\0character.shpk\0");
        const int dataSetSize = 512;
        var data = new byte[16 + 8 + strings.Length + dataSetSize + 12 + 8 + 24 + 12];
        var writer = new Writer(data);
        writer.UInt32(1);
        writer.UInt32(dataSetSize << 16);
        writer.UInt16(strings.Length);
        writer.UInt16(25);
        writer.Byte(2);
        writer.Byte(0);
        writer.Byte(0);
        writer.Byte(0);
        writer.UInt32(0);
        writer.UInt32(12);
        writer.Bytes(strings);
        writer.Half(1); writer.Half(0); writer.Half(0);
        writer.Skip(32 - 6);
        writer.Half(0); writer.Half(1); writer.Half(0);
        writer.Skip(dataSetSize - 32 - 6);
        writer.UInt16(12);
        writer.UInt16(0);
        writer.UInt16(1);
        writer.UInt16(2);
        writer.UInt32(0);
        writer.UInt32(0x2C2A34DD);
        writer.UInt16(0);
        writer.UInt16(12);
        writer.Sampler(0x115306BE, 0);
        writer.Sampler(1449103320, 1);
        writer.Single(0.2415f);
        writer.Single(0.2595833f);
        writer.Single(0.35f);
        return data;
    }

    private sealed class Writer(byte[] data)
    {
        private int position;
        public void Byte(byte value) => data[position++] = value;
        public void UInt16(int value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(position), checked((ushort)value));
            position += 2;
        }
        public void UInt32(uint value)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(position), value);
            position += 4;
        }
        public void Half(float value) => UInt16(BitConverter.HalfToUInt16Bits((Half)value));
        public void Single(float value)
        {
            BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(position), value);
            position += 4;
        }
        public void Bytes(byte[] value)
        {
            value.CopyTo(data, position);
            position += value.Length;
        }
        public void Sampler(uint id, byte textureIndex)
        {
            UInt32(id);
            UInt32(0);
            Byte(textureIndex);
            Skip(3);
        }
        public void Skip(int count) => position += count;
    }
}
