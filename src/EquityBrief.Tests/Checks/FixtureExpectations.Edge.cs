using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Returns;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 12.7: the swing filter's rows scored on their own plan from the night's close,
// over the setup's cap and over twenty sessions as context, the near misses' arithmetic over a
// constructed population with known outcomes, and the edge half's source reaching no recount.
public partial class FixtureExpectations
{
    // The exchange's sessions from a day on, as many as asked for.
    static DateOnly[] SessionsFrom(DateOnly from, int count)
    {
        var sessions = new List<DateOnly>();

        for (var day = from; sessions.Count < count; day = day.AddDays(1))
        {
            if (ExchangeClosures.IsSession(day))
            {
                sessions.Add(day);
            }
        }

        return [.. sessions];
    }

    static ReturnBar[] Closes(DateOnly[] sessions, params decimal[] closes) =>
        [.. closes.Select((close, at) => new ReturnBar(sessions[at + 1], close))];

    [Fact]
    public void TheSwingPlanIsScoredFromTheNightsCloseOverTheCapAndOverTwentySessionsAsContext()
    {
        // Worked by hand: entered at the night's close of 100, stopped below 95, won at 110, so the plan
        // had to be right (100 - 95) of (110 - 95), a third of the time, to come out even.
        var sessions = SessionsFrom(new DateOnly(2026, 1, 2), 70);
        const decimal Stop = 95m, Target = 110m, Close = 100m;

        ForwardReturn Score(string horizon, ReturnBar[] after) =>
            ForwardReturnSeries.OverSetup(after, Stop, Target, null, Close, Close, horizon, ForwardReturnSeries.CapOf(horizon));

        var nineteen = Enumerable.Repeat(101m, 19).ToArray();

        // The target reached on the twentieth session: a win on both horizons, on that session.
        var twentieth = Closes(sessions, [.. nineteen, 110m]);

        foreach (var horizon in ForwardReturnSeries.SwingHorizons)
        {
            var won = Score(horizon, twentieth);

            Assert.Equal((horizon, ForwardReturnSeries.Win, sessions[20]), (won.Horizon, won.Outcome, won.ResolvedOn));
            Assert.Equal(10, won.ReturnPct!.Value, 9);
            Assert.Equal(100.0 / 3, won.BreakEven!.Value, 9);
        }

        // One session later: a win over the cap and, over twenty sessions, unresolved at the twentieth
        // session's close of 105, five per cent from the entry.
        var twentyFirst = Closes(sessions, [.. nineteen, 105m, 111m]);
        var capped = Score(ForwardReturnSeries.Swing, twentyFirst);
        var twenty = Score(ForwardReturnSeries.SwingTwenty, twentyFirst);

        Assert.Equal((ForwardReturnSeries.Win, sessions[21]), (capped.Outcome, capped.ResolvedOn));
        Assert.Equal((ForwardReturnSeries.Unresolved, sessions[20]), (twenty.Outcome, twenty.ResolvedOn));
        Assert.Equal(5, twenty.ReturnPct!.Value, 9);

        // A close below the stop on the third session is a loss on both.
        var stopped = Closes(sessions, 99m, 97m, 94m);

        Assert.All(ForwardReturnSeries.SwingHorizons, horizon => Assert.Equal((ForwardReturnSeries.Loss, sessions[3]), (Score(horizon, stopped).Outcome, Score(horizon, stopped).ResolvedOn)));

        // Flat for 63 sessions: unresolved at the cap's last session and at the twentieth; flat for 19,
        // neither has matured, and flat for 20, the twenty-session outcome has and the capped one has not.
        var flat = Closes(sessions, [.. Enumerable.Repeat(100m, 63)]);

        Assert.Equal((ForwardReturnSeries.Unresolved, sessions[63]), (Score(ForwardReturnSeries.Swing, flat).Outcome, Score(ForwardReturnSeries.Swing, flat).ResolvedOn));
        Assert.Equal((ForwardReturnSeries.Unresolved, sessions[20]), (Score(ForwardReturnSeries.SwingTwenty, flat).Outcome, Score(ForwardReturnSeries.SwingTwenty, flat).ResolvedOn));
        Assert.Null(Score(ForwardReturnSeries.SwingTwenty, flat[..19]).Outcome);
        Assert.Null(Score(ForwardReturnSeries.Swing, flat[..19]).Outcome);
        Assert.Equal(ForwardReturnSeries.Unresolved, Score(ForwardReturnSeries.SwingTwenty, flat[..20]).Outcome);
        Assert.Null(Score(ForwardReturnSeries.Swing, flat[..20]).Outcome);

        // The caps the rows state.
        Assert.Equal((63, 20), (ForwardReturnSeries.CapOf(ForwardReturnSeries.Swing), ForwardReturnSeries.CapOf(ForwardReturnSeries.SwingTwenty)));
    }

