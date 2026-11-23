using EquityBrief.Core.Sweep;

namespace EquityBrief.Core.History;

// Which of a code's bars traded. A bar carrying volume traded, and so did one carrying none whose close moved from the bar
// before it: the provider left its volume out, and the session is read as traded with its volume missing, never as holding
// no price. Only a bar with no volume whose close stands where the bar before left it reads as not trading. The close moved
// is the one traded at where both bars carry it, and the stored close where either does not.
// see: A bar whose close moved with no volume traded with its volume missing
public static class LongTraded
{
    // Whether the bar at the place given carries no volume while its close moved from the bar before it.
    public static bool VolumeMissing(IReadOnlyList<SweepBar> bars, int at) =>
        bars[at].Volume <= 0 && at > 0 && bars[at].Close > 0 && Moved(bars[at - 1], bars[at]);

    // Whether the bar at the place given traded.
    public static bool Traded(IReadOnlyList<SweepBar> bars, int at) => bars[at].Volume > 0 || VolumeMissing(bars, at);

    // The bars that traded, in order.
    public static IEnumerable<SweepBar> Of(IReadOnlyList<SweepBar> bars) => bars.Where((bar, at) => Traded(bars, at));

    static bool Moved(SweepBar before, SweepBar bar) =>
        before.RawClose > 0 && bar.RawClose > 0 ? bar.RawClose != before.RawClose : bar.Close != before.Close;
}
