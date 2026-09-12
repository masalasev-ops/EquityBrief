using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Fundamentals;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Store;

// The fundamentals fetcher, over the committed captures and a store this test
// migrated.
//
// The two halves that matter are opposite. A stored copy that predates a filing
// has to be fetched, or the numbers section shows a quarter old figures forever.
// A stored copy that does not has to cost nothing, because an open that spends on
// every visit is the shape the whole on-demand design exists to avoid
// (see: Deciding not to spend must not cost anything).
public class FundamentalsFetcherTests
{
    static string Folder() => Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05");

    static readonly DateTimeOffset Opened = new(2026, 9, 12, 18, 30, 0, TimeSpan.Zero);

    static IClock Clock() => FixedClock.At(Opened, SessionZones.UnitedStates);

    static IFundamentalsFeed Feed() => RecordedFundamentalsFeed.FromFolder(Folder());

    static FundamentalsFetcher Fetcher(TemporaryStore store, IFundamentalsFeed feed) =>
        new(feed, Clock(), store.DatabaseFile);

    sealed record StoredRow(string Ticker, string FilingDate, string FetchedAt, string Payload, string Source);

    static IReadOnlyList<StoredRow> Rows(TemporaryStore store, string ticker)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT ticker, filing_date, fetched_at, payload, source FROM fundamentals " +
            "WHERE ticker = $ticker ORDER BY filing_date DESC;";
        command.Parameters.AddWithValue("$ticker", ticker);

