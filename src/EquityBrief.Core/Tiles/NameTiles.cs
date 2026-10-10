using EquityBrief.Core.Prices;
using EquityBrief.Core.Quarters;

namespace EquityBrief.Core.Tiles;

// One quarter of the newest fetch as the tiles read it: its end, its revenue and its earnings a share with the analysts'
// estimate, all on that fetch's per-share basis.
public sealed record TileQuarter(DateOnly PeriodEnd, decimal? Revenue, decimal? EpsActual, decimal? EpsEstimate);

// The newest quarter's earnings a share against the analysts' estimate before the report, and against the same quarter
// a year before, each change in per cent of the figure it is read against.
public sealed record EarningsTile(DateOnly Quarter, decimal Actual, decimal? Estimate, double? AgainstEstimate, decimal? YearBefore, double? OnTheYear);

// The year's growth: the analysts' consensus for the fiscal year where a fetch kept one, read through the year's end with
// how many analysts estimate it and the day of the fetch, and otherwise the four newest quarters' revenue against the four
// before them, to the quarter it is read through.
public sealed record GrowthTile(string Basis, double Percent, DateOnly Through, int? Analysts = null, DateOnly? FetchedOn = null)
{
    public const string Sales = "sales";

    public const string Consensus = "consensus";
}

// The forward annual rate a share and the yield it is at the price the page draws, in per cent, and the Treasury's 10-year
// par yield beside it with the session it was read for, where the store holds one.
public sealed record YieldTile(decimal Rate, double Percent, double? TenYear = null, DateOnly? TenYearOn = null);

// The price against the year's high and low with the sessions they were made on: how far under the high it is in per cent
// of the high, and where it sits from the low at nought to the high at one.
public sealed record HighTile(decimal Price, decimal High, DateOnly HighOn, decimal Low, DateOnly LowOn, double FromHigh, double Position);

// The four tiles under the name page's headline, each worked out from stored figures at the price the page draws, the
// last stored close or the delayed quote in the session. One function each, so the page, its file and the quote's price
// read one arithmetic.
// see: Four tiles under the headline are worked by code from stored figures at the price the page draws
public static class NameTiles
{
    // The newest quarter holding reported earnings, against its estimate and against the quarter ending within a week of a
    // year before it. A change is read against a figure above nought alone, so a loss a year before reads no change.
    public static EarningsTile? Earnings(IReadOnlyList<TileQuarter> quarters)
    {
        if (quarters.Where(quarter => quarter.EpsActual is not null).OrderBy(quarter => quarter.PeriodEnd).LastOrDefault() is not { } newest)
        {
            return null;
        }

        var actual = newest.EpsActual!.Value;
        var before = Near(quarters, newest.PeriodEnd.AddYears(-1))?.EpsActual;

        return new EarningsTile(
            newest.PeriodEnd,
            actual,
            newest.EpsEstimate,
            QuarterFetch.Grown(actual, newest.EpsEstimate) is { } against ? Statistic.FromRatio(against) * 100 : null,
            before,
            QuarterFetch.Grown(actual, before) is { } onTheYear ? Statistic.FromRatio(onTheYear) * 100 : null);
    }

    // The four newest quarters' revenue against the four before them, each found within a week of three months apart, and
    // none where any of the eight is missing or the earlier four sum to nought or less.
    public static GrowthTile? SalesGrowth(IReadOnlyList<TileQuarter> quarters)
    {
        if (quarters.Where(quarter => quarter.Revenue is not null).OrderBy(quarter => quarter.PeriodEnd).LastOrDefault() is not { } newest)
        {
            return null;
        }

        var eight = Enumerable.Range(0, 8).Select(back => Near(quarters, newest.PeriodEnd.AddMonths(-3 * back))?.Revenue).ToArray();

        if (eight.Any(revenue => revenue is null))
        {
            return null;
        }

        var grown = QuarterFetch.Grown(eight.Take(4).Sum(revenue => revenue!.Value), eight.Skip(4).Sum(revenue => revenue!.Value));

        return grown is { } part ? new GrowthTile(GrowthTile.Sales, Statistic.FromRatio(part) * 100, newest.PeriodEnd) : null;
    }

    // The year's growth: the analysts' consensus for the current fiscal year where the newest fetch kept its trend, its
    // earnings a share against the year before's, and the sales growth otherwise.
    public static GrowthTile? Growth(IReadOnlyList<EstimatePeriod> trend, DateOnly? fetchedOn, IReadOnlyList<TileQuarter> quarters) =>
        ConsensusGrowth(trend, fetchedOn) ?? SalesGrowth(quarters);

    // The current fiscal year's consensus earnings a share against the year before's, none where either is not filed or
    // the year before's is not above nought.
    public static GrowthTile? ConsensusGrowth(IReadOnlyList<EstimatePeriod> trend, DateOnly? fetchedOn) =>
        trend.FirstOrDefault(period => period.Period == EstimatePeriod.CurrentYear) is { } year
        && QuarterFetch.Grown(year.EpsAverage, year.EpsYearAgo) is { } grown
            ? new GrowthTile(GrowthTile.Consensus, Statistic.FromRatio(grown) * 100, year.PeriodEnd, year.EpsAnalysts, fetchedOn)
            : null;

    // The forward annual rate over the price, none for a company paying nothing or a price of nought.
    public static YieldTile? Yield(decimal? forwardRate, decimal price) =>
        forwardRate is { } rate && rate > 0m && price > 0m
            ? new YieldTile(rate, Statistic.FromRatio(rate / price) * 100)
            : null;

    // The same with the newest 10-year the store holds on or before the night beside it.
    public static YieldTile? YieldBeside(decimal? forwardRate, decimal price, IReadOnlyList<(DateOnly Session, double TenYear)> tenYears, DateOnly night) =>
        Yield(forwardRate, price) is { } tile
            ? tenYears.Where(row => row.Session <= night).OrderBy(row => row.Session).Select(row => ((DateOnly, double)?)row).LastOrDefault() is { } newest
                ? tile with { TenYear = newest.Item2, TenYearOn = newest.Item1 }
                : tile
            : null;

    // The price against the year's high and low, none where the year holds no range.
    public static HighTile? High(decimal price, decimal high, DateOnly highOn, decimal low, DateOnly lowOn) =>
        high > low && high > 0m
            ? new HighTile(price, high, highOn, low, lowOn, Statistic.FromRatio((price - high) / high) * 100, Statistic.FromRatio((price - low) / (high - low)))
            : null;

    static TileQuarter? Near(IReadOnlyList<TileQuarter> quarters, DateOnly end) => quarters
        .Where(quarter => Math.Abs(quarter.PeriodEnd.DayNumber - end.DayNumber) <= QuarterFetch.NearDays)
        .OrderBy(quarter => Math.Abs(quarter.PeriodEnd.DayNumber - end.DayNumber))
        .FirstOrDefault();
}
