namespace EquityBrief.Core.Sweep;

// The exits a setup family's sweep walks its trades and their benchmarks under, on closes, each bought at the
// listing's close, with the result in multiples of the risk the plan put up: the buy less its stop.
// see: A setup family's sweep replays its own rule over the stored history and proposes the best edge among the settings meeting its floors, or brings the strongest where none does
public static class FamilyWalks
{
    // A trailing stop and no target: the stop starts where the plan put it and follows the highest close since
    // the buy at the given distance, never lowered; the trade is sold at the first close under it and at its
    // cap's close where none came. None where the series runs out before either, the trade not yet over.
    public static double? Trailing(ReadOnlySpan<double> closes, int from, double entry, double stop, double trail, int cap, out int sessions)
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

            var close = closes[at];

            if (close < floor)
            {
                sessions = session;

                return (close - entry) / risk;
            }

            floor = Math.Max(floor, close - trail);

            if (session == cap)
            {
                return (close - entry) / risk;
            }
        }

        return null;
    }

    // A stop and a target: a close under the stop sells at that close, a close at or above the target sells
    // there, and the cap's close ends a trade neither reached. None where the series runs out first.
    public static double? Fixed(ReadOnlySpan<double> closes, int from, double entry, double stop, double target, int cap, out int sessions)
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

            var close = closes[at];

            if (close < stop || close >= target)
            {
                sessions = session;

                return (close - entry) / risk;
            }

            if (session == cap)
            {
                return (close - entry) / risk;
            }
        }

        return null;
    }
}