        var rows = new List<StoredRow>();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(new StoredRow(
                reader.GetString(0), reader.GetString(1), reader.GetString(2),
                reader.GetString(3), reader.GetString(4)));
        }

        return rows;
    }

    static string Detail(TemporaryStore store)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT detail FROM run_log WHERE stage = $stage ORDER BY rowid DESC LIMIT 1;";
        command.Parameters.AddWithValue("$stage", FundamentalsFetcher.Stage);

        return (string)command.ExecuteScalar()!;
    }

    [Fact]
    public async Task ANameWithNothingStoredIsFetchedWhateverTheCallerKnows()
    {
        using var store = new TemporaryStore().Migrated();

        var feed = Feed();
        var outcome = await Fetcher(store, feed).RunAsync("AAPL", null, "open-1");

        // Nothing held means fetch, even where the caller knows of no filing: a
        // name with no numbers section has nothing to compare and the first open
        // is what fills it.
        Assert.True(outcome.Fetched);
        Assert.Null(outcome.HeldBefore);
        Assert.Equal(FundamentalsFetcher.StoredFilings, outcome.RowsWritten);
        Assert.Equal(1, feed.Requests);
        Assert.Equal(outcome.RowsWritten, Rows(store, "AAPL").Count);
    }

    [Fact]
    public async Task TwelveFilingsAreStoredAndTheCaptureHoldsMore()
    {
        using var store = new TemporaryStore().Migrated();

        var outcome = await Fetcher(store, Feed()).RunAsync("AAPL", null, "open-1");

        // The window is a selection rather than everything the payload held, which
        // is why the capture holds fourteen: a file holding exactly twelve could
        // not tell a fetcher that selects from one that stores what it was handed.
        Assert.True(
            outcome.QuartersReturned > FundamentalsFetcher.StoredFilings,
            $"The capture holds {outcome.QuartersReturned} filings, which cannot show a window of {FundamentalsFetcher.StoredFilings}.");

        Assert.Equal(FundamentalsFetcher.StoredFilings, outcome.WindowTaken);
        Assert.Equal(FundamentalsFetcher.StoredFilings, Rows(store, "AAPL").Count);

        // And the twelve are the most recent twelve, by filing date. A window that
        // took the oldest would be the same count and the wrong quarters.
        var stored = Rows(store, "AAPL").Select(row => row.FilingDate).ToArray();

        Assert.Equal(stored.OrderByDescending(date => date, StringComparer.Ordinal), stored);
        Assert.Equal("2026-07-31", stored[0]);

        // Twelve is more than the section shows, which is the whole point of the
        // ruling: the display figure is five and the store is not narrowed to it.
        Assert.True(FundamentalsFetcher.StoredFilings > FundamentalsFetcher.ReportedQuarters);
    }

    [Fact]
    public async Task TheWindowIsCountedInFilingsAndNotOverADateRange()
    {
        using var store = new TemporaryStore().Migrated();

        // KEYS files a quarter with no filing date, so the payload holds fifteen
        // rows and fourteen filings. A window counted over a date range would give
        // this name a shorter one than a name that filed on time; counted in
        // filings it gets the same twelve.
        var keysight = await Fetcher(store, Feed()).RunAsync("KEYS", null, "open-keys");
        var apple = await Fetcher(store, Feed()).RunAsync("AAPL", null, "open-aapl");

        Assert.Equal(FundamentalsFetcher.StoredFilings, keysight.WindowTaken);
        Assert.Equal(apple.WindowTaken, keysight.WindowTaken);
        Assert.Equal(1, keysight.QuartersWithNoFilingDate);

        // The two names span different stretches of calendar for the same twelve
        // filings, which is what makes this a count of filings rather than of
        // months.
        var keysightSpan = Rows(store, "KEYS").Select(row => row.FilingDate).ToArray();
        var appleSpan = Rows(store, "AAPL").Select(row => row.FilingDate).ToArray();

        Assert.Equal(keysightSpan.Length, appleSpan.Length);
        Assert.NotEqual(keysightSpan[^1], appleSpan[^1]);
    }

    [Fact]
    public async Task AShortWindowCarriesItsCount()
    {
        using var store = new TemporaryStore().Migrated();

        var full = await Fetcher(store, Feed()).RunAsync("AAPL", null, "open-1");

        Assert.Equal(FundamentalsFetcher.StoredFilings, full.Held);
        Assert.Contains("12 filing(s) held", Detail(store), StringComparison.Ordinal);
        Assert.DoesNotContain("wanted", Detail(store), StringComparison.Ordinal);

        // A reading over fewer than the window says how many it had, the way an
        // indicator row carries its bar count: a panel over four quarters and one
        // over twelve are different readings and a figure that does not say which
        // invites the wrong one. Constructed, because every captured name holds
        // more than twelve.
        var shortWindow = FundamentalsFetcher.Detail(
            new FundamentalsOutcome("NEW", true, null, 3, 3, 3, 0, 3, 0, [], 1));

        Assert.Contains("3 filing(s) held of 12 wanted", shortWindow, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStoredCopyThatDoesNotPredateAFilingCostsNothing()
    {
        using var store = new TemporaryStore().Migrated();

        var first = Feed();
        var opened = await Fetcher(store, first).RunAsync("AAPL", null, "open-1");

        // The second open knows of no filing later than the one already held, so
        // nothing is fetched and nothing is asked of the feed at all. Not one
        // request rather than one request whose rows are discarded, which is the
        // difference between deciding not to spend and spending to decide.
        var second = Feed();
        var again = await Fetcher(store, second).RunAsync("AAPL", new DateOnly(2026, 7, 31), "open-2");

        Assert.False(again.Fetched);
        Assert.Equal(0, second.Requests);
        Assert.Equal(0, again.RowsWritten);
        Assert.Equal(new DateOnly(2026, 7, 31), again.HeldBefore);
        Assert.Equal(opened.RowsWritten, Rows(store, "AAPL").Count);

        // And the run log says which of the two it was, because "nothing was
        // written" and "nothing needed writing" are different mornings.
        Assert.Contains("nothing fetched", Detail(store), StringComparison.Ordinal);
        Assert.Contains("0 request(s)", Detail(store), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStoredCopyPredatingAFilingIsFetched()
    {
        using var store = new TemporaryStore().Migrated();

        await Fetcher(store, Feed()).RunAsync("AAPL", null, "open-1");

        var feed = Feed();
        var again = await Fetcher(store, feed).RunAsync("AAPL", new DateOnly(2026, 11, 2), "open-2");

        // The caller knows of a filing later than anything held, so the fetch
        // happens. Nothing new comes back from a capture taken before that
        // filing, so every row is already held and none is written, which is the
        // truthful outcome rather than an error.
        Assert.True(again.Fetched);
        Assert.Equal(1, feed.Requests);
        Assert.Equal(0, again.RowsWritten);
        Assert.Equal(again.WindowTaken, again.AlreadyHeld);
    }

    [Fact]
    public void TheDecisionIsTheSentenceSectionFifteenStates()
    {
        // The decision on its own, over constructed dates, because the three tests
        // above each reach it through a store and a feed and none of them can show
        // the boundary either side of one day.
        var held = new DateOnly(2026, 7, 31);

        Assert.True(FundamentalsFetcher.NeedsFetching(null, null));
        Assert.True(FundamentalsFetcher.NeedsFetching(null, held));
        Assert.False(FundamentalsFetcher.NeedsFetching(held, null));
        Assert.False(FundamentalsFetcher.NeedsFetching(held, held));
        Assert.False(FundamentalsFetcher.NeedsFetching(held, held.AddDays(-1)));
        Assert.True(FundamentalsFetcher.NeedsFetching(held, held.AddDays(1)));
    }

    [Fact]
    public async Task ASecondOpenWritesNothingAndChangesNothingThatWasWritten()
    {
        using var store = new TemporaryStore().Migrated();

        await Fetcher(store, Feed()).RunAsync("AAPL", null, "open-1");

        var before = Rows(store, "AAPL");

        // Forced past the decision, so what is asserted is the insert's own
        // behaviour on a conflict rather than the decision not to fetch.
        var again = await Fetcher(store, Feed()).RunAsync("AAPL", new DateOnly(2026, 12, 1), "open-2");

        var after = Rows(store, "AAPL");

        Assert.Equal(0, again.RowsWritten);
        Assert.Equal(before.Count, again.AlreadyHeld);
        Assert.Equal(before.Count, after.Count);

        // The conflict is ignored rather than replacing the row, which is what
        // keeps this table insert-only as its ownership row declares. A replace
        // would leave the same row count and a different instant on it, so the
        // instant is what this asserts.
        Assert.Equal(
            before.Select(row => (row.FilingDate, row.FetchedAt, row.Payload)),
            after.Select(row => (row.FilingDate, row.FetchedAt, row.Payload)));
    }

    [Fact]
    public async Task TheRowIsKeyedOnTheFilingDateAndThePayloadCarriesThePeriod()
    {
        using var store = new TemporaryStore().Migrated();

        await Fetcher(store, Feed()).RunAsync("AAPL", null, "open-1");

        var newest = Rows(store, "AAPL")[0];

        Assert.Equal("2026-07-31", newest.FilingDate);

        using var payload = JsonDocument.Parse(newest.Payload);

        Assert.Equal("2026-06-30", payload.RootElement.GetProperty("periodEnd").GetString());

        // The two are weeks apart, which is what makes keying on the wrong one
        // invisible: every row would still be there, each labelled a month early.
        Assert.NotEqual(newest.FilingDate, payload.RootElement.GetProperty("periodEnd").GetString());
    }

    [Fact]
    public async Task AQuarterTheProviderFiledNoDateForIsCountedOnTheRunLogAndNotStored()
    {
        using var store = new TemporaryStore().Migrated();

        var outcome = await Fetcher(store, Feed()).RunAsync("KEYS", null, "open-1");

        Assert.Equal(1, outcome.QuartersWithNoFilingDate);
        Assert.DoesNotContain(Rows(store, "KEYS"), row => row.FilingDate.Length == 0);

        // On the surface the operator reads, rather than only in a return value.
        Assert.Contains("no filing date for", Detail(store), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSourceColumnNamesWhereEveryPartCameFrom()
    {
        using var store = new TemporaryStore().Migrated();

        await Fetcher(store, Feed()).RunAsync("AAPL", null, "open-1");

        using var source = JsonDocument.Parse(Rows(store, "AAPL")[0].Source);

        var parts = source.RootElement.EnumerateObject().ToDictionary(part => part.Name, part => part.Value.GetString());

        // Every part the payload may carry is attributed, not only the ones this
        // name happened to fill.
        Assert.Equal(FundamentalsFetcher.Parts.OrderBy(part => part, StringComparer.Ordinal), parts.Keys.OrderBy(key => key, StringComparer.Ordinal));

        Assert.Equal(FundamentalsFetcher.Provider, parts["quarter"]);
        Assert.Equal(FundamentalsFetcher.Provider, parts["balanceSheet"]);
        Assert.Equal(FundamentalsFetcher.Provider, parts["valuation"]);

        // The two this provider files for nobody, and the one this component works
        // out rather than copying.
        Assert.Equal(FundamentalsFetcher.NotFiled, parts["segments"]);
        Assert.Equal(FundamentalsFetcher.NotFiled, parts["guidance"]);
        Assert.Equal(FundamentalsFetcher.Computed, parts["margin"]);
    }

    [Fact]
    public async Task TheMarginIsThisFilingsOwnAndNotTheTrailingYears()
    {
        using var store = new TemporaryStore().Migrated();

        await Fetcher(store, Feed()).RunAsync("AAPL", null, "open-1");

        using var payload = JsonDocument.Parse(Rows(store, "AAPL")[0].Payload);

        var quarter = payload.RootElement.GetProperty("quarter");

        var revenue = decimal.Parse(quarter.GetProperty("revenue").GetString()!, CultureInfo.InvariantCulture);
        var gross = decimal.Parse(quarter.GetProperty("grossProfit").GetString()!, CultureInfo.InvariantCulture);
        var margin = decimal.Parse(quarter.GetProperty("grossMargin").GetString()!, CultureInfo.InvariantCulture);

        Assert.Equal(decimal.Round(gross / revenue, 6, MidpointRounding.ToEven), margin);

        // The provider files a margin for the trailing twelve months and section 4
        // asks for one per quarter, which is why this is computed at all. The two
        // differ, so a reader that copied the trailing figure onto a quarter would
        // be showing the wrong period rather than a rounding.
        Assert.NotEqual(0.2762m, margin);
    }

    [Fact]
    public void AMarginOverNoRevenueIsAbsentRatherThanZero()
    {
        // A zero would be a figure a reader acts on, and a division would throw on
        // a quarter the provider filed no revenue for. Both are the same case.
        Assert.Null(FundamentalsFetcher.Margined(5m, 0m));
        Assert.Null(FundamentalsFetcher.Margined(5m, null));
        Assert.Null(FundamentalsFetcher.Margined(null, 10m));
        Assert.Equal(0.5m, FundamentalsFetcher.Margined(5m, 10m));

        // A loss is a negative margin rather than an absence, because a company
        // that lost money reported something.
        Assert.Equal(-0.25m, FundamentalsFetcher.Margined(-5m, 20m));
    }

    [Fact]
    public async Task TheValuationAndTheBasesSitOnTheNewestFilingAlone()
    {
        using var store = new TemporaryStore().Migrated();

        await Fetcher(store, Feed()).RunAsync("AAPL", null, "open-1");

        var rows = Rows(store, "AAPL");

        using var newest = JsonDocument.Parse(rows[0].Payload);

        Assert.NotEqual(JsonValueKind.Null, newest.RootElement.GetProperty("valuation").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, newest.RootElement.GetProperty("epsBases").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, newest.RootElement.GetProperty("estimated").ValueKind);

        // A ratio has a price in it and a price moves every session, so writing
        // today's onto a filing from two years ago would state that the market's
        // view of that quarter was this one.
        foreach (var older in rows.Skip(1))
        {
            using var payload = JsonDocument.Parse(older.Payload);

            Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("valuation").ValueKind);
            Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("epsBases").ValueKind);
            Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("estimated").ValueKind);
        }

        // Every row carries the quarter's own figures, which are as of the filing
        // rather than as of the fetch.
        Assert.All(rows, row =>
        {
            using var payload = JsonDocument.Parse(row.Payload);

            Assert.NotEqual(JsonValueKind.Null, payload.RootElement.GetProperty("quarter").ValueKind);
        });
    }

    [Fact]
    public async Task ANameFilingNoEstimateStoresNoneRatherThanAnEmptyOne()
    {
        using var store = new TemporaryStore().Migrated();

        await Fetcher(store, Feed()).RunAsync("KEYS", null, "open-1");

        using var payload = JsonDocument.Parse(Rows(store, "KEYS")[0].Payload);

        Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("estimated").ValueKind);
    }

    [Fact]
    public async Task TheOpenIsRecordedWithWhatItCostAndNoModelCall()
    {
        using var store = new TemporaryStore().Migrated();

        await Fetcher(store, Feed()).RunAsync("AAPL", null, "open-1");

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT model_calls, network_requests, spend, rows_written FROM run_log " +
            "WHERE stage = $stage ORDER BY rowid DESC LIMIT 1;";
        command.Parameters.AddWithValue("$stage", FundamentalsFetcher.Stage);

        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());

        // No model call, which is what every stage on this path records and what
        // the fetch of a document is: a request, not a completion.
        Assert.Equal(0, reader.GetInt32(0));
        Assert.Equal(1, reader.GetInt32(1));
        Assert.Equal("0", reader.GetString(2));
        Assert.Equal(FundamentalsFetcher.StoredFilings, reader.GetInt32(3));
    }

    [Fact]
    public async Task EachOfTheFourNamesIsStoredWithItsOwnFiscalCalendar()
    {
        using var store = new TemporaryStore().Migrated();

        foreach (var ticker in new[] { "AAPL", "MSFT", "KEYS", "NFLX" })
        {
            await Fetcher(store, Feed()).RunAsync(ticker, null, "open-" + ticker);
        }

        var newest = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var ticker in new[] { "AAPL", "MSFT", "KEYS", "NFLX" })
        {
            var rows = Rows(store, ticker);

            Assert.NotEmpty(rows);

            using var payload = JsonDocument.Parse(rows[0].Payload);

            newest[ticker] = payload.RootElement.GetProperty("periodEnd").GetString()!;
        }

        // KEYS reports on a different quarter end from the other three, which is
        // what a reader ordering five quarters by the calendar rather than per name
        // would get wrong.
        Assert.Equal("2026-07-31", newest["KEYS"]);
        Assert.Equal("2026-06-30", newest["AAPL"]);
    }
}
