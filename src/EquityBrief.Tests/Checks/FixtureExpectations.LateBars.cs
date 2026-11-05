using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Families;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Bars;
using Xunit;

namespace EquityBrief.Tests.Checks;

// A member the night's day file carried nothing for, asked again at the end of the fetch and once more in the off-peak run.
// Every expected value below is written from the operator's order and the constructed night's own figures, before the code ran.
// see: A member missing from the night's day file is asked for with its own request at the end of the fetch and once more in the off-peak run, at most 20 asks a night
// see: A late bar's readings are worked over a scratch store by the night's own components, and no pick or ledger row moves
public partial class FixtureExpectations
{
    static readonly DateOnly LateNight = new(2026, 10, 2);
    static readonly DateTimeOffset AtTheFetch = new(2026, 10, 3, 0, 0, 40, TimeSpan.Zero);
    static readonly DateTimeOffset InTheOffPeakRun = new(2026, 10, 3, 4, 5, 0, TimeSpan.Zero);

    sealed class ScriptedBars(IReadOnlyDictionary<string, IReadOnlyList<ProviderBar>> held) : IHistoricalBarFeed
    {
        public int Requests { get; private set; }

        public List<string> Asked { get; } = [];

        public Task<IReadOnlyList<ProviderBar>> BarsAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            Requests++;
            Asked.Add(ticker);

