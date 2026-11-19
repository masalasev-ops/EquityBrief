namespace EquityBrief.Core.Loop;

// The one way a plan trade takes profit, as its walk sells it from a buy and the stop placed from that buy: the whole at its
// target where one stands above the buy; half at the half's price where that comes before the target, the rest at the
// target, the staged levels, the trail or the time limit; and the staged levels below the target. A half at or above the
// target never sells, since a close at the target sells the whole first, and a staged level at or above it likewise, so
// neither is drawn. A page draws this and no stored target, so a trade or an order names one exit wherever it is drawn.
// see: A trade under a plan names the one exit its walk sells by on every page, read from its plan and never from a stored target
public sealed record TradeExit(double? Whole, double? Half, IReadOnlyList<double> Staged, bool Trails, int Cap)
{
    // The first price it takes profit at, which a page draws as its take-profit: the lowest of the half's, the first staged
    // level's and the whole's; none where it sells only on its stop, its trail or its time limit.
    public double? First => new[] { Half, Staged.Count > 0 ? Staged[0] : null, Whole }.Where(price => price is not null).Min();

    // Whether the first price sells part of it, the rest selling later.
    public bool FirstIsPart => First is { } first && first != Whole;

    // Whether it sells on its time limit alone above its stop, naming no price.
    public bool TimeAlone => First is null && !Trails;

    public static TradeExit Of(PlanOrder order, double buy, double stop)
    {
        var sale = order.SaleFrom(buy, stop);
        double? whole = sale.Target is { } own && own > buy ? own : null;
        double? half = sale.HalfAt is { } at && at > buy && !(whole is { } over && at >= over) ? at : null;
        double[] staged = [.. sale.Staged.Where(level => level > buy && !(whole is { } over && level >= over)).Order()];

        return new TradeExit(whole, half, staged, sale.Trail is not null, order.Cap);
    }

    // The exit of an order still waiting, read from the price it waits for and the stop that price places; none where the order
    // waits for the next open, whose price is not known until it fills, or its stop is not placed.
    public static TradeExit? Waiting(PlanOrder order) =>
        order.Level is { } level && PlanPoints.StopOf(order.Stop, level, order.RuleStop, order.ZoneStop, order.TypicalDay) is { } stop && level > stop
            ? Of(order, level, stop)
            : null;

    // The exit of a trade filled at a price, from the stop the plan places from it; none where its stop is not placed.
    public static TradeExit? Filled(PlanOrder order, double fill) =>
        PlanPoints.StopOf(order.Stop, fill, order.RuleStop, order.ZoneStop, order.TypicalDay) is { } stop && fill > stop
            ? Of(order, fill, stop)
            : null;
}
