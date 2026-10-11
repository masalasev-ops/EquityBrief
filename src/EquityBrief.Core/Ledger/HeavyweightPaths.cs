namespace EquityBrief.Core.Ledger;

// A heavyweights' setup's path. Each member of its sector's size cut on a rebalance is bought at that close and held as
// the rule holds a stock it bought: to the close of the first later rebalance at which the rule does not buy it, or to
// the cap's close, whichever comes first, open where the closes end before either. The rule places no stop, so the
// result is the close it ended at over the buy, less one, a fraction of the buy and never a multiple of a risk; and the
// benchmark is the size cut it was chosen from over the same sessions, each member's close at the path's end over its
// close at the buy, less one, averaged over the members holding both.
// see: A sector heavyweight's trade is scored by its percent return less the equal-weighted return of the size cut it was chosen from
// see: A heavyweights' setup is each member of its sector's size cut on a rebalance of the S&P 500's book, held as the rule holds a buy
public static class HeavyweightPaths
{
    // The sessions a heavyweights' setup is given, a year's.
    public const int Cap = 252;

    // `notBought` is every later rebalance, by its place in the closes, at which the rule did not buy the stock.
    public static SetupOutcome Replay(ReadOnlySpan<double> closes, int from, IReadOnlyList<int> notBought, int cap = Cap)
    {
        if (from < 0 || from >= closes.Length || !(closes[from] > 0))
        {
            return new SetupOutcome(null, 0, SetupEnds.None);
        }

        var sold = notBought.Where(at => at > from && at < from + cap).DefaultIfEmpty(int.MaxValue).Min();
        var end = Math.Min(sold, from + cap);

        if (end >= closes.Length)
        {
            return new SetupOutcome(null, closes.Length - 1 - from, SetupEnds.Open);
        }

        return new SetupOutcome((closes[end] / closes[from]) - 1, end - from, end == sold ? SetupEnds.Rebalance : SetupEnds.Cap);
    }

    // The size cut's return over a path's sessions: each member's closes with the place of the buy's session in them and
    // whether its prices stopped before the newest session the read holds, a member with no close there not entered, one
    // whose prices stopped short of the path's end left out, and the benchmark settled once every other member entered
    // holds a close the path's sessions on.
    // see: A setup's benchmark leaves out a member whose prices stop before its trade ends, as the sweeps do
    public static BenchmarkReading Cut(IReadOnlyList<(double[] Closes, int From, bool Stopped)> members, int sessions)
    {
        var sum = 0.0;
        var ended = 0;
        var entered = 0;
        var left = 0;

        foreach (var (closes, from, stopped) in members)
        {
            if (from < 0 || from >= closes.Length || !(closes[from] > 0))
            {
                continue;
            }

            entered++;

            if (from + sessions < closes.Length)
            {
                sum += (closes[from + sessions] / closes[from]) - 1;
                ended++;
            }
            else if (stopped)
            {
                left++;
            }
        }

        return new BenchmarkReading(ended > 0 ? sum / ended : null, ended, entered, left);
    }
}
