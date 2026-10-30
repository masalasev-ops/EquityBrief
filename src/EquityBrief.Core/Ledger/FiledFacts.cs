using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Quarters;

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
// expense; and beside them the share count a filing's cover states, for the day it stands for. A filer's facts are
// stored for each concept, each period of a quarter, nine months or a year ending on or after the first day stored and
// each count's day, as first filed: the earliest filing stating the period or the day, and a later one stating it
// again changes nothing. A quarter is read by the revenue rule over the measure's own concepts, a fiscal fourth quarter
// being the year less its first nine months, and a reading takes only the facts filed before its session, so a
// filing made on the session is read from the session after. From 22.8 the fundamentals scorecard's lines join them:
// the cost of sales beside gross profit, every measure's six months, which a second quarter's cash flow statement
// states and no other span gives, and four lines of the balance sheet, each stored for the day it stands for.
// see: A quarter's revenue is read as first filed, a fiscal fourth quarter being the year less its first nine months
// see: The SEC's facts are stored as first filed in a table the night reads, and a setup's business readings read those filed before its session
// see: The fundamentals scorecard scores every member on nine checks of its filings as first filed, and only the quality family's rule reads it
public static class FiledFacts
{
    public static IReadOnlyList<string> Revenue => FirstFiledRevenue.Concepts;

    public static IReadOnlyList<string> GrossProfit { get; } = ["GrossProfit"];

    // From 22.8 the cost of sales, which with revenue stands for gross profit where a filer states none.
    public static IReadOnlyList<string> CostOfSales { get; } = ["CostOfRevenue", "CostOfGoodsAndServicesSold", "CostOfGoodsSold"];

    // From 22.8 the balance sheet's four lines, each stated for the day it stands for: long-term debt under the concept
    // holding the whole of it, the current maturities with it as Piotroski (2000) reads it, first, then with leases, then
    // the part past a year alone.
    public static IReadOnlyList<string> TotalAssets { get; } = ["Assets"];

    public static IReadOnlyList<string> CurrentAssets { get; } = ["AssetsCurrent"];

    public static IReadOnlyList<string> CurrentLiabilities { get; } = ["LiabilitiesCurrent"];

    public static IReadOnlyList<string> LongTermDebt { get; } =
        ["LongTermDebt", "LongTermDebtAndCapitalLeaseObligationsIncludingCurrentMaturities", "LongTermDebtNoncurrent", "LongTermDebtAndCapitalLeaseObligations"];

    // The concepts stated in dollars for a day rather than a period.
    public static IReadOnlyList<string> OnADay { get; } = [.. TotalAssets, .. CurrentAssets, .. CurrentLiabilities, .. LongTermDebt];

    public static IReadOnlyList<string> OperatingIncome { get; } = ["OperatingIncomeLoss"];

    public static IReadOnlyList<string> NetIncome { get; } = ["NetIncomeLoss", "ProfitLoss"];

    public static IReadOnlyList<string> OperatingCash { get; } =
        ["NetCashProvidedByUsedInOperatingActivities", "NetCashProvidedByUsedInOperatingActivitiesContinuingOperations"];

    public static IReadOnlyList<string> InterestExpense { get; } =
        ["InterestExpense", "InterestExpenseNonoperating", "InterestExpenseDebt", "InterestAndDebtExpense"];

    // The share count a filing's cover states, kept for the day it stands for beside the six measures, which the
    // valuation reading reads.
    // see: The valuation reading reads the prices before the store's year at the store's scale from the history pulled for those years
    public static IReadOnlyList<string> SharesOutstanding => ConceptAnswers.EntityCounts;

    public static IReadOnlyList<string> Concepts { get; } =
    [
        .. Revenue, .. GrossProfit, .. OperatingIncome, .. NetIncome, .. OperatingCash, .. InterestExpense, .. SharesOutstanding,
        .. CostOfSales, .. OnADay,
    ];

    // The first period end stored: four years before the history the ledger is built over begins, enough for the
    // year before's quarter and the quarter before that of the history's first session.
    public static DateOnly FirstPeriodEnd { get; } = new(2015, 1, 1);

