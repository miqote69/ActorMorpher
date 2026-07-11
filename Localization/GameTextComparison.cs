using Dalamud.Game;
using System.Globalization;

namespace ActorMorpher.Localization;

public static class GameTextComparison
{
    public static StringComparer GetComparer(ClientLanguage language)
        => new CultureStringComparer(GetCulture(language), GetOptions(language));

    public static bool Contains(string value, string search, ClientLanguage language)
        => GetCulture(language).CompareInfo.IndexOf(value, search, GetOptions(language)) >= 0;

    private static CultureInfo GetCulture(ClientLanguage language) => language switch
    {
        ClientLanguage.Japanese => CultureInfo.GetCultureInfo("ja-JP"),
        ClientLanguage.German => CultureInfo.GetCultureInfo("de-DE"),
        ClientLanguage.French => CultureInfo.GetCultureInfo("fr-FR"),
        _ => CultureInfo.GetCultureInfo("en-US"),
    };

    private static CompareOptions GetOptions(ClientLanguage language)
        => CompareOptions.IgnoreCase | (language == ClientLanguage.Japanese
            ? CompareOptions.IgnoreWidth | CompareOptions.IgnoreKanaType
            : CompareOptions.None);

    private sealed class CultureStringComparer(CultureInfo culture, CompareOptions options) : StringComparer
    {
        public override int Compare(string? x, string? y) => culture.CompareInfo.Compare(x, y, options);
        public override bool Equals(string? x, string? y) => Compare(x, y) == 0;
        public override int GetHashCode(string obj) => culture.CompareInfo.GetHashCode(obj, options);
    }
}
