using EquityBrief.Core.Sweep;

namespace EquityBrief.Core.Ledger;

// What a setup stores of its own path: the session it was bought on at the close, the buy, the stop as the plan put it,
// the target where the plan names one, the trail's distance where the stop follows the highest close instead, the
// sessions the trade is given, and the stop's distance below the buy in the night's typical moves. Everything a path
// shows is replayed from bars and this, never stored a path at a time.
// see: The bars the fetcher drops are kept in a table of their own that no night reads, and a setup is stored as its anchor
public sealed record SetupAnchor(DateOnly Session, double Entry, double Stop, double? Target, double? Trail, int Cap, double? RiskMoves)
{
    public double Risk => Entry - Stop;

    public bool Trails => Trail is not null;
}

// How a replayed setup ended.
public static class SetupEnds
{
    public const string Stop = "stop";

    public const string Target = "target";

    public const string Trail = "trail";

    public const string Cap = "cap";

    // The series ran out before the trade ended, so it is open as of the last close held.
    public const string Open = "open";

    // The anchor places no trade: a stop at or above the buy, a target at or under it, or a trail of nothing.
    public const string None = "none";
}

// One setup's path under its anchor as the plan's own exit replays it: the result in multiples of the risk, the
// sessions held and how it ended.
public sealed record SetupOutcome(double? Result, int Sessions, string End);

// The one function every setup and every night's benchmark is replayed through, over closes oldest first with the
// anchor's session at `from`: the family walks' own arithmetic, a close under the stop selling at that close, one at or
// over the target selling there, the trail following the highest close since the buy and never lowered, and the cap's
// close ending a trade neither reached, each read the session after the buy onward. A setup replayed from the bar
// store on the night and from the kept bars a year later gives the same figures, since both are the same closes.
// see: The bars the fetcher drops are kept in a table of their own that no night reads, and a setup is stored as its anchor
public static class SetupReplay
{
    public static SetupOutcome Replay(ReadOnlySpan<double> closes, int from, SetupAnchor anchor)
    {
        if (!(anchor.Risk > 0) || (anchor.Trails ? !(anchor.Trail > 0) : !(anchor.Target > anchor.Entry)))
        {
            return new SetupOutcome(null, 0, SetupEnds.None);
        }

        var result = anchor.Trails
            ? FamilyWalks.Trailing(closes, from, anchor.Entry, anchor.Stop, anchor.Trail!.Value, anchor.Cap, out var sessions)
            : FamilyWalks.Fixed(closes, from, anchor.Entry, anchor.Stop, anchor.Target!.Value, anchor.Cap, out sessions);

        if (result is null)
        {
            return new SetupOutcome(null, Math.Max(0, Math.Min(anchor.Cap, closes.Length - 1 - from)), SetupEnds.Open);
        }

        return new SetupOutcome(result, sessions, EndOf(closes, from, anchor, sessions));
    }

    // Why the walk sold where it did, read back off the close it sold at: the cap where its session is the cap's and
    // the close reached neither level, the target where the close stands at or over it, the stop where the close is
    // under the plan's stop, and the trail where it is under the trail the earlier closes set.
    static string EndOf(ReadOnlySpan<double> closes, int from, SetupAnchor anchor, int sessions)
    {
        var close = closes[from + sessions];

        if (!anchor.Trails && close >= anchor.Target!.Value)
        {
            return SetupEnds.Target;
        }

        if (close < anchor.Stop)
        {
            return SetupEnds.Stop;
        }

        if (anchor.Trails)
        {
            var floor = anchor.Stop;

            for (var session = 1; session < sessions; session++)
            {
                floor = Math.Max(floor, closes[from + session] - anchor.Trail!.Value);
            }

            if (close < floor)
            {
                return SetupEnds.Trail;
            }
        }

        return SetupEnds.Cap;
    }
}
