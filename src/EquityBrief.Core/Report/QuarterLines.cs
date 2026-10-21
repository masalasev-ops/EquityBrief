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

// The analysts' revenue estimate for a quarter as a fetch made before its release kept it, and the day of that fetch.
public sealed record RevenueEstimate(decimal Average, DateOnly FetchedOn);

// The analysts' revenue estimate for the fiscal year a quarter ends, as a fetch made before the quarter's release kept it:
// the estimate, the day the year ends and the day of that fetch.
public sealed record YearEstimate(decimal Average, DateOnly YearEnds, DateOnly FetchedOn);

// What the store kept toward the newest quarter's revenue estimate: the quarter's own and the fiscal year's it ends, each
// from the newest fetch made before the quarter's release that filed one, the instant the stock's first estimate fetch was
// made, and the instant the quarter was released; each none where the store holds none.
public sealed record KeptEstimates(RevenueEstimate? Quarter, YearEstimate? Year, DateTimeOffset? KeptFrom, DateTimeOffset? Released);

// A fiscal fourth quarter's revenue estimate as its year's implies it: the year's estimate, the day the year ends and the
// day the estimate was kept, and the year's three earlier quarters' revenue as reported, summed.
public sealed record ImpliedEstimate(decimal Year, DateOnly YearEnds, DateOnly FetchedOn, decimal EarlierQuarters);

