using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Core.Ledger;

// A split a table holds: its day and the shares one share became, the new over the old.
public sealed record StoredSplit(DateOnly Day, double Ratio);

// The splits a stock's closes are read across: a session whose close as traded over its close at the store's scale
// steps by the share check's split step or more from the session before's, where a split table holds a split within the
// days of it whose ratio is the step's own. A step no table holds a split of its size for is read as no split, and a split
// a table holds whose closes show no step of its size is read as none either; both are listed where a person reads them.
// see: A split is read only where a split table holds it and the closes step by it, and a step no table holds is listed and never read
// see: A split table's entry is read only where the closes step by its own ratio, and an entry of another size is set aside and listed
public static class StoredSplits
{
    // The days either side of a step a table's split may stand on and still be its split.
    public const int WithinDays = 5;

    // How far a step may stand from the one its split's ratio makes, as a share: a split on a dividend's ex-date steps by
    // the split and the dividend together.
    public const double RatioWithin = 0.10;

    // Whether a table holds a split within the days of a session whose ratio is the step's: the step times the new
    // shares over the old within the share of one.
    public static bool Holds(IReadOnlyList<StoredSplit> splits, DateOnly session, double step) =>
        splits.Any(split => Math.Abs(split.Day.DayNumber - session.DayNumber) <= WithinDays && Math.Abs((step * split.Ratio) - 1) <= RatioWithin);

    // The step of a session's close as traded over its close at the store's scale against the session before's, none
    // where either session holds no close as traded.
    static decimal? Step(SweepBar before, SweepBar after) =>
        before.Close > 0m && before.RawClose > 0m && after.Close > 0m && after.RawClose > 0m
            ? after.RawClose / after.Close / (before.RawClose / before.Close)
            : null;

    public static bool Steps(double step) => step >= FundamentalScorecard.SplitStep || step <= 1 / FundamentalScorecard.SplitStep;

    // For each bar, what one share the stored volume counts stood for on its session: the product of the steps of every
    // split read after it, so the stored volume, which the provider restates for each later split, times it is the
    // shares that traded that day.
    public static decimal[] SharesTraded(IReadOnlyList<SweepBar> bars, IReadOnlyList<StoredSplit> splits)
    {
        var factors = new decimal[bars.Count];
        var product = 1m;

        for (var at = bars.Count - 1; at >= 0; at--)
        {
            factors[at] = product;

            if (at > 0 && Step(bars[at - 1], bars[at]) is { } step && Statistic.FromRatio(step) is var ratio && Steps(ratio) && Holds(splits, bars[at].Session, ratio))
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

    // What a person reads beside the share check: each step of the split step or more no table holds a split of its size
    // within the days of, and each split a table holds within the closes' span whose closes show no step of its size
    // within its days.
    public static (IReadOnlyList<(DateOnly Session, decimal Step)> Unheld, IReadOnlyList<StoredSplit> Unseen) Unmatched(IReadOnlyList<SweepBar> bars, IReadOnlyList<StoredSplit> splits)
    {
        var unheld = new List<(DateOnly, decimal)>();
        var steps = new List<(DateOnly Session, double Step)>();

        for (var at = 1; at < bars.Count; at++)
        {
            if (Step(bars[at - 1], bars[at]) is { } step && Statistic.FromRatio(step) is var ratio && Steps(ratio))
            {
                steps.Add((bars[at].Session, ratio));

                if (!Holds(splits, bars[at].Session, ratio))
                {
                    unheld.Add((bars[at].Session, step));
                }
            }
        }

        var unseen = bars.Count == 0
            ? []
            : splits
                .Where(split => split.Day > bars[0].Session && split.Day <= bars[^1].Session)
                .Where(split => !steps.Any(step => Holds([split], step.Session, step.Step)))
                .OrderBy(split => split.Day)
                .ToList();

        return (unheld, unseen);
    }
}
