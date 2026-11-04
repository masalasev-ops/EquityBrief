using EquityBrief.Api.Reading;
using EquityBrief.Core.Chart;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Charts;
using Xunit;

namespace EquityBrief.Tests.Checks;

// 22.36 and 22.37: the chart audit as a check, each flag and note worked by hand over constructed sessions.
// see: The chart check reads every member's chart after the bars land and asks nothing of the network, and lists what it finds the morning it appears
// see: A real event the chart check confirms is a line beneath the chart saying what happened on that date
public partial class FixtureExpectations
{
    internal static readonly string[] ChartCheckRows = [.. ComponentAccess.ChartCheckRows, .. SchemaColumns.ChartCheckStoreRows, .. Reading.ReadSurface.WhatHappenedRows];

    static readonly DateOnly CheckFirst = new(2026, 3, 2);

    static CheckedSession Day(int at, decimal close, long volume = 1000, bool pulled = false, decimal? raw = null) =>
        new(CheckFirst.AddDays(at), close, close + 1, close - 1, close, raw ?? close, volume, pulled);

    static StockToCheck Stock(IReadOnlyList<CheckedSession> sessions, Dictionary<string, IReadOnlyDictionary<DateOnly, double>>? averages = null, Dictionary<DateOnly, (decimal, decimal?)>? pull = null, decimal? scale = null, decimal? keptRaw = null, string state = "ok", bool noHistory = false, (string, DateOnly?)? change = null) =>
        new("TKR", sessions, averages ?? [], pull ?? [], scale, keptRaw, state, noHistory, change);

    static readonly StockEvents NoEvents = new(new HashSet<DateOnly>(), new HashSet<DateOnly>(), [], []);

    static DateOnly NightOfCheck => CheckFirst.AddDays(40);

    static IReadOnlyList<ChartFinding> Checked(StockToCheck stock, StockEvents? events = null) =>
        ChartCheck.Read(stock, events ?? NoEvents, NightOfCheck, []);

    [Fact]
    public void AnAverageAQuarterOutsideTheCandlesIsFlaggedOnItsIntervalAndOneInsideIsNot()
    {
        // Candles from 49 to 71: a quarter above the highest is 88.75 and a quarter below the lowest 36.75.
        var sessions = new[] { Day(0, 50), Day(1, 70) };
        var at = sessions[^1].Session;
        var far = Checked(Stock(sessions, new() { ["sma200"] = new Dictionary<DateOnly, double> { [at] = 88.76 } }));

        Assert.Equal(3, far.Count(finding => finding.Flag == "a"));
        Assert.Contains(far, finding => finding.Kind == "sma200 above on daily" && finding.Says.Contains("reaches 88.76", StringComparison.Ordinal));
        Assert.Contains(far, finding => finding.Kind == "sma200 above on weekly");
        Assert.Contains(far, finding => finding.Kind == "sma200 above on monthly");
        Assert.Contains(Checked(Stock(sessions, new() { ["sma20"] = new Dictionary<DateOnly, double> { [at] = 36.74 } })), finding => finding.Kind == "sma20 below on daily");
        Assert.DoesNotContain(Checked(Stock(sessions, new() { ["sma200"] = new Dictionary<DateOnly, double> { [at] = 88.74 } })), finding => finding.Flag == "a");
        Assert.DoesNotContain(Checked(Stock(sessions, new() { ["sma200"] = new Dictionary<DateOnly, double> { [at] = 36.76 } })), finding => finding.Flag == "a");
    }

