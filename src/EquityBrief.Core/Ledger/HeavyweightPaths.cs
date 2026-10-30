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

    // `notBought` is every later rebalance, by its place in the closes, at which the rule did not buy the stock; from 21.4 a
    // book family's setup is replayed the same way, `notBought` the later sessions its gate no longer holds it on and
    // `sold` the end its sale is stored under.
    public static SetupOutcome Replay(ReadOnlySpan<double> closes, int from, IReadOnlyList<int> notBought, int cap = Cap, string sold = SetupEnds.Rebalance)
    {
        if (from < 0 || from >= closes.Length || !(closes[from] > 0))
        {
            return new SetupOutcome(null, 0, SetupEnds.None);
        }

        var soldAt = notBought.Where(at => at > from && at < from + cap).DefaultIfEmpty(int.MaxValue).Min();
        var end = Math.Min(soldAt, from + cap);

        if (end >= closes.Length)
        {
            return new SetupOutcome(null, closes.Length - 1 - from, SetupEnds.Open);
        }

        return new SetupOutcome((closes[end] / closes[from]) - 1, end - from, end == soldAt ? sold : SetupEnds.Cap);
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

    // The size cut's members as the pricing at the next open reads them: each entered member, whether it holds a close the
    // path's sessions on, its close at the buy, which a book's result is a fraction of, and the open of its next session.
    // see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
    public static IReadOnlyList<FillMember> CutMembers(IReadOnlyList<(double[] Closes, double[] Opens, int From)> members, int sessions) =>
    [
        .. members
            .Where(member => member.From >= 0 && member.From < member.Closes.Length && member.Closes[member.From] > 0)
            .Select(member => new FillMember(
                member.From + sessions < member.Closes.Length,
                member.Closes[member.From],
                member.Closes[member.From],
                member.From + 1 < member.Opens.Length && member.From + 1 < member.Closes.Length ? member.Opens[member.From + 1] : null)),
    ];
}