    // The days six months span, its first and last both counted: wide enough for a fiscal calendar of 52 and 53 weeks,
    // apart from a quarter's and nine months'.
    public const int SixMonthsFewest = 170;
    public const int SixMonthsMost = 195;

    // The rows a filer's facts store, one a concept a period, each as first filed.
    public static IReadOnlyList<FiledFactRow> Stored(IReadOnlyDictionary<string, IReadOnlyList<ConceptFact>> facts) =>
    [
        .. facts
            .Where(concept => Concepts.Contains(concept.Key, StringComparer.Ordinal))
            .SelectMany(concept => concept.Value
                .Where(fact => fact.End >= FirstPeriodEnd
                    && (SharesOutstanding.Contains(concept.Key, StringComparer.Ordinal) || OnADay.Contains(concept.Key, StringComparer.Ordinal)
                        ? fact.Start == fact.End
                        : Kept(fact.Start, fact.End)))
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

    // A quarter, nine months or a year, the spans the revenue rule reads, and from 22.8 six months, which the
    // scorecard's four quarters read and the revenue rule does not.
    public static bool Kept(DateOnly start, DateOnly end) =>
        FirstFiledRevenue.Days(start, end) is var days
        && ((days >= FirstFiledRevenue.QuarterFewest && days <= FirstFiledRevenue.QuarterMost)
            || (days >= SixMonthsFewest && days <= SixMonthsMost)
            || (days >= FirstFiledRevenue.NineMonthsFewest && days <= FirstFiledRevenue.NineMonthsMost)
            || (days >= FirstFiledRevenue.YearFewest && days <= FirstFiledRevenue.YearMost));

    public static BusinessReading Read(IReadOnlyList<FiledFactRow> facts, DateOnly session)
    {
        var known = Known(facts, session);
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

    // From 21.4 the business's state off the SEC's facts as first filed before the session, by the rule the night reads
    // the provider's quarters by, for the swing filter's version leaving off a deteriorating business: over the two
    // newest filed quarters, each against the same quarter a year earlier, improving where both grew their sales and
    // widened their operating margin, deteriorating where both shrank their sales and narrowed their margin, steady
    // otherwise; none where fewer than two quarters hold all four figures, which the version fires on as it fires on a
    // state the night did not read.
    // see: The seventh swing family candidate leaves off a member whose business the SEC's facts as first filed read deteriorating through the ledger's catalogue, and the nine are registered again at 21.4 with the eight unchanged versions keeping their records
    public static string? StateOf(IReadOnlyList<FiledFactRow> facts, DateOnly session)
    {
        var known = Known(facts, session);
        var revenue = FirstFiledRevenue.Quarters(known, Revenue);
        var operating = FirstFiledRevenue.Quarters(known, OperatingIncome);

        if (revenue.Count < QuarterReadings.TrajectoryQuarters)
        {
            return null;
        }

        var quarters = revenue
            .TakeLast(QuarterReadings.TrajectoryQuarters)
            .Select(quarter => YearBefore(revenue, quarter) is { } yearBefore
                ? (Growth: Growth(quarter, yearBefore), Margin: Margin(operating, quarter), Earlier: Margin(operating, yearBefore))
                : (Growth: (double?)null, Margin: (double?)null, Earlier: (double?)null))
            .ToArray();

        if (quarters.Any(quarter => quarter.Growth is null || quarter.Margin is null || quarter.Earlier is null))
        {
            return null;
        }

        if (quarters.All(quarter => quarter.Growth > 0 && quarter.Margin > quarter.Earlier))
        {
            return FundamentalState.Improving;
        }

        return quarters.All(quarter => quarter.Growth < 0 && quarter.Margin < quarter.Earlier)
            ? FundamentalState.Deteriorating
            : FundamentalState.Steady;
    }

    // The facts filed before a session, as the revenue rule reads them.
    static FiledRevenue[] Known(IReadOnlyList<FiledFactRow> facts, DateOnly session) =>
    [
        .. facts
            .Where(fact => fact.Filed < session)
            .Select(fact => new FiledRevenue(fact.Concept, fact.Start, fact.End, fact.Value, fact.Filed, fact.Form, fact.Accession)),
    ];

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
