using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// The benchmark the edge is read against: for each candidate, plan and exit, the average result of the same
// plan entered at the same session's close on every member the index held with a bar and no gap, the stop and
// target at the same distances in each member's own typical moves, walked on real closes under the same exit.
// A no-skill walk has no drift, so its average is minus the round trip whatever the market did; this is what
// the market did over the same window in the same shape, on the operator's ruling of 2026-09-30.
// see: The sweep ranks on the edge over the same plan entered on every member, with one open trade a stock and seven conditions tested in steps
public static class SweepBenchmark
{
    // The members read on each session: those with a bar and no gap, as the night reads its members.
    public sealed record Members(int[][] Names, int[][] Bars);

    public static Members On(IReadOnlyList<SweepSeries> series, int sessions)
    {
        var names = new List<int>[sessions];
        var bars = new List<int>[sessions];

        for (var session = 0; session < sessions; session++)
        {
            names[session] = [];
            bars[session] = [];
        }

        for (var name = 0; name < series.Count; name++)
        {
            var one = series[name];

            for (var bar = 0; bar < one.Bars.Length; bar++)
            {
                if (one.Member[bar] && !one.Gap[bar])
                {
                    names[one.SessionAt[bar]].Add(name);
                    bars[one.SessionAt[bar]].Add(bar);
                }
            }
        }

        return new Members([.. names.Select(list => list.ToArray())], [.. bars.Select(list => list.ToArray())]);
    }

    // Fills every plan's benchmark under each exit, over the candidates handed in.
    public static void Fill(IReadOnlyList<SweepCandidate> candidates, IReadOnlyList<SweepSeries> series, Members members, int parallelism)
    {
        var closes = series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray()).ToArray();

        Parallel.For(0, candidates.Count, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, at => Benchmark(candidates[at], series, closes, members));
    }

    static void Benchmark(SweepCandidate candidate, IReadOnlyList<SweepSeries> series, double[][] closes, Members members)
    {
        var names = members.Names[candidate.Session];
        var bars = members.Bars[candidate.Session];
        Span<double> sums = stackalloc double[SweepAxes.Exits];
        Span<int> counts = stackalloc int[SweepAxes.Exits];
        Span<double> outcome = stackalloc double[SweepAxes.Holds.Count];
        Span<bool> has = stackalloc bool[SweepAxes.Holds.Count];

        foreach (var plan in candidate.Plans)
        {
            if (plan is null)
            {
                continue;
            }

            sums.Clear();
            counts.Clear();

            for (var member = 0; member < names.Length; member++)
            {
                var name = names[member];
                var bar = bars[member];
                var atr = series[name].Atr[bar];

                if (double.IsNaN(atr) || atr <= 0)
                {
                    continue;
                }

                var entry = closes[name][bar];
                var risk = plan.StopMoves * atr;

                if (risk <= 0 || entry <= 0)
                {
                    continue;
                }

                var stop = entry - risk;
                var target = entry + (plan.RewardToRisk * risk);

                for (var move = 0; move < 2; move++)
                {
                    Walk(closes[name], bar, entry, stop, target, risk, move == 1, outcome, has);

                    for (var hold = 0; hold < SweepAxes.Holds.Count; hold++)
                    {
                        if (has[hold])
                        {
                            var exit = (hold * 2) + move;

                            sums[exit] += outcome[hold];
                            counts[exit]++;
                        }
                    }
                }
            }

            for (var exit = 0; exit < SweepAxes.Exits; exit++)
            {
                plan.Benchmark[exit] = counts[exit] > 0 ? (float)(sums[exit] / counts[exit]) : float.NaN;
            }
        }
    }

    // The same walk the sweep's own trades take, entered at the close: a close below the stop loses, a close at or
    // above the target wins, each read before the move to break-even, which sets the next session's stop; at each
    // hold's cap the trade still open ends at that close, and a series that runs out before a cap has no result
    // there. The result is the close reached over the entry, in multiples of the risk.
    public static void Walk(double[] closes, int from, double entry, double stop, double target, double risk, bool moveStop, Span<double> outcome, Span<bool> has)
    {
        var holds = SweepAxes.Holds;
        var floor = stop;
        var movesAt = entry + risk;
        var holdAt = 0;

        has.Clear();

        for (var session = 1; session <= holds[^1]; session++)
        {
            var index = from + session;

            if (index >= closes.Length)
            {
                return;
            }

            var close = closes[index];

            if (close < floor || close >= target)
            {
                var result = (close - entry) / risk;

                for (var hold = holdAt; hold < holds.Count; hold++)
                {
                    outcome[hold] = result;
                    has[hold] = true;
                }

                return;
            }

            if (moveStop && close >= movesAt && floor < entry)
            {
                floor = entry;
            }

            while (holdAt < holds.Count && session == holds[holdAt])
            {
                outcome[holdAt] = (close - entry) / risk;
                has[holdAt] = true;
                holdAt++;
            }
        }
    }
}
