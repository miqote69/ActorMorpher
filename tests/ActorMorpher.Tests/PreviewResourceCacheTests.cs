using System;
using System.IO;
using System.Numerics;
using ActorMorpher.Preview;
using ActorMorpher.BulkOutfit;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class PreviewResourceCacheTests
{
    [Fact]
    public void RepeatedLoadsReusePayloadAndEvictionOnlyChangesLoadCount()
    {
        var cache = new PreviewResourceCache<string, byte[]>(8, 2);
        var loads = 0;
        byte[] Load(string key) => cache.GetOrCreate(key, () => { loads++; return new byte[] { 1, 2, 3, 4 }; }, x => x.Length)!;
        var a = Load("a");
        Load("b");
        Assert.Same(a, Load("a"));
        Assert.Equal(2, loads);
        Load("c"); // b is least recently used.
        Assert.Same(a, Load("a"));
        Assert.Equal(a, Load("b"));
        Assert.Equal(4, loads);
    }

    [Fact]
    public void OversizedNullAndExceptionsAreReturnedWithoutCachingOrChangingBehavior()
    {
        var cache = new PreviewResourceCache<string, byte[]>(2);
        var loads = 0;
        byte[]? Load(string key, Func<byte[]?> factory) => cache.GetOrCreate(key, () => { loads++; return factory(); }, x => x.Length);
        for (var attempt = 0; attempt < 2; attempt++)
        {
            Assert.Equal(new byte[] { 1, 2, 3 }, Load("large", () => [1, 2, 3]));
            Assert.Null(Load("missing", () => null));
            Assert.Throws<InvalidDataException>(() => Load("invalid", () => throw new InvalidDataException("unchanged")));
        }
        Assert.Equal(6, loads);
    }

    [Fact]
    public void TextureKeyUsesStainValuesForTheMaterialRatherThanListIdentity()
    {
        var first = new ModelPreviewStains[10];
        first[(int)OutfitSlot.Body] = new(2, 3);
        var other = (ModelPreviewStains[])first.Clone();
        other[(int)OutfitSlot.Feet] = new(8, 9);
        var key = ModelPreviewTextureSource.TextureKey.From("body_top_a.mtrl", ModelPreviewTextureContext.Default with { EquipmentStains = first });
        Assert.Equal(key, ModelPreviewTextureSource.TextureKey.From("body_top_a.mtrl", ModelPreviewTextureContext.Default with { EquipmentStains = other }));
        other[(int)OutfitSlot.Body] = new(2, 4);
        Assert.NotEqual(key, ModelPreviewTextureSource.TextureKey.From("body_top_a.mtrl", ModelPreviewTextureContext.Default with { EquipmentStains = other }));
        other[(int)OutfitSlot.Body] = new(4, 3);
        Assert.NotEqual(key, ModelPreviewTextureSource.TextureKey.From("body_top_a.mtrl", ModelPreviewTextureContext.Default with { EquipmentStains = other }));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
    [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)]
    public void TextureKeySeparatesEveryColorInputAndPath(int field)
    {
        var context = ModelPreviewTextureContext.Default;
        var color = new Vector4(0.123f);
        var changed = field switch
        {
            0 => context with { HairColor = color },
            1 => context with { HairHighlightColor = color },
            2 => context with { SkinColor = color },
            3 => context with { EyeColor = color },
            4 => context with { HeterochromiaColor = color },
            5 => context with { FacialFeatureColor = color },
            6 => context with { FacePaintColor = color },
            7 => context with { FacePaint = 1 },
            8 => context with { UseMaterialHairColor = !context.UseMaterialHairColor },
            _ => context,
        };
        var first = ModelPreviewTextureSource.TextureKey.From("a", context);
        var second = ModelPreviewTextureSource.TextureKey.From(field == 9 ? "b" : "a", changed);
        var cache = new PreviewResourceCache<ModelPreviewTextureSource.TextureKey, byte[]>();
        var a = cache.GetOrCreate(first, () => [1], x => x.Length);
        var b = cache.GetOrCreate(second, () => [2], x => x.Length);
        Assert.NotEqual(first, second);
        Assert.Equal(new byte[] { 1 }, a);
        Assert.Equal(new byte[] { 2 }, b);
        Assert.Same(a, cache.GetOrCreate(first, () => throw new Exception("unexpected reload"), x => x.Length));
    }
}
