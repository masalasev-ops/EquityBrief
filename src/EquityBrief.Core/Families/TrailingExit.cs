using EquityBrief.Core.Prices;
using EquityBrief.Core.Returns;

namespace EquityBrief.Core.Families;

// A trade sold on a trailing stop and no target, scored from the close it was bought at.
//
// The stop starts where the plan put it and follows the highest close since the buy at the distance the
// plan set, the buy's close less its stop, and it is never lowered. The trade is sold at the first close
// under the stop, and at the close of its last session where none came. Everything is read on closes, as a
// setup's stop and target are, because a daily bar does not say what order a session's prices came in.
//
// It has no win, no loss and no break-even: with no target there is no bar it set for itself. Its result
// is what it made, and that against what it put at risk is the figure its family's record reads.
// see: A breakout is a close above the year's high on heavy volume after its ranges narrowed, sold on a trailing stop with no target
public static class TrailingExit
{
    // A trade sold at a close under its trailing stop.
    public const string Trailed = "trailed";

    public static ForwardReturn Over(IReadOnlyList<ReturnBar> after, decimal entry, decimal stop, string horizon, int cap)
    {
        if (entry <= 0 || stop >= entry)
        {
            throw new InvalidOperationException(
                $"The stored plan was bought at {entry} with its stop at {stop}. A stop at or above the buy is not a plan, " +
                "and a trail measured from it would follow the price from above.");
        }

        var trail = entry - stop;
        var floor = stop;

        for (var session = 0; session < Math.Min(after.Count, cap); session++)
        {
            var bar = after[session];

            if (bar.Close < floor)
            {
                return new ForwardReturn(horizon, Trailed, bar.SessionDate, ForwardReturnSeries.ChangeFromEntry(entry, bar.Close), null, entry);
            }

            floor = Math.Max(floor, bar.Close - trail);
        }

        if (after.Count < cap)
        {
            return new ForwardReturn(horizon, null, null, null);
        }

        var last = after[cap - 1];

        return new ForwardReturn(horizon, ForwardReturnSeries.Unresolved, last.SessionDate, ForwardReturnSeries.ChangeFromEntry(entry, last.Close), null, entry);
    }

    // Where the stop stands after the sessions so far, for a trade still open: the plan's stop raised to the
    // highest close since the buy less the plan's distance.
    public static decimal StopAfter(IReadOnlyList<ReturnBar> after, decimal entry, decimal stop)
    {
        var trail = entry - stop;

        return after.Aggregate(stop, (floor, bar) => Math.Max(floor, bar.Close - trail));
    }

    // What the plan put at risk from the buy, as a percentage of it, which is what a result is divided by
    // to be read in multiples of the risk.
    public static double? RiskPct(decimal entry, decimal stop) =>
        entry > 0 && stop < entry ? Statistic.FromRatio((entry - stop) / entry) * 100 : null;
}
