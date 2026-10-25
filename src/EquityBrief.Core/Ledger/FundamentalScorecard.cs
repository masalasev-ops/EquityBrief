using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Ledger;

// What one check of the scorecard answered: passed, failed, not read where a figure it needs is not filed, or not
// applying, a financial company's current ratio and gross margin.
public enum CheckAnswer
{
    Passed,
    Failed,
    NotFiled,
    NotApplying,
}

// The word a scorecard reads as, from the share of the checks that applied which it passed.
public enum ScorecardWord
{
    Strong,
    Mixed,
    Weak,
}

// One check's answer and what it compared: the dollars or the shares where it weighs amounts, the ratio on the newest
// quarter and on the same quarter a year before where it weighs a ratio against its own, and the line not filed where it
// could not be read.
public sealed record ScorecardCheck(
    int Number,
    CheckAnswer Answer,
    decimal? Amount = null,
    decimal? Against = null,
    double? Now = null,
    double? YearBefore = null,
    string? Missing = null);

// A member's scorecard on a session: the newest quarter its four quarters run to and the quarter a year before it,
// whether it is a financial company, the nine checks in their order, and its gross profit over its total assets.
public sealed record Scorecard(DateOnly? Quarter, DateOnly? YearBefore, bool Financial, IReadOnlyList<ScorecardCheck> Checks, double? GrossProfitability)
{
    public int Passed => Checks.Count(check => check.Answer == CheckAnswer.Passed);

    // The checks that applied and were read, the score's own count.
    public int Applied => Checks.Count(check => check.Answer is CheckAnswer.Passed or CheckAnswer.Failed);

    public ScorecardWord? Word => FundamentalScorecard.WordOf(Passed, Applied);
}

// Nine checks of a member's fundamentals after Piotroski (2000), over its filer's facts as first filed before the
// session, so a filing made on the session is read from the session after: the four newest quarters against the four
// before them. A measure's four quarters to a quarter's end are its year to date, the longest span the filings state
// ending that day, plus the fiscal year before less the same span a year earlier, and a fiscal year's end reads its
// year; a balance sheet's line is the figure stated for the quarter's end itself. Profitability: (1) net income above
// nothing, (2) cash from operations above nothing, (3) return on assets, the net income over the total assets at the
// start of the four quarters, higher than a year before and (4) cash from operations above net income. The balance
// sheet: (5) long-term debt over the year's average total assets lower than a year before, read under one concept at
// both ends, the whole of it with its current maturities first, (6)
// current assets over current liabilities higher than a year before and (7) no new shares, the newest count the covers
// state at most the tolerance above the one standing a year before it, the older brought across each split the closes
// show between them, a session whose close as traded over its close at the store's scale steps by the split step or
// more, so neither a split nor a dividend moves it. Efficiency: (8) the gross margin, gross profit over sales with sales less the
// cost of sales standing for gross profit where none is filed, higher than a year before and (9) sales over the total
// assets at the start of the four quarters higher than a year before. A financial company is scored on the seven that
// apply to it; any other passes over a check only where a line it needs is not filed; and the word and a floor are read
// as shares of the checks that applied, with no word under five.
// see: The fundamentals scorecard scores every member on nine checks of its filings as first filed, and only the quality family's rule reads it
public static class FundamentalScorecard
{
    public const int Checks = 9;

    // The fewest checks read that give a word.
    public const int FewestRead = 5;

    // The word's cut points, stated in nine and read as the same share of the checks that applied.
    public const int StrongInNine = 8;
    public const int MixedInNine = 5;

    // The most the newest share count may stand above the one a year before it and still pass, a share: the shares a
    // year's grants to staff add pass, and an offering does not.
    public const double ShareTolerance = 0.02;

    // The days the year-before count may stand from a year before the newest count's day.
    public const int CountYearWithin = 45;

    // A session's close as traded over its close at the store's scale moves by this ratio or more from the session
    // before's only at a split, whose step the share check brings the older count across; a dividend's is far smaller.
    public const double SplitStep = 1.4;

    // The checks a financial company is not scored on: the current ratio's and the gross margin's.
    public static IReadOnlyList<int> NotForFinancials { get; } = [6, 8];

    // The lines a check names where it could not be read.
    public const string NetIncomeLine = "net income";
    public const string CashLine = "cash from operations";
    public const string AssetsLine = "total assets";
    public const string DebtLine = "long-term debt";
    public const string CurrentAssetsLine = "current assets";
    public const string CurrentLiabilitiesLine = "current liabilities";
    public const string SharesLine = "share count";
    public const string SalesLine = "sales";
    public const string GrossProfitLine = "gross profit";

