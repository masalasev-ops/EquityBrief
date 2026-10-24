using EquityBrief.Core.Families;

namespace EquityBrief.Core.Ledger;

// A short-term dip's setup's path. Each setup is bought at its session's close at the provisional plan, its stop the
// plan's typical moves under that close and no target, and walked by the rule's own walk: sold at the next session's
// open after a close under the stop or above the plan's average of closes, and at the close of the time limit's last
// session where neither came; a session the stock's bars skip stops the walk there with the trade open. Its result is
// in multiples of the risk the close set, and the benchmark is the same plan on every member of the index that session,
// each in its own typical moves, settled once every member entered has ended or been left out because its prices
// stopped.
// see: The dip buys at the next session's open and sells at the next open after its exit's close or its stop's, and at its last session's close
// see: A setup's benchmark leaves out a member whose prices stop before its trade ends, as the sweeps do
public static class DipPaths
{
    // The plan every dip setup is read at, the provisional rule's.
    public static DipSetting Plan => DipRule.Provisional;

    // The anchor the plan places on a member at its close and its typical move, none where the move or the stop places no
    // trade.
    public static SetupAnchor? Anchor(DateOnly session, double close, double move)
    {
        var risk = Plan.StopMoves * move;

        if (!(move > 0) || !(risk > 0) || close - risk <= 0)
        {
            return null;
        }

        return new SetupAnchor(session, close, close - risk, null, null, Plan.TimeLimit, Plan.StopMoves);
    }

    // The path over a member's closes and opens oldest first, the anchor's bar at `from`, with each bar's place among the
    // calendar's sessions, which tells a session the bars skip: the sessions it was held to its sale, the close it ended
    // on where the time limit sold it.
    public static SetupOutcome Replay(double[] closes, double[] opens, int[] sessionAt, int from, SetupAnchor anchor)
    {
        if (from < 0 || from >= closes.Length || !(anchor.Risk > 0))
        {
            return new SetupOutcome(null, 0, SetupEnds.None);
        }

        var after = new List<DipSession>(anchor.Cap + 1);

        for (var at = from + 1; at < closes.Length && after.Count <= anchor.Cap; at++)
        {
            if (sessionAt[at] - sessionAt[at - 1] > 1)
            {
                break;
            }

            after.Add(new DipSession(default, at < opens.Length ? opens[at] : 0, closes[at], DipRule.Average(closes, at, Plan.ExitAverage)));
        }

        var path = DipRule.Walk(closes[from], anchor.Stop, anchor.Cap, after);

        return path.End switch
        {
            DipEnd.Open => new SetupOutcome(null, path.Sessions, SetupEnds.Open),
            DipEnd.Limit => new SetupOutcome(path.ResultAtClose, path.Sessions, SetupEnds.Cap),
            DipEnd.Stop => new SetupOutcome(path.ResultAtClose, path.Sessions + 1, SetupEnds.Stop),
            _ => new SetupOutcome(path.ResultAtClose, path.Sessions + 1, SetupEnds.Exit),
        };
    }

    // The same plan on every member as the pricing at the next open reads it: each member's closes, opens and session
    // places with the place of the session's bar in them, its typical move there and whether its prices stopped before the
    // newest session the read holds; a member with no bar on the session or no move is not entered, and one whose prices
    // stopped with its path still open is left out.
    public static (BenchmarkReading Reading, IReadOnlyList<FillMember> Members) WithMembers(IReadOnlyList<(double[] Closes, double[] Opens, int[] SessionAt, int Bar, double Move, bool Stopped)> members, DateOnly session)
    {
        var sum = 0.0;
        var ended = 0;
        var entered = 0;
        var stopped = 0;
        var listed = new List<FillMember>();

        foreach (var (closes, opens, sessionAt, bar, move, pricesStopped) in members)
        {
            if (bar < 0 || bar >= closes.Length || Anchor(session, closes[bar], move) is not { } anchor)
            {
                continue;
            }

            entered++;

            var outcome = Replay(closes, opens, sessionAt, bar, anchor);

            if (outcome.Result is { } result)
            {
                sum += result;
                ended++;
            }
            else if (pricesStopped)
            {
                stopped++;
            }

            listed.Add(new FillMember(outcome.Result is not null, closes[bar], anchor.Risk, bar + 1 < opens.Length && bar + 1 < closes.Length ? opens[bar + 1] : null));
        }

        return (new BenchmarkReading(ended > 0 ? sum / ended : null, ended, entered, stopped), listed);
    }
}
