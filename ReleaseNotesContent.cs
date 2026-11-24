using System.Globalization;
using System.IO;
using System.Text;
using ActorMorpher.Localization;

namespace ActorMorpher;

internal static class ReleaseNotesContent
{
    private static readonly Dictionary<string, string> Sections = new();
    private const string JapaneseLineStartProhibited = "、。，．・！？!?：；:;…‥〜～ー々」』）】〕］｝〉》,.%％)]}"
        + "ぁぃぅぇぉっゃゅょゎゕゖァィゥェォッャュョヮヵヶ";

    public static string Title { get; }

    static ReleaseNotesContent()
    {
        using var stream = typeof(ReleaseNotesContent).Assembly.GetManifestResourceStream("ActorMorpher.ReleaseNotes")!;
        using var reader = new StreamReader(stream);
        var sections = reader.ReadToEnd().Replace("\r\n", "\n").Split("\n## ");
        Title = sections[0].Trim()[2..];
        foreach (var section in sections.Skip(1))
        {
            var headingEnd = section.IndexOf('\n');
            // Render the release document as wrapped text without Markdown heading/code markers.
            Sections.Add(section[..headingEnd], section[(headingEnd + 1)..].Trim()
                .Replace("### ", "").Replace("`", ""));
        }
    }

    public static string GetText(UiLanguage language) => Sections[language switch
    {
        UiLanguage.Japanese => "日本語",
        UiLanguage.German => "Deutsch",
        UiLanguage.French => "Français",
        _ => "English", // Same default language as Localizer.
    }];

    internal static string WrapJapanese(string text, float width, Func<string, float> measure)
    {
        var result = new StringBuilder(text.Length);
        var paragraphs = text.Split('\n');
        for (var index = 0; index < paragraphs.Length; index++)
        {
            if (index > 0) result.Append('\n');
            var paragraph = paragraphs[index];
            if (measure(paragraph) <= width)
                result.Append(paragraph);
            else
                AppendWrappedParagraph(result, paragraph, width, measure);
        }
        return result.ToString();
    }

    private static void AppendWrappedParagraph(StringBuilder result, string paragraph, float width,
        Func<string, float> measure)
    {
        var boundaries = new List<int> { 0 };
        var bullet = paragraph.StartsWith("- ", StringComparison.Ordinal);
        foreach (var offset in StringInfo.ParseCombiningCharacters(paragraph))
        {
            if (offset == 0 || (bullet && offset <= 2)) continue;
            if (CanBreakJapaneseLine(paragraph[offset - 1], paragraph[offset]))
                boundaries.Add(offset);
        }
        boundaries.Add(paragraph.Length);

        for (var index = 0; index < boundaries.Count - 1;)
        {
            var start = boundaries[index];
            var end = FindFittingBoundary(paragraph, boundaries, index, width, measure);
            result.Append(paragraph, start, boundaries[end] - start);
            if (end < boundaries.Count - 1) result.Append('\n');
            index = end;
        }
    }

    private static int FindFittingBoundary(string paragraph, List<int> boundaries, int index, float width,
        Func<string, float> measure)
    {
        var start = boundaries[index];
        var low = index + 1;
        var high = boundaries.Count - 1;
        // If a single unbreakable unit is wider than the viewport, keep the unit together.
        var best = low;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            if (measure(paragraph[start..boundaries[middle]]) <= width)
            {
                best = middle;
                low = middle + 1;
            }
            else
                high = middle - 1;
        }
        return best;
    }

    private static bool CanBreakJapaneseLine(char before, char after)
        => !char.IsWhiteSpace(after)
            && !"「『（【〔［｛〈《([{".Contains(before)
            && !JapaneseLineStartProhibited.Contains(after)
            && !(IsAsciiWordCharacter(before) && IsAsciiWordCharacter(after));

    private static bool IsAsciiWordCharacter(char character)
        => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_' or '+';
}
