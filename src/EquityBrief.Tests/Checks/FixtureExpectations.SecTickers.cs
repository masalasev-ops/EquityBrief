using EquityBrief.Core.Bars;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Quarters;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.3's corrections of the filer: a member's filer is the one the SEC's own ticker file names for it,
// read as sent, kept by the step that reads it and taken by the quarters fetch wherever the file names one, the
// provider's CIK only where it names none, a row carrying another filer asked again by hand, and the step's members the
// tickers the three indices hold on its session alone.
// see: A member's filer is the one the SEC's own ticker file names for it, and the provider's only where the file names none
public partial class FixtureExpectations
{
    static string TickersCapture() =>
        File.ReadAllText(Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05", RecordedSecTickersFeed.File));

    [Fact]
    public void TheSecTickerFileIsReadAsSentAndALineHoldingNoFilerIsRefused()
    {
        var lines = SecEdgarTickers.Parse(TickersCapture());

        // The cut's seven lines in the file's own order, each filer padded to ten digits, Alphabet's two classes under
        // one filer.
        Assert.Equal(
            ["AAPL 0000320193", "GOOGL 0001652044", "MSFT 0000789019", "NFLX 0001065280", "BNY 0001390777", "KEYS 0001601046", "GOOG 0001652044"],
            lines.Select(line => $"{line.Ticker} {line.Cik}"));
        Assert.Equal("Bank of New York Mellon Corp", lines.Single(line => line.Ticker == "BNY").Title);

        // A ticker listed twice is read once, by its first line.
        Assert.Equal(
            "0000000001",
            Assert.Single(SecEdgarTickers.Parse("{\"0\":{\"cik_str\":1,\"ticker\":\"ZZ\",\"title\":\"one\"},\"1\":{\"cik_str\":2,\"ticker\":\"ZZ\",\"title\":\"two\"}}")).Cik);

        // A file that is no object of lines, and a line holding no ticker or no filer, are refused.
        Assert.Throws<FormatException>(() => SecEdgarTickers.Parse("[]"));
        Assert.Throws<FormatException>(() => SecEdgarTickers.Parse("{\"0\":{\"ticker\":\"ZZ\",\"title\":\"no filer\"}}"));
        Assert.Throws<FormatException>(() => SecEdgarTickers.Parse("{\"0\":{\"cik_str\":7,\"title\":\"no ticker\"}}"));
        Assert.Throws<FormatException>(() => SecEdgarTickers.Parse("{\"0\":{\"cik_str\":0,\"ticker\":\"ZZ\",\"title\":\"no filer\"}}"));
    }

    [Fact]
    public async Task TheStepKeepsTheFileUnderItsRunAndNamesTheMembersTheProviderFilesNoCikFor()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute("INSERT INTO company (ticker, fetched_at, cik) VALUES ('AAPL', '2026-10-01T00:00:00Z', '0000320193');");
        store.Execute("INSERT INTO company (ticker, fetched_at, cik) VALUES ('BNY', '2026-10-01T00:00:00Z', NULL);");
        store.Execute("INSERT INTO company (ticker, fetched_at, cik) VALUES ('ZZZZ', '2026-10-01T00:00:00Z', NULL);");
        store.Execute("INSERT INTO company (ticker, fetched_at, cik) VALUES ('GONE', '2026-10-01T00:00:00Z', NULL);");
        store.Execute("INSERT INTO company (ticker, fetched_at, cik) VALUES ('SOON', '2026-10-01T00:00:00Z', NULL);");
        store.Execute("INSERT INTO company (ticker, fetched_at, cik) VALUES ('EDGE', '2026-10-01T00:00:00Z', NULL);");
        store.Execute("INSERT INTO company (ticker, fetched_at, cik) VALUES ('LATE', '2026-10-01T00:00:00Z', NULL);");

        // The members on the session the clock falls on in New York, 2026-10-16, where the UTC date is already the 17th:
        // AAPL and BNY in the S&P 500, ZZZZ in the S&P 600, EDGE in the S&P 400, its span joining on that session, and
        // LATE in the S&P 500, its span leaving on the 17th. GONE's span leaves the S&P 500 on the session and SOON's
        // joins the S&P 400 on a later one, so neither is a member whatever its company row holds.
        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', 'AAPL', '1982-11-30', NULL, '2026-10-16T22:00:00Z');");
        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', 'BNY', NULL, NULL, '2026-10-16T22:00:00Z');");
        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('SML', 'ZZZZ', '2026-10-05', NULL, '2026-10-16T22:00:00Z');");
        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', 'EDGE', '2026-10-16', NULL, '2026-10-16T22:00:00Z');");
        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', 'LATE', '2019-03-01', '2026-10-17', '2026-10-16T22:00:00Z');");
        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', 'GONE', '2022-02-17', '2026-10-16', '2026-10-16T22:00:00Z');");
        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', 'SOON', '2026-10-19', NULL, '2026-10-16T22:00:00Z');");

        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 17, 1, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var feed = new RecordedSecTickersFeed(TickersCapture());
        var outcome = await new SecTickers(clock, store.DatabaseFile).ReadAsync(feed, TextWriter.Null, "sec-tickers-constructed");

        // Every line kept under the run, and of the four members the provider files no CIK for, BNY named and EDGE, LATE
        // and ZZZZ not; GONE and SOON, holding none and named by no line of the file, counted among none of them.
        Assert.Equal((7, 4, null as string), (outcome.Tickers, outcome.Unmatched, outcome.Refusal));
        Assert.Equal(["EDGE", "LATE", "ZZZZ"], outcome.NotListed);
        Assert.Equal(7L, Scalar(store, "SELECT COUNT(*) FROM sec_ticker WHERE run_id = 'sec-tickers-constructed';"));
        Assert.Equal("0001390777", Text(store, "SELECT cik FROM sec_ticker WHERE ticker = 'BNY';"));
        Assert.Equal(1, feed.Documents);

        var row = Text(store, "SELECT outcome || '|' || network_requests || '|' || detail FROM run_log WHERE run_id = 'sec-tickers-constructed' AND stage = 'sec-tickers';");

        Assert.StartsWith("ok|1|7 ticker(s) the SEC's file lists, kept under this run; 1 of the 4 member(s) the provider files no CIK for named by it; not named: EDGE, LATE, ZZZZ", row, StringComparison.Ordinal);

        // A file the archive holds none of stores nothing and says so on its row.
        var none = await new SecTickers(clock, store.DatabaseFile).ReadAsync(new RecordedSecTickersFeed(null), TextWriter.Null, "sec-tickers-none");

        Assert.Equal("the archive holds no ticker file", none.Refusal);
        Assert.Equal(0L, Scalar(store, "SELECT COUNT(*) FROM sec_ticker WHERE run_id = 'sec-tickers-none';"));
        Assert.Equal("failed", Text(store, "SELECT outcome FROM run_log WHERE run_id = 'sec-tickers-none';"));
    }

