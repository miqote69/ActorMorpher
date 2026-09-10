using System;
using System.Linq;
using System.Reflection;
using ActorMorpher.Localization;
using Dalamud.Game;
using Dalamud.Plugin.Services;
using Xunit;

namespace ActorMorpher.Tests;

public sealed class ReleaseNotesTests
{
    [Fact]
    public void EmbeddedNotesDescribeTheBuiltPluginVersion()
        => Assert.Equal($"Actor Morpher v{typeof(ReleaseNotesContent).Assembly.GetName().Version}", ReleaseNotesContent.Title);

    [Theory]
    [InlineData(UiLanguage.Japanese, "リリースノート", "確認が残っている項目")]
    [InlineData(UiLanguage.English, "Release Notes", "Checks still pending")]
    [InlineData(UiLanguage.German, "Versionshinweise", "Noch ausstehende Prüfungen")]
    [InlineData(UiLanguage.French, "Notes de version", "Vérifications encore en attente")]
    public void ChangingUiLanguageChangesTheTabAndEmbeddedNotes(UiLanguage language, string label, string pendingHeading)
    {
        var configuration = new Configuration { UiLanguage = UiLanguage.English };
        var localizer = new Localizer(configuration, CreateClientState(ClientLanguage.English));
        Assert.Contains("Release Notes", ReleaseNotesContent.GetText(localizer.EffectiveLanguage));

        configuration.UiLanguage = language;
        Assert.Equal(label, localizer[TextKey.ReleaseNotes]);
        var text = ReleaseNotesContent.GetText(localizer.EffectiveLanguage);
        Assert.Contains(label, text);
        Assert.DoesNotContain(pendingHeading, text);
        Assert.Equal(8, text.Split('\n').Count(line => line.StartsWith("- ")));
        Assert.Contains("w9005", text);
        Assert.DoesNotContain("CMC", text);
        Assert.DoesNotContain("##", text);
        Assert.DoesNotContain("`", text);

        configuration.UiLanguage = UiLanguage.English;
        Assert.Equal("Release Notes", localizer[TextKey.ReleaseNotes]);
        Assert.Contains("Release Notes", ReleaseNotesContent.GetText(localizer.EffectiveLanguage));
    }

    [Theory]
    [InlineData(ClientLanguage.Japanese, UiLanguage.Japanese)]
    [InlineData(ClientLanguage.English, UiLanguage.English)]
    [InlineData(ClientLanguage.German, UiLanguage.German)]
    [InlineData(ClientLanguage.French, UiLanguage.French)]
    public void AutomaticUsesTheGameLanguageForNotes(ClientLanguage gameLanguage, UiLanguage expectedLanguage)
    {
        var configuration = new Configuration { UiLanguage = UiLanguage.Automatic };
        var localizer = new Localizer(configuration, CreateClientState(gameLanguage));

        Assert.Equal(expectedLanguage, localizer.EffectiveLanguage);
        Assert.Contains(localizer[TextKey.ReleaseNotes], ReleaseNotesContent.GetText(localizer.EffectiveLanguage));
    }

    private static IClientState CreateClientState(ClientLanguage language)
    {
        var state = DispatchProxy.Create<IClientState, LanguageClientState>();
        ((LanguageClientState)(object)state).Language = language;
        return state;
    }

    public class LanguageClientState : DispatchProxy
    {
        public ClientLanguage Language { get; set; }

        protected override object? Invoke(MethodInfo? method, object?[]? args)
            => method?.Name == "get_ClientLanguage" ? Language : throw new NotSupportedException(method?.Name);
    }
}
