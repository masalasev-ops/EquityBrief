using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Readings;

// What a 400 or 600 trade pays to be bought and sold: half the published mean relative effective spread of its company's
// value band and its price band at the buy and half at the sale, from the SEC's table of 2013's trades (Collver 2014,
// Table 4), stated at the table's value and at double. The night and the sweep read it through these functions alone.
// see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it
public static class TradeCost
{
    // The value bands' lower edges in dollars, each band closed below and open above as the table's are; a company above
    // the last is read in it, and one below the first in the first.
    public static IReadOnlyList<decimal> ValueBands { get; } = [250_000_000m, 500_000_000m, 1_000_000_000m, 2_000_000_000m];

    // The price bands' lower edges in dollars, the last open above; a price below the first is read in it.
    public static IReadOnlyList<decimal> PriceBands { get; } = [2m, 6m, 10m, 20m, 40m];

    // The band a company with no count is read in, $1 to 2 billion.
    public const int NoCountBand = 2;

    // The mean relative effective spread in per cent, a row a price band and a column a value band, as Table 4 states it.
    static readonly decimal[,] Spread =
    {
        { 0.301m, 0.244m, 0.235m, 0.221m },
        { 0.237m, 0.144m, 0.127m, 0.121m },
        { 0.267m, 0.139m, 0.088m, 0.074m },
        { 0.344m, 0.170m, 0.089m, 0.056m },
        { 0.514m, 0.251m, 0.129m, 0.072m },
    };

    // The sensitivity every after-cost figure is stated at beside the table's own.
    public const int Doubled = 2;

    public static int ValueBand(decimal? value) =>
        value is not { } held ? NoCountBand : Math.Max(0, LastAtOrBelow(ValueBands, held));

    public static int PriceBand(decimal price) => Math.Max(0, LastAtOrBelow(PriceBands, price));

    // The table's spread for a value band and a price, in per cent.
    public static decimal SpreadPercent(int valueBand, decimal price) => Spread[PriceBand(price), valueBand];

    // The round trip in dollars a share: half the spread at the buy's price and half at the sale's, times the multiple.
    public static decimal RoundTrip(decimal? value, decimal entry, decimal exit, int multiple = 1)
    {
        var band = ValueBand(value);

        return multiple * ((SpreadPercent(band, entry) / 200m * entry) + (SpreadPercent(band, exit) / 200m * exit));
    }

    // The round trip in multiples of the trade's risk, the distance from its buy to its stop, a ratio of two prices.
    public static double InRisk(decimal? value, decimal entry, decimal stop, decimal exit, int multiple = 1) =>
        entry > stop ? Statistic.FromRatio(RoundTrip(value, entry, exit, multiple) / (entry - stop)) : double.NaN;

    // The round trip in per cent of the buy, which a holding scored in percent pays.
    public static double InPercent(decimal? value, decimal entry, decimal exit, int multiple = 1) =>
        entry > 0m ? Statistic.FromRatio(RoundTrip(value, entry, exit, multiple) / entry * 100m) : double.NaN;

    static int LastAtOrBelow(IReadOnlyList<decimal> edges, decimal value)
    {
        var at = -1;

        for (var band = 0; band < edges.Count; band++)
        {
            if (value >= edges[band])
            {
                at = band;
            }
        }

        return at;
    }
}
