using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Membership;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Bars;

// The nightly bar fetch over the captured bulk file.
//
// Every expectation here is derived from the captured response or from the
// membership the fixture holds, never from a number this session chose. The
// bulk file was captured before the parser was written, which is the repair for
// what 1.2 found: a parser whose only fixture was written by the session
// writing the parser is a parser checked against itself.
public class BarFetcherTests
{
    const string Fixture = "membership-2026-09-05";
    const string Index = "GSPC";

    // The night the bulk file is for, which is the session after the stored
    // year ends. Evening in UTC, after the New York close.
    static readonly DateTimeOffset Night = new(2026, 9, 8, 21, 10, 0, TimeSpan.Zero);
    static readonly DateTimeOffset Backfilled = new(2026, 9, 5, 21, 10, 0, TimeSpan.Zero);

    static string FixtureFolder() => Path.Combine(Repository.Root, "fixtures", Fixture);

    static async Task<TemporaryStore> WithAYearStored()
    {
        var store = new TemporaryStore().Migrated();
        var clock = FixedClock.At(Backfilled, SessionZones.UnitedStates);

        await new MembershipLoader(
            RecordedIndexMembershipFeed.FromFile(Path.Combine(FixtureFolder(), "index-constituents.json")),
            clock,
            store.DatabaseFile).LoadAsync(Index, "run-0");

        await new Backfill(
            RecordedHistoricalBarFeed.FromFolder(FixtureFolder()),
            clock,
            store.DatabaseFile).RunAsync(Index, "run-1");

        return store;
    }

    static BarFetcher Fetcher(TemporaryStore store, IBulkPriceFeed feed) =>
        new(feed, FixedClock.At(Night, SessionZones.UnitedStates), store.DatabaseFile);

    static RecordedBulkPriceFeed Feed() => RecordedBulkPriceFeed.FromFolder(FixtureFolder());

    static IReadOnlyList<string> TickersOn(TemporaryStore store, string session)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ticker FROM bar WHERE session_date = $s ORDER BY ticker;";
        command.Parameters.AddWithValue("$s", session);

