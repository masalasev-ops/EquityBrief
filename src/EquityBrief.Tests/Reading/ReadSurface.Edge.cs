using System.Net;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Returns;
using EquityBrief.Web.Marks;
using EquityBrief.Tests.Checks;
using EquityBrief.Worker.Candidates;

namespace EquityBrief.Tests.Reading;

// read-surface, 12.7: the Calibration region's edge half, each swing family candidate's record and the
// near misses read off the run page against a constructed store, no verdict drawn below the block floor,
// no figure moved by the open version's settings changing, and each variant naming what the live filter
// has moved since the version it was defined against.
public partial class ReadSurface
{
    static readonly string NearestBands = TheSwingFamily.Variant(TheSwingFamily.NearestBandsName, "1");

    static readonly string[] TheFamilyNine =
    [
        SwingFamily.LiveCandidate("1"),
        NearestBands,
        TheSwingFamily.Variant(TheSwingFamily.DepthName, "1"),
        TheSwingFamily.Variant(TheSwingFamily.MarketOffName, "1"),
        TheSwingFamily.Variant(TheSwingFamily.StrengthName, "1"),
        TheSwingFamily.Variant(TheSwingFamily.ArrivalName, "1"),
        TheSwingFamily.Variant(TheSwingFamily.DeterioratingName, "1"),
        TheSwingFamily.Variant(TheSwingFamily.RevisionsName, "1"),
        TheSwingFamily.Variant(TheSwingFamily.BestThreeName, "1"),
    ];

    // One gate row under filter version 1 with the family's shadow on it: the candidates named fired and
    // every other one of the nine evaluated and quiet, each verdict naming the plan its trade gate read,
    // the nearest bands' for that variant and section 10's for every other.
    static void EdgeRow(TemporaryStore store, string session, string ticker, bool[] gates, string[] exclusions, params string[] fired)
    {
        var shadow = JsonSerializer.Serialize(new
        {
            candidates = TheFamilyNine.Select(candidate => new
            {
                candidate,
                fired = fired.Contains(candidate),
                values = new Dictionary<string, string> { [SwingFilterRule.PlanValue] = candidate == NearestBands ? FilterSettings.SwingWord : FilterSettings.ClearWord },
            }),
            skipped = Array.Empty<object>(),
        });

        store.Execute(
            "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, trigger_pass, trade, swing_stop, swing_target, exclusions, passed, gates, shadow) " +
            $"VALUES ('{ticker}', '{session}', '1', 'code', {Bit(gates[0])}, {Bit(gates[1])}, {Bit(gates[2])}, {Bit(gates[3])}, {Bit(gates[4])}, '95', '110', " +
            $"'{JsonSerializer.Serialize(exclusions)}', {Bit(gates.All(held => held) && exclusions.Length == 0)}, '{{\"gates\":[],\"notes\":[]}}', '{shadow}');");

        static string Bit(bool value) => value ? "1" : "0";
    }

    static void SwingOutcome(TemporaryStore store, string session, string ticker, string outcome, string horizon = ForwardReturnSeries.Swing) =>
        store.Execute(
            "INSERT INTO forward_return (ticker, session_date, horizon, outcome, resolved_on, return_pct, base_rate, break_even, null_win, null_win_at_sensitivity, planned_risk, on_earnings) " +
            $"VALUES ('{ticker}', '{session}', '{horizon}', '{outcome}', '{session}', 1, NULL, 35, 0.4, 0.4, 5, 0);");

    static string EdgeHalf(string page) =>
        Assert.Single(Blocks(page, "<section class=\"edge-clock\".*?</section>")) + Assert.Single(Blocks(page, "<section class=\"near-misses\".*?</section>"));

    [Fact]
    public async Task TheEdgeClockAndTheNearMissesAreReadOffTheStoredRowsAndDrawNoVerdictBelowTheFloor()
    {
        using var store = await FixtureExpectations.FamilyStore(new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero));

        Assert.Equal(0, (await FixtureExpectations.RegisterVerbAt(store, new DateTimeOffset(2026, 9, 7, 22, 0, 0, TimeSpan.Zero), RegisterVerb.TheFamily)).Code);

