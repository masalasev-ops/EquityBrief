using System.Globalization;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Filter;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 12.2: the swing filter over a constructed store, its rank, a night whose breadth
// is not available, the open version it reads, and a night run again replacing its own rows alone.
public partial class FixtureExpectations
{
    const string FilterNight = "2026-09-08";
    const string FilterBefore = "2026-09-04";

    static readonly DateTimeOffset FilterEvening = new(2026, 9, 8, 21, 10, 0, TimeSpan.Zero);

    // Four members on 2026-09-08, the session before being 2026-09-04. ZZA, ZZB and ZZC pass every gate:
    // an uptrend, a pullback three typical moves of 4 deep into the anchored support band 100 to 104 on
    // a dry-up of 0.8, a close of 102 above the previous high of 101 with no event stored on the session
    // before, and a plan entered at 102 with its stop at 96, 1.5 typical moves below. Their reward to
    // risk and strength are 3 and 0.8, 4 and 0.7, and 3 and 0.9, so worked by hand the order is ZZB,
    // then ZZC ahead of ZZA on strength. ZZD is in a range and passes no trend gate.
    static TemporaryStore FilterStore()
    {
        var store = new TemporaryStore().Migrated();

        var members = new (string Ticker, string Trend, string Ratio, double Strength, string Target)[]
        {
            ("ZZA", "uptrend", "3", 0.8, "120"),
            ("ZZB", "uptrend", "4", 0.7, "126"),
            ("ZZC", "uptrend", "3", 0.9, "120"),
            ("ZZD", "range", "3", 0.2, "120"),
        };

        foreach (var (ticker, trend, _, strength, target) in members)
        {
            store.Execute(
                "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) " +
                $"VALUES ('GSPC', '{ticker}', NULL, NULL, '2026-09-05T21:00:00Z');" +
                "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES " +
                $"('{ticker}', '{FilterBefore}', '100', '101', '99', '100', 1000, 'test', '2026-09-04T21:00:00Z', '100')," +
                $"('{ticker}', '{FilterNight}', '101', '103', '100.5', '102', 1000, 'test', '2026-09-08T21:00:00Z', '102');" +
                "INSERT INTO swing_reading (ticker, session_date, bars, return_short, return_long, place_short, place_long, strength, recent_high, high_session, pullback_sessions, depth, dry_up, tightness, note) " +
                $"VALUES ('{ticker}', '{FilterNight}', 252, 5, 10, {Number(strength)}, {Number(strength)}, {Number(strength)}, '114', '2026-08-31', 5, 3, 0.8, 0.9, NULL);" +
                "INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES " +
                $"('{ticker}', '{FilterNight}', 'atr14', 4, 252), ('{ticker}', '{FilterNight}', 'vol_avg50', 1000, 252);" +
                $"INSERT INTO ladder (ticker, as_of, trend_state, plan) VALUES ('{ticker}', '{FilterNight}', '{trend}', '{{}}');" +
                "INSERT INTO level (ticker, as_of, low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members) VALUES " +
                $"('{ticker}', '{FilterNight}', '100', '104', 'support', 1, 10, 1, '[]'), ('{ticker}', '{FilterNight}', '{target}', '{target}', 'resistance', 1, 5, 1, '[]');" +
                "INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) " +
                $"VALUES ('{ticker}', '{FilterNight}', '[]', 0, '{{\"entryLow\":\"102\",\"entryHigh\":\"102\",\"stop\":\"96\",\"firstTradedTarget\":\"{target}\"}}', '[]');" +
                "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, family, trigger_pass, trigger_event, trade, exclusions, passed, gates) " +
                $"VALUES ('{ticker}', '{FilterBefore}', 'none', 'earlier', 1, 1, 1, 'pullback', 0, 0, 1, '[]', 0, '{{\"gates\":[],\"notes\":[]}}');");
        }

        store.Execute(
            "INSERT INTO market_reading (session_date, members, counted, above, breadth, counted_context, above_context, breadth_context, volume_counted, median_volume_ratio) " +
            $"VALUES ('{FilterNight}', 4, 4, 3, 0.75, 4, 4, 1.0, 4, 1.0);");

        return store;

        static string Number(double value) => value.ToString(CultureInfo.InvariantCulture);
    }

    static Task<SwingFilterOutcome> FilterAsync(TemporaryStore store, string run) =>
        new SwingFilter(FixedClock.At(FilterEvening, SessionZones.UnitedStates), store.DatabaseFile).RunAsync("GSPC", run);

