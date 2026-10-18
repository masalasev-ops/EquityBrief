namespace EquityBrief.Core.Releases;

// One stored release a sample may draw: its accession, the index its filer's setups in the ledger mostly stand under,
// and the day in New York the SEC accepted it.
public sealed record SampleCandidate(string Accession, string Index, DateOnly AcceptedOn);

// The samples the release reading is measured over, each fixed before any is read: half accepted on or before the
// model's cutoff and half after it, each half shared among the indices in proportion to their members by the largest
// remainder, and each index's share drawn from its releases in accession order at one seed, so the same store draws
// the same sample on any machine.
// see: The release reading asks four closed questions written before any release is read, and code turns each answer into a reading
public static class ReleaseSamples
{
    public const int Seed = 20261018;

    // The probes' sample: re-identification and distraction over the same releases, and the twice-read.
    public const int ProbeCount = 200;

    // The reference set the build session labels.
    public const int ReferenceCount = 100;

    // Every sample draws from the releases the SEC accepted before this day in New York, the day the history was fetched,
    // with the members, the companies and the ledger's setups as they stood before it, so a release a slot stores later
    // moves no sample and a probe going on from where it stopped reads the sample it started.
    public static DateOnly Before { get; } = new(2026, 10, 18);

    public static IReadOnlyList<string> Draw(
        IReadOnlyList<SampleCandidate> candidates,
        int count,
        DateOnly cutoff,
        IReadOnlyDictionary<string, int> members,
        int seed = Seed)
    {
        var random = new Random(seed);
        var drawn = new List<string>();

        foreach (var before in new[] { true, false })
        {
            var half = candidates.Where(candidate => candidate.AcceptedOn <= cutoff == before).ToArray();
            var shares = Shares(count / 2, members);

            foreach (var (index, share) in shares.OrderBy(entry => entry.Key, StringComparer.Ordinal))
            {
                var pool = half
                    .Where(candidate => string.Equals(candidate.Index, index, StringComparison.Ordinal))
                    .Select(candidate => candidate.Accession)
                    .Order(StringComparer.Ordinal)
                    .ToArray();

                // A partial Fisher-Yates over the pool, its first places the draw.
                for (var at = 0; at < Math.Min(share, pool.Length); at++)
                {
                    var pick = at + random.Next(pool.Length - at);

                    (pool[at], pool[pick]) = (pool[pick], pool[at]);
                    drawn.Add(pool[at]);
                }
            }
        }

        return drawn;
    }

    // A count shared among the indices in proportion to their members: each its whole share, and what is left to the
    // largest remainders, a tie to the index first by its code.
    public static IReadOnlyDictionary<string, int> Shares(int count, IReadOnlyDictionary<string, int> members)
    {
        var total = members.Values.Sum();

        if (total == 0)
        {
            return new Dictionary<string, int>(StringComparer.Ordinal);
        }

        var exact = members.ToDictionary(entry => entry.Key, entry => 1.0 * count * entry.Value / total, StringComparer.Ordinal);
        var shares = exact.ToDictionary(entry => entry.Key, entry => (int)Math.Floor(entry.Value), StringComparer.Ordinal);
        var left = count - shares.Values.Sum();

        foreach (var index in exact.OrderByDescending(entry => entry.Value - Math.Floor(entry.Value)).ThenBy(entry => entry.Key, StringComparer.Ordinal).Select(entry => entry.Key).Take(left))
        {
            shares[index]++;
        }

        return shares;
    }
}