    public static ScorecardWord? WordOf(int passed, int applied) =>
        applied < FewestRead ? null
        : passed * Checks >= StrongInNine * applied ? ScorecardWord.Strong
        : passed * Checks >= MixedInNine * applied ? ScorecardWord.Mixed
        : ScorecardWord.Weak;

    // Whether a score clears a floor stated in nine, read as the same share of the checks that applied, and never under
    // the checks a word needs.
    public static bool Clears(int passed, int applied, int inNine) => applied >= FewestRead && passed * Checks >= inNine * applied;

    // A financial company: one GICS files in its Financials sector on the session.
    public static bool IsFinancial(string? sector) => string.Equals(sector, GicsSectors.Financials, StringComparison.Ordinal);

    // A member's scorecard on a session from its filer's facts and its prices, the closes the share check reads.
    public static Scorecard Read(IReadOnlyList<FiledFactRow> facts, DateOnly session, bool financial, PriceHistory prices)
    {
        var known = facts.Where(fact => fact.Filed < session).ToArray();
        var quarter = NewestQuarter(known);
        var yearBefore = quarter is { } end ? QuarterNear(known, end.AddYears(-1)) : null;
        var twoBefore = yearBefore is { } then ? QuarterNear(known, then.AddYears(-1)) : null;

        decimal? Four(IReadOnlyList<string> order, DateOnly? end) => end is { } day ? FourQuarters(known, order, day) : null;
        decimal? On(IReadOnlyList<string> order, DateOnly? day) => day is { } at ? OnTheDay(known, order, at) : null;

        var income = Four(FiledFacts.NetIncome, quarter);
        var incomeBefore = Four(FiledFacts.NetIncome, yearBefore);
        var cash = Four(FiledFacts.OperatingCash, quarter);
        var sales = Four(FiledFacts.Revenue, quarter);
        var salesBefore = Four(FiledFacts.Revenue, yearBefore);
        var gross = GrossProfit(known, quarter, sales);
        var grossBefore = GrossProfit(known, yearBefore, salesBefore);
        var assets = On(FiledFacts.TotalAssets, quarter);
        var assetsBefore = On(FiledFacts.TotalAssets, yearBefore);
        var assetsTwoBefore = On(FiledFacts.TotalAssets, twoBefore);

        var checks = new List<ScorecardCheck>
        {
            income is { } one ? new(1, one > 0m ? CheckAnswer.Passed : CheckAnswer.Failed, Amount: one) : new(1, CheckAnswer.NotFiled, Missing: NetIncomeLine),
            cash is { } two ? new(2, two > 0m ? CheckAnswer.Passed : CheckAnswer.Failed, Amount: two) : new(2, CheckAnswer.NotFiled, Missing: CashLine),
            Against(3, Ratio(income, assetsBefore), Ratio(incomeBefore, assetsTwoBefore), higher: true, income is null || incomeBefore is null ? NetIncomeLine : AssetsLine),
            cash is { } flow && income is { } earned
                ? new(4, flow > earned ? CheckAnswer.Passed : CheckAnswer.Failed, Amount: flow, Against: earned)
                : new(4, CheckAnswer.NotFiled, Missing: cash is null ? CashLine : NetIncomeLine),
            Debt(known, quarter, yearBefore, Average(assets, assetsBefore), Average(assetsBefore, assetsTwoBefore)),
            financial
                ? new(6, CheckAnswer.NotApplying)
                : Against(
                    6,
                    Ratio(On(FiledFacts.CurrentAssets, quarter), On(FiledFacts.CurrentLiabilities, quarter)),
                    Ratio(On(FiledFacts.CurrentAssets, yearBefore), On(FiledFacts.CurrentLiabilities, yearBefore)),
                    higher: true,
                    On(FiledFacts.CurrentAssets, quarter) is null || On(FiledFacts.CurrentAssets, yearBefore) is null ? CurrentAssetsLine : CurrentLiabilitiesLine),
            Shares(known, session, prices),
            financial
                ? new(8, CheckAnswer.NotApplying)
                : Against(8, Ratio(gross, sales), Ratio(grossBefore, salesBefore), higher: true, sales is null || salesBefore is null ? SalesLine : GrossProfitLine),
            Against(9, Ratio(sales, assetsBefore), Ratio(salesBefore, assetsTwoBefore), higher: true, sales is null || salesBefore is null ? SalesLine : AssetsLine),
        };

        return new Scorecard(quarter, yearBefore, financial, checks, Ratio(gross, assets));
    }

