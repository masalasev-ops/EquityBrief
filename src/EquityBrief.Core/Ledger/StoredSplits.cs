using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Core.Ledger;

// The splits a stock's closes are read across: a session whose close as traded over its close at the store's scale
// steps by the share check's split step or more from the session before's, where a split table holds a split within
// the days of it. A step no table holds is read as no split, and a split a table holds whose closes show no step is
// read as none either; both are listed where a person reads them.
// see: A split is read only where a split table holds it and the closes step by it, and a step no table holds is listed and never read
public static class StoredSplits
{
    // The days either side of a step a table's split may stand on and still be its split.
    public const int WithinDays = 5;

    // Whether a sorted list of a table's split days holds one within the days of a session.
    public static bool Holds(DateOnly[] splits, DateOnly session)
    {
        var at = Array.BinarySearch(splits, session);

        if (at >= 0)
        {
            return true;
        }

        var next = ~at;

        return (next < splits.Length && splits[next].DayNumber - session.DayNumber <= WithinDays)
            || (next > 0 && session.DayNumber - splits[next - 1].DayNumber <= WithinDays);
    }

    // The step of a session's close as traded over its close at the store's scale against the session before's, none
    // where either session holds no close as traded.
    static decimal? Step(SweepBar before, SweepBar after) =>
        before.Close > 0m && before.RawClose > 0m && after.Close > 0m && after.RawClose > 0m
            ? after.RawClose / after.Close / (before.RawClose / before.Close)
            : null;

    static bool Steps(decimal step) =>
        Statistic.FromRatio(step) is var ratio && (ratio >= FundamentalScorecard.SplitStep || ratio <= 1 / FundamentalScorecard.SplitStep);

    // For each bar, what one share the stored volume counts stood for on its session: the product of the steps of every
    // split read after it, so the stored volume, which the provider restates for each later split, times it is the
    // shares that traded that day.
    public static decimal[] SharesTraded(IReadOnlyList<SweepBar> bars, IReadOnlyCollection<DateOnly> splits)
    {
        DateOnly[] held = [.. splits.Distinct().Order()];
        var factors = new decimal[bars.Count];
        var product = 1m;

        for (var at = bars.Count - 1; at >= 0; at--)
        {
            factors[at] = product;

            if (at > 0 && Step(bars[at - 1], bars[at]) is { } step && Steps(step) && Holds(held, bars[at].Session))
            {
                product *= step;
            }
        }

        return factors;
    }

    // The dollars that changed hands on each session of a window: the close as traded, or the close at the store's scale
    // where none is held, times the stored volume brought back to the shares that traded that day.
    public static IReadOnlyList<(decimal Close, long Volume)> TradedDollars(IReadOnlyList<SweepBar> bars, decimal[] shares, int from, int to)
    {
        var window = new (decimal Close, long Volume)[to - from + 1];

        for (var at = from; at <= to; at++)
        {
            var bar = bars[at];

            window[at - from] = ((bar.RawClose > 0m ? bar.RawClose : bar.Close) * shares[at], bar.Volume);
        }

        return window;
    }

    // What a person reads beside the share check: each step of the split step or more no table holds a split within the
    // days of, and each split a table holds within the closes' span whose closes show no such step within its days.
    public static (IReadOnlyList<(DateOnly Session, decimal Step)> Unheld, IReadOnlyList<DateOnly> Unseen) Unmatched(IReadOnlyList<SweepBar> bars, IReadOnlyCollection<DateOnly> splits)
    {
        DateOnly[] held = [.. splits.Distinct().Order()];
        var unheld = new List<(DateOnly, decimal)>();
        var steps = new List<DateOnly>();

        for (var at = 1; at < bars.Count; at++)
        {
            if (Step(bars[at - 1], bars[at]) is { } step && Steps(step))
            {
                steps.Add(bars[at].Session);

                if (!Holds(held, bars[at].Session))
                {
                    unheld.Add((bars[at].Session, step));
                }
            }
        }

        DateOnly[] stepped = [.. steps];
        var unseen = bars.Count == 0
            ? []
            : held.Where(day => day > bars[0].Session && day <= bars[^1].Session && !Holds(stepped, day)).ToList();

        return (unheld, unseen);
    }
}
