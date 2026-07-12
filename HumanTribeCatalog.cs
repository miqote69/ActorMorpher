namespace ActorMorpher;

public static class HumanTribeCatalog
{
    private static readonly IReadOnlyList<uint> AllTribes = Array.AsReadOnly(
        Enumerable.Range(1, 16).Select(static value => (uint)value).ToArray());
    private static readonly IReadOnlyDictionary<uint, IReadOnlyList<uint>> TribesByRace = Enumerable.Range(1, 8)
        .ToDictionary(
            static race => (uint)race,
            static race => (IReadOnlyList<uint>)Array.AsReadOnly(
                new uint[] { (uint)(race * 2 - 1), (uint)(race * 2) }));

    public static IReadOnlyList<uint> GetTribes(uint race)
        => race == 0
            ? AllTribes
            : TribesByRace.GetValueOrDefault(race, Array.Empty<uint>());

    public static bool IsValidForRace(uint race, uint tribe)
        => tribe == 0
        || race == 0 && tribe is >= 1 and <= 16
        || TribesByRace.TryGetValue(race, out var tribes) && tribes.Contains(tribe);
}
