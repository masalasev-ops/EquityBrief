namespace EquityBrief.Worker.Sweep;

// The market switches an index's second stage reads, each on its own session's close and closed on a session its series
// misses: the index's fund against SPY over a window, HYG against its average over the 50 sessions to the session, and
// HYG over 63 sessions, every close the pull stored adjusted for the funds' payouts; and the S&P 500's breadth on each
// of the index's sessions, read as the index's own is read, from the S&P 500's members as they stood.
public sealed class SweepSwitches
{
    // The sessions HYG's average and its change are read over.
    public const int CreditAverageSessions = 50;

    public const int CreditChangeSessions = 63;

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

    public bool SmallLeads(int session, int window) =>
        session - window >= 0 && fund[session] / spy[session] / (fund[session - window] / spy[session - window]) > 1;

    public bool CreditAboveItsAverage(int session)
    {
        if (session < CreditAverageSessions - 1)
        {
            return false;
        }

        var span = credit[(session - CreditAverageSessions + 1)..(session + 1)];

        return !span.Any(double.IsNaN) && credit[session] > span.Average();
    }

    public bool CreditRising(int session) => session - CreditChangeSessions >= 0 && credit[session] / credit[session - CreditChangeSessions] > 1;

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
