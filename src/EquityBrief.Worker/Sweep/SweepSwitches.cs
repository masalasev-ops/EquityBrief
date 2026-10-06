namespace EquityBrief.Worker.Sweep;

// The market switches an index's second stage reads, each on its own session's close and closed on a session its series
// misses: the index's fund against SPY over a window, HYG against its average over the 50 sessions to the session, and
// HYG over 63 sessions, every close the pull stored adjusted for the funds' payouts; and the S&P 500's breadth on each
// of the index's sessions, read as the index's own is read, from the S&P 500's members as they stood.
public sealed class SweepSwitches
{
    // The sessions HYG's average and its change are read over.
    public const int CreditAverageSessions = Core.Readings.IndexSwitches.CreditAverageSessions;

    public const int CreditChangeSessions = Core.Readings.IndexSwitches.CreditChangeSessions;

    readonly double[] spy;
    readonly double[] fund;
    readonly double[] credit;

    public SweepSwitches(IReadOnlyList<DateOnly> calendar, IReadOnlyList<SweepMarketSeries> market, string fundCode)
    {
        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);

        double[] Aligned(string code)
        {
            var closes = new double[calendar.Count];

            Array.Fill(closes, double.NaN);

            foreach (var (session, close) in market.FirstOrDefault(one => one.Series == code)?.Closes ?? [])
            {
                if (sessionAt.TryGetValue(session, out var at))
                {
                    closes[at] = close;
                }
            }

            return closes;
        }

        (spy, fund, credit) = (Aligned("SPY"), Aligned(fundCode), Aligned("HYG"));
    }

    // Each switch reads open where the reading the night stores for it is above one, through the one function both read.
    public bool SmallLeads(int session, int window) =>
        session - window >= 0 && Core.Readings.IndexSwitches.Relative(fund[session], spy[session], fund[session - window], spy[session - window]) is > 1;

    public bool CreditAboveItsAverage(int session) => Core.Readings.IndexSwitches.OverAverage(credit, session, CreditAverageSessions) is > 1;

    public bool CreditRising(int session) => Core.Readings.IndexSwitches.Change(credit, session, CreditChangeSessions) is > 1;

    // The S&P 500's breadth on each of the index's sessions, none where it reads none.
    public static double?[] LargeBreadth(SweepHistoryInputs large, IReadOnlyList<DateOnly> calendar)
    {
        var largeAt = large.Sessions.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var series = new SweepSeries[large.Names.Count];

        Parallel.For(0, large.Names.Count, name => series[name] = SweepColumns.Series(large.Names[name], largeAt));

        var sessions = SweepColumns.Sessions(series, large.Sessions);

        return [.. calendar.Select(day => largeAt.TryGetValue(day, out var at) ? sessions[at].Breadth : null)];
    }
}