    [Fact]
    public void AMoveIsNamedByWhatExplainsItAndFlaggedWhereNothingDoes()
    {
        // 100 to 56 is a fall of 44.0%.
        IReadOnlyList<CheckedSession> sessions = [Day(0, 100), Day(1, 56)];
        var moves = ChartCheck.MovesOf(sessions);
        var on = sessions[1].Session;
        var before = sessions[0].Session;
        var none = new Dictionary<DateOnly, int>();

        Assert.Equal("On 2026-03-03 the close fell 44.0% in a day, from 100.00 to 56.00, with no split, dividend, results or filing stored for it.", Assert.Single(ChartCheck.ReadMoves(moves, NoEvents, none)).Says);

        ChartFinding Read(StockEvents events, IReadOnlyDictionary<DateOnly, int>? count = null) => Assert.Single(ChartCheck.ReadMoves(moves, events, count ?? none));

        // Results on the session and on the one before, a report after the close moving the next session.
        var results = Read(NoEvents with { Results = new HashSet<DateOnly> { on } });
        Assert.Equal((ChartCheck.Note, "On 2026-03-03 the close fell 44.0% in a day, from 100.00 to 56.00, on its results."), (results.Flag, results.Says));
        Assert.Equal(ChartCheck.Note, Read(NoEvents with { Results = new HashSet<DateOnly> { before } }).Flag);
        Assert.Equal("b", Read(NoEvents with { Results = new HashSet<DateOnly> { before.AddDays(-1) } }).Flag);
        Assert.EndsWith("around a filing.", Read(NoEvents with { Filings = new HashSet<DateOnly> { on } }).Says, StringComparison.Ordinal);

        // A dividend at least half the fall of 44: 22 is, 21.99 is not.
        Assert.EndsWith("on a dividend.", Read(NoEvents with { Dividends = [new CheckedDividend(on, 22m)] }).Says, StringComparison.Ordinal);
        Assert.Equal("b", Read(NoEvents with { Dividends = [new CheckedDividend(on, 21.99m)] }).Flag);

        // Ten members moving that far is the market's; nine is not.
        Assert.EndsWith("one of 10 members moving that far that session.", Read(NoEvents, new Dictionary<DateOnly, int> { [on] = 10 }).Says, StringComparison.Ordinal);
        Assert.Equal("b", Read(NoEvents, new Dictionary<DateOnly, int> { [on] = 9 }).Flag);

        // A split the jump matches is one the prices still carry, whatever else stands on the day.
        var carried = Read(new StockEvents(new HashSet<DateOnly> { on }, new HashSet<DateOnly>(), [], [new CheckedSplit(on, 1622m, 1000m)]));
        Assert.Equal(("b", "split not applied"), (carried.Flag, carried.Kind));

        // 25% exactly is not past the limit.
        Assert.Empty(ChartCheck.MovesOf([Day(0, 100), Day(1, 75)]));
    }

    [Fact]
    public void ThePullIsHeldToTheStoreAtThePagesScaleAndTheJoinToTheStocksOwnMove()
    {
        IReadOnlyList<CheckedSession> sessions = [Day(0, 100, pulled: true), Day(1, 100, raw: 100), Day(2, 100, raw: 100)];
        var (first, second) = (sessions[1].Session, sessions[2].Session);

        // At a scale of 2 the pull's 50 is the store's 100, and 47.6 is 105.04% of it.
        Assert.DoesNotContain(Checked(Stock(sessions, pull: new() { [first] = (50m, 100m), [second] = (50m, 100m) }, scale: 2)), finding => finding.Flag == "c");
        Assert.Contains(Checked(Stock(sessions, pull: new() { [first] = (50m, 100m), [second] = (47.6m, 100m) }, scale: 2)), finding => finding.Kind == "store against pull");

        // A raw close 1.2% from the pull's, the adjusted within its limit.
        Assert.Contains(Checked(Stock(sessions, pull: new() { [first] = (50m, 100m), [second] = (50m, 98.8m) }, scale: 2)), finding => finding.Kind == "raw against pull");

        // The chart's move across the join is 0% and the stock's own, from a kept raw close of 90, 11.1%.
        Assert.Contains(Checked(Stock(sessions, keptRaw: 90)), finding => finding.Kind == "join" && finding.Says.Contains("+11.1%", StringComparison.Ordinal));
        Assert.DoesNotContain(Checked(Stock(sessions, keptRaw: 96)), finding => finding.Kind == "join");

        var missing = Assert.Single(Checked(Stock([Day(0, 100)], noHistory: true)), finding => finding.Kind == "no history");
        Assert.Contains("show one year", missing.Says, StringComparison.Ordinal);
    }

    [Fact]
    public void ADrawnAverageIsHeldToItsClosesWithinOnePerCent()
    {
        var sessions = Enumerable.Range(0, 20).Select(at => Day(at, 100)).ToArray();
        var last = sessions[^1].Session;

        Assert.DoesNotContain(Checked(Stock(sessions, new() { ["sma20"] = new Dictionary<DateOnly, double> { [last] = 100.9 } })), finding => finding.Flag == "c");
        Assert.Contains(Checked(Stock(sessions, new() { ["sma20"] = new Dictionary<DateOnly, double> { [last] = 101.1 } })), finding => finding.Kind == "sma20 against its closes");
    }

