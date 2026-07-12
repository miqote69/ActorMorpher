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
        var included = actors
            .Where(actor => settings.IncludeYourself || !actor.IsLocalPlayer)
            .Where(actor => Matches(actor, settings.Target))
            .ToArray();
        var excluded = settings.Exclusion is { } exclusion
            ? included.Where(actor => Matches(actor, exclusion)).ToArray()
            : Array.Empty<ActorEntry>();
        var excludedKeys = excluded.Select(static actor => actor.Key).ToHashSet();
        var matching = included.Where(actor => !excludedKeys.Contains(actor.Key)).ToArray();
        var eligible = matching
            .Where(static actor => actor.Representations.Count > 0 && actor.Current.Race is not null)
            .Select(static actor => actor.Key)
            .Distinct()
            .ToArray();
        var unavailable = matching.Count(static actor => actor.Representations.Count == 0);

        return new BulkOutfitPreview(
            matching.Length,
            excluded.Length,
            eligible.Length,
            matching.Count(static actor => actor.Representations.Count > 0 && actor.Current.Race is null),
            unavailable,
            eligible);
    }

    private bool Matches(ActorEntry actor, BulkOutfitFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Name)
            && !GameTextComparison.Contains(actor.Name, filter.Name, language()))
            return false;
        if (filter.ActorType == ActorTargetType.Players && actor.Kind != ObjectKind.Pc)
            return false;
        if (filter.ActorType == ActorTargetType.Npcs && actor.Kind == ObjectKind.Pc)
            return false;
        if (filter.Race != 0 && actor.Race != filter.Race)
            return false;
        return filter.Gender is null || actor.Gender == filter.Gender;
    }
}
