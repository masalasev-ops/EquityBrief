using EquityBrief.Core.Prices;
using EquityBrief.Core.Quarters;

namespace EquityBrief.Core.Report;

// One quarter of the newest fetch as the quarter's regions read it: its end and report date, the income statement's
// lines, the cash flow statement's, earnings a share against the analysts' estimate before the report, the close after
// the report and the four quarters' earnings a multiple is read from, and whether the fetch read the gross profit and the
// cash flow lines, which a fetch made before they were kept did not.
public sealed record QuarterRow(
    DateOnly PeriodEnd,
    DateOnly? ReportDate,
    decimal? Revenue,
    decimal? GrossProfit,
    decimal? OperatingIncome,
    decimal? NetIncome,
    decimal? OperatingCashFlow,
    decimal? FreeCashFlow,
    decimal? DividendsPaid,
    decimal? EpsActual,
    decimal? EpsEstimate,
    decimal? EpsTrailing,
    decimal? CloseAfter,
    bool LinesRead);

// The analysts' revenue estimate for a quarter as a fetch made before its report kept it, and the day of that fetch.
public sealed record RevenueEstimate(decimal Average, DateOnly FetchedOn);

// One line of the latest quarter: what was reported, the estimate before the report where one was kept, the difference and
// it in per cent of the estimate, the same quarter a year earlier and the change on it in per cent, and for the three
// income lines their margin on revenue then and a year earlier, in per cent; and why a line reads nothing where it does.
public sealed record QuarterLine(
    string Line,
    decimal? Reported,
    decimal? Estimate,
    decimal? Difference,
    double? AgainstEstimate,
    decimal? YearEarlier,
    double? OnTheYear,
    double? Margin,
    double? MarginYearEarlier,
    bool PerShare,
    string? NotRead);

// The latest quarter: its end, the day it was reported, the quarter a year earlier it is read against, and its lines.
public sealed record LatestQuarter(DateOnly Quarter, DateOnly? Reported, DateOnly? YearEarlier, IReadOnlyList<QuarterLine> Lines);

// One quarter's growth on the same quarter a year earlier, in per cent: revenue's and earnings a share's.
public sealed record QuarterGrowth(DateOnly Quarter, double? Revenue, double? Eps);

// One quarter's gross, operating and net margins on its revenue, in per cent.
public sealed record QuarterMargins(DateOnly Quarter, double? Gross, double? Operating, double? Net);

// One quarter's multiple: the close after its report over its four quarters' earnings a share, both from the one fetch.
public sealed record QuarterMultiple(DateOnly Quarter, decimal Multiple);

// The latest quarter line by line, the growth of the eight newest quarters and the margins of the twelve newest, each
// worked by one function over the newest fetch's quarters, so the page and its file read one arithmetic.
// see: The latest quarter is read line by line against the estimate kept before its report and the same quarter a year earlier
public static class QuarterLines
{
    public const int GrowthQuarters = 8;

    public const int MarginQuarters = QuarterFetch.Kept;

    public const string Revenue = "Revenue";
    public const string GrossProfit = "Gross profit";
    public const string OperatingIncome = "Operating income";
    public const string NetIncome = "Net income";
    public const string Eps = "Earnings a share";
    public const string OperatingCashFlow = "Operating cash flow";
    public const string FreeCashFlow = "Free cash flow";

    // Why a gross profit or a cash flow line reads nothing on a fetch made before the fetch kept them.
    public const string FetchedBefore = "not read: the newest fetch was made before the gross profit and the cash flow lines were kept";

    public const string NotFiled = "not filed";