    [Fact]
    public void ACandleIsNamedForWhatIsWrongWithIt()
    {
        var copied = new CheckedSession(CheckFirst.AddDays(1), 100, 100, 100, 100, 100, 0, false);
        var zero = new CheckedSession(CheckFirst.AddDays(1), 101, 102, 100, 101, 101, 0, false);
        var inverted = new CheckedSession(CheckFirst.AddDays(1), 100, 99, 101, 100, 100, 10, false);
        var outside = new CheckedSession(CheckFirst.AddDays(1), 100, 101, 99, 102, 102, 10, false);

        Assert.Contains(Checked(Stock([Day(0, 100), copied])), finding => (finding.Flag, finding.Kind) == ("e", "copied candle"));
        Assert.Contains(Checked(Stock([Day(0, 100), zero])), finding => (finding.Flag, finding.Kind) == ("e", "zero volume"));
        Assert.Contains(Checked(Stock([Day(0, 100), inverted])), finding => finding.Kind == "impossible candle");
        Assert.Contains(Checked(Stock([Day(0, 100), outside])), finding => finding.Kind == "impossible candle");
        Assert.DoesNotContain(Checked(Stock([Day(0, 100), Day(1, 101)])), finding => finding.Flag == "e");
    }

    [Fact]
    public void AMissingSessionAndAStockBehindTheNightReadingOkAreFlaggedAndASuspectStockIsNot()
    {
        var sessions = new[] { Day(0, 100), Day(2, 100) };
        var traded = new[] { CheckFirst, CheckFirst.AddDays(1), CheckFirst.AddDays(2), CheckFirst.AddDays(3) };
        var read = ChartCheck.Read(Stock(sessions), NoEvents, CheckFirst.AddDays(3), traded);

        Assert.Equal(CheckFirst.AddDays(1), Assert.Single(read, finding => finding.Kind == "missing session").Session);
        Assert.Contains("behind the night of 2026-03-05, while the stock's state reads ok", Assert.Single(read, finding => finding.Kind == "behind reading ok").Says, StringComparison.Ordinal);
        Assert.DoesNotContain(ChartCheck.Read(Stock(sessions, state: "suspect"), NoEvents, CheckFirst.AddDays(3), traded), finding => finding.Kind == "behind reading ok");
        Assert.DoesNotContain(ChartCheck.Read(Stock(sessions), NoEvents, CheckFirst.AddDays(2), traded), finding => finding.Kind == "behind reading ok");
    }