// One line of the latest quarter: what was reported, the estimate before the release where one was kept or its year's
// implies one, the difference and it in per cent of the estimate, the same quarter a year earlier and the change on it in
// per cent, and for the three income lines their margin on revenue then and a year earlier, in per cent; why a line reads
// nothing where it does, why it reads no estimate where it reads none, what an implied estimate was worked from, and why
// it reads nothing a year earlier where it does.
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
    string? NotRead,
    string? EstimateAbsent,
    ImpliedEstimate? Implied,
    string? YearEarlierAbsent = null);

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
// see: The latest quarter is read line by line against the estimates kept before its release and the quarter a year earlier, a fiscal fourth quarter against its year's estimate less three quarters
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

    // Why a line reads nothing a year earlier where the fetch holds no quarter ending within a week of a year before.
    public const string NoYearEarlier = "no quarter held a year earlier";

    // Why a line reads no estimate before the release. No analyst estimate is published for gross profit, operating and
    // net income and the two cash flows; the earnings history files none for the quarter's earnings a share; the stock's
    // estimates are kept from a fetch made after the release, or none is kept for it yet; a fetch made before the release
    // filed none; a fiscal fourth quarter's year less its three quarters cannot be read; or no report date is stored.
    public const string NonePublished = "none published";

    public const string NoneKeptYet = "not kept: no estimate is kept for it yet";

    public const string NotFiledBefore = "not filed before the report";

    public const string QuarterNotHeld = "not read: a quarter of its year holds no revenue";

    public const string NotAboveNought = "not read: its year's estimate less its three quarters is not above nought";

    public const string NoReportDate = "not read: no report date is stored for it";

    public static string NotKept(DateOnly from) => FormattableString.Invariant($"not kept: estimates kept from {from:yyyy-MM-dd}");

    // The calendar's words for a report released before the session opens and after it closes.
    public const string BeforeTheOpen = "before";

    public const string AfterTheClose = "after";

    // The months before a fiscal fourth quarter's end that its year's three earlier quarters end.
    static readonly int[] EarlierQuarters = [3, 6, 9];

    // The instant a quarter was released, as the calendar's timing on its report date places it in New York: the session's
    // open for a release before it, the session's close for one after it, and the start of the day where the calendar
    // states no time or holds no row, a day the exchange does not trade read at the regular hours.
    public static DateTimeOffset ReleasedAt(DateOnly reported, string? timing, TimeZoneInfo zone)
    {
        var session = EquityBrief.Core.Quotes.RegularSession.On(reported, zone);

        return timing switch
        {
            BeforeTheOpen => session?.Open ?? At(reported, EquityBrief.Core.Quotes.RegularSession.Opens, zone),
            AfterTheClose => session?.Close ?? At(reported, EquityBrief.Core.Quotes.RegularSession.Closes, zone),
            _ => At(reported, TimeOnly.MinValue, zone),
        };
    }

    static DateTimeOffset At(DateOnly day, TimeOnly time, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(time, DateTimeKind.Unspecified);

        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    // The newest quarter holding a revenue or reported earnings, each line against the estimate kept before its release and
    // against the quarter ending within a week of a year before it; none where no quarter holds either. Revenue is read
    // against the quarter's own estimate, and where none was kept against the one its fiscal year's estimate implies: the
    // year's less its three earlier quarters as reported, read only where all three hold a revenue and it stands above
    // nought.
    public static LatestQuarter? Latest(IReadOnlyList<QuarterRow> quarters, KeptEstimates? estimates = null)
    {
        if (quarters.Where(quarter => quarter.Revenue is not null || quarter.EpsActual is not null).MaxBy(quarter => quarter.PeriodEnd) is not { } newest)
        {
            return null;
        }

        var earlier = Near(quarters, newest.PeriodEnd.AddYears(-1));
        var (revenueEstimate, revenueAbsent, revenueImplied) = RevenueAgainst(quarters, newest, estimates);

        QuarterLine Line(string line, Func<QuarterRow, decimal?> read, string absent, bool perShare = false, bool kept = false, decimal? estimate = null, bool margin = false, ImpliedEstimate? implied = null)
        {
            var reported = read(newest);
            var before = earlier is null ? null : read(earlier);
            var notRead = reported is not null ? null : kept && !newest.LinesRead ? FetchedBefore : NotFiled;
            var earlierAbsent = before is not null ? null : earlier is null ? NoYearEarlier : kept && !earlier.LinesRead ? FetchedBefore : NotFiled;

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
                notRead,
                estimate is null ? absent : null,
                implied,
                earlierAbsent);
        }

        return new LatestQuarter(
            newest.PeriodEnd,
            newest.ReportDate,
            earlier?.PeriodEnd,
            [
                Line(Revenue, quarter => quarter.Revenue, revenueAbsent, estimate: revenueEstimate, implied: revenueImplied),
                Line(GrossProfit, quarter => quarter.GrossProfit, NonePublished, kept: true, margin: true),
                Line(OperatingIncome, quarter => quarter.OperatingIncome, NonePublished, margin: true),
                Line(NetIncome, quarter => quarter.NetIncome, NonePublished, margin: true),
                Line(Eps, quarter => quarter.EpsActual, NotFiled, perShare: true, estimate: newest.EpsEstimate),
                Line(OperatingCashFlow, quarter => quarter.OperatingCashFlow, NonePublished),
                Line(FreeCashFlow, quarter => quarter.FreeCashFlow, NonePublished, kept: true),
            ]);
    }

    // The revenue estimate the newest quarter is read against, why none is read where it is not, and what an implied one
    // was worked from.
    static (decimal? Estimate, string Absent, ImpliedEstimate? Implied) RevenueAgainst(IReadOnlyList<QuarterRow> quarters, QuarterRow newest, KeptEstimates? estimates)
    {
        if (estimates?.Quarter is { } own)
        {
            return (own.Average, string.Empty, null);
        }

        if (estimates?.Year is { } year)
        {
            var three = EarlierQuarters.Select(months => Near(quarters, newest.PeriodEnd.AddMonths(-months))?.Revenue).ToArray();

            if (three.Any(revenue => revenue is null))
            {
                return (null, QuarterNotHeld, null);
            }

            var reported = three.Sum(revenue => revenue!.Value);
            var left = year.Average - reported;

            return left > 0m
                ? (left, string.Empty, new ImpliedEstimate(year.Average, year.YearEnds, year.FetchedOn, reported))
                : (null, NotAboveNought, null);
        }

        return (null, newest.ReportDate is null || estimates is { Released: null } ? NoReportDate
            : estimates?.KeptFrom is not { } from ? NoneKeptYet
            : estimates.Released is { } released && from >= released ? NotKept(DateOnly.FromDateTime(from.UtcDateTime))
            : NotFiledBefore, null);
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
