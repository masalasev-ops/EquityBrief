using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Membership;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Bars;

// 1.2's done condition, run through the backfill's own surface.
//
// The population is stated wherever a figure is: the fixture carries four
// constituents of which three are current members and one has left, and every
// count below is over those. A claim about roughly five hundred live names is
// not something this suite can assert, and saying so is the point.
public class BackfillTests
{
    const string Index = "GSPC";
    const string Fixture = "membership-2026-09-05";

    static string FixtureFolder() => Path.Combine(Repository.Root, "fixtures", Fixture);

    // The fixture date, so the year the backfill asks for ends where the
    // captured series ends rather than wherever this machine is today.
    static readonly DateTimeOffset Instant = new(2026, 9, 5, 21, 10, 0, TimeSpan.Zero);

    static IClock Clock() => FixedClock.At(Instant, SessionZones.UnitedStates);

    static async Task<TemporaryStore> WithMembership()
    {
        var store = new TemporaryStore().Migrated();
        var feed = RecordedIndexMembershipFeed.FromFile(Path.Combine(FixtureFolder(), "index-constituents.json"));

        await new MembershipLoader(feed, Clock(), store.DatabaseFile).LoadAsync(Index, "run-0");

        return store;
    }

    static Backfill Loader(TemporaryStore store, out RecordedHistoricalBarFeed feed)
    {
        feed = RecordedHistoricalBarFeed.FromFolder(FixtureFolder());

        return new Backfill(feed, Clock(), store.DatabaseFile);
    }

    [Fact]
    public async Task EveryCurrentMemberHoldsAFullYearAndTheNameThatLeftIsNotFetched()
    {
        using var store = await WithMembership();
        var backfill = Loader(store, out var feed);

        var outcome = await backfill.RunAsync(Index, "run-1");

        // Three current members, one departed name. The departed one keeps the
        // history it has, which here is none, and is not fetched: a backfill is
        // owed for names in the index, not for every name ever in it.
        Assert.Equal(3, outcome.Members);
        Assert.Equal(3, outcome.Owed);
        Assert.Equal(3, outcome.Requests);
        Assert.Equal(3, feed.Requests);

        var tickers = Column(store, "SELECT DISTINCT ticker FROM bar ORDER BY ticker;");

        Assert.Equal(["AAPL", "KEYS", "MSFT"], tickers);
        Assert.DoesNotContain("XRAY", tickers);

        // Per name rather than in total, so a name short of its year is visible
        // instead of being covered by another name's surplus.
        //
        // 261 weekday sessions, not the 262 the captured file holds. The window
        // the backfill asks for is the year ending on the session date, which at
        // the fixture instant is 2026-09-05, so it starts on 2025-09-05 and the
        // capture's first bar of 2025-09-04 falls outside it. The window is
        // right and the capture simply reaches one day further back; asserting
        // the edges rather than only the count is what makes that legible.
        foreach (var ticker in tickers)
        {
            var sessions = Column(store, $"SELECT session_date FROM bar WHERE ticker = '{ticker}' ORDER BY session_date;");

            Assert.Equal(261, sessions.Count);
            Assert.Equal("2025-09-05", sessions[0]);
            Assert.Equal("2026-09-04", sessions[^1]);
        }
    }

    [Fact]
    public async Task ASecondRunBackfillsNothingAndSpendsNoRequest()
    {
        using var store = await WithMembership();
        var backfill = Loader(store, out var feed);

        var first = await backfill.RunAsync(Index, "run-1");
        var before = Column(store, "SELECT ticker || ' ' || session_date FROM bar ORDER BY ticker, session_date;");

        var second = await backfill.RunAsync(Index, "run-2");
        var after = Column(store, "SELECT ticker || ' ' || session_date FROM bar ORDER BY ticker, session_date;");

        // Never repeated for a name that already holds its year. Not merely
        // harmless when repeated: no request is made at all, which is the whole
        // reason the rule exists rather than relying on the insert conflicting.
        Assert.Equal(3, first.Requests);
        Assert.Equal(0, second.Owed);
        Assert.Equal(0, second.Requests);
        Assert.Equal(0, second.RowsWritten);
        Assert.Equal(3, feed.Requests);

        Assert.Equal(before, after);
        Assert.Equal(783, before.Count);
    }