    [Fact]
    public void AnActionTheBarsDoNotCarryIsFlaggedAndASpinOffTheyCarryIsANote()
    {
        var exDate = CheckFirst.AddDays(1);

        // CTVA's shape: 1333 for 200 and the factor unchanged across it.
        IReadOnlyList<CheckedSession> unadjusted = [Day(0, 77.65m, raw: 77.65m), Day(1, 12.57m, raw: 12.57m)];
        var ratio = new CheckedSplit(exDate, 1333m, 200m);

        Assert.Contains(Checked(Stock(unadjusted), NoEvents with { Splits = [ratio] }), finding => (finding.Flag, finding.Kind) == ("e", "action not carried"));

        // The same with the factor of 200 over 1333 on the session before: carried, and a note, since no company splits 1333 for 200.
        IReadOnlyList<CheckedSession> adjusted = [Day(0, 11.65m, raw: 77.65m), Day(1, 12.57m, raw: 12.57m)];
        var checkedSplit = Checked(Stock(adjusted), NoEvents with { Splits = [ratio] });

        Assert.DoesNotContain(checkedSplit, finding => finding.Kind == "action not carried");
        Assert.Contains("filed as a split of 1333 for 200", Assert.Single(checkedSplit, finding => finding.Kind == "spin-off").Says, StringComparison.Ordinal);

        // A company's own split, carried, says nothing, as the store's own BKNG and POWL; the store's filings of other events do not read as one.
        IReadOnlyList<CheckedSession> halved = [Day(0, 50m, raw: 100m), Day(1, 50m, raw: 50m)];

        Assert.DoesNotContain(Checked(Stock(halved), NoEvents with { Splits = [new CheckedSplit(exDate, 2m, 1m)] }), finding => finding.Kind == "spin-off");

        foreach (var (shares, old) in new[] { (2m, 1m), (3m, 2m), (5m, 4m), (4m, 3m), (25m, 1m), (1m, 10m), (1m, 5m), (959692m, 319897m), (2m, 3m) })
        {
            Assert.True(ChartCheck.IsACompanysOwnSplit(shares, old), FormattableString.Invariant($"{shares} for {old}"));
        }

        foreach (var (shares, old) in new[] { (1241m, 1000m), (1333m, 200m), (1057m, 1000m), (1907m, 2000m), (105m, 100m), (2.793m, 1m), (1622m, 1000m), (5m, 3m) })
        {
            Assert.False(ChartCheck.IsACompanysOwnSplit(shares, old), FormattableString.Invariant($"{shares} for {old}"));
        }

        // FDX's shape: 1241 for 1000, carried: a note saying so.
        IReadOnlyList<CheckedSession> spun = [Day(0, 331.8m, raw: 411.75m), Day(1, 338.49m, raw: 338.49m)];
        var note = Assert.Single(Checked(Stock(spun), NoEvents with { Splits = [new CheckedSplit(exDate, 1241m, 1000m)] }), finding => finding.Kind == "spin-off");

        Assert.Equal(ChartCheck.Note, note.Flag);
        Assert.Contains("filed as a split of 1241 for 1000", note.Says, StringComparison.Ordinal);
        Assert.Contains("from 411.75 to 338.49", note.Says, StringComparison.Ordinal);

        // A dividend of 2 on a raw close of 100 should move the factor by about 2%; carried at 2%, not carried at none.
        IReadOnlyList<CheckedSession> paid = [Day(0, 98m, raw: 100m), Day(1, 100m, raw: 100m)];
        IReadOnlyList<CheckedSession> unpaid = [Day(0, 100m, raw: 100m), Day(1, 100m, raw: 100m)];

        Assert.DoesNotContain(Checked(Stock(paid), NoEvents with { Dividends = [new CheckedDividend(exDate, 2m)] }), finding => finding.Kind == "action not carried");
        Assert.Contains(Checked(Stock(unpaid), NoEvents with { Dividends = [new CheckedDividend(exDate, 2m)] }), finding => finding.Kind == "action not carried");

        // Under half of 1% of the close asks nothing of the factor.
        Assert.DoesNotContain(Checked(Stock(unpaid), NoEvents with { Dividends = [new CheckedDividend(exDate, 0.49m)] }), finding => finding.Kind == "action not carried");

        // AMCR's shape: an amount a later reverse split restated to 0.65 over a raw close of 8.52, 7.6%, and the factor carrying
        // the 1.5% actually paid, which is past half of the smallest step read, 0.25%; 0.24% is not.
        var restated = new[] { new CheckedDividend(exDate, 0.65m) };

        Assert.DoesNotContain(Checked(Stock([Day(0, 39.71m, raw: 8.52m), Day(1, 40.77m, raw: 8.52m)]), NoEvents with { Dividends = restated }), finding => finding.Kind == "action not carried");
        Assert.Contains(Checked(Stock([Day(0, 40.67m, raw: 8.52m), Day(1, 40.77m, raw: 8.52m)]), NoEvents with { Dividends = restated }), finding => finding.Kind == "action not carried");
    }

    [Fact]
    public void ATickerChangeAndAFirstSessionAreNotes()
    {
        var change = Assert.Single(Checked(Stock([Day(0, 100)], change: ("FI", new DateOnly(2025, 11, 11)))), finding => finding.Kind == "ticker change");

        Assert.Equal((ChartCheck.Note, "On 2025-11-11 the ticker changed from FI to TKR."), (change.Flag, change.Says));
        Assert.Equal("Its earlier sessions were traded under VSCO.", Assert.Single(Checked(Stock([Day(0, 100)], change: ("VSCO", null))), finding => finding.Kind == "ticker change").Says);
        Assert.Equal("It started trading on 2026-03-02.", Assert.Single(Checked(Stock([Day(0, 100)])), finding => finding.Kind == "first session").Says);

        // Two years before the night is no first session, and a stock whose earlier history is simply not pulled says nothing of one.
        Assert.DoesNotContain(ChartCheck.Read(Stock([Day(0, 100)]), NoEvents, CheckFirst.AddYears(2).AddDays(1), []), finding => finding.Kind == "first session");
        Assert.DoesNotContain(Checked(Stock([Day(0, 100)], noHistory: true)), finding => finding.Kind == "first session");
    }

