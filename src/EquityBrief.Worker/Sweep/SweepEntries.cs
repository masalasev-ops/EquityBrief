using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// The levels an index's second stage reads of a breakout's day and of the entry the breakout and the drift may wait
// for: a close in the top quarter of the day's range; and, in place of the buy at the listing's close, the first
// session within ten after it at the pullback the level names, bought at that session's close where the market check
// is open and the member holds it with no gap.
// see: The S&P 400's and 600's breakouts and drift read an entry on the first pullback within ten sessions as a level of their second stage
public static class SweepEntries
{
    // The sessions after a breakout or a reaction its pullback is read within.
    public const int PullbackSessions = 10;

    // The share of the day's range under the close that puts it in the top quarter.
    public const decimal TopQuarter = 0.75m;

    // A close in the top quarter of the day's range, its distance from the low at least three quarters of the range; a
    // day with no range closes at its high and is in it.
    public static bool InTheTopQuarter(SweepBar bar) => bar.Close - bar.Low >= TopQuarter * (bar.High - bar.Low);

    // A breakout bought on its first pullback to the level it broke: the first session within ten after the listing
    // whose low reaches the level and whose close stands above it, bought at that close with its stop and its trail the
    // setting's typical moves in that session's own; none where no session does, or where the first that does finds
    // the market check closed, the member out of the index or holding a gap, or no typical move.
    public static IEnumerable<FamilyListing> BreakoutPullback(FamilyListing listing, SweepSeries one, double level, double moves, IReadOnlyList<SweepColumns.Session> sessions)
    {
        if (double.IsNaN(level))
        {
            yield break;
        }

        for (var bar = listing.Bar + 1; bar <= listing.Bar + PullbackSessions && bar < one.Bars.Length; bar++)
        {
            var close = Statistic.FromPrice(one.Bars[bar].Close);

            if (!(Statistic.FromPrice(one.Bars[bar].Low) <= level) || !(close > level))
            {
                continue;
            }

            var risk = moves * one.Atr[bar];

            if (Opens(one, bar, sessions) && risk > 0 && close - risk > 0)
            {
                yield return listing with { Bar = bar, Session = one.SessionAt[bar], Entry = close, Stop = close - risk, Trail = risk, Move = one.Atr[bar] };
            }

            yield break;
        }
    }

    // A drift bought on the first pullback after its reaction: the first session within ten after the reaction whose
    // close is below the close before it, bought at that close with the reaction's low still its stop and its target the
    // nearer of the band and the setting's multiple of the new risk; none where no session falls, or where the first
    // that does finds the market check closed, the member out of the index or holding a gap, or its close at or under
    // the stop.
    public static IEnumerable<FamilyListing> DriftPullback(FamilyListing listing, SweepSeries one, int reaction, double multiple, IReadOnlyList<SweepColumns.Session> sessions)
    {
        for (var bar = reaction + 1; bar <= reaction + PullbackSessions && bar < one.Bars.Length; bar++)
        {
            if (!(one.Bars[bar].Close < one.Bars[bar - 1].Close))
            {
                continue;
            }

            var close = Statistic.FromPrice(one.Bars[bar].Close);

            if (Opens(one, bar, sessions) && close > listing.Stop)
            {
                var byRisk = close + (multiple * (close - listing.Stop));
                var band = BandAbove(one, bar, close);

                yield return listing with { Bar = bar, Session = one.SessionAt[bar], Entry = close, Target = !double.IsNaN(band) && band < byRisk ? band : byRisk, Move = one.Atr[bar] };
            }

            yield break;
        }
    }

    // A session the rule could list on: the market check open, the member in the index and its series with no gap.
    static bool Opens(SweepSeries one, int bar, IReadOnlyList<SweepColumns.Session> sessions) =>
        one.Member[bar] && !one.Gap[bar] && sessions[one.SessionAt[bar]].Breadth >= FamilySweep.MarketFloor;

    // The lowest band whose low edge sits the drift's typical moves or more above the close, as the drift's own listing
    // reads its target, none where none does or the bar holds no typical move.
    static double BandAbove(SweepSeries one, int bar, double close)
    {
        var move = one.Atr[bar];

        if (!(move > 0))
        {
            return double.NaN;
        }

        var edges = SweepCandidates.LevelsOn(one, bar)
            .Select(level => Statistic.FromPrice(level.LowEdge))
            .Where(edge => edge > close && (edge - close) / move >= DriftRule.TargetBandMoves)
            .ToArray();

        return edges.Length > 0 ? edges.Min() : double.NaN;
    }
}
