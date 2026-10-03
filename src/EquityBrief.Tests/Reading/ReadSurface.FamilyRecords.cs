using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;

namespace EquityBrief.Tests.Reading;

// read-surface, 13.9: the run page's records of the registered family rules, each read over its own trades, a
// trade's benchmark read only once its cap's sessions have passed by the night, and a registered family's card
// and row reading the day its rule went live off the register, each read back off the rendered page over a
// constructed store.
// see: A registered family rule is evaluated every night at its own settings and keeps its own list, its trades stored with their benchmark when they end
// see: Each setup family's correction for luck counts its own rules alone, at most nine a family
public partial class ReadSurface
{
    internal static readonly string[] FamilyRecordPageClaims =
    [
        CheckReach.Key("15.10 Run", "The setup families, one row a registered rule"),
        CheckReach.Key("15.10 Run", "The setup families, each setup's live rule first"),
        CheckReach.Key("15.10 Run", "The setup families, the trades its own list kept"),
        CheckReach.Key("15.10 Run", "The setup families, those decided with their edge over their benchmark in multiples of the risk"),
        CheckReach.Key("15.10 Run", "The setup families, its whole blocks against the look they wait for"),
        CheckReach.Key("15.10 Run", "The setup families, the level its looks are read at"),
    ];

    static readonly string LiveBreakout = TheSetupFamilies.Breakouts[0].Candidate;
    static readonly string ProvisionalBreakout = TheSetupFamilies.Breakouts[^1].Candidate;
    static readonly string NeighbourBreakout = TheSetupFamilies.Breakouts[1].Candidate;
    static readonly string LiveDrift = TheSetupFamilies.Drifts[0].Candidate;

