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

    public BulkOutfitPreview Resolve(
        IReadOnlyList<ActorEntry> actors,
        BulkOutfitSettings settings,
        Func<ActorEntry, ActorSnapshot?>? selectRepresentation = null)
    {
        selectRepresentation ??= static actor => actor.Current;
        var included = actors
            .Where(actor => settings.IncludeYourself || !actor.IsLocalPlayer)
            .Select(actor => (Actor: actor, Representation: selectRepresentation(actor)))
            .Where(static candidate => candidate.Representation is not null)
            .Where(candidate => Matches(candidate.Actor, candidate.Representation!, settings.Target))
            .ToArray();
        var excluded = settings.Exclusion is { } exclusion
            ? included.Where(candidate => Matches(candidate.Actor, candidate.Representation!, exclusion)).ToArray()
            : [];
        var excludedKeys = excluded.Select(static candidate => candidate.Actor.Key).ToHashSet();
        var matching = included.Where(candidate => !excludedKeys.Contains(candidate.Actor.Key)).ToArray();
        var eligible = matching
            .Where(static candidate => candidate.Actor.Representations.Count > 0 && candidate.Representation!.Race is not null)
            .Select(static candidate => candidate.Actor.Key)
            .Distinct()
            .ToArray();
        var unavailable = matching.Count(static candidate => candidate.Actor.Representations.Count == 0);

        return new BulkOutfitPreview(
            matching.Length,
            excluded.Length,
            eligible.Length,
            matching.Count(static candidate => candidate.Actor.Representations.Count > 0 && candidate.Representation!.Race is null),
            unavailable,
            eligible);
    }

    private bool Matches(ActorEntry actor, ActorSnapshot representation, BulkOutfitFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Name)
            && !GameTextComparison.Contains(actor.Name, filter.Name, language()))
            return false;
        if (filter.ActorType == ActorTargetType.Players && actor.Kind != ObjectKind.Pc)
            return false;
        if (filter.ActorType == ActorTargetType.Npcs && actor.Kind == ObjectKind.Pc)
            return false;
        if (filter.ActorType == ActorTargetType.YoungNpcs
            && (actor.Kind == ObjectKind.Pc || representation.BodyType != (byte)NpcAge.Young))
            return false;
        if (filter.Race != 0 && representation.Race != filter.Race)
            return false;
        return filter.Gender is null || representation.Gender == filter.Gender;
    }
}