    // A ratio weighed against its own a year before, higher or lower passing; not read where either is not.
    static ScorecardCheck Against(int number, double? now, double? before, bool higher, string missing) =>
        now is { } current && before is { } then
            ? new(number, (higher ? current > then : current < then) ? CheckAnswer.Passed : CheckAnswer.Failed, Now: current, YearBefore: then)
            : new(number, CheckAnswer.NotFiled, Missing: missing);

    // Long-term debt at the quarter's end over the year's average total assets against the same a year before, the debt
    // read under the first concept in its order stated at both ends.
    static ScorecardCheck Debt(IReadOnlyList<FiledFactRow> known, DateOnly? quarter, DateOnly? yearBefore, decimal? assets, decimal? assetsBefore)
    {
        if (quarter is not { } end || yearBefore is not { } then)
        {
            return new(5, CheckAnswer.NotFiled, Missing: DebtLine);
        }

        foreach (var concept in FiledFacts.LongTermDebt)
        {
            if (OnTheDay(known, [concept], end) is { } debt && OnTheDay(known, [concept], then) is { } debtBefore)
            {
                return Against(5, Ratio(debt, assets), Ratio(debtBefore, assetsBefore), higher: false, AssetsLine);
            }
        }

        return new(5, CheckAnswer.NotFiled, Missing: DebtLine);
    }

    // The newest count the covers state for a day before the session against the count standing nearest a year before
    // it, the older brought across every split between them; not read where either count is not held.
    static ScorecardCheck Shares(IReadOnlyList<FiledFactRow> known, DateOnly session, PriceHistory prices)
    {
        var counts = known
            .Where(fact => FiledFacts.SharesOutstanding.Contains(fact.Concept, StringComparer.Ordinal) && fact.End < session)
            .GroupBy(fact => fact.End)
            .Select(day => FirstOf(day, FiledFacts.SharesOutstanding))
            .OrderBy(fact => fact.End)
            .ToArray();

        if (counts.Length == 0)
        {
            return new(7, CheckAnswer.NotFiled, Missing: SharesLine);
        }

        var newest = counts[^1];
        var target = newest.End.AddYears(-1);
        var before = counts
            .Where(fact => Math.Abs(fact.End.DayNumber - target.DayNumber) <= CountYearWithin)
            .OrderBy(fact => Math.Abs(fact.End.DayNumber - target.DayNumber))
            .FirstOrDefault();

        if (before is null || newest.Value <= 0m || before.Value <= 0m)
        {
            return new(7, CheckAnswer.NotFiled, Missing: SharesLine);
        }

        var ratio = Statistic.FromRatio(newest.Value / before.Value) * SplitsBetween(prices, before.End, newest.End);

        return new(7, ratio <= 1 + ShareTolerance ? CheckAnswer.Passed : CheckAnswer.Failed, Amount: newest.Value, Against: before.Value, Now: ratio - 1);
    }

    // The product of the steps of every split between two days: on each session after the first day and to the second,
    // its close as traded over its close at the store's scale against the same on the session before, where that moves
    // by the split step or more. One where none stands between them, and a split before the first session the prices
    // hold is not seen.
    public static double SplitsBetween(PriceHistory prices, DateOnly from, DateOnly to)
    {
        var product = 1.0;

        for (var at = 1; at < prices.Sessions.Length && prices.Sessions[at] <= to; at++)
        {
            if (prices.Sessions[at] <= from
                || prices.Closes[at - 1] <= 0 || prices.Traded[at - 1] <= 0 || prices.Closes[at] <= 0 || prices.Traded[at] <= 0)
            {
                continue;
            }

            var step = prices.Traded[at] / prices.Closes[at] / (prices.Traded[at - 1] / prices.Closes[at - 1]);

            if (step >= SplitStep || step <= 1 / SplitStep)
            {
                product *= step;
            }
        }

        return product;
    }

    // Gross profit's four quarters to a quarter, or sales less the cost of sales where the filings state no gross profit.
    static decimal? GrossProfit(IReadOnlyList<FiledFactRow> known, DateOnly? quarter, decimal? sales) =>
        quarter is not { } end ? null
        : FourQuarters(known, FiledFacts.GrossProfit, end) is { } gross ? gross
        : sales is { } revenue && FourQuarters(known, FiledFacts.CostOfSales, end) is { } cost ? revenue - cost
        : null;

    static double? Ratio(decimal? part, decimal? whole) =>
        part is { } top && whole is { } bottom && bottom > 0m ? Statistic.FromRatio(top / bottom) : null;

