using EquityBrief.Core.Prices;
using EquityBrief.Core.Returns;

namespace EquityBrief.Core.Sweep;

// The setup horizon's walk with the stop moved to the entry once a close stands the plan's risk above it: the
// exit the sweep reads beside the fixed stop the live scorer reads. Everything else is the scorer's own rule, in
// its order and on closes, the fill entered at the first close inside the plan's range and a target reached
// before the fill never entered; the move is read after a close's stop and target, so the stop a session moves
// is the next session's. Where the move never fires the answer is the scorer's own.
// see: A setup is scored from its entry, and a target reached before the entry is never a win
// see: An unresolved setup is never a win
public static class SweepWalk
{
    // The close a setup was entered at, by the scorer's own rule: the listing's close where it sits inside the
    // plan's range, and otherwise the first close after it at or under the zone's top edge, a close through the
    // stop or at the target before that leaving none. The scorer states the fill on a win and on a loss and not
    // on a setup that ran out of sessions, whose result the sweep counts at its last close as its benchmark
    // counts it, so the fill it is measured from is read here.
    public static decimal? FillOf(IReadOnlyList<ReturnBar> after, decimal stop, decimal target, decimal? entryHigh, decimal closeAtListing, int cap)
    {
        var highestEntry = entryHigh is { } top && top < target ? top : target;

        if (closeAtListing >= stop && closeAtListing <= highestEntry)
        {
            return closeAtListing;
        }

        for (var session = 0; session < Math.Min(after.Count, cap); session++)
        {
            var close = after[session].Close;

            if (close < stop || close >= target)
            {
                return null;
            }

            if (close <= highestEntry)
            {
                return close;
            }
        }

        return null;
    }

    // The ideas' run's touched stop, for a plan bought at the listing's close: a session opening under the stop
    // sells at its open, one whose low reaches the stop sells at the stop, the stop read before the target so a
    // session whose range holds both is a loss, a close at or above the target sells at that close, and the cap's
    // close ends a trade neither reached. The result is in multiples of the risk, the buy less its stop; none
    // where the series runs out first, the trade holding its stock for its cap. A session holding no open is
    // read from its low alone.
    // see: A new idea is added to the base one at a time and kept only where it is better in six of eight years
    public static double? TouchedStop(ReadOnlySpan<double> opens, ReadOnlySpan<double> lows, ReadOnlySpan<double> closes, int from, double entry, double stop, double target, int cap, out int sessions)
    {
        sessions = cap;

        var risk = entry - stop;

        if (risk <= 0 || target <= entry)
        {
            return null;
        }

        for (var session = 1; session <= cap; session++)
        {
            var at = from + session;

            if (at >= closes.Length)
            {
                return null;
            }

            if (opens[at] > 0 && opens[at] < stop)
            {
                sessions = session;

                return (opens[at] - entry) / risk;
            }

            if (lows[at] <= stop)
            {
                sessions = session;

                return (stop - entry) / risk;
            }

            if (closes[at] >= target || session == cap)
            {
                sessions = session;

                return (closes[at] - entry) / risk;
            }
        }

        return null;
    }

    // The touched stop under a trailing stop, for a family whose plan trails and names no target: the stop starts
    // where the plan put it and follows the highest close since the buy at the trail's distance, never lowered; a
    // session opening under it sells at its open, one whose low reaches it sells at the stop, and the cap's close
    // ends a trade neither reached. The stop a session is read against is the one the closes before it set. The
    // result is in multiples of the risk, the buy less the plan's stop; none where the series runs out first.
    // see: The frozen families are read with the pullback's ideas one at a time, and nothing they show is frozen or registered
    public static double? TouchedTrailing(ReadOnlySpan<double> opens, ReadOnlySpan<double> lows, ReadOnlySpan<double> closes, int from, double entry, double stop, double trail, int cap, out int sessions)
    {
        sessions = cap;

        var risk = entry - stop;

        if (risk <= 0 || trail <= 0)
        {
            return null;
        }

        var floor = stop;

        for (var session = 1; session <= cap; session++)
        {
            var at = from + session;

            if (at >= closes.Length)
            {
                return null;
            }

            if (opens[at] > 0 && opens[at] < floor)
            {
                sessions = session;

                return (opens[at] - entry) / risk;
            }

            if (lows[at] <= floor)
            {
                sessions = session;

                return (floor - entry) / risk;
            }

            if (session == cap)
            {
                sessions = session;

                return (closes[at] - entry) / risk;
            }

            floor = Math.Max(floor, closes[at] - trail);
        }

        return null;
    }

    public static ForwardReturn OverSetupMovingTheStop(
        IReadOnlyList<ReturnBar> after,
        decimal stop,
        decimal target,
        decimal? entryHigh,
        decimal closeAtListing,
        int cap)
    {
        if (stop >= target)
        {
            throw new InvalidOperationException($"A plan with its stop at {stop} and its target at {target} is not a plan.");
        }

        var floor = stop;
        var ceiling = target;
        var highestEntry = entryHigh is { } top && top < ceiling ? top : ceiling;

        decimal? entry = closeAtListing >= floor && closeAtListing <= highestEntry ? closeAtListing : null;
        var filledAt = entry is null ? -1 : 0;
        decimal? movesAt = entry is { } fill ? fill + (fill - floor) : null;

        for (var session = 0; session < Math.Min(after.Count, cap); session++)
        {
            var bar = after[session];

            if (bar.Close < floor)
            {
                return entry is { } at
                    ? new ForwardReturn(ForwardReturnSeries.Setup, ForwardReturnSeries.Loss, bar.SessionDate, ForwardReturnSeries.ChangeFromEntry(at, bar.Close), ForwardReturnSeries.BreakEven(at, stop, ceiling), at, cap - filledAt)
                    : new ForwardReturn(ForwardReturnSeries.Setup, ForwardReturnSeries.Loss, bar.SessionDate, null, null, highestEntry, cap - (session + 1));
            }

            if (entry is null)
            {
                if (bar.Close >= ceiling)
                {
                    return new ForwardReturn(ForwardReturnSeries.Setup, ForwardReturnSeries.NeverEntered, bar.SessionDate, null);
                }

                if (bar.Close <= highestEntry)
                {
                    entry = bar.Close;
                    filledAt = session + 1;
                    movesAt = bar.Close + (bar.Close - floor);
                }

                continue;
            }

            if (bar.Close >= ceiling)
            {
                return new ForwardReturn(ForwardReturnSeries.Setup, ForwardReturnSeries.Win, bar.SessionDate, ForwardReturnSeries.ChangeFromEntry(entry.Value, bar.Close), ForwardReturnSeries.BreakEven(entry.Value, stop, ceiling), entry.Value, cap - filledAt);
            }

            if (movesAt is { } moves && bar.Close >= moves && floor < entry.Value)
            {
                floor = entry.Value;
            }
        }

        if (after.Count < cap)
        {
            return new ForwardReturn(ForwardReturnSeries.Setup, null, null, null);
        }

        var last = after[cap - 1];

        return entry is null
            ? new ForwardReturn(ForwardReturnSeries.Setup, ForwardReturnSeries.NeverEntered, last.SessionDate, null)
            : new ForwardReturn(ForwardReturnSeries.Setup, ForwardReturnSeries.Unresolved, last.SessionDate, ForwardReturnSeries.ChangeFromEntry(entry.Value, last.Close), ForwardReturnSeries.BreakEven(entry.Value, stop, ceiling));
    }
}