            return held.TryGetValue(ticker, out var bars)
                ? Task.FromResult(bars)
                : throw new InvalidOperationException($"no answer for {ticker}");
        }
    }

    // The session before the night as stored, and the night's bar the file did not carry.
    static IReadOnlyList<ProviderBar> Holds(decimal close, long volume) =>
    [
        new(LateNight.AddDays(-1), 100m, 100.5m, 99.5m, 100m, 100m, 1_000_000),
        new(LateNight, close, close + 0.5m, close - 1m, close, close, volume),
    ];

    static IReadOnlyList<ProviderBar> EndsBefore() => [new(LateNight.AddDays(-1), 100m, 100.5m, 99.5m, 100m, 100m, 1_000_000)];

    // The S&P 400's constructed night with the file's bars marked as the file's, and each named member's night taken out.
    static TemporaryStore NightMissing(params string[] tickers)
    {
        var store = IndexStore();

        store.Execute($"UPDATE bar SET source = 'bulk' WHERE session_date = '{Day(LateNight)}';");

        foreach (var ticker in tickers)
        {
            store.Execute($"DELETE FROM bar WHERE ticker = '{ticker}' AND session_date = '{Day(LateNight)}';");
        }

        return store;
    }

    static void TheNightFinished(TemporaryStore store) =>
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) " +
            "VALUES ('night-test', 'backup', '2026-10-03T02:55:00Z', '2026-10-03T02:56:00Z', 'ok', 0, 0, 0, '0', '');");

    static LateBarAsker Asker(TemporaryStore store, IHistoricalBarFeed feed, DateTimeOffset at) =>
        new(feed, FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile);

    [Fact]
    public void AnAnswerIsWrittenOnlyWhereItHoldsTheNightsBarAndIsThisStocksSeriesAsStored()
    {
        var stored = new Dictionary<DateOnly, (decimal Close, long Volume)> { [LateNight.AddDays(-1)] = (100m, 1_000_000) };

        var (bar, outcome, why) = LateBars.Judge(LateNight, Holds(102m, 2_000_000), stored);

        Assert.Equal((102m, 2_000_000L, "arrived", "the provider held the bar for 2026-10-02"), (bar!.Close, bar.Volume, outcome, why));

        Assert.Equal(
            (null, "still missing", "the provider's answer ended on 2026-10-01, before 2026-10-02"),
            LateBars.Judge(LateNight, EndsBefore(), stored));

        Assert.Equal(
            (null, "still missing", "the provider's answer held nothing to 2026-10-02"),
            LateBars.Judge(LateNight, [], stored));

        Assert.Equal(
            (null, "still missing", "the provider's bar for 2026-10-02 is a copy of the session before, with no trades"),
            LateBars.Judge(LateNight, [EndsBefore()[0], new ProviderBar(LateNight, 100m, 100m, 100m, 100m, 100m, 0)], stored));

        Assert.Equal(
            (null, "refused", "its bar for 2026-10-02, low 103, open 102, high 102.5, close 102, could not have traded"),
            LateBars.Judge(LateNight, [EndsBefore()[0], new ProviderBar(LateNight, 102m, 102.5m, 103m, 102m, 102m, 5)], stored));

        // Another listing's prices: the session both hold stands a half from the stored close.
        Assert.Equal(
            (null, "refused", "the answer's close for 2026-10-01, 50, stands over a quarter from the stored 100, so it is not this stock's series as stored"),
            LateBars.Judge(LateNight, [new ProviderBar(LateNight.AddDays(-1), 50m, 50m, 50m, 50m, 50m, 5), new ProviderBar(LateNight, 51m, 51m, 51m, 51m, 51m, 5)], stored));

        Assert.Equal(
            (null, "refused", "the answer held no session the store holds to check it against before 2026-10-02"),
            LateBars.Judge(LateNight, [new ProviderBar(LateNight, 102m, 102.5m, 101m, 102m, 102m, 5)], stored));

        // A stock the store holds nothing near has nothing to check against, as the day file's own row has not.
        Assert.Equal("arrived", LateBars.Judge(LateNight, [new ProviderBar(LateNight, 102m, 102.5m, 101m, 102m, 102m, 5)], new Dictionary<DateOnly, (decimal, long)>()).Outcome);
    }

    [Fact]
    public async Task TheFetchsAskWritesTheBarAsTheAppendAlone_StatesEachAndStopsAtTwenty()
    {
        using var store = NightMissing("IA", "ID");

        var before = FamilyRows(store, "SELECT ticker || session_date || open || high || low || close || volume || source || observed_at FROM bar WHERE session_date < '2026-10-02' ORDER BY 1;");
        var feed = new ScriptedBars(new Dictionary<string, IReadOnlyList<ProviderBar>> { ["IA"] = Holds(102m, 2_000_000), ["NO"] = EndsBefore() });
        var outcome = await Asker(store, feed, AtTheFetch).AtTheFetchAsync("night-test", LateNight, ["NO", "IA", "THROWS"]);

        Assert.Equal(3, outcome.Requests);
        Assert.Equal(1, outcome.Arrived);
        Assert.Equal(
            [
                "IA|fetch|2026-10-03T00:00:40Z|arrived|the provider held the bar for 2026-10-02",
                "NO|fetch|2026-10-03T00:00:40Z|still missing|the provider's answer ended on 2026-10-01, before 2026-10-02",
                "THROWS|fetch|2026-10-03T00:00:40Z|failed|the request failed: no answer for THROWS",
            ],
            FamilyRows(store, "SELECT ticker || '|' || stage || '|' || asked_at || '|' || outcome || '|' || detail FROM late_bar_ask ORDER BY ticker;"));

        // The bar is written under its own source with the ask's instant, as the day file's prices and its volume, and no
        // stored bar moved or went.
        Assert.Equal(["102|102.5|101|102|102|2000000|late|2026-10-03T00:00:40Z"], FamilyRows(store, "SELECT open || '|' || high || '|' || low || '|' || close || '|' || raw_close || '|' || volume || '|' || source || '|' || observed_at FROM bar WHERE ticker = 'IA' AND session_date = '2026-10-02';"));
        Assert.Empty(FamilyRows(store, "SELECT ticker FROM bar WHERE ticker = 'NO';"));
        Assert.Equal(before, FamilyRows(store, "SELECT ticker || session_date || open || high || low || close || volume || source || observed_at FROM bar WHERE session_date < '2026-10-02' ORDER BY 1;"));
        Assert.Equal(["ok|3|1|IA arrived: the provider held the bar for 2026-10-02; NO still missing: the provider's answer ended on 2026-10-01, before 2026-10-02; THROWS failed: the request failed: no answer for THROWS"], FamilyRows(store, "SELECT outcome || '|' || network_requests || '|' || rows_written || '|' || detail FROM run_log WHERE stage = 'late-bars';"));

        // Twenty-two missing: twenty asked in ticker order and two stated as past the limit, asked for nothing.
        using var many = NightMissing();

        var names = Enumerable.Range(0, 22).Select(at => FormattableString.Invariant($"N{at:00}")).ToArray();
        var empty = new ScriptedBars(names.ToDictionary(name => name, _ => (IReadOnlyList<ProviderBar>)[]));
        var limited = await Asker(many, empty, AtTheFetch).AtTheFetchAsync("night-test", LateNight, names);

        Assert.Equal(20, limited.Requests);
        Assert.Equal(names[..20], empty.Asked);
        Assert.Equal(
            ["N20|past the limit|past the 20 asks a night makes", "N21|past the limit|past the 20 asks a night makes"],
            FamilyRows(many, "SELECT ticker || '|' || outcome || '|' || detail FROM late_bar_ask WHERE asked_at IS NULL ORDER BY ticker;"));
    }

    [Fact]
    public async Task TheOffPeakRunAsksOnceAfterTheNightFinishedAndNoRunAsksPastTwentyAcrossBoth()
    {
        using var store = NightMissing("IA");

        var names = Enumerable.Range(0, 15).Select(at => FormattableString.Invariant($"N{at:00}")).ToArray();
        var none = names.Append("IA").Append("NH").ToDictionary(name => name, _ => (IReadOnlyList<ProviderBar>)[], StringComparer.Ordinal);
        var fetch = new ScriptedBars(none);

        await Asker(store, fetch, AtTheFetch).AtTheFetchAsync("night-test", LateNight, [.. names, "IA"]);

        Assert.Equal(16, fetch.Requests);

        // The night still writing: nothing asked, and the step's row says why.
        var waiting = new ScriptedBars(none);
        var early = await Asker(store, waiting, InTheOffPeakRun).OffPeakAsync("late-bars-early");

        Assert.Equal(0, waiting.Requests);
        Assert.Equal("the night of 2026-10-02 has not finished, so a later off-peak run asks", early.NotAsked);
        Assert.Equal(["0|the night of 2026-10-02 has not finished, so a later off-peak run asks"], FamilyRows(store, "SELECT network_requests || '|' || detail FROM run_log WHERE run_id = 'late-bars-early';"));

        TheNightFinished(store);

        // Sixteen asked at the fetch leave four for the off-peak run: IA first in ticker order, and N00 to N02.
        var feed = new ScriptedBars(none);
        var run = await Asker(store, feed, InTheOffPeakRun).OffPeakAsync("late-bars-run");

        Assert.Equal(["IA", "N00", "N01", "N02"], feed.Asked);
        Assert.Equal(4, run.Requests);
        Assert.Equal(12, run.Answers.Count(answer => answer.Outcome == LateBars.PastTheLimit));

        // Once more and not twice: a later run the same night asks for none of them.
        var again = new ScriptedBars(none);
        var second = await Asker(store, again, InTheOffPeakRun.AddHours(6)).OffPeakAsync("late-bars-again");

        Assert.Equal(0, again.Requests);
        Assert.Equal("no member of the night of 2026-10-02 is left to ask for", second.NotAsked);
    }

    // The arrival in the off-peak run, read against the rules the constructed night's own test states: IA and IB pass the
    // S&P 400's breakout, IB's own open trade holding it off the list and not out of the rule; NH closes under the high and
    // PR's four quarters sum to a loss, so neither passes.
    [Fact]
    public async Task ALateBarsReadingsAreWorkedForThatStockAloneAndTheRulesItWouldHavePassedNamedWithNoPickTradeOrRecordWritten()
    {
        using var store = NightMissing("IA", "IB", "NH", "PR");

        var answers = new Dictionary<string, IReadOnlyList<ProviderBar>>
        {
            ["IA"] = Holds(102m, 2_000_000),
            ["IB"] = Holds(102m, 1_600_000),
            ["NH"] = Holds(100.2m, 2_000_000),
            ["PR"] = Holds(102m, 2_000_000),
        };

        foreach (var ticker in answers.Keys)
        {
            store.Execute($"INSERT INTO late_bar_ask (ticker, session_date, stage, asked_at, outcome, detail, run_id) VALUES ('{ticker}', '2026-10-02', 'fetch', '2026-10-03T00:00:40Z', 'still missing', 'x', 'night-test');");
        }

        // Each stock marked suspect as the actions step marks one its answer ended short for, but for PR, whose copied
        // candles this bar does not answer, and NH, behind by more than this session.
        store.Execute(
            "INSERT INTO series_state (ticker, state, reason, checked_at, retries) VALUES " +
            "('IA', 'suspect', 'its newest bar is 2026-10-01, behind the night, and the provider holds no later session', '2026-10-03T00:00:41Z', 0), " +
            "('NH', 'suspect', 'its newest bar is 2026-09-20, behind the night, and the provider holds no later session', '2026-10-03T00:00:41Z', 0), " +
            "('PR', 'suspect', 'its stored year holds 1 copied candle(s), no volume at the close before, the newest on 2026-07-21', '2026-10-03T00:00:41Z', 0);");
        TheNightFinished(store);

        var written = new[] { "family_result", "family_pick", "family_trade", "gate_result", "index_family_result", "index_family_pick", "index_family_trade", "index_rule_trade", "dip_result", "dip_pick", "dip_trade", "rule_pick", "listing", "setup", "ledger_reading", "trade_fill", "index_family_night" };
        var counts = written.ToDictionary(table => table, table => FamilyRows(store, $"SELECT COUNT(*) FROM {table};").Single());
        var outcome = await Asker(store, new ScriptedBars(answers), InTheOffPeakRun).OffPeakAsync("late-bars-run");

        Assert.Equal(4, outcome.Arrived);
        Assert.Equal(
            [
                "IA|off-peak|2026-10-03T04:05:00Z|arrived|[\"Breakout\"]",
                "IB|off-peak|2026-10-03T04:05:00Z|arrived|[\"Breakout\"]",
                "NH|off-peak|2026-10-03T04:05:00Z|arrived|[]",
                "PR|off-peak|2026-10-03T04:05:00Z|arrived|[]",
            ],
            FamilyRows(store, "SELECT ticker || '|' || stage || '|' || asked_at || '|' || outcome || '|' || COALESCE(qualified, 'not read') FROM late_bar_ask WHERE stage = 'off-peak' ORDER BY ticker;"));

        // The readings and levels of those stocks on the night alone: the ten indicators each, a band set and a profile, and
        // nothing for a stock whose bar the night's file carried.
        Assert.Equal(["IA|10", "IB|10", "NH|10", "PR|10"], FamilyRows(store, "SELECT ticker || '|' || COUNT(*) FROM indicator WHERE session_date = '2026-10-02' GROUP BY ticker ORDER BY ticker;"));
        Assert.Equal(["IA", "IB", "NH", "PR"], FamilyRows(store, "SELECT DISTINCT ticker FROM volume_profile WHERE as_of = '2026-10-02' ORDER BY ticker;"));
        Assert.Equal(["IA", "IB", "NH", "PR"], FamilyRows(store, "SELECT DISTINCT ticker FROM level WHERE as_of = '2026-10-02' ORDER BY ticker;"));

        // Nothing a night's rules write moved: a would-have-qualified writes no trade or record.
        Assert.All(written, table => Assert.Equal(counts[table], FamilyRows(store, $"SELECT COUNT(*) FROM {table};").Single()));

        // The behind mark this bar answers set back to ok; one behind by more, and copied candles, left as the check left them.
        Assert.Equal(["IA|ok|0", "NH|suspect|0", "PR|suspect|0"], FamilyRows(store, "SELECT ticker || '|' || state || '|' || retries FROM series_state ORDER BY ticker;"));

        // The scratch store is gone.
        Assert.False(Directory.Exists(Path.Combine(Path.GetTempPath(), "equitybrief-late-bars")) && Directory.EnumerateFileSystemEntries(Path.Combine(Path.GetTempPath(), "equitybrief-late-bars")).Any(), "A scratch store was left behind.");
    }

    // The S&P 500's breakout reads the night's stored breadth, which the scratch store's own readers could read only over
    // the stocks it holds indicators for: 70% of the night's members stood above their averages, so the market check is open.
    [Fact]
    public async Task AnSAndP500StocksRulesReadTheBreadthTheNightStoredAndNotOneOverTheScratchStore()
    {
        using var store = new TemporaryStore().Migrated();

        foreach (var (ticker, close, volume) in new[] { ("LA", 102m, 2_000_000L), ("LB", 100.2m, 1_000_000L) })
        {
            StoreYear(store, ticker, IndexYear(close, volume));
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', '2024-01-02', NULL, '2024-01-02T00:00:00Z');");
        }

        store.Execute("UPDATE bar SET source = 'bulk' WHERE session_date = '2026-10-02';");
        store.Execute("DELETE FROM bar WHERE ticker = 'LA' AND session_date = '2026-10-02';");
        store.Execute("INSERT INTO market_reading (session_date, members, counted, above, breadth, counted_context, above_context, breadth_context, volume_counted, median_volume_ratio) VALUES ('2026-10-02', 503, 500, 350, 0.7, 500, 350, 0.7, 500, 1.0);");
        store.Execute("INSERT INTO late_bar_ask (ticker, session_date, stage, asked_at, outcome, detail, run_id) VALUES ('LA', '2026-10-02', 'fetch', '2026-10-03T00:00:40Z', 'still missing', 'x', 'night-test');");
        TheNightFinished(store);

        await Asker(store, new ScriptedBars(new Dictionary<string, IReadOnlyList<ProviderBar>> { ["LA"] = Holds(102m, 2_000_000) }), InTheOffPeakRun).OffPeakAsync("late-bars-run");

        Assert.Equal(["[\"Breakout\"]"], FamilyRows(store, "SELECT COALESCE(qualified, 'not read: ' || detail) FROM late_bar_ask WHERE ticker = 'LA' AND stage = 'off-peak';"));

        // The stock's ladder and its swing reading came back with its readings; the stored breadth did not move.
        Assert.Equal(["1"], FamilyRows(store, "SELECT COUNT(*) FROM ladder WHERE ticker = 'LA' AND as_of = '2026-10-02';"));
        Assert.Equal(["1"], FamilyRows(store, "SELECT COUNT(*) FROM swing_reading WHERE ticker = 'LA' AND session_date = '2026-10-02';"));
        Assert.Equal(["0.7"], FamilyRows(store, "SELECT breadth FROM market_reading WHERE session_date = '2026-10-02';"));
    }

    // A rule step that cannot read the stock's index is not a stock that passed nothing: the row says not read, and the bar stands.
    [Fact]
    public async Task ARuleStepThatCannotReadTheIndexLeavesNoLineSayingTheStockPassedNothing()
    {
        using var store = NightMissing("IA");

        store.Execute("INSERT INTO index_heavyweight_holding (index_code, ticker, entered_on, sector, entry_close, growth, cut, through) VALUES ('MID', 'IA', '2000-01-03', 'Energy', 'not a price', 1.0, '[]', '2000-01-03');");
        store.Execute("INSERT INTO late_bar_ask (ticker, session_date, stage, asked_at, outcome, detail, run_id) VALUES ('IA', '2026-10-02', 'fetch', '2026-10-03T00:00:40Z', 'still missing', 'x', 'night-test');");
        TheNightFinished(store);

        await Asker(store, new ScriptedBars(new Dictionary<string, IReadOnlyList<ProviderBar>> { ["IA"] = Holds(102m, 2_000_000) }), InTheOffPeakRun).OffPeakAsync("late-bars-run");

        Assert.Equal(["not read"], FamilyRows(store, "SELECT COALESCE(qualified, 'not read') FROM late_bar_ask WHERE ticker = 'IA' AND stage = 'off-peak';"));
        Assert.Contains("its readings or rules not worked: ", FamilyRows(store, "SELECT detail FROM late_bar_ask WHERE ticker = 'IA' AND stage = 'off-peak';").Single(), StringComparison.Ordinal);
        Assert.Equal(["late"], FamilyRows(store, "SELECT source FROM bar WHERE ticker = 'IA' AND session_date = '2026-10-02';"));
    }
}
