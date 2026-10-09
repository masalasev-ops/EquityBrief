namespace EquityBrief.Core.Families;

// One revenue figure as one filing states it: the concept it was filed under, the period it covers, the value, the
// day the filing was made, its form and its accession.
public sealed record FiledRevenue(string Concept, DateOnly Start, DateOnly End, decimal Value, DateOnly Filed, string Form, string Accession);

// One quarter's revenue as first filed: the period, the value, the day it was first filed, the concept it was filed
// under, and whether it is a fiscal year less its first nine months.
public sealed record QuarterRevenue(DateOnly Start, DateOnly End, decimal Value, DateOnly Filed, string Concept, bool YearLessNineMonths);

// A quarter's revenue as the first filing stating it states it.
//
// The archive keeps every filing's value with the day it was filed, so a quarter a later filing states again, as the
// year before's column of a later quarter or restated, is read as it was first filed and never as it was last. A
// fiscal fourth quarter is stated by most filers in no filing of its own, the annual report stating the year, so it is
// the year less its first nine months, both as first filed under one concept, and first filed the day the year was.
// Where one filing states a quarter under two concepts, the earlier in the order below is read, the one stating the
// whole of the revenue before one stating a part of it: a bank's revenue net of its interest expense, then revenue,
// then revenue from contracts with customers, then a utility's operating revenue, then the concept retired in 2018.
// see: A quarter's revenue is read as first filed, a fiscal fourth quarter being the year less its first nine months
public static class FirstFiledRevenue
{
    public static IReadOnlyList<string> Concepts { get; } =
    [
        "RevenuesNetOfInterestExpense",
        "Revenues",
        "RevenueFromContractWithCustomerExcludingAssessedTax",
        "RevenueFromContractWithCustomerIncludingAssessedTax",
        "RegulatedAndUnregulatedOperatingRevenue",
        "SalesRevenueNet",
    ];

    // The days a period spans, its first and last both counted, that make it a quarter, nine months or a year: wide
    // enough for a fiscal calendar of 52 and 53 weeks, apart enough that none is read as another.
    public const int QuarterFewest = 80;
    public const int QuarterMost = 100;
    public const int NineMonthsFewest = 260;
    public const int NineMonthsMost = 285;
    public const int YearFewest = 350;
    public const int YearMost = 380;

    public static int Days(DateOnly start, DateOnly end) => end.DayNumber - start.DayNumber + 1;

    // Every quarter the facts state or give as a year less its nine months, one a period, each as first filed: the
    // earliest filing, a quarter stated before one worked out on the same day, then the concept's place in the order.
    public static IReadOnlyList<QuarterRevenue> Quarters(IEnumerable<FiledRevenue> facts) => Quarters(facts, Concepts);

    // The same rule over another measure's concepts, in that measure's order: the setup ledger reads gross profit,
    // operating income and the rest of a filer's quarters by it, each as first filed.
    public static IReadOnlyList<QuarterRevenue> Quarters(IEnumerable<FiledRevenue> facts, IReadOnlyList<string> order)
    {
        var read = facts.Where(fact => Place(fact.Concept, order) >= 0).ToArray();
        var candidates = new List<QuarterRevenue>();

        foreach (var period in read.Where(fact => Spans(fact, QuarterFewest, QuarterMost)).GroupBy(fact => (fact.Start, fact.End)))
        {
            var first = FirstOf(period, order);

            candidates.Add(new QuarterRevenue(period.Key.Start, period.Key.End, first.Value, first.Filed, first.Concept, false));
        }

        foreach (var concept in read.GroupBy(fact => fact.Concept, StringComparer.Ordinal))
        {
            var nines = concept
                .Where(fact => Spans(fact, NineMonthsFewest, NineMonthsMost))
                .GroupBy(fact => (fact.Start, fact.End))
                .Select(period => FirstOf(period, order))
                .ToArray();

            foreach (var year in concept.Where(fact => Spans(fact, YearFewest, YearMost)).GroupBy(fact => (fact.Start, fact.End)).Select(period => FirstOf(period, order)))
            {
                var nine = nines
                    .Where(fact => fact.Start == year.Start && fact.End < year.End)
                    .OrderByDescending(fact => fact.End)
                    .FirstOrDefault();

                if (nine is not null)
                {
                    candidates.Add(new QuarterRevenue(
                        nine.End.AddDays(1),
                        year.End,
                        year.Value - nine.Value,
                        year.Filed > nine.Filed ? year.Filed : nine.Filed,
                        concept.Key,
                        true));
                }
            }
        }

        return
        [
            .. candidates
                .GroupBy(quarter => (quarter.Start, quarter.End))
                .Select(period => period
                    .OrderBy(quarter => quarter.Filed)
                    .ThenBy(quarter => quarter.YearLessNineMonths)
                    .ThenBy(quarter => Place(quarter.Concept, order))
                    .First())
                .OrderBy(quarter => quarter.End),
        ];
    }

    static int Place(string concept, IReadOnlyList<string> order)
    {
        for (var at = 0; at < order.Count; at++)
        {
            if (string.Equals(order[at], concept, StringComparison.Ordinal))
            {
                return at;
            }
        }

        return -1;
    }

    public static bool Spans(FiledRevenue fact, int fewest, int most) => Days(fact.Start, fact.End) is var days && days >= fewest && days <= most;

    static FiledRevenue FirstOf(IEnumerable<FiledRevenue> period, IReadOnlyList<string> order) =>
        period
            .OrderBy(fact => fact.Filed)
            .ThenBy(fact => Place(fact.Concept, order))
            .ThenBy(fact => fact.Accession, StringComparer.Ordinal)
            .First();
}