    // The newest quarter holding a revenue or reported earnings, each line against the estimate kept before its report and
    // against the quarter ending within a week of a year before it; none where no quarter holds either.
    public static LatestQuarter? Latest(IReadOnlyList<QuarterRow> quarters, RevenueEstimate? revenueEstimate = null)
    {
        if (quarters.Where(quarter => quarter.Revenue is not null || quarter.EpsActual is not null).MaxBy(quarter => quarter.PeriodEnd) is not { } newest)
        {
            return null;
        }

        var earlier = Near(quarters, newest.PeriodEnd.AddYears(-1));

        QuarterLine Line(string line, Func<QuarterRow, decimal?> read, bool perShare = false, bool kept = false, decimal? estimate = null, bool margin = false)
        {
            var reported = read(newest);
            var before = earlier is null ? null : read(earlier);
            var notRead = reported is not null ? null : kept && !newest.LinesRead ? FetchedBefore : NotFiled;

            return new QuarterLine(
                line,
                reported,
                estimate,
                reported is { } now && estimate is { } expected ? now - expected : null,
                Percents.FromFraction(QuarterFetch.Grown(reported, estimate)),
                before,
                Percents.FromFraction(QuarterFetch.Grown(reported, before)),
                margin ? Percents.FromFraction(QuarterFetch.Margined(reported, newest.Revenue)) : null,
                margin && earlier is not null ? Percents.FromFraction(QuarterFetch.Margined(before, earlier.Revenue)) : null,
                perShare,
                notRead);
        }

        return new LatestQuarter(
            newest.PeriodEnd,
            newest.ReportDate,
            earlier?.PeriodEnd,
            [
                Line(Revenue, quarter => quarter.Revenue, estimate: revenueEstimate?.Average),
                Line(GrossProfit, quarter => quarter.GrossProfit, kept: true, margin: true),
                Line(OperatingIncome, quarter => quarter.OperatingIncome, margin: true),
                Line(NetIncome, quarter => quarter.NetIncome, margin: true),
                Line(Eps, quarter => quarter.EpsActual, perShare: true, estimate: newest.EpsEstimate),
                Line(OperatingCashFlow, quarter => quarter.OperatingCashFlow),
                Line(FreeCashFlow, quarter => quarter.FreeCashFlow, kept: true),
            ]);
    }

    // The eight newest quarters, oldest first, each with its revenue's and its earnings a share's change on the quarter
    // ending within a week of a year before it, none where either quarter lacks the figure or the earlier is not above
    // nought.
    public static IReadOnlyList<QuarterGrowth> Growth(IReadOnlyList<QuarterRow> quarters) =>
        [
            .. quarters
                .OrderByDescending(quarter => quarter.PeriodEnd)
                .Take(GrowthQuarters)
                .OrderBy(quarter => quarter.PeriodEnd)
                .Select(quarter =>
                {
                    var earlier = Near(quarters, quarter.PeriodEnd.AddYears(-1));

                    return new QuarterGrowth(
                        quarter.PeriodEnd,
                        Percents.FromFraction(QuarterFetch.Grown(quarter.Revenue, earlier?.Revenue)),
                        Percents.FromFraction(QuarterFetch.Grown(quarter.EpsActual, earlier?.EpsActual)));
                }),
        ];

    // The twelve newest quarters, oldest first, each with its gross, operating and net income over its revenue.
    public static IReadOnlyList<QuarterMargins> Margins(IReadOnlyList<QuarterRow> quarters) =>
        [
            .. quarters
                .OrderByDescending(quarter => quarter.PeriodEnd)
                .Take(MarginQuarters)
                .OrderBy(quarter => quarter.PeriodEnd)
                .Select(quarter => new QuarterMargins(
                    quarter.PeriodEnd,
                    Percents.FromFraction(QuarterFetch.Margined(quarter.GrossProfit, quarter.Revenue)),
                    Percents.FromFraction(QuarterFetch.Margined(quarter.OperatingIncome, quarter.Revenue)),
                    Percents.FromFraction(QuarterFetch.Margined(quarter.NetIncome, quarter.Revenue)))),
        ];

    // Each quarter's own multiple, oldest first: the close after its report over its four quarters' earnings a share, read
    // only where both are held and the earnings are above nought, rounded as the valuation reading rounds it, which is the
    // rule the night's valuation position reads its range from.
    // see: Four readings of a member's reported quarters are worked out every night by rules the measured split settled, and its state is read from sales and operating margin alone
    public static IReadOnlyList<QuarterMultiple> Multiples(IReadOnlyList<QuarterRow> quarters) =>
        [
            .. quarters
                .Where(quarter => quarter.CloseAfter is not null && quarter.EpsTrailing is > 0m)
                .OrderBy(quarter => quarter.PeriodEnd)
                .Select(quarter => new QuarterMultiple(quarter.PeriodEnd, decimal.Round(quarter.CloseAfter!.Value / quarter.EpsTrailing!.Value, 6, MidpointRounding.ToEven))),
        ];

    static QuarterRow? Near(IReadOnlyList<QuarterRow> quarters, DateOnly end) => quarters
        .Where(quarter => Math.Abs(quarter.PeriodEnd.DayNumber - end.DayNumber) <= QuarterFetch.NearDays)
        .OrderBy(quarter => Math.Abs(quarter.PeriodEnd.DayNumber - end.DayNumber))
        .FirstOrDefault();
}
