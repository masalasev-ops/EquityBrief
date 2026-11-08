using EquityBrief.Core.Ledger;
using EquityBrief.Core.Providers;

namespace EquityBrief.Core.History;

// The long history's facts: each filer's figures for the periods ending before the standing facts' first, kept as the
// standing table keeps its own, one a concept a period as first filed, read under the standing table's concepts and
// spans, so a period the two tables meet at is read the same way on both sides of it.
// see: The long history's facts are the SEC's figures before the standing table's first, kept as first filed apart from it
public static class LongFacts
{
    // The day the standing table's periods start from: the long table keeps every period ending before it.
    public static DateOnly Before => FiledFacts.FirstPeriodEnd;

    // The rows a filer's facts store in the long table, one a concept a period, each as first filed: the earliest filing
    // stating the period, a tie on the day read by the accession, and a later one stating it again changing nothing.
    public static IReadOnlyList<FiledFactRow> Stored(IReadOnlyDictionary<string, IReadOnlyList<ConceptFact>> facts) =>
    [
        .. facts
            .Where(concept => FiledFacts.Concepts.Contains(concept.Key, StringComparer.Ordinal))
            .SelectMany(concept => concept.Value
                .Where(fact => fact.End < Before
                    && (FiledFacts.SharesOutstanding.Contains(concept.Key, StringComparer.Ordinal) || FiledFacts.OnADay.Contains(concept.Key, StringComparer.Ordinal)
                        ? fact.Start == fact.End
                        : FiledFacts.Kept(fact.Start, fact.End)))
                .GroupBy(fact => (fact.Start, fact.End))
                .Select(period => period
                    .OrderBy(fact => fact.Filed)
                    .ThenBy(fact => fact.Accession, StringComparer.Ordinal)
                    .First())
                .Select(fact => new FiledFactRow(concept.Key, fact.Start, fact.End, fact.Value, fact.Filed, fact.Form, fact.Accession)))
            .OrderBy(row => row.Concept, StringComparer.Ordinal)
            .ThenBy(row => row.End)
            .ThenBy(row => row.Start),
    ];
}