        // Three nights under version 1, whose trade gate reads section 10's plan. Worked by hand: the live
        // filter fires on the name the filter passed on the first two, each plan clear of the noise a win and
        // a loss, and the nearest bands' variant on A's first night and on F's, each read on the plan at the
        // nearest bands, a win on both, where F's plan clear of the noise has no outcome; the other seven fire
        // nowhere. Every candidate was first evaluated on 2026-09-14, two sessions before the page's night.
        // The rows: three admitted, one the trigger alone rejected, one the setup alone rejected, one suspect
        // series alone removed, and one failing two gates in no group.
        bool[] all = [true, true, true, true, true];

        EdgeRow(store, "2026-09-14", "A", all, [], SwingFamily.LiveCandidate("1"), NearestBands);
        EdgeRow(store, "2026-09-14", "B", [true, true, true, false, true], []);
        EdgeRow(store, "2026-09-14", "C", [true, true, false, true, true], []);
        EdgeRow(store, "2026-09-14", "D", [true, true, true, false, false], []);
        EdgeRow(store, "2026-09-15", "A", all, [], SwingFamily.LiveCandidate("1"));
        EdgeRow(store, "2026-09-15", "F", all, [], NearestBands);
        EdgeRow(store, "2026-09-16", "E", all, [SwingGates.SuspectExclusion]);
        SwingOutcome(store, "2026-09-14", "A", "win", ForwardReturnSeries.Clear);
        SwingOutcome(store, "2026-09-14", "A", "win");
        SwingOutcome(store, "2026-09-15", "A", "loss", ForwardReturnSeries.Clear);
        SwingOutcome(store, "2026-09-15", "F", "win");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-09-16"));
        var edge = Assert.Single(Blocks(page, "<section class=\"edge-clock\".*?</section>"));

        Assert.Contains("data-candidates=\"9\"", edge, StringComparison.Ordinal);
        Assert.Contains("Each candidate's first look is read at 8 non-empty blocks, no earlier than 566 sessions after its first night, and it can retire the candidate or leave it and cannot promote it; a promotion can come no earlier than the look at 12 blocks, 818 sessions after its first night.", edge, StringComparison.Ordinal);

        // The live filter first, then the eight in the order registered, each defined against version 1 with
        // the live filter unmoved, two sessions run, no block closed and every figure withheld.
        var rows = Blocks(edge, "<tr data-candidate=.*?</tr>");

        Assert.Equal(TheFamilyNine, rows.Select(row => System.Text.RegularExpressions.Regex.Match(row, "data-candidate=\"([^\"]+)\"").Groups[1].Value));

        foreach (var (row, resolved) in rows.Zip(new[] { 2, 2, 0, 0, 0, 0, 0, 0, 0 }))
        {
            Assert.Contains($"data-defined=\"1\" data-moved=\"\" data-sessions=\"2\" data-blocks=\"0\" data-floor=\"8\" data-resolved=\"{resolved}\" data-withheld=\"true\"", row, StringComparison.Ordinal);
            Assert.Contains("<td>2 since its first night, 2026-09-14</td>", row, StringComparison.Ordinal);
            Assert.Contains("withheld until 8 non-empty blocks", row, StringComparison.Ordinal);
            Assert.DoesNotContain("reached target before stop", row, StringComparison.Ordinal);
        }

        Assert.Contains("<td>version 1, the live settings</td>", rows[0], StringComparison.Ordinal);
        Assert.Contains("<td>version 1, and the live filter has not moved since</td>", rows[1], StringComparison.Ordinal);

        // The near misses over version 1's rows, each group counted and withheld, each row's setup read on the
        // plan version 1's trade gate reads, so F's, with no outcome on it, is admitted and unresolved.
        var misses = Assert.Single(Blocks(page, "<section class=\"near-misses\".*?</section>"));

        Assert.Contains("Over the rows filter version 1 has stored from 2026-09-14", misses, StringComparison.Ordinal);

