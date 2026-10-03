using System.Globalization;
using System.Net;
using System.Net.Http;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the breakout's and the earnings drift's market switches on the operator's ruling of
// 2026-10-03: each switch read by hand at its edge over constructed closes on the store's own sessions and shown to
// read every session as the ideas' run read it, the night's fetch of the two series over a constructed feed, a
// switched rule firing where its family's rule fires on a night its switch is open and on none where it is closed,
// and each family registered again whole to add its switched rule.
// see: The breakout and the earnings drift each register a variant listing only on nights its market switch is open, each family registered again whole and its records replayed
// see: The night asks for the index's and the VIX's daily closes once a series, and keeps them apart from the members' bars
public partial class FixtureExpectations
{
    // The rows the switches add that this check reaches: section 17's row and section 18's.
    internal static readonly string[] MarketSwitchClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Market switches"),
        CheckReach.Key(Scope.FailureTable, "The provider refuses or sends nothing for the index or the VIX on a night"),
    ];

    // Every row the switches add, whichever check reaches it, which the phase's pair names apart.
    internal static readonly string[] MarketSwitchRows =
    [
        CheckReach.Key(Scope.CatalogueTable, "Market series fetcher"),
        CheckReach.Key(Scope.MatrixTable, "Market series fetcher"),
        CheckReach.Key(Scope.StoresTable, "Market series"),
        .. MarketSwitchClaims,
    ];

    // The exchange's sessions ending on a night, oldest first.
    static IReadOnlyList<DateOnly> SessionsTo(DateOnly night, int count)
    {
        var sessions = new List<DateOnly>();

        for (var day = night; sessions.Count < count; day = day.AddDays(-1))
        {
            if (ExchangeClosures.IsSession(day))
            {
                sessions.Insert(0, day);
            }
        }

        return sessions;
    }

    static readonly DateOnly SwitchNight = new(2026, 9, 11);

    [Fact]
    public void EachMarketSwitchIsReadByHandAtItsEdgeOnTheStoresOwnSessions()
    {
        // 201 sessions to 2026-09-11. The index closes at 1,000 on the first of them, outside the 200 its average
        // reads, and at 100 on every other but the night. The average of the 200 to the night is (19,900 + c) / 200
        // for a close of c on the night, so the switch is open exactly where c is above 100.
        var sessions = SessionsTo(SwitchNight, 201);
        Dictionary<DateOnly, double> Index(double night) =>
            sessions.ToDictionary(session => session, session => session == sessions[0] ? 1000.0 : session == SwitchNight ? night : 100.0);

        Gate IndexGate(IReadOnlyList<DateOnly> on, IReadOnlyDictionary<DateOnly, double> index) =>
            Assert.Single(new MarketSwitches(200, 0).GatesOn(MarketCloses.On(on, index, new Dictionary<DateOnly, double>())));

        var atTheEdge = IndexGate(sessions, Index(100));
        var over = IndexGate(sessions, Index(100.5));

        Assert.Equal((MarketSwitches.IndexGate, false), (atTheEdge.Name, atTheEdge.Passed));
        Assert.Equal("the index closed 100 at or below the average of its 200 closes to the night, 100", atTheEdge.Reason);
        Assert.True(over.Passed, over.Reason);
        Assert.Equal(("100.5", "100.0025", "200"), (over.Values["close"], over.Values["average"], over.Values["sessions"]));

        // A close missing from the 200 leaves the average unread and the switch closed, and so do 199 sessions.
        var holed = Index(100.5);

        holed.Remove(sessions[^150]);

        Assert.False(IndexGate(sessions, holed).Passed);
        Assert.Equal("the index's stored closes do not hold all 200 sessions to the night", IndexGate(sessions, holed).Reason);
        Assert.False(IndexGate([.. sessions.Skip(2)], Index(100.5)).Passed);

        // The VIX on the night of 2026-09-11 against its close ten of the store's sessions before, 2026-08-27, with
        // Labor Day between them: a quote on 2026-09-07, which is no session, is passed over, so the close read is
        // 2026-08-27's 20 and not 2026-08-28's 15, ten of the series' own days back.
        var vixSessions = SessionsTo(SwitchNight, 11);

        Assert.Equal((new DateOnly(2026, 8, 27), new DateOnly(2026, 8, 28)), (vixSessions[0], vixSessions[1]));

        Gate VixGate(double night)
        {
            var vix = vixSessions.ToDictionary(session => session, session => session == vixSessions[0] ? 20.0 : session == vixSessions[1] ? 15.0 : session == SwitchNight ? night : 17.0);

            vix[new DateOnly(2026, 9, 7)] = 30.0;

            return Assert.Single(new MarketSwitches(0, 10).GatesOn(MarketCloses.On(vixSessions, new Dictionary<DateOnly, double>(), vix)));
        }

        Assert.True(VixGate(18).Passed, VixGate(18).Reason);
        Assert.Equal("the VIX closed 18 under its close 10 sessions before, 20", VixGate(18).Reason);
        Assert.Equal(MarketSwitches.VixGate, VixGate(18).Name);
        Assert.False(VixGate(20).Passed);
        Assert.Equal("the VIX closed 20 at or above its close 10 sessions before, 20", VixGate(20).Reason);
        Assert.True(VixGate(19.99).Passed);

        // A close missing on either end leaves the switch closed, and a series holding no session closes both.
        Assert.False(Assert.Single(new MarketSwitches(0, 10).GatesOn(MarketCloses.On(vixSessions, new Dictionary<DateOnly, double>(), new Dictionary<DateOnly, double> { [SwitchNight] = 10.0 }))).Passed);
        Assert.All(new MarketSwitches(200, 10).GatesOn(MarketCloses.None), gate => Assert.False(gate.Passed));
        Assert.All(new MarketSwitches(200, 10).GatesOn(null), gate => Assert.False(gate.Passed));
        Assert.Empty(MarketSwitches.None.GatesOn(MarketCloses.None));
    }

    [Fact]
    public void EachMarketSwitchReadsEverySessionAsTheIdeasRunReadIt()
    {
        // 260 sessions to 2026-09-11, the index rising and falling about a slow climb with a close missing early on,
        // the VIX swinging with a close missing and a quote on Labor Day, which is no session. On every session the
        // night's reading of each switch, over the closes to that session, is the ideas' run's over the whole.
        var calendar = SessionsTo(SwitchNight, 260);
        var index = new Dictionary<DateOnly, double>();
        var vix = new Dictionary<DateOnly, double>();

        for (var at = 0; at < calendar.Count; at++)
        {
            if (at != 15)
            {
                index[calendar[at]] = 100 + (10 * Math.Sin(at / 7.0)) + (0.05 * at);
            }

            if (at != 240)
            {
                vix[calendar[at]] = 20 + (5 * Math.Cos(at / 3.0));
            }
        }

        vix[new DateOnly(2026, 9, 7)] = 99.0;

        SweepMarketSeries Series(string name, Dictionary<DateOnly, double> closes) =>
            new(name, [.. closes.OrderBy(pair => pair.Key).Select(pair => (pair.Key, pair.Value))], "constructed");

        var passes = SweepIdeas.Switches(
            new double[calendar.Count],
            new int[calendar.Count],
            new int[calendar.Count],
            SweepIdeas.OnCalendar(Series(MarketCloses.Index, index), calendar),
            SweepIdeas.OnCalendar(Series(MarketCloses.Vix, vix), calendar));

        var switches = new MarketSwitches(SweepIdeas.SlowAverage, SweepIdeas.Lookback);
        var read = new Dictionary<(string Gate, bool Open), int>();

        for (var at = 0; at < calendar.Count; at++)
        {
            var gates = switches.GatesOn(MarketCloses.On([.. calendar.Take(at + 1)], index, vix));

            Assert.Equal(passes[(int)MarketSwitch.IndexOverTwoHundred][at], gates[0].Passed);
            Assert.Equal(passes[(int)MarketSwitch.VixFalling][at], gates[1].Passed);

            foreach (var gate in gates.Where(gate => gate.Values["close"] != "none" && gate.Values[gate.Name == MarketSwitches.IndexGate ? "average" : "before"] != "none"))
            {
                read[(gate.Name, gate.Passed)] = read.GetValueOrDefault((gate.Name, gate.Passed)) + 1;
            }
        }

        // Each switch read open and closed on many sessions it could read, so the agreement is over both answers:
        // the index on the 45 sessions from the 216th, whose 200 closes miss none, and the VIX on 248.
        Assert.Equal(45, read.GetValueOrDefault((MarketSwitches.IndexGate, true)) + read.GetValueOrDefault((MarketSwitches.IndexGate, false)));
        Assert.Equal(248, read.GetValueOrDefault((MarketSwitches.VixGate, true)) + read.GetValueOrDefault((MarketSwitches.VixGate, false)));
        Assert.All(read.Values, count => Assert.True(count >= 10, $"A switch was read one way on {count} session(s), expected at least 10."));
        Assert.Equal(4, read.Count);

        // The settings the variants register are the ones the ideas' run read.
        Assert.Equal((SweepIdeas.SlowAverage, 0), (TheSetupFamilies.BreakoutSwitch.IndexAverageSessions, TheSetupFamilies.BreakoutSwitch.VixLookbackSessions));
        Assert.Equal((0, SweepIdeas.Lookback), (TheSetupFamilies.DriftSwitch.IndexAverageSessions, TheSetupFamilies.DriftSwitch.VixLookbackSessions));
    }

    // A market series feed answering from constructed closes, refusing the series it is told to, timing out on the
    // ones it is told to as the provider's request does once its last try has passed, and recording what it was
    // asked for in the order asked.
    sealed class ConstructedMarket(Dictionary<string, Dictionary<DateOnly, decimal>> series, HashSet<string> refused, HashSet<string>? timesOut = null) : IMarketSeriesFeed
    {
        public int Requests { get; private set; }

        public List<string> Asked { get; } = [];

        public Task<IReadOnlyList<ProviderBar>> SeriesAsync(string name, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            Requests++;
            Asked.Add($"{name} {Day(from)} {Day(to)}");

            if (refused.Contains(name))
            {
                throw new ProviderRefusal("the provider answered 404", transient: false);
            }

            if (timesOut?.Contains(name) == true)
            {
                throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.");
            }

            return Task.FromResult<IReadOnlyList<ProviderBar>>(
            [
                .. series.GetValueOrDefault(name, [])
                    .Where(pair => pair.Key >= from && pair.Key <= to)
                    .OrderBy(pair => pair.Key)
                    .Select(pair => new ProviderBar(pair.Key, pair.Value, pair.Value + 1m, pair.Value - 1m, pair.Value, pair.Value, 0)),
            ]);
        }
    }

    [Fact]
    public async Task TheNightAsksForEachSeriesOnceOverItsWindowAndStoresOnlyTheSessionsNoNightHolds()
    {
        using var store = BreakoutStore();

        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 35, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var night = new DateOnly(2026, 10, 2);
        var index = new Dictionary<DateOnly, decimal> { [night.AddDays(-2)] = 6700m, [night.AddDays(-1)] = 6710m, [night] = 6720m };
        var feed = new ConstructedMarket(new() { [MarketCloses.Index] = index }, [MarketCloses.Vix]);

        var first = await new MarketSeriesFetcher(feed, clock, store.DatabaseFile).RunAsync("night-one");

        // Each series asked once, over the 400 days before the session the bars hold, the index first.
        Assert.Equal(["GSPC 2025-08-28 2026-10-02", "VIX 2025-08-28 2026-10-02"], feed.Asked);
        Assert.Equal(MarketSeriesFetcher.WindowDays, night.DayNumber - new DateOnly(2025, 8, 28).DayNumber);

        // The index's three sessions stored with the night that stored them; the VIX refused, stored nothing and
        // named, and the stage's own row ok, since nothing a live rule reads waits on it.
        Assert.Equal((3, 2, 1), (first.RowsWritten, first.Requests, first.Refused.Count));
        Assert.Equal(
            ["GSPC|2026-09-30|6700|6701|6699|6700|night-one", "GSPC|2026-10-01|6710|6711|6709|6710|night-one", "GSPC|2026-10-02|6720|6721|6719|6720|night-one"],
            FamilyRows(store, "SELECT series, session_date, open, high, low, close, run_id FROM market_bar ORDER BY series, session_date;"));
        Assert.Equal(
            ["market-series|ok|3|0|2|through 2026-10-02: GSPC: 3 new of the 3 session(s) sent, 3 held; VIX: nothing was stored for it, the provider answered 404; 3 session(s) stored, 2 request(s)"],
            FamilyRows(store, "SELECT stage, outcome, rows_written, model_calls, network_requests, detail FROM run_log WHERE run_id = 'night-one';"));

        // The night after, the provider revises a close it sent and serves the VIX: the index's sessions keep the
        // rows the first night wrote, and the VIX's are stored by the second.
        index[night.AddDays(-1)] = 6715m;

        var vix = new Dictionary<DateOnly, decimal> { [night.AddDays(-1)] = 16.5m, [night] = 16.25m };
        var second = await new MarketSeriesFetcher(new ConstructedMarket(new() { [MarketCloses.Index] = index, [MarketCloses.Vix] = vix }, []), clock, store.DatabaseFile).RunAsync("night-two");

        Assert.Equal((2, 0), (second.RowsWritten, second.Refused.Count));
        Assert.Equal(["6710|night-one"], FamilyRows(store, "SELECT close, run_id FROM market_bar WHERE series = 'GSPC' AND session_date = '2026-10-01';"));
        Assert.Equal(["VIX|2|night-two"], FamilyRows(store, "SELECT series, COUNT(*), MIN(run_id) FROM market_bar WHERE series = 'VIX' GROUP BY series;"));
        Assert.StartsWith("through 2026-10-02: GSPC: 0 new of the 3 session(s) sent, 3 held; VIX: 2 new of the 2 session(s) sent, 2 held", second.Detail, StringComparison.Ordinal);

        // A series sent without the night's session says so, and a store holding no session asks nothing.
        index.Remove(night);

        var shortOfTheNight = await new MarketSeriesFetcher(new ConstructedMarket(new() { [MarketCloses.Index] = index, [MarketCloses.Vix] = vix }, []), clock, store.DatabaseFile).RunAsync("night-three");

        Assert.Contains("GSPC: 0 new of the 2 session(s) sent, 3 held, the newest sent 2026-10-01 and none for the night", shortOfTheNight.Detail, StringComparison.Ordinal);

        // A series the provider does not answer in time on any try stores nothing and is named, and the stage still
        // writes its row and returns, so the night goes on.
        var slow = await new MarketSeriesFetcher(new ConstructedMarket(new() { [MarketCloses.Index] = index }, [], [MarketCloses.Vix]), clock, store.DatabaseFile).RunAsync("night-four");

        Assert.Equal((1, 2), (slow.Refused.Count, slow.Requests));
        Assert.Contains("VIX: the provider did not answer in time on any try, so nothing was stored for it", slow.Detail, StringComparison.Ordinal);
        Assert.Equal(["market-series|ok"], FamilyRows(store, "SELECT stage, outcome FROM run_log WHERE run_id = 'night-four';"));

        // And one it answers with no session stores nothing and is named as sending none.
        var sentNone = await new MarketSeriesFetcher(new ConstructedMarket(new() { [MarketCloses.Index] = index }, []), clock, store.DatabaseFile).RunAsync("night-five");

        Assert.Single(sentNone.Refused);
        Assert.Contains("VIX: the provider sent no session, so nothing was stored for it", sentNone.Detail, StringComparison.Ordinal);

        // The night's session is the newest the bars hold and not the clock's: a run on Wednesday 2026-10-07 over bars
        // ending on 2026-10-02, as a night run again for an earlier session is, asks for each series to 2026-10-02.
        var later = new ConstructedMarket(new() { [MarketCloses.Index] = index, [MarketCloses.Vix] = vix }, []);

        await new MarketSeriesFetcher(later, FixedClock.At(new DateTimeOffset(2026, 10, 7, 23, 35, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile).RunAsync("night-six");

        Assert.Equal(["GSPC 2025-08-28 2026-10-02", "VIX 2025-08-28 2026-10-02"], later.Asked);

        using var empty = new TemporaryStore().Migrated();

        var nothing = new ConstructedMarket([], []);
        var none = await new MarketSeriesFetcher(nothing, clock, empty.DatabaseFile).RunAsync("night-empty");

        Assert.Equal((0, 0), (none.Requests, nothing.Requests));
        Assert.Equal(["market-series|ok|0|no session is stored, so no market series was asked for"], FamilyRows(empty, "SELECT stage, outcome, network_requests, detail FROM run_log WHERE run_id = 'night-empty';"));
    }

    // An answer the provider sends with a 200 that cannot be read as sessions, an error page or an array of anything
    // but sessions, is a series not served: named on the stage's own row with its cause, nothing stored for it, the
    // row ok, the other series stored, and the stage returning so the night goes on.
    // see: The night asks for the index's and the VIX's daily closes once a series, and keeps them apart from the members' bars
    [Fact]
    public async Task ASeriesAnsweredInAFormThatCannotBeReadIsNamedStoresNothingAndStopsNothing()
    {
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 35, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        foreach (var (body, run) in new[] { ("<html><body>Service busy</body></html>", "night-page"), ("[\"busy\"]", "night-array") })
        {
            using var store = BreakoutStore();

            var feed = new EodhdMarketSeriesFeed(
                new HttpClient(new OneSeriesUnreadable(MarketCloses.Vix, body)) { BaseAddress = new Uri("https://eodhd.example/api/") },
                new ProviderCredentials("demo-key-not-a-real-one"),
                new ProviderRequest(RetryPolicy.Standard, (_, _) => Task.CompletedTask));

            var outcome = await new MarketSeriesFetcher(feed, clock, store.DatabaseFile).RunAsync(run);

            Assert.Equal((3, 2), (outcome.RowsWritten, outcome.Requests));
            Assert.StartsWith("VIX: nothing was stored for it, its answer could not be read: ", Assert.Single(outcome.Refused), StringComparison.Ordinal);
            Assert.Equal([$"market-series|ok|3|2"], FamilyRows(store, $"SELECT stage, outcome, rows_written, network_requests FROM run_log WHERE run_id = '{run}';"));
            Assert.Contains("VIX: nothing was stored for it, its answer could not be read: ", FamilyRows(store, $"SELECT detail FROM run_log WHERE run_id = '{run}';").Single(), StringComparison.Ordinal);
            Assert.Equal(["GSPC|3"], FamilyRows(store, "SELECT series, COUNT(*) FROM market_bar GROUP BY series;"));
        }
    }

    // A provider answering every series with three sessions to 2026-10-02 but one, which it answers with the body it
    // is handed under a 200.
    sealed class OneSeriesUnreadable(string unreadable, string body) : HttpMessageHandler
    {
        const string Served = """
            [
              {"date":"2026-09-30","open":6700,"high":6701,"low":6699,"close":6700,"adjusted_close":6700,"volume":0},
              {"date":"2026-10-01","open":6710,"high":6711,"low":6709,"close":6710,"adjusted_close":6710,"volume":0},
              {"date":"2026-10-02","open":6720,"high":6721,"low":6719,"close":6720,"adjusted_close":6720,"volume":0}
            ]
            """;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(request.RequestUri!.AbsolutePath.Contains($"/{unreadable}.", StringComparison.Ordinal) ? body : Served),
            });
    }

    [Fact]
    public async Task ASwitchedRuleFiresWhereItsFamilysRuleFiresOnANightItsSwitchIsOpenAndOnNoneWhereItIsClosed()
    {
        using var store = BreakoutStore();

        var registeredAt = new DateTimeOffset(2026, 10, 2, 20, 0, 0, TimeSpan.Zero);
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        Assert.Equal(0, (await RegisterVerbAt(store, registeredAt, RegisterVerb.Family, BreakoutRule.Name)).Code);

        // The index at 100 on each of the store's sessions before the night and at 102 on it: the average of the 200
        // to the night is 100.01, so the switch is open.
        var sessions = FamilyRows(store, $"SELECT DISTINCT session_date FROM bar WHERE session_date <= '{BreakoutNight}' ORDER BY session_date;");

        Assert.True(sessions.Count >= 200, $"The store holds {sessions.Count} session(s), and the switch reads 200.");

        store.Execute(
            "INSERT INTO market_bar (series, session_date, open, high, low, close, run_id) VALUES " +
            string.Join(", ", sessions.Select(session => FormattableString.Invariant($"('GSPC', '{session}', '1', '1', '1', '{(session == BreakoutNight ? 102 : 100)}', 'test')"))) + ";");

        var shadow = FamilyRuleShadow.For(await new CandidateRegistrar(clock, store.DatabaseFile).RowsAsync(), registeredAt.AddHours(3));
        var switched = TheSetupFamilies.Breakouts[^1];

        string FiredBy(Registration rule) =>
            string.Join(",", new[] { "BA", "BB", "BC", "NH", "SH" }.Where(ticker => FamilyRuleShadow.Read(Text(store, $"SELECT shadow FROM family_result WHERE ticker = '{ticker}' AND family = 'breakout' AND session_date = '{BreakoutNight}';")).Single(one => one.Candidate == rule.Candidate).Fired));

        await new FamilyEvaluator(clock, store.DatabaseFile).RunAsync("switch-open", shadow);

        Assert.Equal(("BA,BB,BC", "BA,BB,BC"), (FiredBy(switched), FiredBy(TheSetupFamilies.Breakouts[0])));
        Assert.EndsWith(
            "; the market switches: index over its average open, the index closed 102 above the average of its 200 closes to the night, 100.01",
            FamilyRows(store, "SELECT detail FROM run_log WHERE run_id = 'switch-open';").Single(),
            StringComparison.Ordinal);

        // At 98 on the night the average is 99.99 and the switch is closed: the night run again lists nothing under
        // the switched rule and the live rule's answers stand.
        store.Execute($"UPDATE market_bar SET close = '98' WHERE series = 'GSPC' AND session_date = '{BreakoutNight}';");

        await new FamilyEvaluator(clock, store.DatabaseFile).RunAsync("switch-closed", shadow);

        Assert.Equal((string.Empty, "BA,BB,BC"), (FiredBy(switched), FiredBy(TheSetupFamilies.Breakouts[0])));
        Assert.EndsWith(
            "; the market switches: index over its average closed, the index closed 98 at or below the average of its 200 closes to the night, 99.99",
            FamilyRows(store, "SELECT detail FROM run_log WHERE run_id = 'switch-closed';").Single(),
            StringComparison.Ordinal);

        // The switch's sessions are the store's own: back at 102 on the night but with the series missing its close on
        // one of the store's sessions inside the 200, the average is unread and the switch closed, where counted on the
        // series' own days the 200 would step over the hole and read open.
        store.Execute($"UPDATE market_bar SET close = '102' WHERE series = 'GSPC' AND session_date = '{BreakoutNight}';");
        store.Execute("DELETE FROM market_bar WHERE series = 'GSPC' AND session_date = '2026-09-15';");

        Assert.Contains("2026-09-15", sessions);

        await new FamilyEvaluator(clock, store.DatabaseFile).RunAsync("switch-holed", shadow);

        Assert.Equal((string.Empty, "BA,BB,BC"), (FiredBy(switched), FiredBy(TheSetupFamilies.Breakouts[0])));
        Assert.EndsWith(
            "; the market switches: index over its average closed, the index's stored closes do not hold all 200 sessions to the night",
            FamilyRows(store, "SELECT detail FROM run_log WHERE run_id = 'switch-holed';").Single(),
            StringComparison.Ordinal);

        // Each family's evaluator reads its own switch: the breakout's member and the drift's member each fire
        // under their family's live rule, and under its switched rule only where the switch is open.
        var vixSessions = SessionsTo(SwitchNight, 11);
        var falling = MarketCloses.On(vixSessions, new Dictionary<DateOnly, double>(), vixSessions.ToDictionary(session => session, session => session == SwitchNight ? 15.0 : 20.0));
        var rising = MarketCloses.On(vixSessions, new Dictionary<DateOnly, double>(), vixSessions.ToDictionary(session => session, session => session == SwitchNight ? 25.0 : 20.0));
        var drift = DriftMember();
        var drifts = new DriftCandidate();
        var live = DriftCandidate.ParametersOf(DriftRule.Live);
        var driftSwitched = DriftCandidate.ParametersOf(DriftRule.Live, TheSetupFamilies.DriftSwitch);

        Assert.True(drifts.EvaluateMember(drift with { Market = rising }, live).Passed);
        Assert.Equal((true, false), (drifts.EvaluateMember(drift with { Market = falling }, driftSwitched).Passed, drifts.EvaluateMember(drift with { Market = rising }, driftSwitched).Passed));
        Assert.Equal(MarketSwitches.VixGate, drifts.EvaluateMember(drift with { Market = rising }, driftSwitched).Gates[^1].Name);

        var indexSessions = SessionsTo(SwitchNight, 200);
        var above = MarketCloses.On(indexSessions, indexSessions.ToDictionary(session => session, session => session == SwitchNight ? 102.0 : 100.0), new Dictionary<DateOnly, double>());
        var below = MarketCloses.On(indexSessions, indexSessions.ToDictionary(session => session, session => session == SwitchNight ? 98.0 : 100.0), new Dictionary<DateOnly, double>());
        var breakout = BreakoutMember(102m, 2000);
        var breakouts = new BreakoutCandidate();
        var breakoutSwitched = BreakoutCandidate.ParametersOf(BreakoutRule.Live, TheSetupFamilies.BreakoutSwitch);

        Assert.True(breakouts.EvaluateMember(breakout with { Market = below }, BreakoutCandidate.ParametersOf(BreakoutRule.Live)).Passed);
        Assert.Equal((true, false), (breakouts.EvaluateMember(breakout with { Market = above }, breakoutSwitched).Passed, breakouts.EvaluateMember(breakout with { Market = below }, breakoutSwitched).Passed));
    }

    [Fact]
    public async Task EachSetupFamilyRegisteredAgainRetiresItsStandingRulesAndRegistersTheFamilyTheCodeWritesAtOneInstant()
    {
        using var store = new TemporaryStore().Migrated();

        var registrar = new CandidateRegistrar(FixedClock.At(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile);
        var asWritten = TheSetupFamilies.Breakouts.Take(7).ToArray();

        // With no breakout rule standing there is no family to register again, and nothing is written.
        var (none, noneSaid) = await RegisterVerbAt(store, new DateTimeOffset(2026, 10, 3, 11, 0, 0, TimeSpan.Zero), RegisterVerb.FamilyAgain, BreakoutRule.Name, "--evidence", "the ruling");

        Assert.Equal(1, none);
        Assert.Contains("no rule of the breakout family stands registered, so there is no family to register again and none was written", noneSaid, StringComparison.Ordinal);

        // The freeze's seven as the live store holds them, registered at 2026-10-02 11:35:37 at the version the code
        // carried then and with the four settings they stated, and the drift's live rule beside them.
        for (var at = 0; at < asWritten.Length; at++)
        {
            var settings = BreakoutCandidate.SettingsOf(asWritten[at].Parameters);
            var stated = CandidateEvaluator.Write(new Dictionary<string, double>
            {
                [BreakoutCandidate.HighSessionsParameter] = settings.HighSessions,
                [BreakoutCandidate.VolumeMultipleParameter] = settings.VolumeMultiple,
                [BreakoutCandidate.RangeCeilingParameter] = settings.RangeCeiling,
                [BreakoutCandidate.StopMovesParameter] = settings.StopMoves,
            });

            store.Execute(
                "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) VALUES " +
                FormattableString.Invariant($"({77 + at}, '{asWritten[at].Candidate}', 'the rule the freeze wrote', 'the test', 'breakout', '{stated}', '65ea06f28fff', 'registered', NULL, '2026-10-02T11:35:37Z', NULL);"));
        }

        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) VALUES " +
            "(84, 'the live drift rule as the freeze wrote it', 'the rule', 'the test', 'drift', '{}', '3309f7f59e9e', 'registered', NULL, '2026-10-02T11:35:38Z', NULL);");

        var at1200 = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
        var (code, said) = await RegisterVerbAt(store, at1200, RegisterVerb.FamilyAgain, BreakoutRule.Name, "--evidence", "the operator's ruling of 2026-10-03");

        Assert.Equal(0, code);
        Assert.StartsWith("register: retired 7 and registered 8 at one instant, family of 8 of 9: retired ", said, StringComparison.Ordinal);
        Assert.EndsWith("; on the evidence: the operator's ruling of 2026-10-03" + Environment.NewLine, said, StringComparison.Ordinal);

        // The seven retired on the evidence and the eight registered at that instant, the seven under their names and
        // the eighth the live rule switched on the index, each at the version the code carries and stating both
        // switches; the drift's rule untouched.
        const string At = "2026-10-03T12:00:00Z";

        Assert.Equal(
            [.. asWritten.Select(one => one.Candidate).Order(StringComparer.Ordinal)],
            TextRows(store, $"SELECT retires FROM candidate_register WHERE event = 'retired' AND registered_at = '{At}' AND evidence = 'the operator''s ruling of 2026-10-03' ORDER BY retires;"));
        Assert.Equal(
            [.. TheSetupFamilies.Breakouts.Select(one => one.Candidate)],
            TextRows(store, $"SELECT candidate FROM candidate_register WHERE event = 'registered' AND registered_at = '{At}' ORDER BY id;"));
        Assert.All(
            TextRows(store, $"SELECT evaluator_version || '|' || parameters FROM candidate_register WHERE event = 'registered' AND registered_at = '{At}';"),
            row => Assert.Matches(@"^" + new BreakoutCandidate().Version + @"\|\{.*""indexAverageSessions"": (0|200), .*""vixLookbackSessions"": 0, .*\}$", row));
        Assert.Equal(1, Scalar(store, "SELECT COUNT(*) FROM candidate_register WHERE evaluator = 'drift';"));

        // What restarts: each rule's record counts from the first session on or after the day it registered, so
        // from Monday 2026-10-05 in place of Friday 2026-10-02.
        var standing = CandidateFamily.In(CandidateFamily.Standing(await registrar.RowsAsync(), at1200), BreakoutRule.Name);

        Assert.Equal(8, standing.Count);
        Assert.All(standing, row => Assert.Equal(new DateOnly(2026, 10, 5), FamilyRecords.FirstSession(row.RegisteredAt)));
        Assert.Equal(new DateOnly(2026, 10, 2), FamilyRecords.FirstSession(new DateTimeOffset(2026, 10, 2, 11, 35, 37, TimeSpan.Zero)));

        // Registered again once more it retires the eight and registers them again, and a family no freeze is
        // written for is refused by name with nothing written.
        var (again, againSaid) = await RegisterVerbAt(store, at1200.AddMinutes(1), RegisterVerb.FamilyAgain, BreakoutRule.Name, "--evidence", "again");
        var before = Scalar(store, "SELECT COUNT(*) FROM candidate_register;");
        var (leader, leaderSaid) = await RegisterVerbAt(store, at1200.AddMinutes(2), RegisterVerb.FamilyAgain, LeaderRule.Name, "--evidence", "the ruling");

        Assert.Equal((0, 1), (again, leader));
        Assert.StartsWith("register: retired 8 and registered 8 at one instant, family of 8 of 9", againSaid, StringComparison.Ordinal);
        Assert.Contains("no freeze is written for a family named 'leader', so it was not registered again; the families a freeze is written for are breakout, drift.", leaderSaid, StringComparison.Ordinal);
        Assert.Equal(before, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));
        Assert.Equal(
            ["refused", "refused"],
            TextRows(store, "SELECT outcome FROM run_log WHERE stage = 'candidate-register' AND outcome = 'refused' ORDER BY rowid;"));
    }
}
