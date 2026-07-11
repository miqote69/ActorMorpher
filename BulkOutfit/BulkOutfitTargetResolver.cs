using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game;
using ActorMorpher.Localization;

namespace ActorMorpher.BulkOutfit;

public sealed class BulkOutfitTargetResolver
{
    private readonly IDiagnosticLog diagnostics;
    private readonly Func<ClientLanguage> language;

    public BulkOutfitTargetResolver(IDiagnosticLog? diagnostics = null, Func<ClientLanguage>? language = null)
    {
        this.diagnostics = diagnostics ?? NullDiagnosticLog.Instance;
        this.language = language ?? (() => ClientLanguage.English);
    }

    public BulkOutfitPreview Resolve(IReadOnlyList<ActorEntry> actors, BulkOutfitSettings settings)
    {
        var matching = actors.Where(actor => Matches(actor, settings)).ToArray();
        var eligible = matching
            .Where(static actor => actor.Representations.Count > 0 && actor.Current.Race is not null)
            .Select(static actor => actor.Key)
            .Distinct()
            .ToArray();
        var unavailable = matching.Count(static actor => actor.Representations.Count == 0);

        return new BulkOutfitPreview(
            matching.Length,
            eligible.Length,
            matching.Count(static actor => actor.Representations.Count > 0 && actor.Current.Race is null),
            unavailable,
            eligible);
    }

    private bool Matches(ActorEntry actor, BulkOutfitSettings settings)
    {
        if (!settings.IncludeYourself && actor.IsLocalPlayer)
            return false;
        if (!string.IsNullOrWhiteSpace(settings.Name)
            && !GameTextComparison.Contains(actor.Name, settings.Name, language()))
            return false;
        if (settings.ActorType == ActorTargetType.Players && actor.Kind != ObjectKind.Pc)
            return false;
        if (settings.ActorType == ActorTargetType.Npcs && actor.Kind == ObjectKind.Pc)
            return false;
        if (settings.Race != 0 && actor.Race != settings.Race)
            return false;
        return settings.Gender is null || actor.Gender == settings.Gender;
    }
}