    [Fact]
    public async Task TheRankIsReadBackOffTheStoreInTheOrderWorkedByHand()
    {
        using var store = FilterStore();

        // The targets worked by hand: ZZA and ZZC at 120 from an entry of 102 over a stop of 96 is 18 / 6 = 3,
        // ZZB at 126 is 24 / 6 = 4.
        var outcome = await FilterAsync(store, "filter-rank");

        Assert.Equal((4, 4, 3), (outcome.Members, outcome.RowsWritten, outcome.Passing));
        Assert.Equal(
            ["ZZA|1|3|3", "ZZB|1|1|4", "ZZC|1|2|3", "ZZD|0||3"],
            Query(store, $"SELECT ticker || '|' || passed || '|' || IFNULL(rank, '') || '|' || printf('%g', ladder_reward_to_risk) FROM gate_result WHERE session_date = '{FilterNight}' ORDER BY ticker;"));

        // The session before's rows are the filter's input and are left as they were.
        Assert.Equal(["4|earlier"], Query(store, $"SELECT COUNT(*) || '|' || MIN(code) FROM gate_result WHERE session_date = '{FilterBefore}';"));

        Assert.Contains(
            "4 member(s), 4 through market (0 removed), 3 through trend and strength (1 removed), 3 through setup (0 removed), 3 through trigger (0 removed), 3 through trade (0 removed), 0 excluded, 3 passing",
            Query(store, "SELECT detail FROM run_log WHERE stage = 'swing-filter';").Single(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANightWhoseBreadthIsNotAvailableFailsTheMarketGateForEveryMemberAndStillStoresEveryOtherGate()
    {
        using var store = FilterStore();

        store.Execute($"UPDATE market_reading SET counted = 1, above = 1, breadth = NULL WHERE session_date = '{FilterNight}';");

        await FilterAsync(store, "filter-unread");

        // No member passes the market gate, the three that pass everything else say so on their rows, and
        // nobody is ranked.
        Assert.Equal(
            ["ZZA|0|1|1|1|1|0|", "ZZB|0|1|1|1|1|0|", "ZZC|0|1|1|1|1|0|", "ZZD|0|0|1|1|1|0|"],
            Query(store, $"SELECT ticker || '|' || market || '|' || trend || '|' || setup || '|' || trigger_pass || '|' || trade || '|' || passed || '|' || IFNULL(rank, '') FROM gate_result WHERE session_date = '{FilterNight}' ORDER BY ticker;"));
        Assert.All(
            Query(store, $"SELECT gates FROM gate_result WHERE session_date = '{FilterNight}';"),
            gates => Assert.Contains("breadth is not available: 1 of the 4 members hold a close and a 200-day average, fewer than half", gates, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheFilterReadsTheOpenVersionAndNamesItOnEveryRowAndAClosedOneIsNotRead()
    {
        using var store = FilterStore();

        // A closed version at a floor every night passes, and an open one at 0.9, which 0.75 does not reach.
        store.Execute(
            "INSERT INTO filter_version (version, settings, opened_at, closed_at, evidence) VALUES " +
            $"('v0', '{(FilterSettings.Proposed with { BreadthFloor = 0.1 }).Write()}', '2026-09-01T00:00:00Z', '2026-09-02T00:00:00Z', 'closed')," +
            $"('v1', '{(FilterSettings.Proposed with { BreadthFloor = 0.9 }).Write()}', '2026-09-02T00:00:00Z', NULL, 'open');");

        var outcome = await FilterAsync(store, "filter-version");

        Assert.Equal("v1", outcome.Version);
        Assert.Equal(["v1|0|4"], Query(store, $"SELECT version || '|' || SUM(market) || '|' || COUNT(*) FROM gate_result WHERE session_date = '{FilterNight}' GROUP BY version;"));
        Assert.Contains("filter version v1", Query(store, "SELECT detail FROM run_log WHERE stage = 'swing-filter';").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANightRunAgainReplacesItsOwnRowsAndNoOtherNights()
    {
        using var store = FilterStore();

        await FilterAsync(store, "filter-first");
        store.Execute($"UPDATE listing SET plan_at_listing = '{{\"entryLow\":\"102\",\"entryHigh\":\"102\",\"stop\":\"96\",\"firstTradedTarget\":\"108\"}}' WHERE ticker = 'ZZA';");
        await FilterAsync(store, "filter-again");

        // One row per member for the night, ZZA's now at (108 - 102) / 6 = 1 and failing the trade, and the
        // session before's rows untouched.
        Assert.Equal(["4"], Query(store, $"SELECT COUNT(*) FROM gate_result WHERE session_date = '{FilterNight}';"));
        Assert.Equal(["1|0|0"], Query(store, $"SELECT printf('%g', ladder_reward_to_risk) || '|' || trade || '|' || passed FROM gate_result WHERE ticker = 'ZZA' AND session_date = '{FilterNight}';"));
        Assert.Equal(["4|earlier"], Query(store, $"SELECT COUNT(*) || '|' || MIN(code) FROM gate_result WHERE session_date = '{FilterBefore}';"));
    }

    [Fact]
    public async Task AGateReadingAnAbsentValueFailsWithItsReasonForTheFixturesMemberTheNightReadNothingFor()
    {
        // AAPL holds no bar for the replay's night, so every gate reading one of its values fails, each
        // naming why, and the market gate, which reads the night rather than the name, is answered.
        using var store = await FixtureReplay.ReplayedAsync();

        var row = Assert.Single(Query(store, "SELECT market || '|' || trend || '|' || setup || '|' || trigger_pass || '|' || trade || '|' || passed FROM gate_result WHERE ticker = 'AAPL';"));
        var reasons = System.Text.Json.JsonDocument.Parse(Query(store, "SELECT gates FROM gate_result WHERE ticker = 'AAPL';").Single())
            .RootElement.GetProperty("gates").EnumerateArray()
            .ToDictionary(gate => gate.GetProperty("gate").GetString()!, gate => gate.GetProperty("reason").GetString()!, StringComparer.Ordinal);

        Assert.Equal("1|0|0|0|0|0", row);
        Assert.Equal("no bar for this session; the last session stored for the name is 2026-08-10", reasons[SwingGates.Trend]);
        Assert.Equal("no bar for this session; the last session stored for the name is 2026-08-10", reasons[SwingGates.Setup]);
        Assert.Equal("no previous session's high to read the trigger against", reasons[SwingGates.Trigger]);
        Assert.Equal("no tranche is placed, so there is nothing to size", reasons[SwingGates.Trade]);
    }

    [Fact]
    public async Task TonightsListIsDrawnTheSameWithTheFilterStoredAndWithout()
    {
        // The filter writes its own rows and nothing tonight's list reads, so the list is the one the six
        // reasons made whether or not the rows are there.
        using var store = await FixtureReplay.ReplayedAsync();
        using var host = new EquityBrief.Tests.Reading.ReadSurface.Host(store.Root);
        using var client = host.CreateClient();

        var with = await client.GetStringAsync("/screens/tonight/2026-09-08");

        Assert.NotEmpty(Query(store, "SELECT ticker FROM gate_result;"));

        store.Execute("DELETE FROM gate_result;");

        Assert.Equal(with, await client.GetStringAsync("/screens/tonight/2026-09-08"));
    }

    [Fact]
    public async Task TheFilterReadsEachNamesArrivalWindowOffTheResultsItStoredOnItsOwnEarlierSessions()
    {
        using var store = FilterStore();

        // Two earlier sessions, 2026-09-02 and 09-03, before the session before of 09-04, and tonight's close
        // at 101, the previous high, so no member's event happens tonight. Each name's events stored on the
        // three sessions before, newest first: ZZA fired on 09-04 and not on 09-03, so it arrived one
        // session back; ZZB fired on 09-04 and 09-03 and not on 09-02, arriving two back, the window's last
        // session; ZZC fired on all three, arriving before the window; ZZD fired on 09-04 and 09-03 and
        // stored nothing on 09-02, which the answer turns on. Worked by hand, the trigger passes ZZA and
        // ZZB and fails ZZC and ZZD, naming 09-02 for ZZD.
        (string Ticker, int On04, int On03, int? On02)[] fired =
        [
            ("ZZA", 1, 0, 0),
            ("ZZB", 1, 1, 0),
            ("ZZC", 1, 1, 1),
            ("ZZD", 1, 1, null),
        ];

        foreach (var (ticker, on04, on03, on02) in fired)
        {
            store.Execute(
                $"UPDATE bar SET close = '101', raw_close = '101' WHERE ticker = '{ticker}' AND session_date = '{FilterNight}';" +
                $"UPDATE gate_result SET trigger_event = {on04} WHERE ticker = '{ticker}' AND session_date = '{FilterBefore}';" +
                "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES " +
                $"('{ticker}', '2026-09-02', '100', '101', '99', '100', 1000, 'test', '2026-09-02T21:00:00Z', '100')," +
                $"('{ticker}', '2026-09-03', '100', '101', '99', '100', 1000, 'test', '2026-09-03T21:00:00Z', '100');" +
                "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, family, trigger_pass, trigger_event, trade, exclusions, passed, gates) " +
                $"VALUES ('{ticker}', '2026-09-03', 'none', 'earlier', 1, 1, 1, 'pullback', 0, {on03}, 1, '[]', 0, '{{\"gates\":[],\"notes\":[]}}');");

            if (on02 is { } stored)
            {
                store.Execute(
                    "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, family, trigger_pass, trigger_event, trade, exclusions, passed, gates) " +
                    $"VALUES ('{ticker}', '2026-09-02', 'none', 'earlier', 1, 1, 1, 'pullback', 0, {stored}, 1, '[]', 0, '{{\"gates\":[],\"notes\":[]}}');");
            }
        }

        await FilterAsync(store, "filter-window");

        Assert.Equal(
            ["ZZA|0|1", "ZZB|0|1", "ZZC|0|0", "ZZD|0|0"],
            Query(store, $"SELECT ticker || '|' || trigger_event || '|' || trigger_pass FROM gate_result WHERE session_date = '{FilterNight}' ORDER BY ticker;"));

        var trigger = Query(store, $"SELECT json_extract(gates, '$.gates[3].values.arrived') || '|' || json_extract(gates, '$.gates[3].reason') FROM gate_result WHERE session_date = '{FilterNight}' ORDER BY ticker;");

        Assert.Equal(
            [
                "2026-09-04|the trigger first fired on 2026-09-04, 1 session(s) before tonight, inside the 3-session window",
                "2026-09-03|the trigger first fired on 2026-09-03, 2 session(s) before tonight, inside the 3-session window",
                "none|the trigger did not arrive in the last 3 sessions",
                "none|no gate result is stored for 2026-09-02, so the trigger's arrival cannot be read",
            ],
            trigger);
    }
}