    [Fact]
    public async Task TheCheckReadsTheStoreWritesTheNightsRowsAndNamesWhatIsNew()
    {
        using var store = new TemporaryStore().Migrated();
        var sessions = TradedTo(FaultNight, 4);

        Member(store, "AAA");
        Stored(store, "AAA", sessions.Select(day => Priced(day, 100m)));
        Member(store, "BBB");
        Stored(store, "BBB", sessions.SkipLast(1).Select(day => Priced(day, 100m)).Append(Priced(sessions[^1], 40m)));
        store.Execute(FormattableString.Invariant($"INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('BBB', '{sessions[^1]:yyyy-MM-dd}', 'earnings', 'after', '', '2026-10-01T00:00:00Z');"));

        // Announced and not yet joined, it is no member of the night.
        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', 'SOON', '2026-12-01', NULL, '2026-10-02T23:00:00Z');");
        Stored(store, "SOON", sessions.Select(day => Priced(day, 100m)));

        var clock = FixedClock.At(NightAt(FaultNight), SessionZones.UnitedStates);
        var first = await new ChartChecker(clock, store.DatabaseFile).RunAsync("GSPC", "night-1");

        Assert.Equal(2, first.Stocks);
        Assert.Empty(Query(store, "SELECT ticker FROM chart_check WHERE ticker = 'SOON';"));
        Assert.Empty(first.New);
        Assert.Equal(["ok"], Query(store, "SELECT outcome FROM run_log WHERE run_id = 'night-1' AND stage = 'chart-check';"));

        var note = FormattableString.Invariant($"BBB|note|move|{sessions[^1]:yyyy-MM-dd}");
        Assert.Contains(note, Query(store, "SELECT ticker || '|' || flag || '|' || kind || '|' || session_date FROM chart_check WHERE ticker = 'BBB' AND flag = 'note' AND kind = 'move';"));

        // A flag new the night after turns the row partial and is named; the night before stands beside it and an older one goes.
        store.Execute("DELETE FROM calendar WHERE ticker = 'BBB';");

        var (second, third) = (new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6));

        foreach (var ticker in new[] { "AAA", "BBB" })
        {
            Stored(store, ticker, [Priced(second, ticker == "AAA" ? 100m : 40m)]);
        }

        var next = await new ChartChecker(FixedClock.At(NightAt(second), SessionZones.UnitedStates), store.DatabaseFile).RunAsync("GSPC", "night-2");

        Assert.Contains(next.New, one => one.StartsWith("BBB b no event", StringComparison.Ordinal));
        Assert.Equal(["partial"], Query(store, "SELECT outcome FROM run_log WHERE run_id = 'night-2' AND stage = 'chart-check';"));
        Assert.Contains("BBB b no event", Query(store, "SELECT detail FROM run_log WHERE run_id = 'night-2' AND stage = 'chart-check';").Single(), StringComparison.Ordinal);

        foreach (var ticker in new[] { "AAA", "BBB" })
        {
            Stored(store, ticker, [Priced(third, ticker == "AAA" ? 100m : 40m)]);
        }

        var last = await new ChartChecker(FixedClock.At(NightAt(third), SessionZones.UnitedStates), store.DatabaseFile).RunAsync("GSPC", "night-3");

