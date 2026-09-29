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
