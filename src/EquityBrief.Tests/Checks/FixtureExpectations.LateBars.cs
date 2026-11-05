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
        var scratches = Scratches();
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

        Assert.Equal(["IA", "IB", "NH", "PR"], FamilyRows(store, "SELECT ticker FROM entry_reading WHERE session_date = '2026-10-02' ORDER BY ticker;"));

        // Nothing a night's rules write moved: a would-have-qualified writes no trade or record.
        Assert.All(written, table => Assert.Equal(counts[table], FamilyRows(store, $"SELECT COUNT(*) FROM {table};").Single()));

        // The behind mark this bar answers set back to ok; one behind by more, and copied candles, left as the check left them.
        Assert.Equal(["IA|ok|0", "NH|suspect|0", "PR|suspect|0"], FamilyRows(store, "SELECT ticker || '|' || state || '|' || retries FROM series_state ORDER BY ticker;"));

        // The scratch store is gone.
        Assert.Equal(scratches, Scratches());
    }

    // The entry check's reader over the constructed night, read by hand: a row for each of the seven members with a bar, no row for
    // one holding none, and a second run replacing its own and writing nothing a rule reads.
    // see: The entry check reads a member's bars every night into a table of its own, and no rule reads it
    // Its first read keeps what stands as a baseline; a later read keeps a new name with its night, from a filing fetched since or a
    // title, keeps a name once however often it is read, and takes a company's own spelling for no split.
    [Fact]
    public async Task ANameSplitForTheFirstTimeIsKeptWithItsNightAndTheFirstReadIsABaseline()
    {
        using var store = new TemporaryStore().Migrated();

        static string Filing(string ticker, string fetched, string label) =>
            $"('{ticker}', '{fetched[..10]}', '{fetched}', '{{\"segments\":{{\"report\":\"r\",\"title\":\"t\",\"periods\":[],\"groups\":[{{\"label\":\"{label}\",\"figures\":[]}}],\"consolidated\":[]}}}}', 's')";

        store.Execute($"INSERT INTO fundamentals (ticker, filing_date, fetched_at, payload, source) VALUES {Filing("OLD", "2026-10-01T00:00:00Z", "HomeBuildingOps")};");
        store.Execute(
            "INSERT INTO insider_trade (cik, ticker, accession, filed, form, owners, owner_ciks, director, officer, title, ten_percent, sequence, security, traded, code, shares, acquired, direct, source, run_id) VALUES " +
            "('1', 'OLD', 'a', '2026-01-01', '4', 'x', '[]', 0, 1, 'ViceChairman', 0, 1, 'Common', '2026-01-01', 'P', '1', 1, 1, 's', 'r');");

        var first = new DateTimeOffset(2026, 10, 2, 22, 0, 0, TimeSpan.Zero);
        var reader = new EquityBrief.Worker.Names.SplitNameReader(FixedClock.At(first, SessionZones.UnitedStates), store.DatabaseFile);
        var baseline = await reader.RunAsync("night-one", new DateOnly(2026, 10, 2));

        Assert.Equal((2, 2, true), (baseline.Read, baseline.New, baseline.Baseline));
        Assert.Equal(["HomeBuildingOps|Home Building Ops|OLD|filing|1", "ViceChairman|Vice Chairman|OLD|insider|1"], FamilyRows(store, "SELECT name || '|' || spaced || '|' || ticker || '|' || source || '|' || baseline FROM split_name ORDER BY name;"));
        Assert.Empty(await Reading.ReadSurface.ApiOf(store).SplitNamesOnAsync(new DateOnly(2026, 10, 2)));

        store.Execute($"INSERT INTO fundamentals (ticker, filing_date, fetched_at, payload, source) VALUES {Filing("NEW", "2026-10-05T00:00:00Z", "IntlVehiclePmts")}, {Filing("OWN", "2026-10-05T00:00:00Z", "YouTube")}, {Filing("OLD", "2026-10-05T00:00:00Z", "HomeBuildingOps")};");

        var later = new EquityBrief.Worker.Names.SplitNameReader(FixedClock.At(first.AddDays(4), SessionZones.UnitedStates), store.DatabaseFile);
        var night = await later.RunAsync("night-two", new DateOnly(2026, 10, 6));

        Assert.Equal((1, false), (night.New, night.Baseline));
        Assert.Equal(["IntlVehiclePmts|2026-10-06|0"], FamilyRows(store, "SELECT name || '|' || first_night || '|' || baseline FROM split_name WHERE run_id = 'night-two';"));
        Assert.Equal("IntlVehiclePmts", Assert.Single(await Reading.ReadSurface.ApiOf(store).SplitNamesOnAsync(new DateOnly(2026, 10, 6))).Name);
        Assert.Equal(3, FamilyRows(store, "SELECT name FROM split_name;").Count);

        // A filing fetched before the last read is not read again.
        store.Execute($"INSERT INTO fundamentals (ticker, filing_date, fetched_at, payload, source) VALUES {Filing("BACK", "2026-10-03T00:00:00Z", "BackDatedOps")};");
        Assert.Equal(0, (await later.RunAsync("night-three", new DateOnly(2026, 10, 7))).New);
    }

    [Fact]
    public async Task TheEntryReaderWritesARowForEveryMemberWithABarAndNothingARuleReads()
    {
        using var store = IndexStore();

        // GONE's newest bar stands before the night. IA's history is pulled to the session before its first stored one, closing at 150, its pull holding
        // its first stored session at the store's scale.
        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', 'GONE', '2024-01-02', NULL, '2024-01-02T00:00:00Z');");
        store.Execute("INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('GONE', '2026-10-01', '10', '10', '10', '10', 5, 'test', '2026-10-01T21:00:00Z', '10');");

        var first = FamilyRows(store, "SELECT MIN(session_date) || '|' || (SELECT close FROM bar b WHERE b.ticker = 'IA' AND b.session_date = (SELECT MIN(session_date) FROM bar c WHERE c.ticker = 'IA')) FROM bar WHERE ticker = 'IA';").Single().Split('|');

        store.Execute(
            "INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull) VALUES " +
            "('IA', '2026-01-23', '150', '151', '149', '150', '150', 5, 'p1'), " +
            $"('IA', '{first[0]}', '{first[1]}', '{first[1]}', '{first[1]}', '{first[1]}', '{first[1]}', 5, 'p1');");
        store.Execute(
            "INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES " +
            "('IA', '2026-10-02', 'atr14', 2.5, 14), ('IA', '2026-10-02', 'rsi14', 70, 14), ('IA', '2026-10-02', 'vol_avg50', 1000000, 50);");

        // IA's newest stored release names an item, one of the day before it and one after the night being read past; IB's names none;
        // the calendar stores IA's report two days before its release, an actual of 2.31 against an estimate of 1.54; IC holds no release.
        static string Hex(string text) => "X'" + Convert.ToHexString(EquityBrief.Core.Releases.ResultsReleases.Compressed(text).Gzip) + "'";

        store.Execute("INSERT INTO company (ticker, fetched_at, cik) VALUES ('IA', '2026-10-01T00:00:00Z', '0000000001'), ('IB', '2026-10-01T00:00:00Z', '0000000002');");
        store.Execute(
            "INSERT INTO release_document (accession, cik, form, filed, accepted_at, exhibit, url, text_gzip, text_sha256, text_length, run_id, release) VALUES " +
            $"('a1', '0000000001', '8-K', '2026-09-30', '2026-09-30T12:00:00Z', 'e', 'u', {Hex("It took a restructuring charge of $0.11 per share.")}, 's', 1, 'r', 1), " +
            $"('a2', '0000000001', '8-K', '2026-10-01', '2026-10-01T12:00:00Z', 'e', 'u', {Hex("EPS of $1.83; adjusted EPS* of $2.31; both included a benefit of approximately $0.67 from net tariff refunds.")}, 's', 1, 'r', 1), " +
            $"('a3', '0000000001', '8-K', '2026-10-09', '2026-10-09T12:00:00Z', 'e', 'u', {Hex("A one-time gain on sale added $0.99 per share.")}, 's', 1, 'r', 1), " +
            $"('b1', '0000000002', '8-K', '2026-10-01', '2026-10-01T12:00:00Z', 'e', 'u', {Hex("Sales rose this quarter.")}, 's', 1, 'r', 1);");
        store.Execute("INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('IA', '2026-09-29', 'earnings', 'before', '{\"estimate\":\"1.54\",\"actual\":\"2.31\"}', '2026-10-01T00:00:00Z');");

        // IA's wait plan by hand: a range, its close of 102 between averages of 103 and 100. Its gap runs from 100.5 to 101 and a band's top of
        // 99 stands 1.5 under it, within the typical day of 2.5, so the zone is 99 to 101, its middle 100. The band's low of 97 has 90 beneath
        // it, the stop, a risk of 10; twice that needs 120, so the exit is 125, 2.5 to 1, the zone's top 1 under the close, 0.4 typical days.
        // An older night's band and one row of IB's trend say the night's own bands are read. IB holds no averages, so it draws why it has none.
        store.Execute("DELETE FROM level WHERE ticker = 'IA';");
        store.Execute(
            "INSERT INTO level (ticker, as_of, low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members) VALUES " +
            "('IA', '2026-10-01', '100', '100.4', 'support', 0, 1, 1, '[]'), " +
            "('IA', '2026-10-02', '90', '92', 'support', 0, 1, 1, '[]'), " +
            "('IA', '2026-10-02', '97', '99', 'support', 0, 1, 1, '[]'), " +
            "('IA', '2026-10-02', '110', '112', 'resistance', 0, 1, 1, '[]'), " +
            "('IA', '2026-10-02', '125', '127', 'resistance', 0, 1, 1, '[]');");
        store.Execute("DELETE FROM indicator WHERE ticker IN ('IA', 'IB') AND name IN ('sma50', 'sma200');");
        store.Execute("INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ('IA', '2026-10-02', 'sma50', 103, 50), ('IA', '2026-10-02', 'sma200', 100, 200);");
        store.Execute("DELETE FROM swing WHERE ticker = 'IA';");
        store.Execute(
            "INSERT INTO swing (ticker, session_date, direction, price, confirmed_on) VALUES " +
            "('IA', '2026-09-01', 'low', '90', '2026-09-08'), ('IA', '2026-09-15', 'low', '91', '2026-09-22'), " +
            "('IA', '2026-09-08', 'high', '110', '2026-09-15'), ('IA', '2026-09-22', 'high', '112', '2026-09-29');");

        // ID's by hand, an uptrend, each price set from its close: its close over a 50-session average over a 200-session one, and its lows rising.
        // The band under the close has one 10 lower beneath it. The last low under the band stands above that, so the stop trails at it, and not
        // at the one swing high under the band, which is what a read of the wrong swings would trail.
        var idClose = FamilyRows(store, "SELECT close FROM bar WHERE ticker = 'ID' AND session_date = '2026-10-02';").Single();
        var idLow = decimal.Parse(idClose, CultureInfo.InvariantCulture) - 2m;

        store.Execute("DELETE FROM level WHERE ticker = 'ID';");
        store.Execute(FormattableString.Invariant($"INSERT INTO level (ticker, as_of, low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members) VALUES ('ID', '2026-10-02', '{idLow}', '{idLow + 1m}', 'support', 0, 1, 1, '[]'), ('ID', '2026-10-02', '{idLow - 10m}', '{idLow - 9m}', 'support', 0, 1, 1, '[]');"));
        store.Execute("DELETE FROM indicator WHERE ticker = 'ID' AND session_date = '2026-10-02' AND name IN ('atr14', 'sma50', 'sma200');");
        store.Execute(FormattableString.Invariant($"INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ('ID', '2026-10-02', 'atr14', 2.5, 14), ('ID', '2026-10-02', 'sma50', {idLow - 20m}, 50), ('ID', '2026-10-02', 'sma200', {idLow - 30m}, 200);"));
        store.Execute("DELETE FROM swing WHERE ticker = 'ID';");
        store.Execute(FormattableString.Invariant($"INSERT INTO swing (ticker, session_date, direction, price, confirmed_on) VALUES ('ID', '2026-09-01', 'low', '{idLow - 6m}', '2026-09-08'), ('ID', '2026-09-15', 'low', '{idLow - 3m}', '2026-09-22'), ('ID', '2026-09-20', 'high', '{idLow - 2m}', '2026-09-25');"));

        var written = new[] { "family_result", "family_pick", "gate_result", "index_family_result", "index_family_pick", "dip_result", "listing", "setup", "ledger_reading" };
        var counts = written.ToDictionary(table => table, table => FamilyRows(store, $"SELECT COUNT(*) FROM {table};").Single());
        var reader = new EquityBrief.Worker.EntryCheck.EntryReader(FixedClock.At(AtTheFetch, SessionZones.UnitedStates), store.DatabaseFile);
        var outcome = await reader.RunAsync("night-test");

        Assert.Equal((LateNight, 7, 1), (outcome.Night!.Value, outcome.Read, outcome.WithoutABar));
        Assert.Equal(["FL", "IA", "IB", "IC", "ID", "NH", "PR"], FamilyRows(store, "SELECT ticker FROM entry_reading WHERE session_date = '2026-10-02' ORDER BY ticker;"));

        // IA's night: 102 on 2,000,000 shares after a year near 100. Its low of 101 stands above the prior high of 100.5, so it gapped;
        // twice its volume against an average of 1,000,000; the RSI and the typical day as stored; and last this high in the pull.
        Assert.Equal(["102|2.5|70.0|2.0|100.5|101"], FamilyRows(store, "SELECT close || '|' || typical_day || '|' || printf('%.1f', rsi) || '|' || printf('%.1f', volume_ratio) || '|' || gap_high || '|' || gap_low FROM entry_reading WHERE ticker = 'IA';"));
        Assert.Equal(["2026-01-23|2026-01-23"], FamilyRows(store, "SELECT highest_since || '|' || history_start FROM entry_reading WHERE ticker = 'IA';"));

        Assert.Equal(
            ["2026-10-01|0.67|1|1|0.77"],
            FamilyRows(store, "SELECT release_on || '|' || one_off_amount || '|' || one_off_tied || '|' || one_off_direction || '|' || beat FROM entry_reading WHERE ticker = 'IA';"));
        Assert.StartsWith("EPS of $1.83", FamilyRows(store, "SELECT one_off_sentence FROM entry_reading WHERE ticker = 'IA';").Single(), StringComparison.Ordinal);
        Assert.Equal(["2026-10-01|none|none"], FamilyRows(store, "SELECT release_on || '|' || COALESCE(one_off_amount, 'none') || '|' || COALESCE(beat, 'none') FROM entry_reading WHERE ticker = 'IB';"));
        Assert.Equal(["none"], FamilyRows(store, "SELECT COALESCE(release_on, 'none') FROM entry_reading WHERE ticker = 'IC';"));

        Assert.Equal(["99|101|90|125|2.5|0.4|none"], FamilyRows(store, "SELECT wait_low || '|' || wait_high || '|' || wait_stop || '|' || wait_exit || '|' || printf('%.1f', wait_reward) || '|' || printf('%.1f', zone_days) || '|' || COALESCE(wait_why, 'none') FROM entry_reading WHERE ticker = 'IA';"));
        Assert.Equal(["none|the trend could not be classified, so the ladder places nothing"], FamilyRows(store, "SELECT COALESCE(wait_low, 'none') || '|' || wait_why FROM entry_reading WHERE ticker = 'IB';"));
        Assert.Equal([(idLow - 3m).ToString(CultureInfo.InvariantCulture)], FamilyRows(store, "SELECT wait_stop FROM entry_reading WHERE ticker = 'ID';"));

        // A report on the calendar more than a week from the release is another report's, and no beat is read from it.
        store.Execute("UPDATE calendar SET event_date = '2026-09-23' WHERE ticker = 'IA';");
        await reader.RunAsync("night-far");
        Assert.Equal(["none"], FamilyRows(store, "SELECT COALESCE(beat, 'none') FROM entry_reading WHERE ticker = 'IA';"));
        store.Execute("UPDATE calendar SET event_date = '2026-09-24' WHERE ticker = 'IA';");
        await reader.RunAsync("night-near");
        Assert.Equal(["0.77"], FamilyRows(store, "SELECT beat FROM entry_reading WHERE ticker = 'IA';"));

        await reader.RunAsync("night-again");

        Assert.Equal(["7"], FamilyRows(store, "SELECT COUNT(*) FROM entry_reading;"));
        Assert.Equal(["night-again"], FamilyRows(store, "SELECT DISTINCT run_id FROM entry_reading;"));
        Assert.All(written, table => Assert.Equal(counts[table], FamilyRows(store, $"SELECT COUNT(*) FROM {table};").Single()));
        Assert.Equal(["7 member(s) read for 2026-10-02, 1 holding no bar on it"], FamilyRows(store, "SELECT detail FROM run_log WHERE run_id = 'night-test' AND stage = 'entry-readings';"));
    }

    // The scratch stores under the machine's temporary folder, which a run adds none to.
    static IReadOnlyList<string> Scratches()
    {
        var folder = Path.Combine(Path.GetTempPath(), "equitybrief-late-bars");

        return Directory.Exists(folder) ? [.. Directory.EnumerateFileSystemEntries(folder).Order(StringComparer.Ordinal)] : [];
    }

    // A mark of copied candles stands whatever session they fell on: the newest copy on the very session before the night is
    // not the answer ending short that a bar for the night answers.
    [Fact]
    public async Task ACopiedCandleMarkStandsEvenWhereItsDayIsTheSessionBeforeTheNight()
    {
        using var store = NightMissing("IA");

        store.Execute("INSERT INTO late_bar_ask (ticker, session_date, stage, asked_at, outcome, detail, run_id) VALUES ('IA', '2026-10-02', 'fetch', '2026-10-03T00:00:40Z', 'still missing', 'x', 'night-test');");
        store.Execute("INSERT INTO series_state (ticker, state, reason, checked_at, retries) VALUES ('IA', 'suspect', 'its stored year holds 1 copied candle(s), no volume at the close before, the newest on 2026-10-01', '2026-10-03T00:00:41Z', 0);");
        TheNightFinished(store);

        await Asker(store, new ScriptedBars(new Dictionary<string, IReadOnlyList<ProviderBar>> { ["IA"] = Holds(102m, 2_000_000) }), InTheOffPeakRun).OffPeakAsync("late-bars-run");

        Assert.Equal(["IA|suspect"], FamilyRows(store, "SELECT ticker || '|' || state FROM series_state;"));
    }

    // An index's own breadth is read over all its members: IA, alone late among the constructed night's seven, every one
    // above its 200-day average, reads a breadth of 100% and the market check open, and passes. Read over IA's own bars with
    // the other six holding none it would be one of seven, under the floor of 45%, and the check closed.
    [Fact]
    public async Task AStocksRulesReadTheBreadthOfItsWholeIndexAndNotItsOwnBarsAlone()
    {
        using var store = NightMissing("IA");

        store.Execute("INSERT INTO late_bar_ask (ticker, session_date, stage, asked_at, outcome, detail, run_id) VALUES ('IA', '2026-10-02', 'fetch', '2026-10-03T00:00:40Z', 'still missing', 'x', 'night-test');");
        TheNightFinished(store);

        await Asker(store, new ScriptedBars(new Dictionary<string, IReadOnlyList<ProviderBar>> { ["IA"] = Holds(102m, 2_000_000) }), InTheOffPeakRun).OffPeakAsync("late-bars-run");

        Assert.Equal(["[\"Breakout\"]"], FamilyRows(store, "SELECT COALESCE(qualified, 'not read: ' || detail) FROM late_bar_ask WHERE ticker = 'IA' AND stage = 'off-peak';"));
    }

    // The S&P 500's breakout reads the night's stored breadth, which the scratch store's own readers could read only over
    // the stocks it holds indicators for: 70% of the night's members stood above their averages, so the market check is open.
    [Fact]
    public async Task AnSAndP500StocksRulesReadTheBreadthTheNightStoredAndNotOneOverTheScratchStore()
    {
        using var store = new TemporaryStore().Migrated();

        // LA and five others, whom the scratch store holds no indicators for, so over it the breadth is not available.
        foreach (var (ticker, close, volume) in new[] { ("LA", 102m, 2_000_000L), ("LB", 100.2m, 1_000_000L), ("LC", 100.2m, 1_000_000L), ("LD", 100.2m, 1_000_000L), ("LE", 100.2m, 1_000_000L), ("LF", 100.2m, 1_000_000L) })
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
