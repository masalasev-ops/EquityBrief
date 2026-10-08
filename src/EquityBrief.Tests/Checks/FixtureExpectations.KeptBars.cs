using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Bars;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.3: the sessions the fetcher drops as they fall out of the year it keeps land in the kept bars
// in the drop's own transaction, each row as it stood with the night that kept it, a session already kept left as it
// was, and a drop the store refuses keeps none.
// see: The bars the fetcher drops are kept in a table of their own that no night reads, and a setup is stored as its anchor
public partial class FixtureExpectations
{
    static readonly DateTimeOffset KeptNight = new(2026, 9, 8, 21, 10, 0, TimeSpan.Zero);

    [Fact]
    public async Task TheSessionsTheFetcherDropsLandInTheKeptBarsInItsOwnTransactionWithTheNightThatKeptThem()
    {
        using var store = await Replayed();

        // Two sessions older than the year the night of 2026-09-08 keeps, which the backfill's year does not reach,
        // beside whatever the backfill stored before the boundary.
        foreach (var (session, close) in new[] { ("2025-09-02", "10"), ("2025-09-03", "11") })
        {
            Insert(store, "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
                $"VALUES ('AAPL', '{session}', '{close}', '{close}', '{close}', '{close}', 1000, 'constructed', '{session}T21:00:00Z', '{close}');");
        }

        var before = Query(store, "SELECT ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close FROM bar WHERE session_date < '2025-09-08' ORDER BY ticker, session_date;");

        Assert.True(before.Count >= 2, $"{before.Count} sessions before the boundary");
        Assert.Empty(Query(store, "SELECT * FROM kept_bar;"));

        await new BarFetcher(RecordedBulkPriceFeed.FromFolder(Folder()), FixedClock.At(KeptNight, SessionZones.UnitedStates), store.DatabaseFile).RunAsync(Index, "kept-fetch");

        // The two dropped sessions kept whole, each row as it stood with the night that kept it, and gone from the bars;
        // the boundary session still a bar and kept nowhere.
        Assert.Equal(before, Query(store, "SELECT ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close FROM kept_bar ORDER BY ticker, session_date;"));
        Assert.Equal(["2026-09-08"], Query(store, "SELECT DISTINCT kept_on FROM kept_bar;"));
        Assert.Empty(Query(store, "SELECT 1 FROM bar WHERE session_date < '2025-09-08';"));
        Assert.NotEmpty(Query(store, "SELECT 1 FROM bar WHERE session_date = '2025-09-08' AND ticker = 'AAPL';"));

        // A session kept once is left as it was by a later fetch that would keep it again: the row's own values stand.
        Insert(store, "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
            "VALUES ('AAPL', '2025-09-03', '99', '99', '99', '99', 5, 'again', '2025-09-03T21:00:00Z', '99');");

        await new BarFetcher(RecordedBulkPriceFeed.FromFolder(Folder()), FixedClock.At(KeptNight, SessionZones.UnitedStates), store.DatabaseFile).RunAsync(Index, "kept-fetch-again");

        Assert.Equal(["AAPL|2025-09-03|11|constructed"], Query(store, "SELECT ticker || '|' || session_date || '|' || close || '|' || source FROM kept_bar WHERE session_date = '2025-09-03';"));
        Assert.Equal([before.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)], Query(store, "SELECT COUNT(*) FROM kept_bar;"));
    }

    [Fact]
    public async Task ADropTheStoreRefusesKeepsNoBarEither()
    {
        using var store = await Replayed();

        Insert(store, "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
            "VALUES ('AAPL', '2025-09-02', '10', '10', '10', '10', 1000, 'constructed', '2025-09-02T21:00:00Z', '10');");
        Insert(store, "CREATE TRIGGER keep_every_bar BEFORE DELETE ON bar BEGIN SELECT RAISE(ABORT, 'the bar is kept'); END;");

        var bars = Query(store, "SELECT COUNT(*) FROM bar;");

        await Assert.ThrowsAsync<SqliteException>(() =>
            new BarFetcher(RecordedBulkPriceFeed.FromFolder(Folder()), FixedClock.At(KeptNight, SessionZones.UnitedStates), store.DatabaseFile).RunAsync(Index, "kept-fetch-refused"));

        // The copy and the drop are one transaction with the night's bars: the refusal leaves the kept bars empty and
        // the bars as they were, the night's own not stored either.
        Assert.Empty(Query(store, "SELECT * FROM kept_bar;"));
        Assert.Equal(bars, Query(store, "SELECT COUNT(*) FROM bar;"));
    }
}
