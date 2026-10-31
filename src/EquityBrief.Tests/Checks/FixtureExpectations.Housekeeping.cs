using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Dividends;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Quarters;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.12: the monthly run's two housekeeping asks. Its facts step, after it reads the SEC's ticker
// file, asks the quarters of each member of the three indices whose newest company row carries no filer or another than
// the file names, at most 220 a run and the rest left to the next; and its dividends step asks each member no run has
// kept a dividend for, from the history run's first date. Each states its asks and their weighted calls before its
// first request.
// see: The monthly run asks the quarters of each member whose filer is none or not the SEC's own and the dividends of each member none were kept for, each ask stated before the first
public partial class FixtureExpectations
{
    // The row 21.12 adds that this check reaches: section 17's housekeeping asks.
    internal static readonly string[] HousekeepingClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The monthly run's housekeeping asks"),
    ];

    static readonly DateTimeOffset HousekeepingAt = new(2026, 11, 7, 14, 0, 0, TimeSpan.Zero);

    static void Member(TemporaryStore store, string index, string ticker) =>
        store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('{index}', '{ticker}', '2020-01-02', NULL, '2026-09-01T23:30:00Z');");

    [Fact]
    public async Task TheMonthlyRunAsksAMemberWithNoFilerAndOneWithAnotherAndNotOneMatchingTheSecsFileStatingItsAsksFirst()
    {
        using var store = new TemporaryStore().Migrated();
        var clock = FixedClock.At(HousekeepingAt, SessionZones.UnitedStates);

        // AAPL's row carries the file's filer, BNY's none, NFLX's another than the file's; MSFT's none in the S&P 400, so
        // the wider indices are read beside the S&P 500's own; and ZZZZ's none, but it is a member of no index.
        Member(store, "GSPC", "AAPL");
        Member(store, "GSPC", "BNY");
        Member(store, "GSPC", "NFLX");
        Member(store, "MID", "MSFT");
        await new SecTickers(clock, store.DatabaseFile).ReadAsync(new RecordedSecTickersFeed(TickersCapture()), TextWriter.Null, "sec-tickers-constructed");
        store.Execute("INSERT INTO company (ticker, fetched_at, cik, cik_source) VALUES ('AAPL', '2026-10-01T00:00:00Z', '0000320193', 'sec');");
        store.Execute("INSERT INTO company (ticker, fetched_at, cik) VALUES ('BNY', '2026-10-01T00:00:00Z', NULL);");
        store.Execute("INSERT INTO company (ticker, fetched_at, cik, cik_source) VALUES ('NFLX', '2026-10-01T00:00:00Z', '0000000077', 'provider');");
        store.Execute("INSERT INTO company (ticker, fetched_at, cik) VALUES ('MSFT', '2026-10-01T00:00:00Z', NULL);");
        store.Execute("INSERT INTO company (ticker, fetched_at, cik) VALUES ('ZZZZ', '2026-10-01T00:00:00Z', NULL);");

        var fundamentals = new FilerFundamentals(new Dictionary<string, string>(StringComparer.Ordinal));
        var said = new List<(string Line, int Requests)>();
        var outcome = await new QuarterFetcher(fundamentals, new AClose(), () => 0, clock, store.DatabaseFile).RunAsync(
            "GSPC",
            "loop-month-constructed",
            wider: ["MID", "SML"],
            most: QuarterFetcher.MonthlyFilerAsks,
            stating: line => said.Add((line, fundamentals.Requests)),
            filers: true);

        Assert.Equal(["BNY", "MSFT", "NFLX"], outcome.Asked.Select(ask => ask.Ticker));

        // Stated once, before any request, with the eleven weighted calls an ask.
        Assert.Equal(
            [("3 ask(s) of the 3 member(s) whose newest company row carries no filer or another than the SEC's own ticker file's, at most 33 weighted call(s)", 0)],
            said);
        Assert.Equal(3, fundamentals.Requests);

        // Each asked member's new row carries the file's filer.
        Assert.Equal("0001390777|sec", Text(store, "SELECT cik || '|' || cik_source FROM company WHERE ticker = 'BNY' ORDER BY fetched_at DESC LIMIT 1;"));
        Assert.Equal("0001065280|sec", Text(store, "SELECT cik || '|' || cik_source FROM company WHERE ticker = 'NFLX' ORDER BY fetched_at DESC LIMIT 1;"));
    }

    [Fact]
    public async Task TheMonthlyRunsFilerAsksStopAtTheirCapWithOneUnderItAskingAndTheRestLeftToTheNextRun()
    {
        Assert.Equal(220, QuarterFetcher.MonthlyFilerAsks);

        using var store = new TemporaryStore().Migrated();
        var clock = FixedClock.At(HousekeepingAt, SessionZones.UnitedStates);

        // 221 members of the S&P 600 whose rows carry no filer, one past the cap.
        var owed = Enumerable.Range(0, QuarterFetcher.MonthlyFilerAsks + 1).Select(at => $"Q{at:D3}").ToArray();

        foreach (var ticker in owed)
        {
            Member(store, "SML", ticker);
            store.Execute($"INSERT INTO company (ticker, fetched_at, cik) VALUES ('{ticker}', '2026-10-01T00:00:00Z', NULL);");
        }

        var said = new List<string>();
        var capped = await new QuarterFetcher(new FilerFundamentals(new Dictionary<string, string>(StringComparer.Ordinal)), new AClose(), () => 0, clock, store.DatabaseFile)
            .RunAsync("GSPC", "loop-month-capped", wider: ["MID", "SML"], most: QuarterFetcher.MonthlyFilerAsks, stating: said.Add, filers: true);

        Assert.Equal(owed.Take(220), capped.Asked.Select(ask => ask.Ticker));
        Assert.Equal(["220 ask(s) of the 221 member(s) whose newest company row carries no filer or another than the SEC's own ticker file's, at most 2420 weighted call(s)"], said);

        // The next month's run, the 220 asked now carrying a filer, asks the one left: one owed, under the cap, is asked.
        var next = FixedClock.At(HousekeepingAt.AddDays(35), SessionZones.UnitedStates);
        store.Execute("UPDATE company SET cik = '0000000001' WHERE ticker <> 'Q220';");

        var under = await new QuarterFetcher(new FilerFundamentals(new Dictionary<string, string>(StringComparer.Ordinal)), new AClose(), () => 0, next, store.DatabaseFile)
            .RunAsync("GSPC", "loop-month-under", wider: ["MID", "SML"], most: QuarterFetcher.MonthlyFilerAsks, filers: true);

        Assert.Equal(["Q220"], under.Asked.Select(ask => ask.Ticker));
    }

    sealed class DividendsAsked : IDividendHistoryFeed
    {
        public List<(string Ticker, DateOnly From)> Asked { get; } = [];

        public int Requests { get; private set; }

        public Task<IReadOnlyList<DividendPaid>> DividendsAsync(string ticker, DateOnly from, CancellationToken cancellation = default)
        {
            Requests++;
            Asked.Add((ticker, from));

            return Task.FromResult<IReadOnlyList<DividendPaid>>(
                ticker == "PAYS" ? [new DividendPaid(ticker, new DateOnly(2026, 9, 15), 0.25m, 0.25m, null, null, null, "Quarterly", "USD")] : []);
        }
    }

    [Fact]
    public async Task TheMonthlyRunsDividendsStepAsksEachMemberNoRunKeptADividendForAndNotOneWithAnyStatingItsAsksFirst()
    {
        using var store = new TemporaryStore().Migrated();
        var clock = FixedClock.At(HousekeepingAt, SessionZones.UnitedStates);

        // KEPT holds one kept dividend; PAYS and NONE hold none in the S&P 500 and WIDE none in the S&P 600, so three are
        // asked, from the history run's first date; PAYS answers one and NONE pays none.
        Member(store, "GSPC", "KEPT");
        Member(store, "GSPC", "PAYS");
        Member(store, "GSPC", "NONE");
        Member(store, "SML", "WIDE");
        store.Execute("INSERT INTO dividend_event (ticker, ex_date, amount, source, run_id) VALUES ('KEPT', '2026-06-15', '0.5', 'night', 'constructed');");

        var feed = new DividendsAsked();
        var said = new List<(string Line, int Requests)>();
        var kept = await new DividendKeeper(clock, store.DatabaseFile)
            .UnkeptAsync(feed, MonthlyRun.Indices, "loop-month-dividends", line => said.Add((line, feed.Requests)));

        Assert.Equal([("NONE", new DateOnly(2018, 1, 1)), ("PAYS", new DateOnly(2018, 1, 1)), ("WIDE", new DateOnly(2018, 1, 1))], feed.Asked);
        Assert.Equal(DividendKeeper.HistoryFrom, new DateOnly(2018, 1, 1));
        Assert.Equal(
            [("asks about 3 weighted call(s) of the provider's 100,000 a day: 3 member(s) of GSPC, MID, SML today no run has kept a dividend for, one request each, from 2018-01-01", 0)],
            said);
        Assert.Equal((3, 1, 1), (kept.Asked, kept.Handed, kept.Kept));
        Assert.Equal(1L, Scalar(store, "SELECT COUNT(*) FROM dividend_event WHERE ticker = 'PAYS' AND source = 'history' AND run_id = 'loop-month-dividends';"));

        // Asked again a month on, PAYS now holds its dividend and is not asked; NONE and WIDE still hold none and are.
        var again = new DividendsAsked();
        await new DividendKeeper(FixedClock.At(HousekeepingAt.AddDays(35), SessionZones.UnitedStates), store.DatabaseFile)
            .UnkeptAsync(again, MonthlyRun.Indices, "loop-month-dividends-again");

        Assert.Equal(["NONE", "WIDE"], again.Asked.Select(one => one.Ticker));
    }
}
