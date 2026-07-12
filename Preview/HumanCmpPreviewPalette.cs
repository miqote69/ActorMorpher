using System.Numerics;

namespace ActorMorpher.Preview;

public static class HumanCmpPreviewPalette
{
    private const int ColorParametersSize = 9_216;
    private const int RaceParametersOffset = ColorParametersSize * 2;
    private const int GenderTribeParametersSize = 5_120;
    private const int HairParametersOffset = 1_024;
    private const int HairColorSize = 8;
    private const int HighlightParametersOffset = 1_024;

    public static bool TryGetHairColors(
        byte[] data,
        byte tribe,
        byte sex,
        byte hairColor,
        byte highlightColor,
        out Vector4 main,
        out Vector4 highlight)
    {
        main = default;
        highlight = default;
        if (tribe is < 1 or > 16 || sex > 1)
            return false;

        var genderTribeIndex = (tribe - 1) * 2 + sex;
        var mainOffset = RaceParametersOffset
            + genderTribeIndex * GenderTribeParametersSize
            + HairParametersOffset
            + hairColor * HairColorSize;
        var highlightOffset = HighlightParametersOffset + highlightColor * 4;
        if (mainOffset > data.Length - 4 || highlightOffset > data.Length - 4)
            return false;

        main = ReadRgba(data, mainOffset);
        highlight = ReadRgba(data, highlightOffset);
        return true;
    }

    private static Vector4 ReadRgba(byte[] data, int offset)
        => new(
            data[offset] / 255.0f,
            data[offset + 1] / 255.0f,
            data[offset + 2] / 255.0f,
            data[offset + 3] / 255.0f);
}