    // One registration row, written as the registrar writes it.
    static void RegisteredRule(TemporaryStore store, long id, Registration registration, string at, string? retires = null) =>
        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) VALUES " +
            $"({id}, '{registration.Candidate}', 'rule', 'test', '{registration.Evaluator}', '{CandidateEvaluator.Write(registration.Parameters)}', 'v', " +
            $"'{(retires is null ? CandidateFamily.Registered : CandidateFamily.Retired)}', {(retires is null ? "NULL" : $"'{retires}'")}, '{at}', {(retires is null ? "NULL" : "'constructed'")});");

    // One trade a rule kept, ended or not, with its result and benchmark where written.
    static void Kept(TemporaryStore store, string candidate, string family, string ticker, string session, string? endedOn, double? result, double? benchmark) =>
        store.Execute(
            "INSERT INTO family_trade (candidate, ticker, session_date, family, place, entry, stop, target, risk_moves, reward_to_risk, cap, ended_on, result, benchmark, members) VALUES " +
            $"('{candidate}', '{ticker}', '{session}', '{family}', 1, '100', '97', NULL, 1.5, NULL, 63, " +
            $"{(endedOn is null ? "NULL" : $"'{endedOn}'")}, {Number(result)}, {Number(benchmark)}, {(benchmark is null ? "NULL" : "400")});");

    static string Number(double? value) => value is { } held ? held.ToString(CultureInfo.InvariantCulture) : "NULL";

    [Fact]
    public async Task TheRunPageDrawsEachRegisteredRuleOfANewSetupWithItsOwnRecordAndItsSetupsOwnLevel()
    {
        // The breakouts frozen on 2026-01-02, a Friday the exchange traded: the live rule and the provisional
        // setting's variant standing, and one neighbour registered and retired unread; the drift's live rule
        // registered the same day.
        using var store = await FamilyPagesStore();

        RegisteredRule(store, 101, TheSetupFamilies.Breakouts[0], "2026-01-02T12:00:00Z");
        RegisteredRule(store, 102, TheSetupFamilies.Breakouts[^1], "2026-01-02T12:00:00Z");
        RegisteredRule(store, 103, TheSetupFamilies.Breakouts[1], "2026-01-02T12:00:00Z");
        RegisteredRule(store, 104, TheSetupFamilies.Breakouts[1], "2026-01-03T12:00:00Z", retires: NeighbourBreakout);
        RegisteredRule(store, 105, TheSetupFamilies.Drifts[0], "2026-01-02T12:00:00Z");

        // The live breakout's five trades. The first block of 63 sessions runs from 2026-01-02 to 2026-04-02,
        // and its cap of 63 sessions has passed by the night, so it is whole. T1 made 1.5 risks against a
        // benchmark of 0.5, an edge of 1.0; T2 lost 1.0 against -0.2, an edge of -0.8; T3 ended with no result
        // when its closes ran out; T5 ended with no benchmark written; T4, kept two sessions before the night,
        // is open. Decided: T1 and T2, an edge of 0.2 / 2 = 0.1, in one whole block. A trade that ended after
        // the night, T6 kept by the variant, is read as open on it.
        Kept(store, LiveBreakout, "breakout", "T1", "2026-01-05", "2026-01-20", 1.5, 0.5);
        Kept(store, LiveBreakout, "breakout", "T2", "2026-01-06", "2026-02-10", -1.0, -0.2);
        Kept(store, LiveBreakout, "breakout", "T3", "2026-01-07", "2026-04-08", null, 0.1);
        Kept(store, LiveBreakout, "breakout", "T5", "2026-02-02", "2026-02-20", 2.0, null);
        Kept(store, LiveBreakout, "breakout", "T4", "2026-09-30", null, null, null);

        // The variant's two: one decided, 0.5 against 0.0, in the first block; one ended after the night.
        Kept(store, ProvisionalBreakout, "breakout", "T1", "2026-01-05", "2026-02-02", 0.5, 0.0);
        Kept(store, ProvisionalBreakout, "breakout", "T6", "2026-09-29", "2026-10-05", 1.0, 0.2);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var run = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{TheSwitch}"));
        var records = Assert.Single(Blocks(run, "<table class=\"list-table family-records\".*?</table>"));
        var rows = Regex.Matches(records, "<tr data-family=\"[^\"]+\" data-rule=.*?</tr>", RegexOptions.Singleline).Select(match => match.Value).ToArray();

        // One row a standing rule, the breakouts' live rule first and then its variant, the retired neighbour
        // drawing none, then the drift's. Each setup's level is 0.05 over its own distinct trials: the
        // breakouts' two, the neighbour retired unread counted in none, and the drift's one.
        Assert.Equal(
            [
                $"<tr data-family=\"breakout\" data-rule=\"{LiveBreakout}\" data-live=\"true\" data-trades=\"5\" data-decided=\"2\" data-edge=\"0.1\" data-blocks=\"1\" data-look=\"8\" data-level=\"0.025\">",
                $"<tr data-family=\"breakout\" data-rule=\"{ProvisionalBreakout}\" data-live=\"false\" data-trades=\"2\" data-decided=\"1\" data-edge=\"0.5\" data-blocks=\"1\" data-look=\"8\" data-level=\"0.025\">",
                $"<tr data-family=\"drift\" data-rule=\"{LiveDrift}\" data-live=\"true\" data-trades=\"0\" data-decided=\"0\" data-edge=\"none\" data-blocks=\"0\" data-look=\"8\" data-level=\"0.05\">",
            ],
            rows.Select(row => row[..(row.IndexOf('>') + 1)]));
        Assert.DoesNotContain(NeighbourBreakout, records, StringComparison.Ordinal);

        // The cells a reader reads: the rule named and marked live, the trades, the decided ones, the edge to
        // the thousandth, the whole blocks against the eight the first look reads, and the level.
        Assert.Contains(
            $"<td>{LiveBreakout} <b>live</b></td><td class=\"r num\">5</td><td class=\"r num\">2</td><td class=\"r num\">0.100</td><td class=\"r num\">1 of 8</td><td class=\"r num\">0.025</td>",
            rows[0],
            StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\">none yet</td><td class=\"r num\">0 of 8</td><td class=\"r num\">0.05</td>", rows[2], StringComparison.Ordinal);

        // The same counts the store gives, by the rule's own rows alone.
        Assert.Equal(["5"], Strings(store, $"SELECT COUNT(*) FROM family_trade WHERE candidate = '{LiveBreakout}' AND session_date <= '{TheSwitch}';"));

        // The setups' table: the breakouts live since the day the live rule registered, the one variant standing
        // and the live rule's record in words.
        var setups = Assert.Single(Blocks(run, "<table class=\"list-table family-run\".*?</table>"));

        Assert.Contains("<tr data-family=\"breakout\" data-state=\"live\" data-live-since=\"2026-01-02\" data-variants=\"1\"", setups, StringComparison.Ordinal);
        Assert.Contains("<td class=\"family-record\">2 of 5 trades decided, an edge of 0.100; 1 whole blocks of the 8 its first look reads</td>", setups, StringComparison.Ordinal);
        Assert.Contains("<tr data-family=\"drift\" data-state=\"live\" data-live-since=\"2026-01-02\" data-variants=\"0\"", setups, StringComparison.Ordinal);
        Assert.Contains($"<td class=\"family-record\">no trade kept yet; its first look reads 8 whole blocks of {EquityBrief.Core.Returns.Blocks.Sessions} sessions</td>", setups, StringComparison.Ordinal);

        // A setup's level moves with its own rules alone: a pullback rule registered beside them leaves both.
        RegisteredRule(store, 106, new Registration("the pullback at another setting", "rule", "test", SwingFilterRule.EvaluatorName, SwingFilterRule.ParametersOf(Core.Filter.FilterSettings.Proposed)), "2026-01-04T12:00:00Z");

        var again = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{TheSwitch}"));

        Assert.Equal(
            ["0.025", "0.025", "0.05"],
            Regex.Matches(Assert.Single(Blocks(again, "<table class=\"list-table family-records\".*?</table>")), "data-level=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
    }

    // A trade stopped out before the night whose cap's sessions pass after it: its benchmark, which the recorder
    // writes on the night the cap passes, is not read on the night's page, so the trade is not decided there and
    // draws no edge, and it is decided from the night its cap's last session closes.
    [Fact]
    public async Task ATradeStoppedOutBeforeTheNightIsDecidedOnlyFromTheNightItsCapsSessionsHavePassed()
    {
        using var store = await FamilyPagesStore();

        RegisteredRule(store, 101, TheSetupFamilies.Breakouts[0], "2026-09-25T12:00:00Z");

        // The review's trade: listed on 2026-09-28 and stopped out on 2026-09-30 for a loss of one risk, with the
        // benchmark of 0.2 that the night its cap of 63 sessions passed wrote.
        Kept(store, LiveBreakout, "breakout", "S1", "2026-09-28", "2026-09-30", -1.0, 0.2);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var run = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{TheSwitch}"));
        var records = Assert.Single(Blocks(run, "<table class=\"list-table family-records\".*?</table>"));

        Assert.Contains(
            $"<tr data-family=\"breakout\" data-rule=\"{LiveBreakout}\" data-live=\"true\" data-trades=\"1\" data-decided=\"0\" data-edge=\"none\"",
            records,
            StringComparison.Ordinal);

        // Worked by hand on the exchange's calendar, Thanksgiving and Christmas closed: 2026-12-24 is the 62nd session
        // after the listing and 2026-12-28 the 63rd, so the benchmark is read on the second and not on the first,
        // while the result is read from the night the trade ended.
        static FamilyTradeRow TheTrade(IReadOnlyList<FamilyTradeRow> trades) => Assert.Single(trades, trade => trade.Candidate == LiveBreakout);

        var api = Api(store);
        var ended = TheTrade(await api.FamilyTradesAsync(new DateOnly(2026, 9, 30)));

        Assert.Equal((-1.0, (double?)null), (ended.Result!.Value, ended.Benchmark));
        Assert.Null(TheTrade(await api.FamilyTradesAsync(new DateOnly(2026, 12, 24))).Benchmark);
        Assert.Equal(0.2, TheTrade(await api.FamilyTradesAsync(new DateOnly(2026, 12, 28))).Benchmark);
    }

    [Fact]
    public async Task ARegisteredFamilysCardReadsTheDayItsRuleWentLiveOffTheRegisterAndTheRunPageDrawsNoRecordsBeforeAFreeze()
    {
        using var store = await FamilyPagesStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // Before any freeze the breakouts' card is provisional and the run page draws no table of records.
        Assert.Contains("data-state=\"provisional\" data-live-since=\"none\" data-variants=\"0\"", FamilyCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}")), BreakoutRule.Name), StringComparison.Ordinal);
        Assert.DoesNotContain("family-records", WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{TheSwitch}")), StringComparison.Ordinal);

        // The breakouts' freeze written on 2026-10-01 as the command writes it, the live rule and its seven variants,
        // the one switched on the index among them.
        for (var at = 0; at < TheSetupFamilies.Breakouts.Count; at++)
        {
            RegisteredRule(store, 200 + at, TheSetupFamilies.Breakouts[at], "2026-10-01T21:00:00Z");
        }

        var card = FamilyCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}")), BreakoutRule.Name);

        Assert.Contains("data-state=\"live\" data-live-since=\"2026-10-01\" data-variants=\"7\"", card, StringComparison.Ordinal);
        Assert.DoesNotContain(SetupFamilies.Provisional, card, StringComparison.Ordinal);
        Assert.Equal(8, Regex.Matches(Assert.Single(Blocks(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{TheSwitch}")), "<table class=\"list-table family-records\".*?</table>")), "<tr data-family=\"breakout\"").Count);

        // A freeze written after the night's own end is not read on it: registered a second into the day after,
        // the card stays provisional.
        using var later = await FamilyPagesStore();

        RegisteredRule(later, 300, TheSetupFamilies.Breakouts[0], "2026-10-03T00:00:01Z");

        using var laterHost = new Host(later.Root);
        using var laterClient = laterHost.CreateClient();

        Assert.Contains("data-state=\"provisional\"", FamilyCardOf(WebUtility.HtmlDecode(await laterClient.GetStringAsync($"/screens/tonight/{TheSwitch}")), BreakoutRule.Name), StringComparison.Ordinal);
    }
}
