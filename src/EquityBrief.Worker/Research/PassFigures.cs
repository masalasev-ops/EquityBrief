using EquityBrief.Core.Time;
using EquityBrief.Worker.Facts;
using EquityBrief.Worker.Fundamentals;

namespace EquityBrief.Worker.Research;

// The company's figures a research pass writes from, fetched before it and the night's facts file
// assembled again from them where the fetch changed what the file would hold.
//
// A plain pass fetches only where the store holds nothing for the name, because the archive is
// addressed by the identifier that fetch stores and the pass reads the company's own release from
// there. A regenerate fetches whatever is held, so the report is written from the figures as they
// stand on the day it runs, and asks first whether a pass for the name ran to the end today, since
// the pass will then start nothing and a fetch would buy nothing. Null where it was not asked.
// see: A name's facts file is assembled again for its night when an open fetches its fundamentals
// see: A regenerated report is written whole by the paid model from the company's figures as they stand on the day it runs, once a name a day
public static class PassFigures
{
    public static async Task<FundamentalsOutcome?> FetchAsync(
        FundamentalsFetcher fetcher,
        IClock clock,
        string databaseFile,
        string ticker,
        bool regenerate,
        string runId)
    {
        var fundamentals = !regenerate
            ? await fetcher.RunAsync(ticker, null, runId)
            : await ResearchRunner.WrittenTodayAsync(databaseFile, clock, ticker)
                ? null
                : await fetcher.RefetchAsync(ticker, runId);

        // Where the fetch stored a filing the night had not seen, and after any fetch a regenerate
        // made, since its copy of the parts that move with the price is the day's. A re-run replaces
        // only the files that now differ, which is this name's.
        // see: A re-run replaces a night's facts file where the store now computes a different one
        if (fundamentals is { RowsWritten: > 0 } || (regenerate && fundamentals is { Fetched: true }))
        {
            await new FactsAssembler(clock, databaseFile).RunAsync(runId);
            await new ChangeDetector(clock, databaseFile).RunAsync(runId);
        }

        return fundamentals;
    }
}