        foreach (var (group, count, resolved) in new (string, int, int)[]
        {
            (EdgeClock.Admitted, 3, 2), (SwingGates.Market, 0, 0), (SwingGates.Trend, 0, 0), (SwingGates.Setup, 1, 0), (SwingGates.Trigger, 1, 0), (SwingGates.Trade, 0, 0),
            (SwingGates.EarningsExclusion, 0, 0), (SwingGates.SuspectExclusion, 1, 0), (SwingGates.GapExclusion, 0, 0),
        })
        {
            Assert.Contains($"data-group=\"{group}\" data-kind=\"{(group == EdgeClock.Admitted ? EdgeClock.Admitted : EdgeClock.Exclusions.Contains(group) ? EdgeClock.Exclusion : EdgeClock.Gate)}\" data-rows=\"{count}\" data-resolved=\"{resolved}\" data-blocks=\"0\" data-withheld=\"true\"", misses, StringComparison.Ordinal);
        }

        // The open version's settings rewritten under it change none of the half's figures, which are read
        // off what the nights stored and never recounted.
        var before = EdgeHalf(page);
        var settings = Strings(store, "SELECT settings FROM filter_version WHERE version = '1';").Single();

        store.Execute("UPDATE filter_version SET settings = json_set(settings, '$.strengthFloor', 0.9, '$.rewardToRiskFloor', 3.0) WHERE version = '1';");

