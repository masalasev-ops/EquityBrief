using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// A pullback trade's path replayed from its buy under one of three exits, on closes, as the benchmark walks every
// member: a close under the stop loses and one at or over the target wins, each read before a move, which sets the
// next session's stop, and the cap's close ends a trade neither reached. Under the fixed exit the stop stands; under
// the second it moves to the buy once a close stands a risk up; under the third, once one does, it trails two typical
// moves under the highest close since the buy, never lowered.
// see: The S&P 400 pullback with profit and cover is read at half steps about the setting the brief names and its trades under three exits
public static class SweepExits
{
    public const int Fixed = 0;

    public const int BreakEven = 1;

    public const int Trail = 2;

    // The trailing stop's distance under the highest close, in typical moves.
    public const double TrailMoves = 2;

    public static string Words(int exit) => exit switch
    {
        Fixed => "the stop where the plan puts it",
        BreakEven => "the stop to the buy once a close stands a risk up",
        _ => "a stop trailing 2 typical moves under the highest close once a close stands a risk up",
    };

    // The result in multiples of the risk, none where the series runs out before the trade ends.
    public static double? Replay(double[] closes, int from, double entry, double stop, double target, double move, int exit, int cap)
    {
        var risk = entry - stop;

        if (!(risk > 0) || !(target > entry))
        {
            return null;
        }

        var floor = stop;
        var highest = entry;
        var armed = false;

        for (var session = 1; session <= cap; session++)
        {
            var at = from + session;

            if (at >= closes.Length)
            {
                return null;
            }

            var close = closes[at];

            if (close < floor || close >= target || session == cap)
            {
                return (close - entry) / risk;
            }

            highest = Math.Max(highest, close);
            armed |= exit != Fixed && close >= entry + risk;

            if (armed)
            {
                floor = Math.Max(floor, exit == BreakEven ? entry : highest - (TrailMoves * move));
            }
        }

        return null;
    }

    // The same plan entered at the session's close on every member the index held with a bar and no gap, its stop and
    // target at the same distances in each member's own typical moves, replayed under the same exit: the mean of the
    // results the history reaches the end of.
    public static double Benchmark(IReadOnlyList<SweepSeries> series, double[][] closes, SweepBenchmark.Members members, int session, double stopMoves, double rewardToRisk, int exit, int cap)
    {
        var names = members.Names[session];
        var bars = members.Bars[session];
        var (sum, count) = (0.0, 0);

        for (var at = 0; at < names.Length; at++)
        {
            var move = series[names[at]].Atr[bars[at]];
            var entry = closes[names[at]][bars[at]];
            var risk = stopMoves * move;

            if (!(risk > 0) || entry - risk <= 0)
            {
                continue;
            }

            if (Replay(closes[names[at]], bars[at], entry, entry - risk, entry + (rewardToRisk * risk), move, exit, cap) is { } result)
            {
                sum += result;
                count++;
            }
        }

        return count > 0 ? sum / count : double.NaN;
    }
}
