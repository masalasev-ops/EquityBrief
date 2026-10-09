using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Ledger;

// One figure as the filed facts store keep it: the concept, the period, the value, the day it was first filed, its
// form and its accession.
public sealed record FiledFactRow(string Concept, DateOnly Start, DateOnly End, decimal Value, DateOnly Filed, string Form, string Accession);

// A setup's business readings as they stood: its filer's newest quarter's revenue against the same quarter a year
// before, how far that growth moved from the quarter before's, the gross and the operating margin against the same
// quarter a year before's, and the newest fiscal year's cash from operations over its net income where that income is
// above nothing. Each is none where the facts filed before the session do not state its parts.
public sealed record BusinessReading(
    double? RevenueGrowth,
    double? GrowthChange,
    double? GrossMarginChange,
    double? OperatingMarginChange,
    double? CashOverIncome);

// The SEC's facts as the setup ledger stores and reads them.
//
// Six measures, each the concepts a filer may state it under in the order one filing stating two is read by: revenue
// as the revenue rule orders it, gross profit, operating income, net income, cash from operations and interest
// expense. A filer's facts are stored for each concept, each period of a quarter, nine months or a year ending on or
// after the first day stored, as first filed: the earliest filing stating the period, and a later one stating it again
// changes nothing. A quarter is read by the revenue rule over the measure's own concepts, a fiscal fourth quarter
// being the year less its first nine months, and a reading takes only the facts filed before its session, so a
// filing made on the session is read from the session after.
// see: A quarter's revenue is read as first filed, a fiscal fourth quarter being the year less its first nine months
// see: The SEC's facts are stored as first filed in a table the night reads, and a setup's business readings read those filed before its session
public static class FiledFacts
{
    public static IReadOnlyList<string> Revenue => FirstFiledRevenue.Concepts;

    public static IReadOnlyList<string> GrossProfit { get; } = ["GrossProfit"];

    public static IReadOnlyList<string> OperatingIncome { get; } = ["OperatingIncomeLoss"];

    public static IReadOnlyList<string> NetIncome { get; } = ["NetIncomeLoss", "ProfitLoss"];

    public static IReadOnlyList<string> OperatingCash { get; } =
        ["NetCashProvidedByUsedInOperatingActivities", "NetCashProvidedByUsedInOperatingActivitiesContinuingOperations"];

    public static IReadOnlyList<string> InterestExpense { get; } =
        ["InterestExpense", "InterestExpenseNonoperating", "InterestExpenseDebt", "InterestAndDebtExpense"];

    public static IReadOnlyList<string> Concepts { get; } =
        [.. Revenue, .. GrossProfit, .. OperatingIncome, .. NetIncome, .. OperatingCash, .. InterestExpense];

    // The first period end stored: four years before the history the ledger is built over begins, enough for the
    // year before's quarter and the quarter before that of the history's first session.
    public static DateOnly FirstPeriodEnd { get; } = new(2015, 1, 1);

