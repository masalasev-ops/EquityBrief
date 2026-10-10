using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Report;

// One dividend the store kept: its ex-dividend date and its amount a share as the provider restated it on the day it was
// read.
public sealed record KeptDividend(DateOnly ExDate, decimal Amount);

// One quarter's cash as the dividend's safety reads it: its end, its free cash flow and the dividends it paid.
public sealed record CashQuarter(DateOnly PeriodEnd, decimal? FreeCashFlow, decimal? DividendsPaid);

// One calendar year's dividends a share: their total, how many were paid, whether the year is whole, which the night's
// own year is not, and whether its total rose on the year before's, none where the two cannot be compared.
public sealed record DividendYear(int Year, decimal Total, int Payments, bool Whole, bool? Raised);

// One session's trailing yield, the dividends of the year to it over its close in per cent, beside the 10-year that
// session, each none where it is not held.
public sealed record YieldPoint(DateOnly Session, double? Yield, double? TenYear);

// The dividend's safety for a payer: the rate a share and the yield at the price drawn, the 10-year and the spread over it
// in points, the payout on earnings and on free cash flow in per cent with what each was read from, every year's
// dividends with the raises, how many whole years in a row it raised, and the yield against the 10-year over the sessions.
public sealed record DividendSafetyReading(
    decimal Rate,
    bool RateIsForward,
    double? Yield,
    double? TenYear,
    DateOnly? TenYearOn,
    double? Spread,
    double? PayoutOnEarnings,
    decimal? EarningsTrailing,
    double? PayoutOnCash,
    decimal? DividendsPaid,
    decimal? FreeCashFlow,
    IReadOnlyList<DividendYear> Years,
    int RaisedInARow,
    IReadOnlyList<YieldPoint> Series);

// The dividend's safety, worked by one function from stored figures at the price the page draws, so the page and its file
// read one arithmetic and no screen computes it.
// see: A payer's dividend is read for its safety against its earnings, its free cash flow and the Treasury's 10-year, with every year it raised
public static class DividendSafety
{
    // The quarters the payout on free cash flow sums, a year.
    public const int CashQuarters = 4;

    // The days a trailing yield sums the dividends over.
    public const int TrailingDays = 365;

    // A payer is one whose fetch files a forward rate above nought, or that paid a dividend kept in the year to the night;
    // none for any other. The rate is the forward rate where one is filed, and otherwise the year's kept dividends summed.
    public static DividendSafetyReading? Of(
        decimal? forwardRate,
        decimal? price,
        decimal? epsTrailing,
        IReadOnlyList<CashQuarter> quarters,
        IReadOnlyList<KeptDividend> kept,
        IReadOnlyList<(DateOnly Session, double TenYear)> tenYears,
        IReadOnlyList<(DateOnly Session, decimal Close)> closes,
        DateOnly night)
    {
        var lastYear = kept.Where(paid => paid.ExDate <= night && paid.ExDate > night.AddDays(-TrailingDays)).ToArray();
        var forward = forwardRate is > 0m;

        if (!forward && lastYear.Length == 0)
        {
            return null;
        }

        var rate = forward ? forwardRate!.Value : lastYear.Sum(paid => paid.Amount);
        var tenYear = tenYears.Where(row => row.Session <= night).OrderBy(row => row.Session).LastOrDefault();
        double? yield = price is { } shown ? Tiles.NameTiles.Yield(rate, shown)?.Percent : null;
        double? tenYearOn = tenYear == default ? null : tenYear.TenYear;

        var four = quarters.OrderByDescending(quarter => quarter.PeriodEnd).Take(CashQuarters).ToArray();
        var cashRead = four.Length == CashQuarters && four.All(quarter => quarter.FreeCashFlow is not null && quarter.DividendsPaid is not null);
        decimal? paidOut = cashRead ? four.Sum(quarter => Math.Abs(quarter.DividendsPaid!.Value)) : null;
        decimal? freeCash = cashRead ? four.Sum(quarter => quarter.FreeCashFlow!.Value) : null;

        var years = Years(kept.Where(paid => paid.ExDate <= night).ToArray(), night);

        return new DividendSafetyReading(
            rate,
            forward,
            yield,
            tenYearOn,
            tenYear == default ? null : tenYear.Session,
            yield is { } at && tenYearOn is { } treasury ? at - treasury : null,
            epsTrailing is > 0m ? Statistic.FromRatio(rate / epsTrailing.Value) * 100 : null,
            epsTrailing,
            paidOut is { } dividends && freeCash is > 0m ? Statistic.FromRatio(dividends / freeCash.Value) * 100 : null,
            paidOut,
            freeCash,
            years,
            InARow(years),
            Series(kept, tenYears, closes));
    }

    // Every year the kept dividends fall in, oldest first: the night's own year not whole, and a whole year raised where its
    // total stands above the whole year before's and both paid as many times, since a fifth payment in a year is no raise.
    public static IReadOnlyList<DividendYear> Years(IReadOnlyList<KeptDividend> kept, DateOnly night)
    {
        var years = kept
            .GroupBy(paid => paid.ExDate.Year)
            .OrderBy(year => year.Key)
            .Select(year => (Year: year.Key, Total: year.Sum(paid => paid.Amount), Payments: year.Count(), Whole: year.Key < night.Year))
            .ToArray();

        return
        [
            .. years.Select((year, at) =>
            {
                var before = at > 0 ? years[at - 1] : default;
                bool? raised = at > 0 && year.Whole && before.Whole && before.Year == year.Year - 1 && before.Payments == year.Payments
                    ? year.Total > before.Total
                    : null;

                return new DividendYear(year.Year, year.Total, year.Payments, year.Whole, raised);
            }),
        ];
    }

    // How many whole years in a row, back from the newest, the dividend was raised.
    public static int InARow(IReadOnlyList<DividendYear> years) =>
        years.Where(year => year.Whole).Reverse().TakeWhile(year => year.Raised == true).Count();

    // Each session's trailing yield, read only where the kept dividends reach back a year before it, beside that session's
    // 10-year where one is held.
    static IReadOnlyList<YieldPoint> Series(
        IReadOnlyList<KeptDividend> kept,
        IReadOnlyList<(DateOnly Session, double TenYear)> tenYears,
        IReadOnlyList<(DateOnly Session, decimal Close)> closes)
    {
        var earliest = kept.Count == 0 ? (DateOnly?)null : kept.Min(paid => paid.ExDate);
        var treasury = tenYears.ToDictionary(row => row.Session, row => row.TenYear);

        return
        [
            .. closes
                .OrderBy(close => close.Session)
                .Select(close =>
                {
                    double? trailing = earliest is { } first && first <= close.Session.AddDays(-TrailingDays) && close.Close > 0m
                        ? Statistic.FromRatio(kept.Where(paid => paid.ExDate <= close.Session && paid.ExDate > close.Session.AddDays(-TrailingDays)).Sum(paid => paid.Amount) / close.Close) * 100
                        : null;

                    return new YieldPoint(close.Session, trailing, treasury.TryGetValue(close.Session, out var ten) ? ten : null);
                })
                .Where(point => point.Yield is not null || point.TenYear is not null),
        ];
    }
}
