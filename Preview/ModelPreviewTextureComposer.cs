using Lumina.Data.Files;
using System.Numerics;

namespace ActorMorpher.Preview;

public static class ModelPreviewTextureComposer
{
    public static ModelPreviewTexturePayload ComposeHairBaseColor(
        ModelPreviewTextureContext context,
        TexFile normal,
        TexFile mask)
    {
        var width = normal.Header.Width;
        var height = normal.Header.Height;
        ValidateDimensions(width, height);
        var output = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; ++y)
        {
            for (var x = 0; x < width; ++x)
            {
                var targetOffset = (y * width + x) * 4;
                var normalOffset = SampleOffset(normal, x, y, width, height);
                var maskOffset = SampleOffset(mask, x, y, width, height);
                var highlight = normal.ImageData[normalOffset] / 255.0f;
                var occlusion = mask.ImageData[maskOffset + 3] / 255.0f;
                var color = Vector4.Lerp(context.HairColor, context.HairHighlightColor, highlight) * occlusion;
                output[targetOffset] = ToByte(color.Z);
                output[targetOffset + 1] = ToByte(color.Y);
                output[targetOffset + 2] = ToByte(color.X);
                output[targetOffset + 3] = normal.ImageData[normalOffset + 3];
            }
        }
        return ModelPreviewTexturePayload.FromBgra(width, height, output);
    }

    public static ModelPreviewTexturePayload? ComposeCharacterBaseColor(
        MtrlPreviewData material,
        TexFile index,
        TexFile? diffuse,
        TexFile? normal)
    {
        if (index.Header.Width == 0 || index.Header.Height == 0 || material.DiffuseRows.Count == 0)
            return null;
        var width = index.Header.Width;
        var height = index.Header.Height;
        ValidateDimensions(width, height);

        var indexPixels = index.ImageData;
        var output = new byte[checked(width * height * 4)];
        for (var y = 0; y < height; ++y)
        {
            for (var x = 0; x < width; ++x)
            {
                var targetOffset = (y * width + x) * 4;
                var indexOffset = SampleOffset(index, x, y, width, height);
                var tablePair = (int)MathF.Round(indexPixels[indexOffset + 2] / 17.0f);
                var firstRow = Math.Clamp(tablePair * 2, 0, material.DiffuseRows.Count - 1);
                var secondRow = Math.Min(firstRow + 1, material.DiffuseRows.Count - 1);
                var blend = 1.0f - indexPixels[indexOffset + 1] / 255.0f;
                var color = Vector3.Lerp(material.DiffuseRows[firstRow], material.DiffuseRows[secondRow], blend);
                color = new Vector3(
                    MathF.Sqrt(Math.Max(0, color.X)),
                    MathF.Sqrt(Math.Max(0, color.Y)),
                    MathF.Sqrt(Math.Max(0, color.Z)));
                if (diffuse is not null)
                {
                    var diffuseOffset = SampleOffset(diffuse, x, y, width, height);
                    color *= new Vector3(
                        diffuse.ImageData[diffuseOffset + 2] / 255.0f,
                        diffuse.ImageData[diffuseOffset + 1] / 255.0f,
                        diffuse.ImageData[diffuseOffset] / 255.0f);
                }

                output[targetOffset] = ToByte(color.Z);
                output[targetOffset + 1] = ToByte(color.Y);
                output[targetOffset + 2] = ToByte(color.X);
                output[targetOffset + 3] = normal is null
                    ? byte.MaxValue
                    : normal.ImageData[SampleOffset(normal, x, y, width, height)];
            }
        }
        return ModelPreviewTexturePayload.FromBgra(width, height, output);
    }

    private static int SampleOffset(TexFile texture, int x, int y, int targetWidth, int targetHeight)
    {
        var sourceX = Math.Min(texture.Header.Width - 1, x * texture.Header.Width / targetWidth);
        var sourceY = Math.Min(texture.Header.Height - 1, y * texture.Header.Height / targetHeight);
        return checked((sourceY * texture.Header.Width + sourceX) * 4);
    }

    private static byte ToByte(float value)
        => checked((byte)Math.Clamp((int)MathF.Round(value * 255.0f), 0, 255));

    private static void ValidateDimensions(int width, int height)
    {
        if (width <= 0 || height <= 0 || (long)width * height > 16_777_216)
            throw new InvalidDataException("Preview material texture dimensions are invalid.");
    }
}

public sealed record ModelPreviewTexturePayload(
    string? GamePath,
    int Width,
    int Height,
    byte[]? BgraPixels)
{
    public static ModelPreviewTexturePayload FromGame(string path) => new(path, 0, 0, null);
    public static ModelPreviewTexturePayload FromBgra(int width, int height, byte[] pixels) => new(null, width, height, pixels);
}
