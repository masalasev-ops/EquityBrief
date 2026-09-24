using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Moves;

// One session as the peers table's readings take it: its high and its close, each the adjusted
// price the store holds.
public readonly record struct PeerBar(DateOnly SessionDate, decimal High, decimal Close);

// A name's two readings for the peers table, taken over the bars the store holds for it, with how
// many there are: how far its newest close sits below the highest high among them, and its return
// over the last sixty sessions, which a name holding too few bars for it does not have.
public sealed record PeerReading(DateOnly Session, decimal YearHigh, double BelowHighPct, double? ReturnPct, int Bars);

// The peers table's two readings, a pure function of a session-ordered series as the move
// arithmetic is, so the annotator reads and writes and this decides.
// see: Peers are shown by price alone, in section 2 beside the move table
public static class PeerReadings
{
    // The sessions the return is taken over. Sixty is a quarter of a year of trading, the span a
    // reader comparing a name with its group asks about, and a name holding fewer bars than it and
    // the one it is measured from has no return rather than one over a shorter span, which would
    // read as the same figure without being one.
    public const int ReturnWindow = 60;

    // The readings over a name's stored bars in session order, and none for a name holding none.
    public static PeerReading? Of(IReadOnlyList<PeerBar> bars)
    {
        if (bars.Count == 0)
        {
            return null;
        }

        var newest = bars[^1];
        var high = bars.Max(bar => bar.High);

        // The distance below the high in per cent of the high, so a close at the high reads 0.
        var below = high > 0 ? Statistic.FromRatio((high - newest.Close) / high) * 100 : 0;

        double? back = null;

        if (bars.Count > ReturnWindow && bars[^(ReturnWindow + 1)].Close is > 0 and var from)
        {
            back = Statistic.FromRatio((newest.Close - from) / from) * 100;
        }

        return new PeerReading(newest.SessionDate, high, below, back, bars.Count);
    }
}
