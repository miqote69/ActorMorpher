using System.IO;
using ActorMorpher.Localization;

namespace ActorMorpher;

internal static class ReleaseNotesContent
{
    private static readonly Dictionary<string, string> Sections = new();

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
}