        Assert.Equal(before, EdgeHalf(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-09-16"))));

        store.Execute($"UPDATE filter_version SET settings = '{settings}' WHERE version = '1';");

        // An acceptance moves the live filter to version 2 at a strength floor of 0.6: its new candidate is
        // defined against version 2 and evaluated on no night yet, and each variant, still defined against
        // version 1, names the setting the live filter has moved since.
        Assert.Equal(0, (await FixtureExpectations.ShapeVerb(store, new DateTimeOffset(2026, 9, 17, 22, 0, 0, TimeSpan.Zero), "--settings", "strengthFloor=0.6", "--evidence", "the first live trigger")).Code);

        var moved = Blocks(Assert.Single(Blocks(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-09-16")), "<section class=\"edge-clock\".*?</section>")), "<tr data-candidate=.*?</tr>");

        Assert.Contains($"data-candidate=\"{SwingFamily.LiveCandidate("2")}\" data-live=\"true\" data-defined=\"2\" data-moved=\"\"", moved[0], StringComparison.Ordinal);
        Assert.Contains("<td>no night has evaluated it yet</td>", moved[0], StringComparison.Ordinal);
        Assert.All(moved.Skip(1), row => Assert.Contains("data-defined=\"1\" data-moved=\"strengthFloor\"", row, StringComparison.Ordinal));
        Assert.Contains("<td>version 1; the live filter has since moved strengthFloor</td>", moved[1], StringComparison.Ordinal);
    }

    // Nine closed blocks from 2025-01-02, read 629 sessions after the first night. Worked by hand: the
    // live filter wins one and loses one a block against a calibrated bar of 40% and a break-even of
    // 35%, so 50% of 18; the variant registered beside it wins one a block in five blocks, below the
    // floor. The near misses over the same sessions: admitted as the live filter's, the trigger alone
    // two wins a block against 30% and 30%, and the trade alone a loss a block in five blocks.
    static (DateOnly Night, DateOnly First, IReadOnlyList<EdgeCandidate> Candidates, IReadOnlyList<NearMissRow> Rows) NineBlocks()
    {
        var sessions = new List<DateOnly>();

        for (var day = new DateOnly(2025, 1, 2); sessions.Count < 630; day = day.AddDays(1))
        {
            if (ExchangeClosures.IsSession(day))
            {
                sessions.Add(day);
            }
        }

        var night = sessions[629];
        var registered = new DateTimeOffset(2024, 12, 31, 22, 0, 0, TimeSpan.Zero);
        var parameters = JsonSerializer.Serialize(SwingFilterRule.ParametersOf(FilterSettings.Proposed));
        RegisterRow[] register =
        [
            new(1, SwingFamily.LiveCandidate("1"), string.Empty, string.Empty, SwingFilterRule.EvaluatorName, parameters, string.Empty, CandidateFamily.Registered, null, registered, null),
            new(2, NearestBands, string.Empty, string.Empty, SwingFilterRule.EvaluatorName, parameters, string.Empty, CandidateFamily.Registered, null, registered, null),
        ];

        CandidateSetup Setup(DateOnly on, string outcome, double bar, double breakEven) => new(on, outcome, bar, bar, breakEven, 0, 1, false);

        var live = new List<CandidateSetup>();
        var variant = new List<CandidateSetup>();
        var rows = new List<NearMissRow>();

        for (var block = 0; block < 9; block++)
        {
            var on = sessions[block * EquityBrief.Core.Returns.Blocks.Sessions];

            live.Add(Setup(on, ForwardReturnSeries.Win, 0.4, 35));
            live.Add(Setup(on, ForwardReturnSeries.Loss, 0.4, 35));
            rows.Add(new NearMissRow(on, true, true, true, true, true, [], true, Setup(on, ForwardReturnSeries.Win, 0.4, 35)));
            rows.Add(new NearMissRow(on, true, true, true, true, true, [], true, Setup(on, ForwardReturnSeries.Loss, 0.4, 35)));
            rows.Add(new NearMissRow(on, true, true, true, false, true, [], false, Setup(on, ForwardReturnSeries.Win, 0.3, 30)));
            rows.Add(new NearMissRow(on, true, true, true, false, true, [], false, Setup(on, ForwardReturnSeries.Win, 0.3, 30)));

            if (block < 5)
            {
                variant.Add(Setup(on, ForwardReturnSeries.Win, 0.3, 30));
                rows.Add(new NearMissRow(on, true, true, true, true, false, [], false, Setup(on, ForwardReturnSeries.Loss, 0.4, 35)));
            }
        }

        var candidates = EdgeClock.Candidates(
            register,
            new Dictionary<string, IReadOnlyList<CandidateSetup>>(StringComparer.Ordinal) { [register[0].Candidate] = live, [register[1].Candidate] = variant },
            new Dictionary<string, DateOnly>(StringComparer.Ordinal) { [register[0].Candidate] = sessions[0], [register[1].Candidate] = sessions[0] },
            night,
            registered.AddDays(1));

        return (night, sessions[0], candidates, rows);
    }

    [Fact]
    public void TheEdgeHalfDrawsEachFigureFromTheFloorOnAndNoneBelowIt()
    {
        var (night, first, candidates, rows) = NineBlocks();
        var marks = new MarkRenderer();
        var edge = Blocks(WebUtility.HtmlDecode(marks.Edge(new EdgeView(night, candidates, EdgeClock.FirstLookSessions, EdgeClock.EarliestPromotionSessions))), "<tr data-candidate=.*?</tr>");

        Assert.Contains("data-sessions=\"629\" data-blocks=\"9\" data-floor=\"8\" data-resolved=\"18\" data-withheld=\"false\"", edge[0], StringComparison.Ordinal);
        Assert.Contains("<td>50% reached target before stop, against a planned break-even of 35% and a calibrated null of 40%</td>", edge[0], StringComparison.Ordinal);
        Assert.Contains("data-blocks=\"5\" data-floor=\"8\" data-resolved=\"5\" data-withheld=\"true\"", edge[1], StringComparison.Ordinal);
        Assert.Contains("withheld until 8 non-empty blocks", edge[1], StringComparison.Ordinal);
        Assert.DoesNotContain("reached target before stop", edge[1], StringComparison.Ordinal);

        var misses = WebUtility.HtmlDecode(marks.NearMisses(new NearMissView(night, "1", first, EdgeClock.NearMisses(rows, night))));

        Assert.Contains("<tr data-group=\"admitted\" data-kind=\"admitted\" data-rows=\"18\" data-resolved=\"18\" data-blocks=\"9\" data-withheld=\"false\"><td>admitted by the filter</td><td class=\"num\">18</td><td class=\"num\">18</td><td class=\"num\">9 of 8</td><td>50% reached target before stop, against a planned break-even of 35% and a calibrated null of 40%</td></tr>", misses, StringComparison.Ordinal);
        Assert.Contains("<td>rejected by trigger alone</td><td class=\"num\">18</td><td class=\"num\">18</td><td class=\"num\">9 of 8</td><td>100% reached target before stop, against a planned break-even of 30% and a calibrated null of 30%</td>", misses, StringComparison.Ordinal);
        Assert.Contains("<td>rejected by trade alone</td><td class=\"num\">5</td><td class=\"num\">5</td><td class=\"num\">5 of 8</td><td class=\"not-yet\">withheld until 8 non-empty blocks</td>", misses, StringComparison.Ordinal);
        Assert.Contains("Over the rows filter version 1 has stored from 2025-01-02", misses, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheBlocksBesideAProposalAndTheBlocksTheCommandHoldsAnAcceptanceToAreReadOffTheLiveFiltersOwnRows()
    {
        using var store = await FixtureExpectations.FamilyStore(new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero));

        Assert.Equal(0, (await FixtureExpectations.RegisterVerbAt(store, new DateTimeOffset(2026, 9, 7, 22, 0, 0, TimeSpan.Zero), RegisterVerb.TheFamily)).Code);

        // The first acceptance while the live filter stands costs its restart and states nothing, and the
        // live filter's candidate is version 2's from then.
        Assert.Equal(0, (await FixtureExpectations.ShapeVerb(store, new DateTimeOffset(2026, 9, 8, 22, 0, 0, TimeSpan.Zero), "--settings", "strengthFloor=0.6", "--evidence", "the first live trigger")).Code);

        // Version 2's candidate fires on a swing filter row whose plan won, on the first session of its
        // record, and the listings' newest night is 125 sessions later, so its first block has closed with
        // its outcome window: one non-empty block, worked by hand.
        var sessions = new List<DateOnly>();

        for (var day = new DateOnly(2025, 1, 2); sessions.Count < 127; day = day.AddDays(1))
        {
            if (EquityBrief.Core.Bars.ExchangeClosures.IsSession(day))
            {
                sessions.Add(day);
            }
        }

        var first = sessions[1].ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var night = sessions[126].ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var shadow = JsonSerializer.Serialize(new { candidates = new[] { new { candidate = SwingFamily.LiveCandidate("2"), fired = true, values = new Dictionary<string, string>() } }, skipped = Array.Empty<object>() });

        store.Execute(
            "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, trigger_pass, trade, swing_stop, swing_target, exclusions, passed, gates, shadow) " +
            $"VALUES ('A', '{first}', '2', 'code', 1, 1, 1, 1, 1, '95', '110', '[]', 1, '{{\"gates\":[],\"notes\":[]}}', '{shadow}');");
        SwingOutcome(store, first, "A", "win");
        store.Execute(
            "INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons, band_strength) " +
            $"VALUES ('A', '{night}', '[]', 0, '{{}}', '{{\"candidates\":[],\"skipped\":[]}}', 0);");
        FixtureExpectations.StoreProposal(store, "2", FilterSettings.Proposed with { StrengthFloor = 0.55 });

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{night}"));

        Assert.Contains("data-accepted-while-live=\"1\" data-blocks=\"1\"", Assert.Single(Blocks(page, "<p class=\"restarts\".*?</p>")), StringComparison.Ordinal);

        // The command holds a later acceptance to the same count: another is refused and it is accepted.
        var (refused, said) = await FixtureExpectations.ShapeVerb(store, new DateTimeOffset(2026, 9, 9, 22, 0, 0, TimeSpan.Zero), "--settings", "strengthFloor=0.55", "--evidence", "a later trigger", "--restarts", "0");

        Assert.Equal(1, refused);
        Assert.Contains($"'{SwingFamily.LiveCandidate("2")}' has run 1 non-empty block(s)", said, StringComparison.Ordinal);
        Assert.Equal(0, (await FixtureExpectations.ShapeVerb(store, new DateTimeOffset(2026, 9, 10, 22, 0, 0, TimeSpan.Zero), "--settings", "strengthFloor=0.55", "--evidence", "a later trigger", "--restarts", "1")).Code);
    }
}