    // A company's fundamentals as the test has the provider file them, a CIK or none for each member.
    sealed class FilerFundamentals(IReadOnlyDictionary<string, string> ciks) : IFundamentalsFeed
    {
        public int Requests { get; private set; }

        public Task<CompanyFundamentals> FundamentalsAsync(string ticker, CancellationToken cancellation = default)
        {
            Requests++;

            var end = new DateOnly(2026, 6, 30);
            FiledQuarter[] filed =
            [
                new(end, end.AddDays(30), new QuarterFigures(1000m, 400m, 100m, 300m, 110m), new BalanceSheet(null, null, null, null, null), new ReportedEarnings(end.AddDays(25), EventTiming.After, 1.00m, 1.00m)),
            ];

            return Task.FromResult(new CompanyFundamentals(
                ticker, "USD", ciks.TryGetValue(ticker, out var cik) ? cik : string.Empty, filed, null, new EpsBases(null, null, null), new ValuationRatios(null, null),
                new MarketValue(null), new AnalystRatings(null, null, null, null, null, null, null), [], 0));
        }
    }

    sealed class AClose : IHistoricalBarFeed
    {
        public int Requests { get; private set; }

        public Task<IReadOnlyList<ProviderBar>> BarsAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            Requests++;

            return Task.FromResult<IReadOnlyList<ProviderBar>>([new ProviderBar(to, 50m, 51m, 49m, 50m, 50m, 1000)]);
        }
    }

    [Fact]
    public async Task TheQuartersFetchTakesTheSecFilerWhereverTheFileNamesOneAndTheProvidersOnlyWhereItNamesNone()
    {
        using var store = new TemporaryStore().Migrated();

        foreach (var ticker in new[] { "AAPL", "BNY", "MSFT", "QQQQ", "ZZZZ" })
        {
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', '2020-01-02', NULL, '2026-09-01T23:30:00Z');");
        }

        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 16, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        await new SecTickers(clock, store.DatabaseFile).ReadAsync(new RecordedSecTickersFeed(TickersCapture()), TextWriter.Null, "sec-tickers-constructed");

        // The provider files Apple's own CIK, another filer's for MSFT, one for QQQQ, which the SEC's file does not list,
        // and none for BNY or ZZZZ, the file listing BNY alone of the two.
        await new QuarterFetcher(
            new FilerFundamentals(new Dictionary<string, string>(StringComparer.Ordinal) { ["AAPL"] = "320193", ["MSFT"] = "999", ["QQQQ"] = "4242" }),
            new AClose(),
            () => 0,
            clock,
            store.DatabaseFile).RunAsync("GSPC", "quarters-20261016");

        Assert.Equal("0000320193|sec", Text(store, "SELECT cik || '|' || cik_source FROM company WHERE ticker = 'AAPL';"));
        Assert.Equal("0000789019|sec", Text(store, "SELECT cik || '|' || cik_source FROM company WHERE ticker = 'MSFT';"));
        Assert.Equal("0001390777|sec", Text(store, "SELECT cik || '|' || cik_source FROM company WHERE ticker = 'BNY';"));
        Assert.Equal("0000004242|provider", Text(store, "SELECT cik || '|' || cik_source FROM company WHERE ticker = 'QQQQ';"));
        Assert.Equal(1L, Scalar(store, "SELECT COUNT(*) FROM company WHERE ticker = 'ZZZZ' AND cik IS NULL AND cik_source IS NULL;"));

        // The refetch by hand of the members whose newest company row carries no filer asks ZZZZ alone on the next session,
        // BNY's row carrying the SEC's.
        var refetched = await new QuarterFetcher(
            new FilerFundamentals(new Dictionary<string, string>(StringComparer.Ordinal)),
            new AClose(),
            () => 0,
            FixedClock.At(new DateTimeOffset(2026, 10, 19, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync("GSPC", "quarters-unfiled", unfiled: true);

        Assert.Equal(["ZZZZ"], refetched.Asked.Select(ask => ask.Ticker));

        // A member whose newest company row carries a filer other than the file's, as a row the provider's CIK filled before
        // the file's was taken first: the refetch of such members asks NFLX alone, QQQQ's being a ticker the file does not
        // list and ZZZZ's carrying none, and its new row carries the file's filer.
        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', 'NFLX', '2020-01-02', NULL, '2026-09-01T23:30:00Z');");
        store.Execute("INSERT INTO company (ticker, fetched_at, cik, cik_source) VALUES ('NFLX', '2026-10-01T00:00:00Z', '0000000077', 'provider');");

        var said = new List<string>();
        var misfiled = await new QuarterFetcher(
            new FilerFundamentals(new Dictionary<string, string>(StringComparer.Ordinal) { ["NFLX"] = "77" }),
            new AClose(),
            () => 0,
            FixedClock.At(new DateTimeOffset(2026, 10, 20, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync("GSPC", "quarters-misfiled", stating: said.Add, misfiled: true);

        Assert.Equal(["NFLX"], misfiled.Asked.Select(ask => ask.Ticker));
        Assert.Equal(["1 ask(s) of the 1 member(s) whose newest company row carries a filer other than the SEC's own ticker file's, at most 11 weighted call(s)"], said);
        Assert.Equal("0001065280|sec", Text(store, "SELECT cik || '|' || cik_source FROM company WHERE ticker = 'NFLX' ORDER BY fetched_at DESC LIMIT 1;"));

        // Run again on the next session, it asks none: NFLX's newest row carries the file's filer, whatever its earlier one
        // carried.
        var again = await new QuarterFetcher(
            new FilerFundamentals(new Dictionary<string, string>(StringComparer.Ordinal) { ["NFLX"] = "77" }),
            new AClose(),
            () => 0,
            FixedClock.At(new DateTimeOffset(2026, 10, 21, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync("GSPC", "quarters-misfiled-again", misfiled: true);

        Assert.Empty(again.Asked);
    }
}
