namespace ActorMorpher.GPose;

public sealed class GPoseMappingResolver
{
    public IReadOnlyDictionary<ActorRepresentationKey, LogicalActorKey> Resolve(
        IReadOnlyList<ActorEntry> normalActors,
        IReadOnlyList<ActorEntry> currentActors)
    {
        var result = new Dictionary<ActorRepresentationKey, LogicalActorKey>();
        var candidates = currentActors.SelectMany(static actor => actor.Representations).ToArray();

        foreach (var source in normalActors)
        {
            var sourceSnapshot = source.Current;
            var available = candidates
                .Where(candidate => candidate.RepresentationKey.ObjectIndex != source.Key.OriginalObjectIndex)
                .Where(candidate => !result.ContainsKey(candidate.RepresentationKey))
                .ToArray();

            var match = Unique(available, candidate => source.Key.GameObjectId != 0
                    && candidate.RepresentationKey.GameObjectId == source.Key.GameObjectId)
                ?? Unique(available, candidate => source.Key.EntityId is not 0 and not 0xE0000000
                    && candidate.RepresentationKey.EntityId == source.Key.EntityId)
                ?? Unique(available, candidate => candidate.BaseId == source.Key.BaseId && candidate.ObjectKind == source.Key.ObjectKind)
                ?? Unique(available, candidate => StrictMatch(sourceSnapshot, candidate));

            if (match is not null)
                result.Add(match.RepresentationKey, source.Key);
        }

        return result;
    }

    private static ActorSnapshot? Unique(IEnumerable<ActorSnapshot> candidates, Func<ActorSnapshot, bool> predicate)
    {
        using var enumerator = candidates.Where(predicate).Take(2).GetEnumerator();
        if (!enumerator.MoveNext())
            return null;

        var first = enumerator.Current;
        return enumerator.MoveNext() ? null : first;
    }

    private static bool StrictMatch(ActorSnapshot source, ActorSnapshot candidate)
        => source.Name == candidate.Name
        && source.ObjectKind == candidate.ObjectKind
        && source.BaseId == candidate.BaseId
        && source.ModelCharaId == candidate.ModelCharaId
        && source.Race == candidate.Race
        && source.Gender == candidate.Gender
        && source.BodyType == candidate.BodyType;
}
