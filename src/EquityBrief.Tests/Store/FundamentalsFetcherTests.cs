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

    // The same fetcher with the filings archive behind it. A separate helper rather
    // than a default, so every test written before 6.2 keeps reading a row the
    // archive did not touch and the two worlds stay distinguishable.
    static FundamentalsFetcher WithArchive(
        TemporaryStore store,
        IFundamentalsFeed feed,
        IFilingsArchiveFeed? archive = null) =>
        new(feed, Clock(), store.DatabaseFile, archive ?? new RecordedFilingsArchiveFeed(Folder()));

    // An archive that refuses, which is the case the fixture cannot reach and the
    // one the two providers failing apart is about.
    sealed class Refusing : IFilingsArchiveFeed
    {
        public int Requests { get; private set; }

        public Task<ArchiveFilings> FilingsAsync(string ticker, string cik, CancellationToken cancellation = default)
        {
            Requests++;

            throw new ProviderRefusal("the archive could not be reached", transient: true);
        }
    }

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
            new FundamentalsOutcome("NEW", true, null, 3, 3, 3, 0, 3, 0, [], 1, 0, null, false));

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

        // The one this component works out rather than copying.
        Assert.Equal(FundamentalsFetcher.Computed, parts["margin"]);

        // And the three the archive supplies, which this fetch had no archive for.
        // Not the same statement as the provider filing none: a read that did not
        // happen and a provider that files a part for nobody are different rows, and
        // one that could not tell them apart would report a company with no segments.
        Assert.Equal(FundamentalsFetcher.NotRead, parts["segments"]);
        Assert.Equal(FundamentalsFetcher.NotRead, parts["guidance"]);
        Assert.Equal(FundamentalsFetcher.NotRead, parts["facts"]);
    }

    [Fact]
    public async Task TheArchivesPartsAreAttributedToTheArchiveAndNotToTheProvider()
    {
        using var store = new TemporaryStore().Migrated();

        await WithArchive(store, Feed()).RunAsync("AAPL", null, "open-1");

        using var source = JsonDocument.Parse(Rows(store, "AAPL")[0].Source);

        var parts = source.RootElement.EnumerateObject().ToDictionary(part => part.Name, part => part.Value.GetString());

        Assert.Equal(FundamentalsFetcher.Archive, parts["segments"]);
        Assert.Equal(FundamentalsFetcher.Archive, parts["guidance"]);
        Assert.Equal(FundamentalsFetcher.Archive, parts["facts"]);

        // And the company financials provider still owns its own eight, so one row
        // carrying two providers says which filled what.
        Assert.Equal(FundamentalsFetcher.Provider, parts["quarter"]);
        Assert.Equal(FundamentalsFetcher.Provider, parts["balanceSheet"]);
    }

    [Fact]
    public async Task AQuarterEndsOnTheDateItsOwnReportStatesAndOnTheProvidersLabelOnlyWhereTheArchiveIndexesNone()
    {
        using var store = new TemporaryStore().Migrated();

        await WithArchive(store, Feed()).RunAsync("AAPL", null, "open-1");

        var labelled = (await Feed().FundamentalsAsync("AAPL")).Filed.ToDictionary(
            quarter => quarter.FilingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            quarter => quarter.PeriodEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var ends = Rows(store, "AAPL").Select(row =>
        {
            using var payload = JsonDocument.Parse(row.Payload);
            using var source = JsonDocument.Parse(row.Source);

            return (row.FilingDate, Ended: payload.RootElement.GetProperty("periodEnd").GetString()!, From: source.RootElement.GetProperty("periodEnd").GetString()!);
        }).ToArray();

        // The captured index holds six periodic reports, the annual one among them, and
        // each of the six newest quarters takes its report's own period, days before the
        // month's last day the provider labels it with.
        Assert.Equal(
            ["2026-06-27", "2026-03-28", "2025-12-27", "2025-09-27", "2025-06-28", "2025-03-29"],
            ends.Take(6).Select(end => end.Ended));
        Assert.All(ends.Take(6), end => Assert.Equal(FundamentalsFetcher.Archive, end.From));
        Assert.All(ends.Take(6), end => Assert.NotEqual(labelled[end.FilingDate], end.Ended));

        // A quarter the index holds no report for keeps the provider's label, and the
        // row says whose date it is.
        Assert.Equal(FundamentalsFetcher.StoredFilings - 6, ends.Skip(6).Count());
        Assert.All(ends.Skip(6), end => Assert.Equal((labelled[end.FilingDate], FundamentalsFetcher.Provider), (end.Ended, end.From)));
    }

    [Fact]
    public async Task TheArchivesPartsSitOnTheNewestFilingAlone()
    {
        using var store = new TemporaryStore().Migrated();

        await WithArchive(store, Feed()).RunAsync("AAPL", null, "open-1");

        var rows = Rows(store, "AAPL");

        Assert.Equal(FundamentalsFetcher.StoredFilings, rows.Count);

        using var newest = JsonDocument.Parse(rows[0].Payload);

        // A segment table is read from one filing's own report page and the guidance
        // from one announcement's exhibit, so writing either onto a historical row
        // would state that a 2024 quarter's segments were this quarter's.
        Assert.Equal(JsonValueKind.Object, newest.RootElement.GetProperty("segments").ValueKind);
        Assert.Equal(JsonValueKind.Object, newest.RootElement.GetProperty("guidance").ValueKind);
        Assert.Equal(JsonValueKind.Array, newest.RootElement.GetProperty("facts").ValueKind);

        foreach (var row in rows.Skip(1))
        {
            using var older = JsonDocument.Parse(row.Payload);

            Assert.Equal(JsonValueKind.Null, older.RootElement.GetProperty("segments").ValueKind);
            Assert.Equal(JsonValueKind.Null, older.RootElement.GetProperty("guidance").ValueKind);
            Assert.Equal(JsonValueKind.Null, older.RootElement.GetProperty("facts").ValueKind);
        }
    }

    [Fact]
    public async Task TheStoredSegmentFiguresAreTheOnesTheArchiveRendered()
    {
        using var store = new TemporaryStore().Migrated();

        await WithArchive(store, Feed()).RunAsync("AAPL", null, "open-1");

        using var payload = JsonDocument.Parse(Rows(store, "AAPL")[0].Payload);

        var segments = payload.RootElement.GetProperty("segments");

        Assert.Equal("R46.htm", segments.GetProperty("report").GetString());
        Assert.Equal(1_000_000, segments.GetProperty("scale").GetInt32());
        Assert.Equal(4, segments.GetProperty("periods").GetArrayLength());
        Assert.Equal(6, segments.GetProperty("groups").GetArrayLength());

        // Money as text in the invariant form, which is the storage form money
        // takes, and the scale already applied.
        var sales = segments.GetProperty("consolidated").EnumerateArray().First(figure =>
            figure.GetProperty("lineItem").GetString() == "Net sales"
            && figure.GetProperty("months").GetInt32() == 3);

        Assert.Equal("109417000000", sales.GetProperty("value").GetString());
        Assert.Equal("2026-06-27", sales.GetProperty("ended").GetString());
    }

    [Fact]
    public async Task GuidanceIsStoredAsThePassageAndNeverAsAFigure()
    {
        using var store = new TemporaryStore().Migrated();

        // The filer whose release carries guidance under a heading.
        // see: Guidance is stored as management's own prose, and the facts file carries each figure the passage states as the claim checker reads it
        await WithArchive(store, Feed()).RunAsync("KEYS", null, "open-1");

        using var payload = JsonDocument.Parse(Rows(store, "KEYS")[0].Payload);

        var guidance = payload.RootElement.GetProperty("guidance");

        Assert.True(guidance.GetProperty("located").GetBoolean());
        Assert.Equal("Outlook", guidance.GetProperty("heading").GetString());
        Assert.Equal("exhibit991-q326pressrelease.htm", guidance.GetProperty("document").GetString());
        Assert.Equal("2026-08-18", guidance.GetProperty("filedOn").GetString());
        Assert.Contains("$1.930 billion to $1.950 billion", guidance.GetProperty("passage").GetString()!, StringComparison.Ordinal);

        // No figure anywhere in it. The passage is what was filed and the store
        // holds no revenue or earnings guide struck from it.
        Assert.Equal(
            ["document", "filedOn", "located", "heading", "passage"],
            guidance.EnumerateObject().Select(field => field.Name));
    }

    [Fact]
    public async Task AnExhibitWithNoHeadingIsStoredAsNotLocatedRatherThanAsNoGuidance()
    {
        using var store = new TemporaryStore().Migrated();

        // The other filer, whose exhibit states none at all. Both rows carry the
        // document and its date, because the exhibit is the evidence for which of
        // the two it was.
        await WithArchive(store, Feed()).RunAsync("AAPL", null, "open-1");

        using var payload = JsonDocument.Parse(Rows(store, "AAPL")[0].Payload);

        var guidance = payload.RootElement.GetProperty("guidance");

        Assert.False(guidance.GetProperty("located").GetBoolean());
        Assert.Equal(JsonValueKind.Null, guidance.GetProperty("passage").ValueKind);
        Assert.Equal("a8-kex991q3202606272026.htm", guidance.GetProperty("document").GetString());

        // And the part is still attributed to the archive, because the archive
        // answered: what it served was an exhibit with no guidance in it.
        using var source = JsonDocument.Parse(Rows(store, "AAPL")[0].Source);

        Assert.Equal(FundamentalsFetcher.Archive, source.RootElement.GetProperty("guidance").GetString());
    }

    [Fact]
    public async Task AnArchiveThatRefusesLosesItsFourPartsAndNotTheProvidersEight()
    {
        using var store = new TemporaryStore().Migrated();

        var refusing = new Refusing();
        var outcome = await WithArchive(store, Feed(), refusing).RunAsync("AAPL", null, "open-1");

        Assert.Equal(1, refusing.Requests);

        // The fetch stood. Twelve filings stored, which is what the other provider
        // supplied, because refusing the whole fetch would lose eight figures to
        // recover two.
        Assert.Equal(FundamentalsFetcher.StoredFilings, outcome.Held);
        Assert.Equal(FundamentalsFetcher.StoredFilings, outcome.RowsWritten);

        using var source = JsonDocument.Parse(Rows(store, "AAPL")[0].Source);

        Assert.Equal(FundamentalsFetcher.NotRead, source.RootElement.GetProperty("segments").GetString());
        Assert.Equal(FundamentalsFetcher.Provider, source.RootElement.GetProperty("quarter").GetString());

        // And the operator reads it, which is what makes a caught refusal legitimate
        // rather than swallowed.
        Assert.Contains("absent: segments, revenueTables, guidance, facts", Detail(store), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachRowCarriesItsGrowthOnTheSameQuarterAYearBeforeAndOnTheQuarterBeforeIt()
    {
        // AAPL's quarter to June 2026 against the same quarter of 2025 and against the one to
        // March 2026, worked by hand from the captured payload: revenue 109,417 against 94,036
        // and 111,184 millions, net income 29,789 against 23,434 millions, and earnings per
        // share 2.02 against 1.57, each change a fraction of the earlier figure.
        using var store = new TemporaryStore().Migrated();

        await Fetcher(store, Feed()).RunAsync("AAPL", null, "open-1");

        var rows = Rows(store, "AAPL");

        using var newest = JsonDocument.Parse(rows[0].Payload);

        var growth = newest.RootElement.GetProperty("growth");

        Assert.Equal("2025-06-30", growth.GetProperty("yearEarlier").GetString());
        Assert.Equal("0.163565", growth.GetProperty("revenue").GetString());
        Assert.Equal("0.271187", growth.GetProperty("netIncome").GetString());
        Assert.Equal("0.286624", growth.GetProperty("epsActual").GetString());
        Assert.Equal("2026-03-31", growth.GetProperty("quarterBefore").GetString());
        Assert.Equal("-0.015893", growth.GetProperty("revenueOnTheQuarterBefore").GetString());

        using var source = JsonDocument.Parse(rows[0].Source);

        Assert.Equal(FundamentalsFetcher.ComputedAcrossFilings, source.RootElement.GetProperty(FundamentalsFetcher.GrowthPart).GetString());

        // The two oldest rows stored, whose quarters a year before the capture does not hold,
        // carry the quarter before and no year's growth rather than a guessed one.
        Assert.All(rows.TakeLast(2), row =>
        {
            using var older = JsonDocument.Parse(row.Payload);

            var grown = older.RootElement.GetProperty("growth");

            Assert.Equal(JsonValueKind.Null, grown.GetProperty("yearEarlier").ValueKind);
            Assert.Equal(JsonValueKind.Null, grown.GetProperty("revenue").ValueKind);
            Assert.Equal(JsonValueKind.String, grown.GetProperty("quarterBefore").ValueKind);
        });

        // An earlier figure of zero or less, or none, gives no growth.
        Assert.Equal(1.5m, FundamentalsFetcher.Grown(5m, 2m));
        Assert.Null(FundamentalsFetcher.Grown(5m, 0m));
        Assert.Null(FundamentalsFetcher.Grown(5m, -2m));
        Assert.Null(FundamentalsFetcher.Grown(null, 2m));
    }

    // An archive answering as the recorded one does, with other tables of revenue by a
    // grouping put on the read, which neither filer the recording holds files.
    sealed class WithRevenueTables(IFilingsArchiveFeed inner, IReadOnlyList<SegmentBreakdown> tables) : IFilingsArchiveFeed
    {
        public int Requests => inner.Requests;

        public async Task<ArchiveFilings> FilingsAsync(string ticker, string cik, CancellationToken cancellation = default) =>
            await inner.FilingsAsync(ticker, cik, cancellation) is var read
                ? read with
                {
                    RevenueTables = tables,
                    PartsNotCarried = [.. read.PartsNotCarried.Where(part => part != SecEdgarArchive.RevenueTables)],
                }
                : throw new InvalidOperationException();
    }

    [Fact]
    public async Task TheNewestRowCarriesTheFilingsOtherRevenueTablesInTheSegmentTablesShape()
    {
        // The table by market platform a filing states beside its segments, stored on the
        // newest row as the segment table is, and the source column naming the archive for
        // it. Where the filing names none, the source says the archive served none.
        using var store = new TemporaryStore().Migrated();

        var page = File.ReadAllText(Path.Combine(Folder(), "segment-report-NVDA-R67.htm"));
        var platform = SecEdgarArchive.Breakdown(page, "R67.htm")!;

        await WithArchive(store, Feed(), new WithRevenueTables(new RecordedFilingsArchiveFeed(Folder()), [platform]))
            .RunAsync("AAPL", null, "open-1");

        var rows = Rows(store, "AAPL");

        using var newest = JsonDocument.Parse(rows[0].Payload);
        using var source = JsonDocument.Parse(rows[0].Source);

        var table = Assert.Single(newest.RootElement.GetProperty("revenueTables").EnumerateArray());

        Assert.Equal("R67.htm", table.GetProperty("report").GetString());
        Assert.Equal(1_000_000, table.GetProperty("scale").GetInt32());

        var dataCenter = table.GetProperty("groups").EnumerateArray()
            .Single(group => group.GetProperty("label").GetString() == "Data Center")
            .GetProperty("figures").EnumerateArray()
            .First(figure => figure.GetProperty("lineItem").GetString() == "Revenue"
                && figure.GetProperty("months").GetInt32() == 3
                && figure.GetProperty("ended").GetString() == "2026-07-26");

        // 89,023 as rendered under '$ in Millions'.
        Assert.Contains(">89,023<", page, StringComparison.Ordinal);
        Assert.Equal(89_023_000_000m, decimal.Parse(dataCenter.GetProperty("value").GetString()!, CultureInfo.InvariantCulture));
        Assert.Equal(FundamentalsFetcher.Archive, source.RootElement.GetProperty(SecEdgarArchive.RevenueTables).GetString());

        // Only the newest row, for the reason the segment table sits there alone.
        Assert.All(rows.Skip(1), row =>
        {
            using var older = JsonDocument.Parse(row.Payload);

            Assert.Equal(JsonValueKind.Null, older.RootElement.GetProperty("revenueTables").ValueKind);
        });

        using var plain = new TemporaryStore().Migrated();

        await WithArchive(plain, Feed()).RunAsync("AAPL", null, "open-1");

        using var none = JsonDocument.Parse(Rows(plain, "AAPL")[0].Source);

        Assert.Equal(FundamentalsFetcher.NotFiled, none.RootElement.GetProperty(SecEdgarArchive.RevenueTables).GetString());
    }

    [Fact]
    public async Task EachGroupOfTheFilingsOwnTablesCarriesItsGrowthOnTheSameMonthsAYearBefore()
    {
        // Worked by hand from the captured tables: AAPL's net sales 109,417 against 94,036 millions
        // for the company and 45,781 against 41,198 for the Americas, the quarter to 2026-06-27
        // against the one to 2025-06-28; and NVDA's data center revenue 89,023 against 41,096 in
        // its table by market platform. A cost, parenthesised, is a base below zero and grows by
        // nothing.
        using var store = new TemporaryStore().Migrated();

        var page = File.ReadAllText(Path.Combine(Folder(), "segment-report-NVDA-R67.htm"));

        await WithArchive(store, Feed(), new WithRevenueTables(new RecordedFilingsArchiveFeed(Folder()), [SecEdgarArchive.Breakdown(page, "R67.htm")!]))
            .RunAsync("AAPL", null, "open-1");

        var rows = Rows(store, "AAPL");

        using var newest = JsonDocument.Parse(rows[0].Payload);
        using var source = JsonDocument.Parse(rows[0].Source);

        var grown = newest.RootElement.GetProperty("tableGrowth").EnumerateArray().ToArray();

        string Of(string report, Func<string, bool> label, string lineItem) => grown
            .Single(figure => figure.GetProperty("report").GetString() == report
                && label(figure.GetProperty("label").GetString()!)
                && figure.GetProperty("lineItem").GetString() == lineItem)
            .GetProperty("value").GetString()!;

        Assert.Equal("0.163565", Of("R46.htm", label => label == FundamentalsFetcher.CompanyRows, "Net sales"));
        Assert.Equal("0.111243", Of("R46.htm", label => label.StartsWith("Americas", StringComparison.Ordinal), "Net sales"));
        Assert.Equal("1.166221", Of("R67.htm", label => label == "Data Center", "Revenue"));

        Assert.All(grown, figure =>
        {
            Assert.Equal(3, figure.GetProperty("months").GetInt32());
            Assert.NotEqual("Cost of sales", figure.GetProperty("lineItem").GetString());
        });

        Assert.Equal("2025-06-28", grown.First(figure => figure.GetProperty("report").GetString() == "R46.htm").GetProperty("yearEarlier").GetString());
        Assert.Equal(FundamentalsFetcher.ComputedFromTables, source.RootElement.GetProperty(FundamentalsFetcher.TableGrowthPart).GetString());

        // Only the newest row carries it, beside the tables it is computed from.
        Assert.All(rows.Skip(1), row =>
        {
            using var older = JsonDocument.Parse(row.Payload);

            Assert.Equal(JsonValueKind.Null, older.RootElement.GetProperty("tableGrowth").ValueKind);
        });
    }

    [Fact]
    public async Task TheRunLogSaysWhatTheArchiveCostAndWhichReportItRead()
    {
        using var store = new TemporaryStore().Migrated();

        await WithArchive(store, Feed()).RunAsync("AAPL", null, "open-1");

        var detail = Detail(store);

        Assert.Contains("segments from R46.htm", detail, StringComparison.Ordinal);
        Assert.DoesNotContain("guidance located", detail, StringComparison.Ordinal);

        using var second = new TemporaryStore().Migrated();

        await WithArchive(second, Feed()).RunAsync("KEYS", null, "open-1");

        Assert.Contains("segments from R85.htm", Detail(second), StringComparison.Ordinal);
        Assert.Contains("guidance located", Detail(second), StringComparison.Ordinal);
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
    public async Task TheAnalystsRatingsAreCopiedOntoTheNewestFilingAsTheProviderFilesThem()
    {
        // AAPL's, read off the captured payload by hand: a mean rating of 4.0417 on a scale of one
        // to five, a mean target price of 324.4016, and 23, 7, 16, 1 and 1 analysts at each grade
        // from a strong buy to a strong sell.
        // see: The fundamentals row carries the analysts' ratings the provider files, on the newest filing alone
        using var store = new TemporaryStore().Migrated();

        await Fetcher(store, Feed()).RunAsync("AAPL", null, "open-1");

        var rows = Rows(store, "AAPL");

        using var newest = JsonDocument.Parse(rows[0].Payload);
        using var source = JsonDocument.Parse(rows[0].Source);

        var ratings = newest.RootElement.GetProperty("ratings");

        Assert.Equal("4.0417", ratings.GetProperty("rating").GetString());
        Assert.Equal("324.4016", ratings.GetProperty("targetPrice").GetString());
        Assert.Equal([23, 7, 16, 1, 1], new[] { "strongBuy", "buy", "hold", "sell", "strongSell" }.Select(grade => ratings.GetProperty(grade).GetInt32()));
        Assert.Equal(FundamentalsFetcher.Provider, source.RootElement.GetProperty(FundamentalsFetcher.RatingsPart).GetString());
    }

    [Fact]
    public async Task TheDividendIsCopiedOntoTheNewestFilingAsTheProviderFilesIt()
    {
        // AAPL's, read off the captured payload by hand: 1.08 a share, a yield of 0.0033 and a
        // payout ratio of 0.1216, ex-dividend on 2026-08-10 and paid on 2026-08-13; KEYS files a
        // rate of zero and no dates, which is stored as filed rather than left out.
        // see: The numbers section shows the dividend the provider files, on the newest filing alone
        using var store = new TemporaryStore().Migrated();

        await Fetcher(store, Feed()).RunAsync("AAPL", null, "open-1");
        await Fetcher(store, Feed()).RunAsync("KEYS", null, "open-2");

        var apple = Rows(store, "AAPL");

        using var newest = JsonDocument.Parse(apple[0].Payload);
        using var source = JsonDocument.Parse(apple[0].Source);

        var dividend = newest.RootElement.GetProperty("dividend");

        Assert.Equal(
            ["1.08", "0.0033", "0.1216", "2026-08-10", "2026-08-13"],
            new[] { "forwardAnnualRate", "forwardYield", "payoutRatio", "exDividendDate", "payDate" }.Select(part => dividend.GetProperty(part).GetString()));
        Assert.Equal(FundamentalsFetcher.Provider, source.RootElement.GetProperty(FundamentalsFetcher.DividendPart).GetString());

        // On the newest filing alone, for the reason the ratios are.
        Assert.All(apple.Skip(1), row =>
        {
            using var older = JsonDocument.Parse(row.Payload);

            Assert.Equal(JsonValueKind.Null, older.RootElement.GetProperty("dividend").ValueKind);
        });

        using var keys = JsonDocument.Parse(Rows(store, "KEYS")[0].Payload);

        var none = keys.RootElement.GetProperty("dividend");

        Assert.Equal("0", none.GetProperty("forwardAnnualRate").GetString());
        Assert.Equal(JsonValueKind.Null, none.GetProperty("exDividendDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, none.GetProperty("payDate").ValueKind);
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
