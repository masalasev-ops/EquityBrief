namespace EquityBrief.Worker.Ledger;

// From the 24.19 correction of 2026-11-28 how a ledger build finds a setup's history: the long history reads one code as several
// parts where its code passed to another company or joined another, each part holding sessions no other part of the code
// holds, so a code alone names no history and a code and a session together name one.
// see: A ledger build finds a setup's history by its code and its session, and runs one month over every code before it rebuilds
public static class LedgerParts
{
    // Every series a code is read as, in the order read.
    public static Dictionary<string, int[]> Of(IReadOnlyList<string> codes) =>
        codes
            .Select((code, place) => (code, place))
            .GroupBy(pair => pair.code, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(pair => pair.place).ToArray(), StringComparer.Ordinal);

    // The series of a code holding a session, each series' sessions its places among the history's sessions, oldest first;
    // -1 where no part of the code holds it.
    public static int PlaceAt(IReadOnlyDictionary<string, int[]> parts, Func<int, int[]> sessionsOf, string code, int at)
    {
        if (!parts.TryGetValue(code, out var places))
        {
            return -1;
        }

        foreach (var place in places)
        {
            if (Array.BinarySearch(sessionsOf(place), at) >= 0)
            {
                return place;
            }
        }

        return -1;
    }

    // The codes read as more than one part and how many sessions two of their parts both hold, none where each part holds its
    // own, which the lookup by code and session rests on.
    public static IReadOnlyList<(string Code, int Shared)> SharingASession(IReadOnlyDictionary<string, int[]> parts, Func<int, int[]> sessionsOf) =>
    [
        .. parts.Where(pair => pair.Value.Length > 1)
            .Select(pair => (Code: pair.Key, Shared: pair.Value.SelectMany(sessionsOf).GroupBy(at => at).Count(group => group.Count() > 1)))
            .Where(pair => pair.Shared > 0)
            .OrderBy(pair => pair.Code, StringComparer.Ordinal),
    ];

    // The codes of a rebalance's size cuts whose heavyweights' setup the readings' own source leaves out: it takes a code's first
    // part, so a code read in parts whose first part holds no bar of the session, where a later part does, has none.
    // owes: The heavyweights' setups find a code's history by its session in the first pin move after the 2027-02-06 run
    public static IReadOnlyList<string> SkippedByTheHeavyweightsRead(IReadOnlyDictionary<string, int[]> parts, Func<int, int[]> sessionsOf, IEnumerable<string> cut, int at) =>
    [
        .. cut.Distinct(StringComparer.Ordinal)
            .Where(code => parts.TryGetValue(code, out var places)
                && places.Length > 1
                && Array.BinarySearch(sessionsOf(places[0]), at) < 0
                && PlaceAt(parts, sessionsOf, code, at) >= 0)
            .Order(StringComparer.Ordinal),
    ];
}