    [Fact]
    public async Task TheFillerScoresEverySwingFilterRowCarryingAPlanAndNoOther()
    {
        using var store = new TemporaryStore().Migrated();

        var sessions = SessionsFrom(new DateOnly(2026, 1, 2), 70);
        var night = sessions[0];

        void Bars(string ticker, decimal rawClose, params decimal[] after)
        {
            store.Execute(
                "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
                $"VALUES ('{ticker}', '{Day(night)}', '100', '100', '100', '100', 1000, 'test', '2026-01-02T22:00:00Z', '{rawClose.ToString(CultureInfo.InvariantCulture)}');");

            for (var at = 0; at < after.Length; at++)
            {
                var close = after[at].ToString(CultureInfo.InvariantCulture);

                store.Execute(
                    "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
                    $"VALUES ('{ticker}', '{Day(sessions[at + 1])}', '{close}', '{close}', '{close}', '{close}', 1000, 'test', '2026-01-02T22:00:00Z', '{close}');");
            }
        }

        void Gate(string ticker, string? stop, string? target) =>
            store.Execute(
                "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, trigger_pass, trade, swing_stop, swing_target, exclusions, passed, gates) " +
                $"VALUES ('{ticker}', '{Day(night)}', '1', 'code', 1, 1, {(stop is null ? 0 : 1)}, 1, 1, {(stop is null ? "NULL" : $"'{stop}'")}, {(target is null ? "NULL" : $"'{target}'")}, '[]', 0, '{{\"gates\":[],\"notes\":[]}}');");

        var nineteen = Enumerable.Repeat(101m, 19).ToArray();

        // Worked by hand, each entered at the night's close of 100 with its stop at 95 and target at 110.
        Bars("WIN21", 100m, [.. nineteen, 105m, 111m]);
        Gate("WIN21", "95", "110");
        Bars("LOSS", 100m, 99m, 97m, 94m);
        Gate("LOSS", "95", "110");
        Bars("FLAT", 100m, [.. Enumerable.Repeat(100m, 63)]);
        Gate("FLAT", "95", "110");
        Bars("YOUNG", 100m, [.. Enumerable.Repeat(100m, 19)]);
        Gate("YOUNG", "95", "110");

        // A plan whose night's raw close of 94 sits below its stop, or of 111 above its target, could not be
        // entered at the close, and a row whose setup found no band has no plan: none of the three is scored,
        // and the two plans are counted as not scorable.
        Bars("BELOW", 94m, 100m);
        Gate("BELOW", "95", "110");
        Bars("ABOVE", 111m, 100m);
        Gate("ABOVE", "95", "110");
        Bars("NOPLAN", 100m, 100m);
        Gate("NOPLAN", null, null);

        var filled = await new ForwardReturnFiller(FixedClock.At(new DateTimeOffset(2026, 6, 1, 22, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile).RunAsync("swing-fill");

        Assert.Equal((6, 2), (filled.PlansExamined, filled.PlansNotScorable));
        Assert.Equal(
            [
                $"FLAT|swing|unresolved|{Day(sessions[63])}|0.0|33.3",
                $"FLAT|swing-20|unresolved|{Day(sessions[20])}|0.0|33.3",
                $"LOSS|swing|loss|{Day(sessions[3])}|-6.0|33.3",
                $"LOSS|swing-20|loss|{Day(sessions[3])}|-6.0|33.3",
                $"WIN21|swing|win|{Day(sessions[21])}|11.0|33.3",
                $"WIN21|swing-20|unresolved|{Day(sessions[20])}|5.0|33.3",
                "YOUNG|swing|none|none|none|none",
                "YOUNG|swing-20|none|none|none|none",
            ],
            Query(store, "SELECT ticker, horizon, IFNULL(outcome, 'none'), IFNULL(resolved_on, 'none'), CASE WHEN return_pct IS NULL THEN 'none' ELSE printf('%.1f', return_pct) END, CASE WHEN break_even IS NULL THEN 'none' ELSE printf('%.1f', break_even) END FROM forward_return ORDER BY ticker, horizon;"));
        Assert.Contains("6 swing plan(s) read, 2 not scorable from the night's close", Query(store, "SELECT detail FROM run_log WHERE run_id = 'swing-fill';").Single(), StringComparison.Ordinal);

        // A decided outcome is not written again, and a young one is written once it matures.
        store.Execute("DELETE FROM bar WHERE ticker = 'LOSS' AND session_date > '" + Day(night) + "';");
        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
            $"VALUES ('YOUNG', '{Day(sessions[20])}', '110', '110', '110', '110', 1000, 'test', '2026-01-02T22:00:00Z', '110');");

        await new ForwardReturnFiller(FixedClock.At(new DateTimeOffset(2026, 6, 2, 22, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile).RunAsync("swing-fill-again");

        Assert.Equal(["loss|loss"], Query(store, "SELECT group_concat(outcome, '|') FROM forward_return WHERE ticker = 'LOSS';"));
        Assert.Equal(["win|win"], Query(store, "SELECT group_concat(outcome, '|') FROM forward_return WHERE ticker = 'YOUNG';"));

        static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task EverySwingPlanOnTheFixturesNightCarriesBothSwingHorizonsNotYetMatured()
    {
        var swing = Expected("forward-returns").GetProperty("swing");

        Assert.Equal([.. swing.GetProperty("horizons").EnumerateArray().Select(horizon => horizon.GetString()!)], ForwardReturnSeries.SwingHorizons);
        Assert.All(ForwardReturnSeries.SwingHorizons, horizon => Assert.Equal(swing.GetProperty("caps").GetProperty(horizon).GetInt32(), ForwardReturnSeries.CapOf(horizon)));

        using var store = await FixtureReplay.ReplayedAsync();

        // Each plan's rows, read against the gate rows rather than a count written down: every row carrying
        // a plan has one row on each swing horizon, a row carrying none has none, and none has matured.
        var plans = Query(store, "SELECT ticker || '|' || session_date FROM gate_result WHERE swing_stop IS NOT NULL AND swing_target IS NOT NULL ORDER BY 1;");
        var perPlan = swing.GetProperty("rowsPerPlan").GetInt32();

        Assert.NotEmpty(plans);
        Assert.Equal(
            [.. plans.SelectMany(plan => ForwardReturnSeries.SwingHorizons.Select(horizon => plan + "|" + horizon + "|none"))],
            Query(store, "SELECT ticker || '|' || session_date || '|' || horizon || '|' || IFNULL(outcome, 'none') FROM forward_return WHERE horizon IN ('swing', 'swing-20') ORDER BY ticker, session_date, horizon;"));
        Assert.Equal(plans.Count * perPlan, Query(store, "SELECT COUNT(*) FROM forward_return WHERE horizon IN ('swing', 'swing-20');").Select(count => int.Parse(count, CultureInfo.InvariantCulture)).Single());
    }

    [Fact]
    public void TheNearMissesGiveTheArithmeticTheirConstructedOutcomesPredict()
    {
        // Nine blocks of 63 sessions from 2025-01-02, a row set placed on each block's first session, the
        // first of them the first night the version stored, read on the night the ninth block's last
        // outcome window closes, 629 sessions after the first. Worked by hand:
        // admitted, a win and a loss a block against a bar of 40% and a break-even of 35%: 18 setups, 50%;
        // the trigger alone, two wins a block against 30% and 30%: 100%; the trade alone, a loss a block in
        // five blocks, withheld below the floor of 8; the setup alone, nine rows and no plan, nothing
        // scored; trend and strength alone, nine rows unresolved, nothing scored; suspect series alone, a
        // loss a block against 50% and 40%: 0%. A row failing the trigger and the trade, winning, belongs to
        // no group, and so do a row failing the trigger alone with the earnings exclusion, losing, and a row
        // passing every gate with the earnings and suspect series exclusions both, winning: a gate's group
        // takes a row carrying no exclusion, and an exclusion's group a row every gate passed carrying that
        // exclusion and no other, so the earnings and gap groups hold none.
        var sessions = SessionsFrom(new DateOnly(2025, 1, 2), 630);
        var night = sessions[629];
        var rows = new List<NearMissRow>();

        NearMissRow Row(DateOnly session, bool[] gates, string[] exclusions, string? outcome, double bar = 0.4, double breakEven = 35) =>
            new(session, gates[0], gates[1], gates[2], gates[3], gates[4], exclusions, gates.All(held => held) && exclusions.Length == 0,
                outcome is null ? null : new CandidateSetup(session, outcome, bar, bar, breakEven, 0, 1, false));

        bool[] All() => [true, true, true, true, true];

        bool[] Failing(params int[] gates) => [.. Enumerable.Range(0, 5).Select(at => !gates.Contains(at))];

        for (var block = 0; block < 9; block++)
        {
            var on = sessions[block * Blocks.Sessions];

            rows.Add(Row(on, All(), [], ForwardReturnSeries.Win));
            rows.Add(Row(on, All(), [], ForwardReturnSeries.Loss));
            rows.Add(Row(on, Failing(3), [], ForwardReturnSeries.Win, 0.3, 30));
            rows.Add(Row(on, Failing(3), [], ForwardReturnSeries.Win, 0.3, 30));

            if (block < 5)
            {
                rows.Add(Row(on, Failing(4), [], ForwardReturnSeries.Loss));
            }

            rows.Add(Row(on, Failing(2), [], null));
            rows.Add(Row(on, Failing(1), [], ForwardReturnSeries.Unresolved));
            rows.Add(Row(on, All(), [SwingGates.SuspectExclusion], ForwardReturnSeries.Loss, 0.5, 40));
            rows.Add(Row(on, Failing(3, 4), [], ForwardReturnSeries.Win));
            rows.Add(Row(on, Failing(3), [SwingGates.EarningsExclusion], ForwardReturnSeries.Loss));
            rows.Add(Row(on, All(), [SwingGates.EarningsExclusion, SwingGates.SuspectExclusion], ForwardReturnSeries.Win));
        }

        var groups = EdgeClock.NearMisses(rows, night).ToDictionary(group => group.Group, StringComparer.Ordinal);

        Assert.Equal(
            [EdgeClock.Admitted, SwingGates.Market, SwingGates.Trend, SwingGates.Setup, SwingGates.Trigger, SwingGates.Trade, SwingGates.EarningsExclusion, SwingGates.SuspectExclusion, SwingGates.GapExclusion],
            EdgeClock.NearMisses(rows, night).Select(group => group.Group));

        void Read(string group, int rowCount, int blocks, int setups, double? share, double? bar, double? breakEven)
        {
            var read = groups[group];

            Assert.Equal((rowCount, blocks, setups), (read.Rows, read.Record.Blocks, read.Record.Setups));
            Assert.Equal(share, read.Record.Share);
            Assert.Equal(bar is null ? (double?)null : Math.Round(bar.Value, 9), read.Record.NullShare is { } held ? Math.Round(held, 9) : (double?)null);
            Assert.Equal(breakEven, read.Record.PlannedBreakEven);
        }

        Read(EdgeClock.Admitted, 18, 9, 18, 50, 40, 35);
        Read(SwingGates.Trigger, 18, 9, 18, 100, 30, 30);
        Read(SwingGates.SuspectExclusion, 9, 9, 9, 0, 50, 40);
        Read(SwingGates.Setup, 9, 0, 0, null, null, null);
        Read(SwingGates.Trend, 9, 0, 0, null, null, null);
        Read(SwingGates.Market, 0, 0, 0, null, null, null);
        Read(SwingGates.EarningsExclusion, 0, 0, 0, null, null, null);
        Read(SwingGates.GapExclusion, 0, 0, 0, null, null, null);

        var trade = groups[SwingGates.Trade];

        Assert.Equal((5, 5, CandidateRecord.BelowTheBlockFloor), (trade.Rows, trade.Record.Blocks, trade.Record.Withheld));
        Assert.Equal(CandidateRecord.Shown, groups[EdgeClock.Admitted].Record.Withheld);
    }

    [Fact]
    public void TheEdgeHalfsSourceReachesNoRecountOfTheNights()
    {
        // The edge half reads what the nights stored. The recount the shape clock may make, the gates
        // evaluated again over stored readings, the ranking and the year replayed, is named here and read
        // for in the edge half's own sources, and the scan is shown to find a name it is looking for.
        string[] recount = ["ShapeProposals", "FilterCounts", "SwingGates.Evaluate", "SwingGates.Ranked", "SessionReplay", "SwingFunnel", "ShapeClock", "GateInputs"];
        string[] sources = ["src/EquityBrief.Core/Filter/EdgeClock.cs", "src/EquityBrief.Api/Reading/EdgeScreen.cs"];

        foreach (var source in sources)
        {
            var text = File.ReadAllText(Path.Combine(Repository.Root, source));

            Assert.All(recount, name => Assert.DoesNotContain(name, text, StringComparison.Ordinal));
        }

        Assert.Contains("ShapeProposals", File.ReadAllText(Path.Combine(Repository.Root, "src/EquityBrief.Worker/Filter/ShapeProposer.cs")), StringComparison.Ordinal);
    }
}
