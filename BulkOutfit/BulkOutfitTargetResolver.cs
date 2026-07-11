using Dalamud.Game.ClientState.Objects.Enums;

namespace ActorMorpher.BulkOutfit;

public sealed class BulkOutfitTargetResolver
{
    private readonly IDiagnosticLog diagnostics;

    public BulkOutfitTargetResolver(IDiagnosticLog? diagnostics = null)
        => this.diagnostics = diagnostics ?? NullDiagnosticLog.Instance;

    public BulkOutfitPreview Resolve(IReadOnlyList<ActorEntry> actors, BulkOutfitSettings settings)
    {
        var matching = actors.Where(actor => Matches(actor, settings)).ToArray();
        var eligible = matching
            .Where(static actor => actor.Representations.Count > 0 && actor.Current.ModelCharaId == 0)
            .Select(static actor => actor.Key)
            .Distinct()
            .ToArray();
        var unavailable = matching.Count(static actor => actor.Representations.Count == 0);

        return new BulkOutfitPreview(
            matching.Length,
            eligible.Length,
            matching.Count(static actor => actor.Representations.Count > 0 && actor.Current.ModelCharaId != 0),
            unavailable,
            eligible);
    }

    private static bool Matches(ActorEntry actor, BulkOutfitSettings settings)
    {
        if (!settings.IncludeYourself && actor.IsLocalPlayer)
            return false;
        if (!string.IsNullOrWhiteSpace(settings.Name)
            && !actor.Name.Contains(settings.Name, StringComparison.CurrentCultureIgnoreCase))
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
