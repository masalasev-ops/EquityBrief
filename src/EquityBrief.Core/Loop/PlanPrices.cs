namespace EquityBrief.Core.Loop;

// A stock's levels plan on a session as the test of the buy and sell points reads it: each zone's top with its own stop, the
// staged sales, and the bands above with real touches.
public sealed record PlanLevels(IReadOnlyList<(double Top, double? Stop)> Zones, IReadOnlyList<double> Staged, IReadOnlyList<double> Bands);

// A combination priced on a pick's session: its words, the price a zone or the average buys at, none for the next open, the
// sessions it waits for that price, its stop, and how it sells: a target, staged levels above the buy, a trail, the dip's
// 2-session RSI, or none of them, the time limit alone, which every sale also ends at.
public sealed record PlanPrice(string Combination, double? Buy, int Window, double Stop, double? Target, IReadOnlyList<double> Staged, double? Trail, double? RsiAbove, int Cap);

// The plan the test of the buy and sell points recommends for a family on an index, priced on one pick as that test prices
// the plan on the newest session it reads: the buy at the level, or at the close for the next open, the stop placed from that
// price, and the sale read off the stock's own levels plan or the rule's own.
// see: A watched pick on Home draws the plan the long test recommends for its kind of pick, priced on its own levels and not traded
public static class PlanPrices
{
    public static PlanPrice? Of(PlanCombination combination, double close, PlanLevels levels, double? average20, double typicalDay, double ruleStop, PlanSale ruleSale, int cap)
    {
        var zone = PlanPoints.Zones.ToList().IndexOf(combination.Entry);
        (double Top, double? Stop)? zoneRead = zone >= 0 && zone < levels.Zones.Count ? levels.Zones[zone] : null;
        double? level = combination.Entry == PlanPoints.NextOpen ? null
            : combination.Entry == PlanPoints.Average ? (average20 is { } average && average > 0 ? average : null)
            : zoneRead?.Top;

        if (combination.Entry != PlanPoints.NextOpen && level is null)
        {
            return null;
        }

        var price = level ?? close;

        if (PlanPoints.StopOf(combination.Stop, price, ruleStop, zoneRead?.Stop, typicalDay) is not { } stop || !(price > stop) || cap <= 0)
        {
            return null;
        }

        var sale = combination.Exit switch
        {
            PlanPoints.RuleSale => ruleSale,
            PlanPoints.FirstBand => new PlanSale(levels.Bands.Where(edge => edge > price).Order().Cast<double?>().FirstOrDefault(), null, []),
            PlanPoints.StagedSales => new PlanSale(null, null, levels.Staged),
            _ => new PlanSale(null, null, []),
        };

        return new PlanPrice(combination.Words, level, combination.Window, stop, sale.Target, [.. sale.Staged.Where(edge => edge > price).Order()], sale.Trail, sale.RsiAbove, cap);
    }

    // The combination a recommendation names by its words; none where none does.
    public static PlanCombination? Named(string? words) =>
        words is null ? null : PlanPoints.Combinations.FirstOrDefault(one => one.Words == words);

    // Whether a combination is the traded plan itself, the next open at the rule's stop and sale.
    public static bool Traded(PlanCombination combination) => PlanPoints.Changes(combination) == 0;
}
