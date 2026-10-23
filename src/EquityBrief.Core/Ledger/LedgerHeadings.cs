using EquityBrief.Core.Loop;

namespace EquityBrief.Core.Ledger;

// One heading of the chart of what is behind a stock: its name and the ledger's readings it groups, by their columns.
public sealed record LedgerHeading(string Name, IReadOnlyList<string> Readings);

// The ledger's readings grouped under the chart's seven headings, every reading under exactly one, and the arithmetic the
// chart draws from them: a stock's percentile among its index's members under each heading, and each heading's push on
// the learned score. A reading read off a market series is the same for every member of an index on a night, so it stands
// in its heading's push and in no percentile, where every member would hold the same place on it. The insiders' four
// readings stand from 19.13 under analyst sentiment, the nearest of the seven headings the operator ruled, and from 20.8
// the release readings beside them, what the results release itself said beside the surprise the provider filed.
// see: A pick's card and a stock's page draw what is behind it by heading, its percentiles always and its push where the score passed
public static class LedgerHeadings
{
    public static IReadOnlyList<LedgerHeading> All { get; } =
    [
        new("Momentum",
        [
            "close_over_twenty", "close_over_fifty", "close_over_long", "fifty_over_long", "rsi", "rsi_up", "return_quarter",
            "return_half_year", "return_twelve_less_one", "strength", "strength_twelve_less_one", "high_ratio", "since_high",
            "depth", "rsi_low", "freshness", "band_strength", "reaction_moves", "reward_to_risk", "breadth", "highs_less_lows",
            "index_over_long", "mid_over_large", "small_over_large", "credit_over_fifty", "return_week", "return_month",
            "close_place", "overnight_less_intraday", "sector_month", "sector_quarter", "low_ratio", "streak", "alpha_one", "alpha_four",
        ]),
        new("Valuation", ["value_place"]),
        new("Analyst sentiment", ["surprise_percent", "surprise_sessions", "earnings_sessions", "insider_net_buy", "insider_buyers", "insider_officer_buy", "insider_unplanned_sale", "release_results", "release_outlook", "release_tone", "release_demand"]),
        new("Volatility", ["move_share", "gap_down", "tightness", "range_ratio", "vix", "vix_change", "largest_return", "volatility_month", "residual_volatility", "beta", "skewness"]),
        new("Size and liquidity", ["liquidity", "volume_ratio", "dry_up", "volume_multiple", "up_volume_share", "price_volume_correlation", "illiquidity", "volume_slope", "alpha_two", "alpha_three", "alpha_six"]),
        new("Financial strength", ["profit", "coverage", "cash_over_income"]),
        new("Earnings quality", ["revenue_growth", "growth_change", "gross_margin_change", "operating_margin_change"]),
    ];

    // The readings read off a market series, the same for every member of an index on a night.
    public static IReadOnlySet<string> Market { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "breadth", "highs_less_lows", "index_over_long", "vix", "vix_change", "mid_over_large", "small_over_large", "credit_over_fifty",
    };

    // A value's place among the values the members hold, the share of them under it and half the share level with it, the
    // value's own among them; none where the value is not held or no member holds one.
    public static double? Percentile(double? value, IReadOnlyList<double?> among)
    {
        if (value is not { } own)
        {
            return null;
        }

        var held = 0;
        var under = 0.0;

        foreach (var other in among)
        {
            if (other is not { } figure)
            {
                continue;
            }

            held++;
            under += figure < own ? 1 : figure == own ? 0.5 : 0;
        }

        return held == 0 ? null : under / held;
    }

    // A stock's percentile under each heading in the headings' order: the mean of its places among the members on each of
    // the heading's own readings it holds, the market's left out, and none where it holds none of them. Readings are in
    // the catalogue's order, the stock's and each member's alike.
    public static IReadOnlyList<double?> Percentiles(IReadOnlyList<double?> stock, IReadOnlyList<IReadOnlyList<double?>> members) =>
    [
        .. All.Select<LedgerHeading, double?>(heading =>
        {
            var places = heading.Readings
                .Where(column => !Market.Contains(column))
                .Select(LedgerReadings.IndexOf)
                .Select(at => Percentile(stock[at], [.. members.Select(member => member[at])]))
                .OfType<double>()
                .ToArray();

            return places.Length == 0 ? null : places.Average();
        }),
    ];

    // Each heading's push on a fitted score in the headings' order: the score's weights times the stock's readings on
    // their standard scale, each less its mean over the setups the score learned on, summed by heading, in the score's own
    // unit; none where a reading the score weighs is not held, as the score itself is then none. The pushes sum to the
    // score less the score of a setup standing at those means.
    public static IReadOnlyList<double>? Pushes(RidgeModel model, IReadOnlyList<double?> readings)
    {
        var pushes = new double[All.Count];

        for (var at = 0; at < model.Readings.Count; at++)
        {
            if (readings[model.Readings[at]] is not { } value)
            {
                return null;
            }

            pushes[HeadingOf(model.Readings[at])] += model.Weights[at] * (value - model.Means[at]) / model.Spreads[at];
        }

        return pushes;
    }

    // The score of a setup standing at the means of the setups a score learned on, which the pushes are read against.
    public static double Centre(RidgeModel model) =>
        Enumerable.Range(0, model.Readings.Count).Sum(at => model.Weights[at] / model.Spreads[at] * model.Means[at]);

    // The heading a reading stands under, by its place in the catalogue.
    public static int HeadingOf(int reading)
    {
        var column = LedgerReadings.All[reading].Column;

        for (var at = 0; at < All.Count; at++)
        {
            if (All[at].Readings.Contains(column, StringComparer.Ordinal))
            {
                return at;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(reading), column, "No heading groups this reading.");
    }
}
