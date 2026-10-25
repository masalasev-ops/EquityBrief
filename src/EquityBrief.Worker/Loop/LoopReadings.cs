using System.Collections.Concurrent;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Worker.Ledger;

namespace EquityBrief.Worker.Loop;

// The ledger's readings of a listing on its session over the pulled history, each read through the catalogue's own
// function as the history build reads it, so a condition the engines find reads what a rule's hooks read on the night;
// each kept once read. A run through a day before the scorecard's readings join the loop reads them as none.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
// see: The fundamentals scorecard and the quality family join phase 22 ahead of the hold on new work, and the monthly run reads none of them before its first run after 2026-11-07
public sealed class LoopReadings
{
    readonly LoopRead read;
    readonly double[][] closes;
    readonly LedgerMarket market;
    readonly LedgerContext context;
    readonly DateOnly through;
    readonly ConcurrentDictionary<(int Name, int Session), IReadOnlyList<double?>> held = new();

    LoopReadings(LoopRead read, LedgerMarket market, LedgerContext context, DateOnly through)
    {
        this.read = read;
        this.market = market;
        this.context = context;
        this.through = through;
        closes = [.. read.Series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray())];
    }

    public static async Task<LoopReadings> ReadAsync(LoopRead read, string databaseFile, DateOnly through, CancellationToken cancellation = default)
    {
        var (market, context) = await SetupLedger.HistoryReadingsAsync(databaseFile, read.Inputs, read.Series, read.Members, read.Income, through, cancellation, read.Index);

        return new LoopReadings(read, market, context, through);
    }

    // Whether the run reads the reading at a place in the catalogue.
    public bool Reads(int place) => LedgerReadings.LoopReads(place, through);

    // A member's readings on a session at its bar.
    public IReadOnlyList<double?> Of(int name, int bar, int session) =>
        held.GetOrAdd((name, session), _ => Masked(LedgerSetups.Readings(read.Index, name, read.Series[name], closes[name], bar, read.Sessions[session], read.Members, read.Calendar, session, market, context, read.Series), through));

    // The readings a run through a day reads, each the loop does not read yet left none.
    public static double?[] Masked(double?[] readings, DateOnly through)
    {
        for (var at = 0; at < readings.Length; at++)
        {
            if (!LedgerReadings.LoopReads(at, through))
            {
                readings[at] = null;
            }
        }

        return readings;
    }
}
