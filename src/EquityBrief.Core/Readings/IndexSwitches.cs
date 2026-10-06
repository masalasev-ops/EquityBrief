namespace EquityBrief.Core.Readings;

// The market readings an S&P 400's or 600's rule may be switched on, each on a session's own closes and none where one it
// reads is missing: an index fund's adjusted close against SPY's over a window, as the one's change over the other's, and
// HYG's adjusted close against its average over the 50 sessions to it and against its close 63 sessions before. One
// function the night and the sweep share; a switch reads open where its reading is above one.
// see: The 400 and 600 rules start provisional with liquidity floors and a profit gate before any testing
public static class IndexSwitches
{
    // The windows a fund is read against SPY over.
    public static IReadOnlyList<int> SmallWindows { get; } = [126, 252];

    public const int CreditAverageSessions = 50;

    public const int CreditChangeSessions = 63;

    // A fund's change over SPY's between two sessions: the fund's close over SPY's on the later, over the same on the earlier.
    public static double? Relative(double fundNow, double spyNow, double fundThen, double spyThen) =>
        Held(fundNow) && Held(spyNow) && Held(fundThen) && Held(spyThen) ? fundNow / spyNow / (fundThen / spyThen) : null;

    // A close over the mean of the closes of the sessions to it, none where one of them is missing.
    public static double? OverAverage(IReadOnlyList<double> closes, int at, int sessions)
    {
        if (at < sessions - 1 || at >= closes.Count)
        {
            return null;
        }

        var span = closes.Skip(at - sessions + 1).Take(sessions).ToArray();

        return span.All(Held) ? closes[at] / span.Average() : null;
    }

    // A close over the close the given number of sessions before it.
    public static double? Change(IReadOnlyList<double> closes, int at, int sessions) =>
        at - sessions >= 0 && at < closes.Count && Held(closes[at]) && Held(closes[at - sessions]) ? closes[at] / closes[at - sessions] : null;

    static bool Held(double close) => !double.IsNaN(close) && close > 0;
}