    // The rows a filer's facts store, one a concept a period, each as first filed.
    public static IReadOnlyList<FiledFactRow> Stored(IReadOnlyDictionary<string, IReadOnlyList<ConceptFact>> facts) =>
    [
        .. facts
            .Where(concept => Concepts.Contains(concept.Key, StringComparer.Ordinal))
            .SelectMany(concept => concept.Value
                .Where(fact => fact.End >= FirstPeriodEnd && Kept(fact.Start, fact.End))
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

    // A quarter, nine months or a year, the spans the revenue rule reads.
    public static bool Kept(DateOnly start, DateOnly end) =>
        FirstFiledRevenue.Days(start, end) is var days
        && ((days >= FirstFiledRevenue.QuarterFewest && days <= FirstFiledRevenue.QuarterMost)
            || (days >= FirstFiledRevenue.NineMonthsFewest && days <= FirstFiledRevenue.NineMonthsMost)
            || (days >= FirstFiledRevenue.YearFewest && days <= FirstFiledRevenue.YearMost));

    public static BusinessReading Read(IReadOnlyList<FiledFactRow> facts, DateOnly session)
    {
        var known = facts
            .Where(fact => fact.Filed < session)
            .Select(fact => new FiledRevenue(fact.Concept, fact.Start, fact.End, fact.Value, fact.Filed, fact.Form, fact.Accession))
            .ToArray();

        var revenue = FirstFiledRevenue.Quarters(known, Revenue);
        var gross = FirstFiledRevenue.Quarters(known, GrossProfit);
        var operating = FirstFiledRevenue.Quarters(known, OperatingIncome);

        var latest = revenue.Count > 0 ? revenue[^1] : null;
        var yearBefore = latest is null ? null : YearBefore(revenue, latest);
        var growth = Growth(latest, yearBefore);

        var previous = latest is null
            ? null
            : revenue.LastOrDefault(quarter => Math.Abs(quarter.End.DayNumber - latest.Start.AddDays(-1).DayNumber) <= RevenueGrowth.YearBeforeWithin);
        var previousGrowth = previous is null ? null : Growth(previous, YearBefore(revenue, previous));

        return new BusinessReading(
            growth,
            growth is { } now && previousGrowth is { } then ? now - then : null,
            MarginChange(gross, latest, yearBefore),
            MarginChange(operating, latest, yearBefore),
            CashOverIncome(known));
    }

    static QuarterRevenue? YearBefore(IReadOnlyList<QuarterRevenue> quarters, QuarterRevenue quarter)
    {
        var target = quarter.End.AddYears(-1);

        return quarters
            .Where(one => Math.Abs(one.End.DayNumber - target.DayNumber) <= RevenueGrowth.YearBeforeWithin)
            .OrderBy(one => Math.Abs(one.End.DayNumber - target.DayNumber))
            .FirstOrDefault();
    }

    static double? Growth(QuarterRevenue? quarter, QuarterRevenue? yearBefore) =>
        quarter is not null && yearBefore is not null && yearBefore.Value > 0m
            ? Statistic.FromRatio((quarter.Value / yearBefore.Value) - 1m)
            : null;

    // A margin is the measure's quarter over the revenue quarter of the same period.
    static double? MarginChange(IReadOnlyList<QuarterRevenue> measure, QuarterRevenue? latest, QuarterRevenue? yearBefore) =>
        Margin(measure, latest) is { } now && Margin(measure, yearBefore) is { } then ? now - then : null;

    static double? Margin(IReadOnlyList<QuarterRevenue> measure, QuarterRevenue? revenue) =>
        revenue is not null && revenue.Value > 0m
        && measure.FirstOrDefault(quarter => quarter.Start == revenue.Start && quarter.End == revenue.End) is { } part
            ? Statistic.FromRatio(part.Value / revenue.Value)
            : null;

    // The newest fiscal year's cash from operations over its net income, both the year as first filed under the
    // measure's concepts in order, read only where the year's net income is above nothing.
    static double? CashOverIncome(IReadOnlyList<FiledRevenue> known)
    {
        var income = Years(known, NetIncome);

        if (income.Count == 0)
        {
            return null;
        }

        var newest = income[^1];
        var cash = Years(known, OperatingCash).FirstOrDefault(year => year.Start == newest.Start && year.End == newest.End);

        return newest.Value > 0m && cash is not null ? Statistic.FromRatio(cash.Value / newest.Value) : null;
    }

    static IReadOnlyList<FiledRevenue> Years(IReadOnlyList<FiledRevenue> known, IReadOnlyList<string> order) =>
    [
        .. known
            .Where(fact => order.Contains(fact.Concept, StringComparer.Ordinal)
                && FirstFiledRevenue.Spans(fact, FirstFiledRevenue.YearFewest, FirstFiledRevenue.YearMost))
            .GroupBy(fact => (fact.Start, fact.End))
            .Select(period => period
                .OrderBy(fact => fact.Filed)
                .ThenBy(fact => IndexOf(order, fact.Concept))
                .ThenBy(fact => fact.Accession, StringComparer.Ordinal)
                .First())
            .OrderBy(fact => fact.End),
    ];

    static int IndexOf(IReadOnlyList<string> order, string concept)
    {
        for (var at = 0; at < order.Count; at++)
        {
            if (string.Equals(order[at], concept, StringComparison.Ordinal))
            {
                return at;
            }
        }

        return order.Count;
    }
}
