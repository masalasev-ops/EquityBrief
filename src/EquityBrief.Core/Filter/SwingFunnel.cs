using System.Globalization;

namespace EquityBrief.Core.Filter;

// The funnel: how many of the night's members each gate passed in order, each count the members
// passing that gate and every gate before it, then how many of those no exclusion removed. And each
// gate relaxed alone, how many pass every other gate and no exclusion, which is what the shape counts
// read to see what one gate is doing on its own.
public static class SwingFunnel
{
    // The members, then one count per gate in section 11's order, then the ones no exclusion removed.
    public static IReadOnlyList<int> Of(IReadOnlyCollection<GateResult> results)
    {
        var counts = new List<int> { results.Count };

        for (var gate = 0; gate < SwingGates.Order.Length; gate++)
        {
            var through = gate;
            counts.Add(results.Count(result => result.Gates.Take(through + 1).All(passed => passed.Passed)));
        }

        counts.Add(results.Count(result => result.Passed));

        return counts;
    }

    // For each gate in order, how many members pass every gate but that one, and no exclusion.
    public static IReadOnlyList<int> RelaxedAlone(IReadOnlyCollection<GateResult> results) =>
    [
        .. Enumerable.Range(0, SwingGates.Order.Length).Select(relaxed => results.Count(result =>
            result.Exclusions.Count == 0 && result.Gates.Where((_, at) => at != relaxed).All(gate => gate.Passed))),
    ];

    // The night's run log line: each step's count with what it removed, and the version it ran under.
    public static string Line(IReadOnlyList<int> funnel, int excluded, int passing, string version)
    {
        var parts = new List<string> { FormattableString.Invariant($"{funnel[0]} member(s)") };

        for (var gate = 0; gate < SwingGates.Order.Length; gate++)
        {
            parts.Add(FormattableString.Invariant($"{funnel[gate + 1]} through {SwingGates.Order[gate]} ({funnel[gate] - funnel[gate + 1]} removed)"));
        }

        parts.Add(FormattableString.Invariant($"{excluded} excluded"));
        parts.Add(FormattableString.Invariant($"{passing} passing"));
        parts.Add(version == "none" ? "no filter version open, so section 17's proposed values" : "filter version " + version);

        return string.Join(", ", parts);
    }
}