    // The mean of a line at a year's two ends, none where either is not filed.
    static decimal? Average(decimal? end, decimal? start) => end is { } one && start is { } other ? (one + other) / 2m : null;

    // The newest quarter's end: the newest end of a span of net income the filings state.
    static DateOnly? NewestQuarter(IReadOnlyList<FiledFactRow> known) =>
        known
            .Where(fact => FiledFacts.NetIncome.Contains(fact.Concept, StringComparer.Ordinal) && SpanOf(fact) is not null)
            .Select(fact => (DateOnly?)fact.End)
            .Max();

    // The end of a span of net income nearest a day, within the days the revenue rule lets a quarter's end move.
    static DateOnly? QuarterNear(IReadOnlyList<FiledFactRow> known, DateOnly day) =>
        known
            .Where(fact => FiledFacts.NetIncome.Contains(fact.Concept, StringComparer.Ordinal) && SpanOf(fact) is not null
                && Math.Abs(fact.End.DayNumber - day.DayNumber) <= RevenueGrowth.YearBeforeWithin)
            .OrderBy(fact => Math.Abs(fact.End.DayNumber - day.DayNumber))
            .Select(fact => (DateOnly?)fact.End)
            .FirstOrDefault();

    // A measure's four quarters to a quarter's end as first filed: the year to date plus the fiscal year before less the
    // same span a year earlier, or the year itself at a fiscal year's end; none where a part is not filed.
    public static decimal? FourQuarters(IReadOnlyList<FiledFactRow> known, IReadOnlyList<string> order, DateOnly end)
    {
        var ending = known
            .Where(fact => order.Contains(fact.Concept, StringComparer.Ordinal) && fact.End == end && SpanOf(fact) is not null)
            .ToArray();

        if (ending.Length == 0)
        {
            return null;
        }

        var longest = ending.Max(fact => SpanOf(fact)!.Value);
        var toDate = FirstOf(ending.Where(fact => SpanOf(fact) == longest), order);

        if (longest == Span.Year)
        {
            return toDate.Value;
        }

        var yearBefore = FirstWhere(known, order, fact => SpanOf(fact) == Span.Year && Near(fact.End, toDate.Start.AddDays(-1)));
        var sameBefore = yearBefore is null
            ? null
            : FirstWhere(known, order, fact => SpanOf(fact) == longest && Near(fact.Start, yearBefore.Start) && Near(fact.End, end.AddYears(-1)));

        return yearBefore is null || sameBefore is null ? null : toDate.Value + yearBefore.Value - sameBefore.Value;
    }

    // A balance sheet line's figure for a day as first filed, under the first concept in its order stated for it.
    public static decimal? OnTheDay(IReadOnlyList<FiledFactRow> known, IReadOnlyList<string> order, DateOnly day) =>
        FirstWhere(known, order, fact => fact.Start == fact.End && fact.End == day)?.Value;

    enum Span
    {
        Quarter,
        Six,
        Nine,
        Year,
    }

    static Span? SpanOf(FiledFactRow fact) =>
        FirstFiledRevenue.Days(fact.Start, fact.End) switch
        {
            >= FirstFiledRevenue.QuarterFewest and <= FirstFiledRevenue.QuarterMost => Span.Quarter,
            >= FiledFacts.SixMonthsFewest and <= FiledFacts.SixMonthsMost => Span.Six,
            >= FirstFiledRevenue.NineMonthsFewest and <= FirstFiledRevenue.NineMonthsMost => Span.Nine,
            >= FirstFiledRevenue.YearFewest and <= FirstFiledRevenue.YearMost => Span.Year,
            _ => null,
        };

    static bool Near(DateOnly one, DateOnly other) => Math.Abs(one.DayNumber - other.DayNumber) <= RevenueGrowth.YearBeforeWithin;

    static FiledFactRow? FirstWhere(IReadOnlyList<FiledFactRow> known, IReadOnlyList<string> order, Func<FiledFactRow, bool> holds)
    {
        var held = known.Where(fact => order.Contains(fact.Concept, StringComparer.Ordinal) && holds(fact)).ToArray();

        return held.Length == 0 ? null : FirstOf(held, order);
    }

    // The figure first filed among several for one period or day: the earliest filing, then the concept's place in the
    // measure's order, then the accession.
    static FiledFactRow FirstOf(IEnumerable<FiledFactRow> facts, IReadOnlyList<string> order) =>
        facts
            .OrderBy(fact => fact.Filed)
            .ThenBy(fact => Place(order, fact.Concept))
            .ThenBy(fact => fact.Accession, StringComparer.Ordinal)
            .First();

    static int Place(IReadOnlyList<string> order, string concept)
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