    [Fact]
    public async Task TheRequestCountInTheRunLogEqualsTheNamesLackingHistory()
    {
        using var store = await WithMembership();
        var backfill = Loader(store, out _);

        await backfill.RunAsync(Index, "run-1");
        await backfill.RunAsync(Index, "run-2");

        // The done condition, read off the store rather than off the return
        // value, because the run log is the surface the operator reads it on.
        var logged = Rows(store, "SELECT run_id, CAST(network_requests AS TEXT) FROM run_log WHERE stage = 'backfill' ORDER BY run_id;");

        Assert.Equal([("run-1", "3"), ("run-2", "0")], logged);

        var written = Rows(store, "SELECT run_id, CAST(rows_written AS TEXT) FROM run_log WHERE stage = 'backfill' ORDER BY run_id;");

        Assert.Equal([("run-1", "783"), ("run-2", "0")], written);

        // And the backfill costs no model call, like everything on the nightly
        // path.
        var free = Column(store, "SELECT DISTINCT CAST(model_calls AS TEXT) FROM run_log;");

        Assert.Equal("0", Assert.Single(free));
    }

    [Fact]
    public async Task PricesAreStoredAsTextAndReadBackAsDecimal()
    {
        using var store = await WithMembership();
        var backfill = Loader(store, out _);

        await backfill.RunAsync(Index, "run-1");

        var types = Column(store, "SELECT DISTINCT typeof(open) || ' ' || typeof(close) || ' ' || typeof(volume) FROM bar;");

        // TEXT for the money columns and INTEGER for the count, which is the
        // storage half of the money rule holding in a populated store rather
        // than only in the migration text price-storage-form reads.
        Assert.Equal("text text integer", Assert.Single(types));

        var close = Column(store, "SELECT close FROM bar WHERE ticker = 'AAPL' ORDER BY session_date LIMIT 1;");

        // Round-trips through the money helper, in the invariant form.
        Assert.Equal(EquityBrief.Data.Money.FromStorage(close[0]), decimal.Parse(close[0], System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task ANameTheFeedHasNoCaptureForFailsRatherThanReadingAsDone()
    {
        // A recorded feed that answered an unknown ticker with an empty series
        // would look exactly like a name the provider has no history for, and
        // the backfill would record it as done and never ask again.
        using var store = await WithMembership();

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();
        using var insert = connection.CreateCommand();
        insert.CommandText =
            @"INSERT INTO membership (index_code, ticker, joined, ""left"", observed_at)
              VALUES ($i, 'NOPE', '2020-01-02', NULL, '2026-09-05T21:10:00Z');";
        insert.Parameters.AddWithValue("$i", Index);
        insert.ExecuteNonQuery();

        var backfill = Loader(store, out _);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => backfill.RunAsync(Index, "run-1"));

        Assert.Contains("NOPE", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRecordedFeedTakesTheAdjustedCloseAndRefusesWhatItCannotRead()
    {
        // The stored series is adjusted, so the parser reads adjusted_close and
        // not close. Taking the wrong one would leave the series drifting from
        // every chart it is compared against, with nothing failing.
        var bars = RecordedHistoricalBarFeed.Parse(
            """[{"date":"2026-09-04","open":"10.00","high":"11.00","low":"9.00","close":"10.50","adjusted_close":"5.25","volume":1000}]""",
            "TEST");

        Assert.Equal(5.25m, Assert.Single(bars).Close);

        Assert.Throws<FormatException>(() => RecordedHistoricalBarFeed.Parse("{}", "TEST"));
        Assert.Throws<FormatException>(() => RecordedHistoricalBarFeed.Parse(
            """[{"date":"04/09/2026","open":"1","high":"1","low":"1","close":"1","adjusted_close":"1","volume":1}]""",
            "TEST"));
        Assert.Throws<FormatException>(() => RecordedHistoricalBarFeed.Parse(
            """[{"date":"2026-09-04","open":"1","high":"1","low":"1","close":"1","volume":1}]""",
            "TEST"));
    }

    static IReadOnlyList<string> Column(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var values = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    static IReadOnlyList<(string, string)> Rows(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<(string, string)>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        return rows;
    }
}
