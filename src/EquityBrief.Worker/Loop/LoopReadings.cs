using System.Collections.Concurrent;
using EquityBrief.Core.Prices;
using EquityBrief.Worker.Ledger;

namespace EquityBrief.Worker.Loop;

// The ledger's readings of a listing on its session over the pulled history, each read through the catalogue's own
// function as the history build reads it, so a condition the engines find reads what a rule's hooks read on the night;
// each kept once read.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
public sealed class LoopReadings
{
    readonly LoopRead read;
    readonly double[][] closes;
    readonly LedgerMarket market;
    readonly LedgerContext context;
    readonly ConcurrentDictionary<(int Name, int Session), IReadOnlyList<double?>> held = new();

    LoopReadings(LoopRead read, LedgerMarket market, LedgerContext context)
    {
        this.read = read;
        this.market = market;
        this.context = context;
        closes = [.. read.Series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray())];
    }

    public static async Task<LoopReadings> ReadAsync(LoopRead read, string databaseFile, DateOnly through, CancellationToken cancellation = default)
    {
        var (market, context) = await SetupLedger.HistoryReadingsAsync(databaseFile, read.Inputs, read.Series, read.Members, read.Income, through, cancellation);

        return new LoopReadings(read, market, context);
    }

    // A member's readings on a session at its bar.
    public IReadOnlyList<double?> Of(int name, int bar, int session) =>
        held.GetOrAdd((name, session), _ => LedgerSetups.Readings(read.Index, name, read.Series[name], closes[name], bar, read.Sessions[session], read.Members, read.Calendar, session, market, context));
}
