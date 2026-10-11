namespace EquityBrief.Core.Ledger;

// A setup's plan stated in typical moves, so the same plan can be entered on every member in each member's own move:
// the stop that many moves under the buy, and either a target that many risks above it or a trail that many moves
// under the highest close.
public sealed record SetupPlan(double StopMoves, double? RewardToRisk, double? TrailMoves, int Cap)
{
    public bool Trails => TrailMoves is not null;

    // The anchor this plan places on one member at its close and its typical move, none where the move or the stop
    // places no trade.
    public SetupAnchor? On(DateOnly session, double close, double move)
    {
        var risk = StopMoves * move;

        if (!(move > 0) || !(risk > 0) || close - risk <= 0)
        {
            return null;
        }

        return new SetupAnchor(
            session,
            close,
            close - risk,
            Trails ? null : close + (RewardToRisk!.Value * risk),
            Trails ? TrailMoves!.Value * move : null,
            Cap,
            StopMoves);
    }
}

// What the same plan came to on every member of the index that session: the mean of the results the closes reach the
// end of, how many that is, how many members the plan was entered on, and how many of those were left out because
// their prices stopped before their path ended. The benchmark is settled once every member entered has ended its path
// or been left out and at least one has ended, and a setup's edge is read against it only then; before that the mean
// is over the members ended so far and is not the benchmark, and a session no member was entered on has none.
// see: A setup's benchmark leaves out a member whose prices stop before its trade ends, as the sweeps do
public sealed record BenchmarkReading(double? Mean, int Ended, int Entered, int Stopped = 0)
{
    public bool Settled => Ended > 0 && Ended + Stopped == Entered;
}

public static class SetupBenchmark
{
    // Each member's closes with the place of the session's bar in them, its typical move on that bar, and whether its
    // prices stopped before the newest session the read holds, as a takeover's or a delisting's do; a member with no bar
    // on the session or no move is not entered, and one whose prices stopped with its path still open is left out.
    public static BenchmarkReading Of(IReadOnlyList<(double[] Closes, int Bar, double Move, bool Stopped)> members, DateOnly session, SetupPlan plan)
    {
        var sum = 0.0;
        var ended = 0;
        var entered = 0;
        var stopped = 0;

        foreach (var (closes, bar, move, pricesStopped) in members)
        {
            if (bar < 0 || bar >= closes.Length || plan.On(session, closes[bar], move) is not { } anchor)
            {
                continue;
            }

            entered++;

            var outcome = SetupReplay.Replay(closes, bar, anchor);

            if (outcome.Result is { } result)
            {
                sum += result;
                ended++;
            }
            else if (pricesStopped)
            {
                stopped++;
            }
        }

        return new BenchmarkReading(ended > 0 ? sum / ended : null, ended, entered, stopped);
    }
}
