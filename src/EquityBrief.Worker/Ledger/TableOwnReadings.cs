using EquityBrief.Core.History;
using EquityBrief.Core.Ledger;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Ledger;

// What the every-session table reads its own way over a member's readings as the night works them, which its build writes and
// its check rebuilds alike: the weekdays to the next report off the code's filings, and the readings of a bar's volume none
// where its close moved with no volume. From the pin move before the run of 2027-02-06 the seven it read its own way, the
// third and fourth alphas and the two liquidity readings at the traded price and the three reach readings over the fixed
// length of history, are the shared code's, the night's and every build's alike, and none is listed expected.
// see: The every-session table reads four readings at the traded price and three over a fixed length of history, the night's own
public static class TableOwnReadings
{
    // The readings the check lists apart from the ledger's and marks expected: none once the shared code reads the seven.
    public static IReadOnlyList<string> Columns { get; } = [];

    // From the operator's decisions of 2026-11-25, item D, the short interest, which the builds read from the two newest
    // settlements and a night with no cap until the pin move carried the cap into the shared code: none listed since.
    // see: A build reads a stock's short interest only from the two newest settlements FINRA published before the session
    public static IReadOnlyList<string> UntilThePinMove { get; } = [];

    // Whether the check lists a difference on a reading as expected where the ledger's setup is held to the table's row.
    public static bool Expected(string reading) =>
        Columns.Contains(reading, StringComparer.Ordinal) || UntilThePinMove.Contains(reading, StringComparer.Ordinal);

    // The reports given are the code's report days off its filings, which the weekdays to the next report count to; none
    // given leaves that reading as the night works it.
    public static void Apply(double?[] readings, SweepSeries one, int bar, IReadOnlyList<DateOnly>? reports = null)
    {
        void Set(string column, double? value) => readings[LedgerReadings.IndexOf(column)] = value;

        // see: The every-session table counts the weekdays to the next report from the results 8-K each quarter's 10-Q or 10-K closes
        if (reports is not null)
        {
            Set("earnings_sessions", LedgerSetups.EarningsSessions(reports, [one.Bars[bar].Session], 0));
        }

        // see: A bar whose close moved with no volume traded with its volume missing
        if (LongTraded.VolumeMissing(one.Bars, bar))
        {
            foreach (var column in TableReadings.VolumeColumns)
            {
                Set(column, null);
            }
        }
    }
}