        Assert.Empty(last.New);
        Assert.Equal(["ok"], Query(store, "SELECT outcome FROM run_log WHERE run_id = 'night-3' AND stage = 'chart-check';"));
        Assert.Equal(["2026-10-05", "2026-10-06"], Query(store, "SELECT DISTINCT night FROM chart_check ORDER BY night;"));
    }

    [Fact]
    public async Task TheCheckAsksNothingAndTheDayFilesSplitsAreKeptTheFirstStanding()
    {
        Assert.Empty(ChartChecker.Access.Feeds);

        using var store = new TemporaryStore().Migrated();
        var sessions = TradedTo(FaultNight, 8);
        var day = sessions[^1];

        Member(store, "AAA");
        Member(store, "BAD");
        Stored(store, "AAA", sessions.Select(session => Priced(session, 100m)));
        Stored(store, "BAD", sessions.Select(session => Priced(session, 100m)));
        AskedOn(store, "AAA", FaultNight);
        AskedOn(store, "BAD", FaultNight);

        Task Night(string run, params (string, DateOnly, string)[] filed) =>
            new CorporateActionChecker(new SplitsByDay(filed), new SeriesByTicker([]), FixedClock.At(NightAt(FaultNight), SessionZones.UnitedStates), store.DatabaseFile).RunAsync("GSPC", run);

        // A name no member holds and a ratio that is not two numbers are left out.
        await Night("night-1", ("AAA", day, "1333.000000/200.000000"), ("NOPE", day, "2.000000/1.000000"), ("BAD", day, "not a ratio"));

        var kept = FormattableString.Invariant($"AAA|{day:yyyy-MM-dd}|1333.000000|200.000000");

        Assert.Equal([kept], Query(store, "SELECT ticker || '|' || ex_date || '|' || new_shares || '|' || old_shares FROM split_event;"));

        // A later answer for the same day leaves the first standing.
        await Night("night-2", ("AAA", day, "3.000000/1.000000"));

        Assert.Equal([kept], Query(store, "SELECT ticker || '|' || ex_date || '|' || new_shares || '|' || old_shares FROM split_event;"));
    }

    sealed class SplitsByDay(params (string Ticker, DateOnly On, string Ratio)[] filed) : ICorporateActionFeed
    {
        public int Requests { get; private set; }

        public Task<IReadOnlyList<CorporateAction>> ActionsAsync(string exchange, DateOnly session, CancellationToken cancellation = default)
        {
            Requests += 2;

            return Task.FromResult<IReadOnlyList<CorporateAction>>([.. filed.Where(one => one.On == session).Select(one => new CorporateAction(one.Ticker, one.On, ActionKind.Split, one.Ratio))]);
        }
    }

    [Fact]
    public void TheProofPageListsWhatIsNewSinceTheNightBeforeFirst()
    {
        ChartProofRow Row(string night, string ticker, string kind) => new(night, ticker, "e", kind, "2026-10-01", ticker + " " + kind);

        var rows = new[] { Row("2026-11-02", "OLD", "zero volume"), Row("2026-11-03", "OLD", "zero volume"), Row("2026-11-03", "NEW", "copied candle") };

        Assert.Equal(["NEW"], ChartProof.Fresh(rows).Select(row => row.Ticker));
        Assert.Empty(ChartProof.Fresh([Row("2026-11-03", "NEW", "copied candle")]));

        var page = ChartProof.Page(rows);

        Assert.Contains("2 finding(s) on 2 stock(s), 0 of them in the pulled years, 1 new since the night before", page, StringComparison.Ordinal);
        Assert.Contains("href=\"/#/chart/NEW\"", page, StringComparison.Ordinal);
        Assert.Contains("No night has run", ChartProof.Page([]), StringComparison.Ordinal);
        Assert.DoesNotContain("data-flag=\"pulled-years\"", page, StringComparison.Ordinal);

        // A finding in the pulled years stands in the fold and not under its flag's list.
        var folded = ChartProof.Page([Row("2026-11-03", "OLD", "zero volume" + ChartCheck.InThePulledYears), Row("2026-11-03", "NEW", "copied candle")]);
        var fold = folded.IndexOf("data-flag=\"pulled-years\"", StringComparison.Ordinal);

        Assert.True(fold > 0);
        Assert.Contains("Flag e", folded[..fold], StringComparison.Ordinal);
        Assert.Contains("NEW copied candle", folded[..fold], StringComparison.Ordinal);
        Assert.DoesNotContain("OLD zero volume", folded[..fold], StringComparison.Ordinal);
        Assert.Contains("OLD zero volume", folded[fold..], StringComparison.Ordinal);
    }

    [Fact]
    public void AFindingOnASessionBeforeTheYearSaysSoInItsKindAndAStoredOneDoesNot()
    {
        var pulledCandle = new CheckedSession(CheckFirst.AddDays(1), 101, 102, 100, 101, null, 0, true);

        Assert.Contains(Checked(Stock([Day(0, 100, pulled: true), pulledCandle, Day(2, 101)])), finding => finding.Kind == "zero volume" + ChartCheck.InThePulledYears);
        Assert.Contains(Checked(Stock([Day(0, 100, pulled: true), pulledCandle with { Open = 100, High = 100, Low = 100, Close = 100 }, Day(2, 101)])), finding => finding.Kind == "copied candle" + ChartCheck.InThePulledYears);

        var pulledMove = Assert.Single(ChartCheck.ReadMoves(ChartCheck.MovesOf([Day(0, 100, pulled: true), Day(1, 56, pulled: true)]), NoEvents, new Dictionary<DateOnly, int>()));

        Assert.Equal("no event" + ChartCheck.InThePulledYears, pulledMove.Kind);
        Assert.Equal("no event", Assert.Single(ChartCheck.ReadMoves(ChartCheck.MovesOf([Day(0, 100, pulled: true), Day(1, 56)]), NoEvents, new Dictionary<DateOnly, int>())).Kind);
    }
}
