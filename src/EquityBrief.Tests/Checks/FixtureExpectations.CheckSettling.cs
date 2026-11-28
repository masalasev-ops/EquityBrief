using System.Globalization;
using EquityBrief.Core.History;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.History;
using EquityBrief.Worker.Ledger;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 24.19 closing plan's items 3 and 4 and its addition: the check's differences sorted at 1%, one off a
// stock's own prices held to a second source over the sessions to it and corrected where the source parts from it, marked where
// it agrees or no source holds it; a wild close kept, replaced or marked; a stretch of another instrument's prices filled or
// excluded; every reader of the every-session table skipping a marked stock-session; the corrected codes' rows written again as
// a full rebuild writes them; and the draws, each worked by hand over constructed series and a constructed store.
// see: A difference of 1% or more the check finds is settled by a second source's closes where one agrees with a side, and otherwise its stock-session is marked unreliable and skipped by every reader of the table
public partial class FixtureExpectations
{
    // The check's settling's rows: the stores it writes and section 17's row of its bands.
    internal static string[] CheckSettlingRows => [.. SchemaColumns.CheckSettlingStoreRows, .. CheckSettlingClaims];

    internal static readonly string[] CheckSettlingClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The check's settling"),
    ];

    static readonly Dictionary<string, SourceBar[]> NoBars = new(StringComparer.Ordinal);

    static string Stamped(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static SweepBar Bar(DateOnly session, decimal close, decimal? raw = null) => new(session, close, close, close, 1000, close, raw ?? close);

    // The store's closes held to a source's: at half scale they hold the ratio 2 on every session, and one source close 2% off
    // stands apart while one 0.5% off agrees.
    [Fact]
    public void TheStoresClosesAreHeldToASourcesAtTheMedianRatioEachWithinOnePerCentOrApart()
    {
        SweepBar[] stored = [.. FillDays.Select((day, at) => Bar(day, StoredClose(at)))];
        var source = HalfScale(at => at == 10 ? StoredClose(at) / 2 * 1.02m : at == 11 ? StoredClose(at) / 2 * 1.005m : StoredClose(at) / 2);
        var audit = PriceAudit.Hold(stored, source, FillDays.ToHashSet())!;

        Assert.Equal(2m, audit.Ratio);
        Assert.Equal([FillDays[10]], audit.Apart.Select(held => held.Session));
        Assert.True(audit.Held.Single(held => held.Session == FillDays[11]).Agrees);
        Assert.Null(PriceAudit.Hold(stored, [], FillDays.ToHashSet()));
        Assert.Equal(41, PriceAudit.Around(stored, FillDays[30]).Count);
        Assert.Equal(21, PriceAudit.Around(stored, FillDays[0]).Count);
        Assert.Equal(["2005 to 2012", "2013 to 2018", "2019 on"], new[] { new DateOnly(2012, 12, 31), new DateOnly(2013, 1, 2), new DateOnly(2019, 1, 2) }.Select(PriceAudit.Period));
    }

    // A wild close by hand: moves of 1% a session either way, a 20% rise taken back in full the next session is wild; one kept
    // the next session is not, and one taken back by less than half is not.
    [Fact]
    public void AWildCloseMovesFiveTimesItsTypicalMoveAndTheNextSessionTakesBackMoreThanHalf()
    {
        static SweepBar[] Series(Func<int, decimal, decimal> at)
        {
            var bars = new List<SweepBar>();
            var close = 100m;

            for (var place = 0; place < 40; place++)
            {
                close = at(place, place % 2 == 0 ? close * 1.01m : close / 1.01m);
                bars.Add(Bar(FillDays[place], decimal.Round(close, 4)));
            }

            return [.. bars];
        }

        Assert.Equal([FillDays[30]], PriceAudit.Wild(Series((at, close) => at == 30 ? close * 1.2m : at == 31 ? close / 1.2m : close)));
        Assert.Empty(PriceAudit.Wild(Series((at, close) => at == 30 ? close * 1.2m : close)));
        Assert.Empty(PriceAudit.Wild(Series((at, close) => at == 30 ? close * 1.2m : at == 31 ? close / 1.05m : close)));
        Assert.Empty(PriceAudit.Wild(Series((_, close) => close)));
    }

    // The sort by hand: 0.5% apart accepted; 2% apart on a reading off the stock's own prices settled by a source, on the VIX
    // marked as not from its prices, one the table reads its own way by design counted as a built fix; and a side holding none
    // the whole of it.
    [Fact]
    public void ADifferenceUnderOnePerCentIsAcceptedAndOneOverItSortedByWhetherItsPricesCanSettleIt()
    {
        SessionDifference Of(string reading, double? table, double? other, bool expected = false) => new("MID", SessionDifference.Setup, "pullback", "AAA", FillDays[5], reading, table, other, expected);

        Assert.Equal(0.5 / 100.5, CheckSettling.Size(Of("rsi", 100, 100.5)), 9);
        Assert.Equal(CheckSettling.Small, CheckSettling.Group(Of("rsi", 100, 100.5)));
        Assert.Equal(CheckSettling.Priced, CheckSettling.Group(Of("rsi", 100, 102)));
        Assert.Equal(CheckSettling.NotPriced, CheckSettling.Group(Of("vix", 100, 102)));
        Assert.Equal(CheckSettling.Built, CheckSettling.Group(Of("rsi", 100, 102, expected: true)));
        Assert.Equal(1, CheckSettling.Size(Of("rsi", null, 1)));
        Assert.True(CheckSettling.FromItsPrices("return_quarter"));
        Assert.False(CheckSettling.FromItsPrices("profit"));
        Assert.False(CheckSettling.FromItsPrices("no such reading"));
    }

    // A mark covers its stock and sessions on every reading; a later row lifting it ends it; a lift of another reading's mark
    // leaves it; an excluded stretch covers every session in it.
    [Fact]
    public void AMarkedStockSessionIsSkippedUntilALaterRowLiftsIt()
    {
        UnreliableMark Mark(string ticker, DateOnly first, DateOnly last, string reading, string kind, string at) => new(ticker, first, last, reading, kind, at);

        var marked = new MarkedSessions(
        [
            Mark("AAA", FillDays[5], FillDays[5], "rsi", MarkedSessions.Unreliable, "2026-11-28T01:00:00.000Z"),
            Mark("AAA", FillDays[5], FillDays[5], "close", MarkedSessions.Unreliable, "2026-11-28T01:00:00.000Z"),
            Mark("AAA", FillDays[5], FillDays[5], "rsi", MarkedSessions.Lifted, "2026-11-29T01:00:00.000Z"),
            Mark("BBB", FillDays[10], FillDays[20], MarkedSessions.Stretch, MarkedSessions.Excluded, "2026-11-28T01:00:00.000Z"),
            Mark("CCC", FillDays[3], FillDays[3], "close", MarkedSessions.Unreliable, "2026-11-28T01:00:00.000Z"),
            Mark("CCC", FillDays[3], FillDays[3], "close", MarkedSessions.Lifted, "2026-11-29T01:00:00.000Z"),
        ]);

        Assert.True(marked.Skips("AAA", FillDays[5]));
        Assert.False(marked.Skips("AAA", FillDays[6]));
        Assert.True(marked.Skips("BBB", FillDays[10]) && marked.Skips("BBB", FillDays[20]));
        Assert.False(marked.Skips("BBB", FillDays[21]));
        Assert.False(marked.Skips("CCC", FillDays[3]));
        Assert.Equal(2, marked.Count);
    }

    // The draw by hand: the same seed draws the same stock-sessions, at most the codes given, spread across them in turn.
    [Fact]
    public void TheDrawRepeatsFromItsSeedAndSpreadsItsSessionsAcrossAtMostTheCodesGiven()
    {
        (string, string, DateOnly)[] population = [.. new[] { "AAA", "BBB", "CCC", "DDD" }.SelectMany(ticker => FillDays.Select(day => ("GSPC", ticker, day)))];

        var once = PriceAudit.Draw(population, 9, 3, "seed");

        Assert.Equal(once, PriceAudit.Draw(population, 9, 3, "seed"));
        Assert.NotEqual(once, PriceAudit.Draw(population, 9, 3, "other"));
        Assert.Equal(3, once.Select(one => one.Ticker).Distinct().Count());
        Assert.All(once.GroupBy(one => one.Ticker), group => Assert.Equal(3, group.Count()));
    }

    // Over a constructed store: ALL's difference on its 50th session off its own prices, Yahoo's closes at half scale but 3% off
    // on its 45th, is corrected there at twice Yahoo's close, 66.95 for the store's 65; HOL's, Yahoo agreeing over every session to it, is marked with
    // its reading named; a difference on the VIX is marked as not from its prices; and one 0.5% apart is accepted. The long
    // history's reader reads ALL's corrected close; the recheck finding ALL still apart marks it unreliable instead; and every
    // row the settling writes takes inserts alone.
    [Fact]
    public async Task ADifferenceOffAStocksPricesIsCorrectedWhereASourcePartsFromThemAndMarkedWhereItAgrees()
    {
        using var store = FillStore();
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 29, 4, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        SourceBar[] allYahoo = [.. FillDays.Select((day, at) => new SourceBar(day, (20m + at) / 2, (20m + at) / 2, (20m + at) / 2, at == 45 ? (20m + at) / 2 * 1.03m : (20m + at) / 2, 500, null))];
        var holYahoo = HalfScale();
        var feed = new KeyedSources(new Dictionary<string, SourceBar[]>(StringComparer.Ordinal) { ["ALL"] = allYahoo, ["HOL"] = holYahoo }, NoBars);
        var fill = new SecondSourceFill(store.DatabaseFile, clock, feed, tiingo: false);
        SessionDifference[] differences =
        [
            new("MID", SessionDifference.Setup, "pullback", "ALL", FillDays[50], "return_quarter", 0.10, 0.12, false),
            new("MID", SessionDifference.Setup, "pullback", "HOL", FillDays[50], "rsi", 50, 60, false),
            new("MID", SessionDifference.Rebuilt, null, "ALL", FillDays[40], "vix", 20, 25, false),
            new("MID", SessionDifference.Rebuilt, null, "ALL", FillDays[41], "rsi", 50, 50.25, false),
        ];

        var said = new StringWriter();

        Assert.Empty(await fill.SettleAsync(differences, false, said));
        Assert.Contains("nothing asked", said.ToString(), StringComparison.Ordinal);
        Assert.Empty(feed.Asked);

        var settled = await fill.SettleAsync(differences, true, TextWriter.Null);

        Assert.Equal(
            [("ALL", FillDays[40], SecondSourceFill.NotItsPrices), ("ALL", FillDays[41], SecondSourceFill.Accepted), ("ALL", FillDays[50], SecondSourceFill.Corrected), ("HOL", FillDays[50], SecondSourceFill.Agreeing)],
            settled.Where(one => one.Category == "difference").Select(one => (one.Ticker, one.Session, one.Outcome)).Order());
        Assert.Equal("ALL 66.95 correction", (string)One(store, "SELECT ticker || ' ' || close || ' ' || kind FROM long_fill_bar;")!);
        Assert.Equal(["ALL|vix|unreliable", "HOL|rsi|unreliable"], Query(store, "SELECT ticker || '|' || reading || '|' || kind FROM unreliable_mark ORDER BY ticker, reading;"));
        Assert.Equal(4L, (long)One(store, "SELECT COUNT(*) FROM check_settled;")!);

        var (series, _) = await new LongHistoryReader(store.DatabaseFile).CodeSeriesAsync("ALL", FillDays[^1]);

        Assert.Equal(66.95m, series.Single(bar => bar.Session == FillDays[45]).Close);
        Assert.Equal([("MID", "ALL", FillDays[50])], await fill.CorrectedOnAsync());

        var proved = await fill.ProveAsync([new SessionDifference("MID", SessionDifference.Setup, "pullback", "ALL", FillDays[50], "return_quarter", 0.10, 0.12, false)], true, TextWriter.Null);

        Assert.False(Assert.Single(proved).Stands);
        Assert.Equal(1L, (long)One(store, "SELECT COUNT(*) FROM unreliable_mark WHERE ticker = 'ALL' AND reading = 'return_quarter';")!);
        Assert.True(Assert.Single(await fill.ProveAsync([], false, TextWriter.Null)).Stands);
        Assert.Throws<SqliteException>(() => store.Execute("DELETE FROM unreliable_mark;"));
        Assert.Throws<SqliteException>(() => store.Execute("UPDATE check_settled SET outcome = 'x';"));
        Assert.Throws<SqliteException>(() => store.Execute("DELETE FROM price_check;"));
    }

    // A wild close over a constructed store: kept where Yahoo agrees with it; replaced at twice Yahoo's close where Yahoo holds
    // the session and agrees with the closes around it; marked where no source holds the session.
    [Fact]
    public async Task AWildCloseIsKeptWhereASourceAgreesReplacedWhereOneHoldsItAndMarkedOtherwise()
    {
        using var store = FillStore();
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 29, 4, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        SweepBar[] series = [.. FillDays.Select((day, at) => Bar(day, at == 30 ? StoredClose(at) * 1.5m : StoredClose(at)))];
        var agreeing = HalfScale(at => at == 30 ? StoredClose(at) * 1.5m / 2 : StoredClose(at) / 2);

        var kept = await new SecondSourceFill(store.DatabaseFile, clock, new KeyedSources(new Dictionary<string, SourceBar[]>(StringComparer.Ordinal) { ["WLD"] = agreeing }, NoBars), tiingo: false)
            .SettleWildAsync("wild-kept", "WLD", series, [FillDays[30]]);

        Assert.Equal("kept: yahoo agrees", Assert.Single(kept).Outcome);

        var replaced = await new SecondSourceFill(store.DatabaseFile, clock, new KeyedSources(new Dictionary<string, SourceBar[]>(StringComparer.Ordinal) { ["WLD"] = HalfScale() }, NoBars), tiingo: false)
            .SettleWildAsync("wild-replaced", "WLD", series, [FillDays[30]]);

        Assert.Equal("replaced from yahoo", Assert.Single(replaced).Outcome);
        Assert.Equal("13", (string)One(store, "SELECT close FROM long_fill_bar WHERE run_id = 'wild-replaced';")!);

        var marked = await new SecondSourceFill(store.DatabaseFile, clock, new KeyedSources(NoBars, NoBars), tiingo: false)
            .SettleWildAsync("wild-marked", "WLD", series, [FillDays[30]]);

        Assert.StartsWith("marked", Assert.Single(marked).Outcome, StringComparison.Ordinal);
        Assert.Equal(1L, (long)One(store, "SELECT COUNT(*) FROM unreliable_mark WHERE run_id = 'wild-marked';")!);
    }

    // A stretch of another instrument's prices over a constructed store: its twentieth to thirty-ninth sessions at three times
    // the rest, Yahoo holding the right prices at half the store's scale, is filled from Yahoo at twice its prices; with Yahoo
    // holding three of its twenty sessions, more than a tenth of it would be marked and it is excluded as a block.
    [Fact]
    public async Task AStretchOfAnotherInstrumentsPricesIsFilledFromASourceOrExcludedAsABlock()
    {
        using var store = FillStore();
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 29, 4, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        SweepBar[] series = [.. FillDays.Select((day, at) => Bar(day, at is >= 20 and < 40 ? StoredClose(at) * 3 : StoredClose(at)))];
        DateOnly[] stretch = [.. FillDays[20..40]];

        var filled = await new SecondSourceFill(store.DatabaseFile, clock, new KeyedSources(new Dictionary<string, SourceBar[]>(StringComparer.Ordinal) { ["STR"] = HalfScale() }, NoBars), tiingo: false)
            .SettleStretchAsync("stretch-filled", "STR", series, stretch);

        Assert.Equal(("filled from yahoo", 20), (Assert.Single(filled).Outcome, Assert.Single(filled).Sessions));
        Assert.Equal(20L, (long)One(store, "SELECT COUNT(*) FROM long_fill_bar WHERE run_id = 'stretch-filled';")!);
        Assert.Equal("12", (string)One(store, "SELECT close FROM long_fill_bar WHERE run_id = 'stretch-filled' AND session_date = '" + Stamped(FillDays[20]) + "';")!);

        var thin = HalfScale().Where(bar => bar.Session < FillDays[20] || bar.Session >= FillDays[37]).ToArray();
        var excluded = await new SecondSourceFill(store.DatabaseFile, clock, new KeyedSources(new Dictionary<string, SourceBar[]>(StringComparer.Ordinal) { ["STR"] = thin }, NoBars), tiingo: false)
            .SettleStretchAsync("stretch-excluded", "STR", series, stretch);

        Assert.StartsWith("excluded", Assert.Single(excluded).Outcome, StringComparison.Ordinal);
        Assert.Equal(Stamped(FillDays[20]) + " " + Stamped(FillDays[39]) + " excluded",(string)One(store, "SELECT first_session || ' ' || last_session || ' ' || kind FROM unreliable_mark WHERE run_id = 'stretch-excluded';")!);
    }

    // Every reader of the every-session table skips a marked stock-session: the widths test's every member drops AAA's marked
    // session and keeps it once a later row lifts the mark.
    [Fact]
    public async Task EveryReaderOfTheEverySessionTableSkipsAMarkedStockSession()
    {
        using var store = new TemporaryStore().Migrated();
        var row = "[" + string.Join(",", Enumerable.Repeat("1", LedgerReadings.Count)) + "]";

        store.Execute("INSERT INTO session_reading (index_code, session_date, ticker, readings, pin, source) VALUES "
            + string.Join(", ", FillDays[..3].SelectMany(day => new[] { "AAA", "BBB" }.Select(ticker => FormattableString.Invariant($"('GSPC', '{day:yyyy-MM-dd}', '{ticker}', '{row}', 'p', 'build')")))) + ";");
        store.Execute("INSERT INTO unreliable_mark (run_id, ticker, first_session, last_session, reading, kind, why, marked_at) VALUES ('r1', 'AAA', '" + Stamped(FillDays[1]) + "', '" + Stamped(FillDays[1]) + "', 'rsi', 'unreliable', 'w', '2026-11-28T01:00:00.000Z');");

        Assert.Equal(5, (await EquityBrief.Worker.Loop.RangeWidthTest.EveryMemberAsync(store.DatabaseFile, "GSPC")).Count);
        Assert.DoesNotContain(await EquityBrief.Worker.Loop.RangeWidthTest.EveryMemberAsync(store.DatabaseFile, "GSPC"), one => one.Ticker == "AAA" && one.Session == FillDays[1]);

        store.Execute("INSERT INTO unreliable_mark (run_id, ticker, first_session, last_session, reading, kind, why, marked_at) VALUES ('r2', 'AAA', '" + Stamped(FillDays[1]) + "', '" + Stamped(FillDays[1]) + "', 'rsi', 'lifted', 'a source gave good prices', '2026-11-29T01:00:00.000Z');");

        Assert.Equal(6, (await EquityBrief.Worker.Loop.RangeWidthTest.EveryMemberAsync(store.DatabaseFile, "GSPC")).Count);
    }

    // The codes' rows written again as a full rebuild writes them: AAA's close moved on its 31st session by a kept correction,
    // the per-code rewrite of AAA leaves BBB's rows as they were and writes AAA's as a full rebuild over the same store writes
    // them, row for row.
    [Fact]
    public async Task TheCodesRowsWrittenAgainAreAFullRebuildsRowsForThoseCodes()
    {
        var days = SessionReadingsDays();
        var built = 0;
        SetupLedger ledger(TemporaryStore store) => new(FixedClock.At(new DateTimeOffset(2026, 11, 21, 15, 0, 0, TimeSpan.Zero).AddMinutes(++built), SessionZones.UnitedStates), store.DatabaseFile);
        static Task NoWait(TimeSpan _, CancellationToken __) => Task.CompletedTask;

        TemporaryStore Built()
        {
            var store = new TemporaryStore().Migrated();

            store.Execute("INSERT INTO long_span (index_code, ticker, joined, \"left\", read_from, run_id) VALUES ('GSPC', 'AAA', '2004-12-31', NULL, 'fund', 'r'), ('GSPC', 'BBB', '2004-12-31', NULL, 'fund', 'r');");
            store.Execute("INSERT INTO long_bar (ticker, session_date, open, high, low, close, raw_close, volume, run_id) VALUES "
                + string.Join(", ", days.Select((day, at) => SessionBar("AAA", day, 10m + (0.1m * at))).Concat(days.Select((day, at) => SessionBar("BBB", day, 20m - (0.05m * at))))) + ";");

            return store;
        }

        using var perCode = Built();
        using var full = Built();

        Assert.Equal(0, await ledger(perCode).SessionReadingsAsync(perCode.Root, "GSPC", word: true, TextWriter.Null, NoWait));
        Assert.Equal(0, await ledger(full).SessionReadingsAsync(full.Root, "GSPC", word: true, TextWriter.Null, NoWait));

        var before = Query(perCode, "SELECT ticker || '|' || session_date || '|' || readings FROM session_reading WHERE ticker = 'BBB' ORDER BY session_date;");
        var correction = FormattableString.Invariant($"INSERT INTO long_fill_bar (ticker, session_date, open, high, low, close, raw_close, volume, source, ratio, volume_proportion, kind, run_id) VALUES ('AAA', '{days[30]:yyyy-MM-dd}', '15', '15', '15', '15', '15', 1000, 'yahoo', '1', '1', 'correction', 'c');");

        perCode.Execute(correction);
        full.Execute(correction);

        Assert.Equal(2, await ledger(perCode).CodesAgainAsync(perCode.Root, "GSPC", new HashSet<string>(StringComparer.Ordinal) { "AAA" }, word: false, TextWriter.Null, NoWait));
        Assert.Equal(0, await ledger(perCode).CodesAgainAsync(perCode.Root, "GSPC", new HashSet<string>(StringComparer.Ordinal) { "AAA" }, word: true, TextWriter.Null, NoWait));
        Assert.Equal(0, await ledger(full).SessionReadingsAsync(full.Root, "GSPC", word: true, TextWriter.Null, NoWait, again: true));

        string Rows(TemporaryStore store, string ticker) => string.Join("\n", Query(store, $"SELECT ticker || '|' || session_date || '|' || readings FROM session_reading WHERE ticker = '{ticker}' ORDER BY session_date;"));

        Assert.Equal(Rows(full, "AAA"), Rows(perCode, "AAA"));
        Assert.NotEqual(string.Join("\n", Query(perCode, "SELECT readings FROM session_reading WHERE ticker = 'AAA' AND session_date = '" + days[30].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "';")), string.Join("\n", before));
        Assert.Equal(before, Query(perCode, "SELECT ticker || '|' || session_date || '|' || readings FROM session_reading WHERE ticker = 'BBB' ORDER BY session_date;"));
    }

    // The sources as recorded answers by symbol, Yahoo's and Tiingo's, each ask named.
    sealed class KeyedSources(IReadOnlyDictionary<string, SourceBar[]> yahoo, IReadOnlyDictionary<string, SourceBar[]> tiingo) : ISecondSourceFeed
    {
        public List<string> Asked { get; } = [];

        public Task<SourceAnswer> YahooAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellation = default) => Answer("yahoo " + symbol, yahoo.GetValueOrDefault(symbol) ?? [], from, to);

        public Task<SourceAnswer> StooqAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellation = default) => Answer("stooq " + symbol, [], from, to);

        public Task<(DateOnly First, DateOnly Last)?> TiingoMetaAsync(string symbol, CancellationToken cancellation = default)
        {
            Asked.Add("tiingo meta " + symbol);

            return Task.FromResult<(DateOnly, DateOnly)?>(tiingo.ContainsKey(symbol) ? (FillDays[0], FillDays[^1]) : null);
        }

        public Task<SourceAnswer> TiingoAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellation = default) => Answer("tiingo " + symbol, tiingo.GetValueOrDefault(symbol) ?? [], from, to);

        Task<SourceAnswer> Answer(string ask, SourceBar[] bars, DateOnly from, DateOnly to)
        {
            Asked.Add(ask);

            var held = bars.Where(bar => bar.Session >= from && bar.Session <= to).ToArray();

            return Task.FromResult(new SourceAnswer(held, held.Length == 0 ? "sent no session" : FormattableString.Invariant($"sent {held.Length} session(s)")));
        }
    }
}