        var found = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            found.Add(reader.GetString(0));
        }

        return found;
    }

    [Fact]
    public void TheCapturedFileHoldsNamesTheIndexDoesNotHave()
    {
        // Stated first, because every assertion below rests on it. If the
        // provider only returned index members, the filter would be untested
        // and the fetcher could ignore membership entirely and still pass.
        var rows = RecordedBulkPriceFeed.Parse(
            File.ReadAllText(Directory.GetFiles(FixtureFolder(), "bulk-*.json").Single()),
            BarFetcher.Exchange);

        var tickers = rows.Select(row => row.Ticker).ToArray();

        Assert.Equal(7, rows.Count);
        Assert.Contains("AAPL", tickers);
        Assert.Contains("XRAY", tickers);
        Assert.Contains("AAL", tickers);
        Assert.Contains("A", tickers);
        Assert.Contains("AA", tickers);

        // One session, which is what a bulk last-day file is.
        Assert.Single(rows.Select(row => row.Bar.SessionDate).Distinct());
    }

    [Fact]
    public async Task ANightStoresTheDayForCurrentMembersAndNobodyElse()
    {
        using var store = await WithAYearStored();
        var feed = Feed();

        var outcome = await Fetcher(store, feed).RunAsync(Index, "run-night");

        // Three current members, derived from the fixture's own membership
        // rather than written here: the two departed constituents and the two
        // names that were never in the index are in the file and are not stored.
        var populations = Fixtures.Populations(FixtureFolder());
        var current = populations.Constituents - populations.Departed.Count;

        Assert.Equal(current, outcome.MembersStored);
        Assert.Equal(current, outcome.RowsWritten);
        Assert.Equal(1, outcome.Requests);
        Assert.Equal(new DateOnly(2026, 9, 8), outcome.Session);

        Assert.Equal(["AAPL", "KEYS", "MSFT"], TickersOn(store, "2026-09-08"));
    }

    [Fact]
    public async Task OneRequestServesTheWholeUniverse()
    {
        // The claim the nightly path rests on, asserted as a number the feed
        // counted rather than as an absence of a loop in the source.
        using var store = await WithAYearStored();
        var feed = Feed();

        await Fetcher(store, feed).RunAsync(Index, "run-night");

        Assert.Equal(1, feed.Requests);
    }

    [Fact]
    public async Task TheNightAppendsToTheSeriesRatherThanInterruptingIt()
    {
        // The captured session is the next trading day after the stored year
        // ends, the Monday between being a holiday, so the series stays
        // contiguous. A fetcher that stored the wrong day would leave a gap
        // here rather than a shorter year.
        using var store = await WithAYearStored();

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var before = connection.CreateCommand();
        before.CommandText = "SELECT MAX(session_date) FROM bar WHERE ticker = 'AAPL';";

        Assert.Equal("2026-09-04", (string)before.ExecuteScalar()!);

        await Fetcher(store, Feed()).RunAsync(Index, "run-night");

        using var after = connection.CreateCommand();
        after.CommandText = "SELECT MAX(session_date) FROM bar WHERE ticker = 'AAPL';";

        Assert.Equal("2026-09-08", (string)after.ExecuteScalar()!);
    }

    [Fact]
    public async Task ASecondRunOnTheSameNightStoresNothingNew()
    {
        using var store = await WithAYearStored();

        await Fetcher(store, Feed()).RunAsync(Index, "run-night");
        var again = await Fetcher(store, Feed()).RunAsync(Index, "run-night-2");

        Assert.Equal(0, again.RowsWritten);
        Assert.Equal(0, again.RowsDropped);
        Assert.Equal(3, again.MembersStored);
    }

    [Fact]
    public async Task RetentionDropsWhatFellOutOfTheWindowAndNothingInside()
    {
        // The boundary is the session the file is for, less a year, so a replay
        // drops what that night would have dropped rather than what tonight
        // would. 2026-09-08 less a year is 2025-09-08, and the stored year
        // starts on 2025-09-05, so three sessions fall out per name.
        using var store = await WithAYearStored();

        var outcome = await Fetcher(store, Feed()).RunAsync(Index, "run-night");

        Assert.Equal(new DateOnly(2025, 9, 8), outcome.Oldest);

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var oldest = connection.CreateCommand();
        oldest.CommandText = "SELECT MIN(session_date) FROM bar;";

        var kept = (string)oldest.ExecuteScalar()!;

        Assert.True(
            string.CompareOrdinal(kept, "2025-09-08") >= 0,
            $"The oldest stored session is {kept}, which is inside the window the drop should have cleared.");

        using var below = connection.CreateCommand();
        below.CommandText = "SELECT COUNT(*) FROM bar WHERE session_date < '2025-09-08';";

        Assert.Equal(0L, (long)below.ExecuteScalar()!);

        // And the drop is measured rather than reported: it is the difference
        // in the table's own row count across the transaction.
        Assert.True(outcome.RowsDropped > 0, "Nothing was dropped, so the retention path never ran.");
    }

    [Fact]
    public async Task TheRunLogRecordsOneRequestAndZeroModelCalls()
    {
        // A green report is a statement about the build. The night's own claim
        // is about a run, so it is recorded where a person reads it and where
        // nightly-cost can read it back.
        using var store = await WithAYearStored();

        await Fetcher(store, Feed()).RunAsync(Index, "run-night");

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT model_calls, network_requests, rows_written FROM run_log " +
            "WHERE run_id = 'run-night' AND stage = $stage;";
        command.Parameters.AddWithValue("$stage", BarFetcher.Stage);

        using var reader = command.ExecuteReader();

        Assert.True(reader.Read(), "The fetch left no run log row.");
        Assert.Equal(0L, reader.GetInt64(0));
        Assert.Equal(1L, reader.GetInt64(1));
        Assert.Equal(3L, reader.GetInt64(2));
    }

    [Fact]
    public void ARowFromAnotherExchangeIsSkippedRatherThanStored()
    {
        // A store keyed on ticker alone would collide two listings of one
        // symbol, so the row's own exchange decides rather than the request's.
        var rows = RecordedBulkPriceFeed.Parse(
            """
            [{"code":"AAPL","exchange_short_name":"US","date":"2026-09-08","open":1,"high":2,"low":1,"close":2,"adjusted_close":2,"volume":10},
             {"code":"AAPL","exchange_short_name":"LSE","date":"2026-09-08","open":1,"high":2,"low":1,"close":2,"adjusted_close":2,"volume":10}]
            """,
            "US");

        Assert.Single(rows);
        Assert.Equal("AAPL", rows[0].Ticker);
    }

    [Fact]
    public void AResponseWithNoCodeIsRefusedRatherThanReadAsANamelessRow()
    {
        var refusal = Assert.Throws<FormatException>(() => RecordedBulkPriceFeed.Parse(
            """[{"date":"2026-09-08","open":1,"high":2,"low":1,"close":2,"adjusted_close":2,"volume":10}]""",
            "US"));

        Assert.Contains("carries no code", refusal.Message, StringComparison.Ordinal);
    }
}
