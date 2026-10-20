using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Sweep;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Indices;

namespace EquityBrief.Tests.Reading;

// read-surface, 20.11: a family frozen on the S&P 400 on a sweep that passed none keeps its line naming the years the
// setting frozen there lost, and its card states the figures of the sweep its freeze followed with the sentence that no
// setting beat luck or the tester's bar; the rule of the day before a freeze, kept in the background, is on no card's tabs
// and in no account and named on the Run page; a night run again after a freeze is read by the register it read; each
// family's own market check is read off the parts the night stored; and the chips, the words and Past picks name each
// rule's own check, the drift's entry and revenue and the families frozen.
// see: A frozen card on the S&P 400 or 600 states its sweep's figures and that no setting beat luck or the tester's bar
// see: A freeze on the S&P 400 or 600 registers a market variant beside each swing rule and keeps the rule it replaces in the background for 63 sessions
// see: A night run again for the newest session reads the register as it stands when it runs where the operator says so, and its page reads the rules that run read
// see: Each frozen rule on the S&P 400 and 600 reads its market check at the level its own sweep chose
public partial class ReadSurface
{
    // A recorded answer with the figures its run stated, as the sweep-answer command writes them.
    static void RecordFigured(TemporaryStore store, string run, string family, string? design, string answer, string recordedAt, AnswerFigures figures)
    {
        static string Real(double? value) => value is { } held ? held.ToString("R", CultureInfo.InvariantCulture) : "NULL";

        static string Whole(int? value) => value is { } held ? held.ToString(CultureInfo.InvariantCulture) : "NULL";

        var years = "{" + string.Join(",", figures.YearEdges.OrderBy(year => year.Key).Select(year => $"\"{year.Key.ToString(CultureInfo.InvariantCulture)}\":{(year.Value is { } edge ? edge.ToString("R", CultureInfo.InvariantCulture) : "null")}")) + "}";

        store.Execute(
            "INSERT INTO sweep_answer (run, index_code, family, design, answer, recorded_at, setting, trades, edge, year_edges, without_largest, settings_read, meeting, luck, tester_run, adjusted, tester_passed) VALUES " +
            $"('{run}', 'MID', '{family}', {(design is null ? "NULL" : $"'{design}'")}, '{answer}', '{recordedAt}', '{figures.Setting}', {figures.Trades}, {Real(figures.Edge)}, '{years}', {Real(figures.WithoutLargest)}, " +
            $"{Whole(figures.SettingsRead)}, {Whole(figures.Meeting)}, {Real(figures.Luck)}, {(figures.TesterRun is { } tester ? $"'{tester}'" : "NULL")}, {Real(figures.Adjusted)}, {(figures.TesterPassed is { } passed ? (passed ? "1" : "0") : "NULL")});");
    }

    // The year edges of a run over 2019 to 2026, in order.
    static Dictionary<int, double?> YearsOf(params double?[] edges) =>
        edges.Select((edge, at) => (Year: 2019 + at, Edge: edge)).ToDictionary(one => one.Year, one => one.Edge);

    // The S&P 400 breakout's sweep of 20.11 as its run stated it, passing none: the strongest setting it read, its trades,
    // its edge after costs, its eight years, four of them above nothing, 2025 and 2026 below it, its edge without its five
    // largest, 0 of its 378 settings meeting the floors where luck passes 54.6, and the tester's adjusted p of 0.1798 on the
    // family's search, not passing.
    static readonly AnswerFigures MidBreakout = new(
        "high=251|volume=1.5|ceiling=1|stop=3|market=5",
        1033,
        0.023,
        YearsOf(0.091, 0.054, -0.012, 0.033, -0.045, 0.078, -0.071, -0.096),
        -0.010,
        378,
        0,
        54.6,
        "20261020T062626Z",
        0.1798,
        false);

    // The frozen card's figures line those figures give, worked by hand: the setting read where none passed, 1,033 trades at
    // +0.023 risks and -0.010 without its five largest, 0.091, 0.054, 0.033 and 0.078 above nothing of the eight, 2025 and
    // 2026 in the loss's red, then the count against luck and the tester's p against the bar of 0.05 over 12, 0.0042 to four
    // places.
    const string MidBreakoutFrozen =
        "<p class=\"family-frozen\" data-frozen=\"2026-10-02\" data-run=\"20261001T120000Z\" data-answer=\"none passed\" data-setting=\"high=251|volume=1.5|ceiling=1|stop=3|market=5\" " +
        "data-trades=\"1033\" data-edge=\"0.023\" data-years=\"2019=0.091 2020=0.054 2021=-0.012 2022=0.033 2023=-0.045 2024=0.078 2025=-0.071 2026=-0.096\" data-without-largest=\"-0.01\" " +
        "data-settings-read=\"378\" data-meeting=\"0\" data-luck=\"54.6\" data-tester-run=\"20261020T062626Z\" data-adjusted=\"0.1798\" data-tester-passed=\"false\" data-beat-luck=\"false\">" +
        "Frozen on 2026-10-02 at the strongest setting its sweep read, <span class=\"frozen-setting\">high=251|volume=1.5|ceiling=1|stop=3|market=5</span>: " +
        "1,033 trades at an edge after costs of <span class=\"gain\">+0.023</span> risks, <span class=\"loss\">-0.010</span> without its five largest results, above nothing in 4 of the 8 years it stored, 2025 at <span class=\"loss\">-0.071</span> and 2026 at <span class=\"loss\">-0.096</span>. " +
        "No setting it read beat luck or the tester's bar: 0 of the 378 settings it read met the floors where luck alone passes about 54.6, and the tester read its search at an adjusted p of 0.1798 against its bar of 0.0042, " +
        "so this rule is a starting point and not a proven edge.</p>";

    const string KeptLine = "<p class=\"family-sweep\" data-sweep=\"none passed\" data-lost=\"2025 2026\">Its sweep found no setting that passed the floors, and the setting frozen here lost in 2025 and 2026</p>";

    [Fact]
    public async Task AnSAndP400BreakoutFrozenOnASweepThatPassedNoneKeepsItsLineNamingTheYearsItLostAndStatesItsSweepsFigures()
    {
        using var store = UniversesStore();

        // The sweep's answer recorded the day before the night, and the breakout frozen on the night at noon, the night
        // reading its list by the live rule.
        RecordFigured(store, "20261001T120000Z", "breakout", null, "none passed", "2026-10-01T12:00:00Z", MidBreakout);

        var live = FreezeTheBreakout(store, "2026-10-02T12:00:00Z");

        ReadByTheLiveBreakout(store, live);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<string> Card() => FamilyCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400")), BreakoutRule.Name);

        // The line kept beneath its standing, naming 2025 and 2026, the figures line beneath it word for word, and M1 still
        // listed.
        var card = await Card();

        Assert.Contains("</details></header>" + KeptLine + MidBreakoutFrozen, card, StringComparison.Ordinal);
        Assert.Equal(["M1"], CardPicksOf(card));

        // The family frozen again on the night an hour later, the S&P 500's breadth in the first rule's place and the night
        // reading its list by the new rule: two freezes stand at or after the answer, so the line goes; the new rule's freeze
        // followed the same answer, so its figures line stands, frozen the same day.
        var breakout = (IndexRuleCandidate)CandidateEvaluators.Find("breakout-400")!;
        var (again, refusal) = IndexRules.Freeze("breakout", "MID", new Dictionary<string, double>(IndexRules.Provisional(breakout)) { [IndexRuleCandidate.LargeBreadthParameter] = 1 }, [], [], new DateTimeOffset(2026, 10, 2, 13, 0, 0, TimeSpan.Zero));

        Assert.Null(refusal);
        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) VALUES " +
            $"(250, '{Doubled(live)}', 'rule', 'test', 'breakout-400', '{{}}', 'v', 'retired', '{Doubled(live)}', '2026-10-02T13:00:00Z', 'constructed');");
        RegisteredRule(store, 251, again![0], "2026-10-02T13:00:00Z");
        ReadByTheLiveBreakout(store, again[0].Candidate);

        var refrozen = await Card();

        Assert.DoesNotContain("family-sweep", refrozen, StringComparison.Ordinal);
        Assert.Contains("</details></header>" + MidBreakoutFrozen, refrozen, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnSAndP400HeavyweightsFrozenOnItsDesignsAnswerStatesItsFiguresInPoints()
    {
        using var store = UniversesStore();

        // Design (a)'s sweep passed, its run stating 335 holdings at 0.0217 of the buy against the size cut, six years of
        // the eight above nothing, 53 of its 648 settings meeting the floors where luck passes 93.7 and the tester's p of
        // 0.261; design (b)'s passed an hour later with no figures. The family frozen at design (a) on the night and read by it.
        RecordFigured(
            store,
            "20261001T120000Z",
            "heavyweight",
            "a",
            "passed",
            "2026-10-01T12:00:00Z",
            new AnswerFigures("size=10|look-back=251|leaders=2|beta=on|exit=drop", 335, 0.0217, YearsOf(0.03, 0.02, -0.01, 0.04, 0.01, 0.05, 0.0911, -0.0281), -0.0008, 648, 53, 93.7, "20261020T062626Z", 0.261, false));
        RecordAnswer(store, "20261001T130000Z", "MID", "heavyweight", "b", "passed", "2026-10-01T13:00:00Z");
        FreezeTheHeavyweights(store, readByIt: true);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var card = HeavyweightCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400")));

        // By hand: the setting its sweep would run, 335 holdings at 2.17 points and -0.08 without its five largest, 0.03,
        // 0.02, 0.04, 0.01, 0.05 and 0.0911 above nothing, six of the eight, 2025 at 9.11 points and 2026 at -2.81, then the
        // count against luck and the p to four places. No line: both designs passed.
        Assert.Equal(
            "<p class=\"family-frozen\" data-frozen=\"2026-10-02\" data-run=\"20261001T120000Z\" data-answer=\"passed\" data-setting=\"size=10|look-back=251|leaders=2|beta=on|exit=drop\" " +
            "data-trades=\"335\" data-edge=\"0.0217\" data-years=\"2019=0.03 2020=0.02 2021=-0.01 2022=0.04 2023=0.01 2024=0.05 2025=0.0911 2026=-0.0281\" data-without-largest=\"-0.0008\" " +
            "data-settings-read=\"648\" data-meeting=\"53\" data-luck=\"93.7\" data-tester-run=\"20261020T062626Z\" data-adjusted=\"0.261\" data-tester-passed=\"false\" data-beat-luck=\"false\">" +
            "Frozen on 2026-10-02 at the setting its sweep would run, <span class=\"frozen-setting\">size=10|look-back=251|leaders=2|beta=on|exit=drop</span>: " +
            "335 holdings at an edge after costs of <span class=\"gain\">+2.17</span> points, <span class=\"loss\">-0.08</span> without its five largest results, above nothing in 6 of the 8 years it stored, 2025 at <span class=\"gain\">+9.11</span> and 2026 at <span class=\"loss\">-2.81</span>. " +
            "No setting it read beat luck or the tester's bar: 53 of the 648 settings it read met the floors where luck alone passes about 93.7, and the tester read its search at an adjusted p of 0.2610 against its bar of 0.0042, " +
            "so this rule is a starting point and not a proven edge.</p>",
            Assert.Single(Blocks(card, "<p class=\"family-frozen\".*?</p>")));
        Assert.Contains("</details></header><p class=\"family-frozen\"", card, StringComparison.Ordinal);
        Assert.DoesNotContain("family-sweep", card, StringComparison.Ordinal);
    }

    [Fact]
    public void AFrozenCardsSentenceSaysOnlyWhatItsSweepStoredOfLuckAndTheTester()
    {
        // The S&P 400 pullback's search as its run stated it: passed, no count of settings meeting the floors, the tester's
        // p of 0.6552 not passing, its last two years above nothing.
        var pullback = new AnswerFigures("strength=0.75|depth=1-3", 717, 0.19, YearsOf(0.1, 0.2, -0.1, 0.3, 0.05, -0.02, 0.424, 0.093), 0.148, null, null, null, "20261020T062626Z", 0.6552, false);

        static string Frozen(AnswerFigures figures, string answer = "passed", bool variant = false)
        {
            var live = new RuleChoiceView("the live rule", RuleScreenWords.LiveSlug, null, true, "Live");
            var other = new RuleChoiceView("a variant", "v1", 1, false, "V1");
            var rule = new RuleView(variant ? other : live, [live, other], [], variant ? Array.Empty<VariantPickView>() : null, null, null, null, true, null);
            var card = new MarkRenderer().FamilyCard(new FamilyCardView("pullback", "Pullback", "Pullbacks to support", "Pullback in an uptrend", "A rule.", 1, 4, new DateOnly(2026, 10, 21), 1, [], [], "No stock passed this setup tonight.", RuleChoice: rule, Frozen: new FrozenSweepView(new DateOnly(2026, 10, 21), "20261020T073338Z", answer, figures, false)));

            return Regex.Match(card, "<p class=\"family-frozen\".*?</p>").Value;
        }

        // The tester alone, the run stating no count: its figures, six of the eight years above nothing, and the tester's p.
        var tested = Frozen(pullback);

        Assert.Equal(
            ">Frozen on 2026-10-21 at the setting its sweep would run, <span class=\"frozen-setting\">strength=0.75|depth=1-3</span>: 717 trades at an edge after costs of <span class=\"gain\">+0.190</span> risks, " +
            "<span class=\"gain\">+0.148</span> without its five largest results, " +
            "above nothing in 6 of the 8 years it stored, 2025 at <span class=\"gain\">+0.424</span> and 2026 at <span class=\"gain\">+0.093</span>. " +
            "No setting it read beat the tester's bar: the tester read its search at an adjusted p of 0.6552 against its bar of 0.0042, so this rule is a starting point and not a proven edge.</p>",
            tested[tested.IndexOf(">Frozen on", StringComparison.Ordinal)..]);
        Assert.Contains("data-settings-read=\"none\" data-meeting=\"none\" data-luck=\"none\"", Frozen(pullback), StringComparison.Ordinal);

        // Luck alone, no tester stored: 0 of 567 against 81.9.
        Assert.EndsWith(
            ". No setting it read beat luck: 0 of the 567 settings it read met the floors where luck alone passes about 81.9, so this rule is a starting point and not a proven edge.</p>",
            Frozen(pullback with { SettingsRead = 567, Meeting = 0, Luck = 81.9, TesterRun = null, Adjusted = null, TesterPassed = null }),
            StringComparison.Ordinal);

        // A count above luck, a tester that passed, or neither stored: no sentence, the figures standing alone.
        Assert.EndsWith("2026 at <span class=\"gain\">+0.093</span>.</p>", Frozen(pullback with { SettingsRead = 648, Meeting = 100, Luck = 93.7 }), StringComparison.Ordinal);
        Assert.EndsWith("2026 at <span class=\"gain\">+0.093</span>.</p>", Frozen(pullback with { TesterPassed = true }), StringComparison.Ordinal);
        Assert.EndsWith("2026 at <span class=\"gain\">+0.093</span>.</p>", Frozen(pullback with { TesterRun = null, Adjusted = null, TesterPassed = null }), StringComparison.Ordinal);

        // A tester that read too few blocks says so, and a variant chosen on the card draws no figures line.
        Assert.Contains("the tester read too few blocks of its search to pass it, so this rule", Frozen(pullback with { Adjusted = null }), StringComparison.Ordinal);
        Assert.Equal(string.Empty, Frozen(pullback, variant: true));
    }

    [Fact]
    public async Task TheRuleBeforeAFreezeIsOnNoCardsTabsAndInNoAccountAndTheRunPageNamesIt()
    {
        using var store = UniversesStore();

        // The S&P 400 breakout frozen on the S&P 500's breadth with its market variant, the check off: the freeze keeps the
        // rule of today, the provisional rule, in the background, three registrations in all.
        var breakout = (IndexRuleCandidate)CandidateEvaluators.Find("breakout-400")!;
        var (registrations, refusal) = IndexRules.Freeze(
            "breakout",
            "MID",
            new Dictionary<string, double>(IndexRules.Provisional(breakout)) { [IndexRuleCandidate.LargeBreadthParameter] = 1 },
            [new Dictionary<string, double> { [IndexRuleCandidate.LargeBreadthParameter] = IndexRuleCandidate.NoMarketCheck, [IndexRuleCandidate.BreadthFloorParameter] = 0 }],
            [],
            new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));

        Assert.Null(refusal);
        Assert.Equal(3, registrations!.Count);

        var background = registrations[2].Candidate;

        Assert.StartsWith(FamilyRecords.BackgroundPrefix, background, StringComparison.Ordinal);

        foreach (var (registration, place) in registrations.Select((one, place) => (one, place)))
        {
            RegisteredRule(store, 260 + place, registration, "2026-10-02T12:00:00Z");
        }

        ReadByTheLiveBreakout(store, registrations[0].Candidate);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // Tonight: the live rule and its one variant on the tabs, the variant counted alone, the rule before the freeze on
        // no tab and nowhere on the page, and no line saying no variant is registered.
        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));
        var card = FamilyCardOf(page, BreakoutRule.Name);

        Assert.Contains("data-state=\"live\" data-live-since=\"2026-10-02\" data-variants=\"1\"", card, StringComparison.Ordinal);
        Assert.Equal(["Live", "V1 · market check off"], Regex.Matches(card, "<a class=\"rule-tab[^>]*>([^<]*)</a>").Select(match => match.Groups[1].Value));
        Assert.DoesNotContain(background, page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-no-variant", card, StringComparison.Ordinal);

        // Paper trading: the live rule's account and the variant's, none for the rule before the freeze.
        var paper = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper?universe=400"));

        Assert.Contains(registrations[0].Candidate, paper, StringComparison.Ordinal);
        Assert.Contains(registrations[1].Candidate, paper, StringComparison.Ordinal);
        Assert.DoesNotContain(background, paper, StringComparison.Ordinal);

        // The Run page: its record's row named as the rule before the freeze kept 63 sessions beside it, read by no test;
        // the family's setup row counting the one variant.
        var run = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{IndexNight}?universe=400"));
        var records = Assert.Single(Blocks(run, "<table class=\"list-table family-records\".*?</table>"));
        var row = Assert.Single(Blocks(records, $"<tr data-family=\"breakout\" data-rule=\"{Regex.Escape(background)}\".*?</tr>"));

        Assert.Equal("The rule before the freeze, kept 63 sessions beside it", RunScreen.BeforeTheFreeze);
        Assert.Contains($"<span class=\"fam-tag\" data-family=\"breakout\">{RunScreen.BeforeTheFreeze}</span>", row, StringComparison.Ordinal);
        Assert.Contains("data-live=\"false\"", row, StringComparison.Ordinal);
        Assert.Contains("<td class=\"degraded\">not read</td>", row, StringComparison.Ordinal);

        // Read by no test, it is no trial of the family's: the live rule and its variant are the two, each read at 5 per
        // cent over two, 0.025, and the family's heading row states the one level its rows share.
        var two = (ReasonVerdict.Significance / 2).ToString("0.#####", CultureInfo.InvariantCulture);

        Assert.Equal("0.025", two);
        Assert.Contains($"data-level=\"{two}\"", Assert.Single(Blocks(records, $"<tr data-family=\"breakout\" data-rule=\"{Regex.Escape(registrations[0].Candidate)}\".*?</tr>")), StringComparison.Ordinal);
        Assert.Contains($"data-level=\"{two}\"", row, StringComparison.Ordinal);
        Assert.Contains($"<tr class=\"family-head\" data-family=\"breakout\" data-level=\"{two}\">", records, StringComparison.Ordinal);
        Assert.Contains("<tr data-family=\"breakout\" data-state=\"live\" data-live-since=\"2026-10-02\" data-variants=\"1\"", Assert.Single(Blocks(run, "<table class=\"list-table family-run\".*?</table>")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFrozenFamilyWithNoVariantDrawsNoLineSayingNoVariantIsRegisteredBeforeItsFreeze()
    {
        using var store = UniversesStore();

        // The breakout frozen on the S&P 500's breadth alone, the rule of today kept in the background: no variant counted
        // and no line, where the provisional drift's card beside it draws the line.
        var breakout = (IndexRuleCandidate)CandidateEvaluators.Find("breakout-400")!;
        var (registrations, refusal) = IndexRules.Freeze("breakout", "MID", new Dictionary<string, double>(IndexRules.Provisional(breakout)) { [IndexRuleCandidate.LargeBreadthParameter] = 1 }, [], [], new DateTimeOffset(2026, 10, 2, 12, 0, 0, TimeSpan.Zero));

        Assert.Null(refusal);
        Assert.Equal(2, registrations!.Count);

        foreach (var (registration, place) in registrations.Select((one, place) => (one, place)))
        {
            RegisteredRule(store, 270 + place, registration, "2026-10-02T12:00:00Z");
        }

        ReadByTheLiveBreakout(store, registrations[0].Candidate);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));
        var card = FamilyCardOf(page, BreakoutRule.Name);

        Assert.Contains("data-state=\"live\" data-live-since=\"2026-10-02\" data-variants=\"0\"", card, StringComparison.Ordinal);
        Assert.Equal(["Live"], Regex.Matches(card, "<a class=\"rule-tab[^>]*>([^<]*)</a>").Select(match => match.Groups[1].Value));
        Assert.DoesNotContain("data-no-variant", card, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"no-variant\" data-no-variant=\"true\">{RuleScreen.NoVariantLine("S&P 400")}</p>", FamilyCardOf(page, DriftRule.Name), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANightRunAgainAfterAFreezeIsReadOnItsPageByTheRegisterItRead()
    {
        // The S&P 400 breakout and sector heavyweights frozen on 2026-10-03 at 11:00, after the night of 2026-10-02 was past,
        // the breakout's sweep's none recorded at 10:00 that morning, and the night's lists read by the live rules; the
        // night's settings stating the instant it read the register at, where given.
        async Task<(string Breakout, string Heavyweights)> CardsReadAt(string? registerAt)
        {
            using var store = UniversesStore();

            RecordAnswer(store, "20261003T100000Z", "MID", "breakout", null, "none passed", "2026-10-03T10:00:00Z");

            var live = FreezeTheBreakout(store, "2026-10-03T11:00:00Z");

            FreezeTheHeavyweights(store, readByIt: false, at: "2026-10-03T11:00:00Z");
            store.Execute($"UPDATE index_family_night SET settings = json_set(settings, '$.live', json_array(json_object('family', 'breakout', 'candidate', '{Doubled(live)}'), json_object('family', 'heavyweight', 'candidate', 'the heavyweights'))) WHERE index_code = 'MID';");

            if (registerAt is not null)
            {
                store.Execute($"UPDATE index_family_night SET settings = json_set(settings, '$.registerAt', '{registerAt}') WHERE index_code = 'MID';");
            }

            // A retirement written after the night read the register, which its page reads none of.
            store.Execute(
                "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) VALUES " +
                $"(280, '{Doubled(live)}', 'rule', 'test', 'breakout-400', '{{}}', 'v', 'retired', '{Doubled(live)}', '2026-10-03T12:00:00Z', 'constructed');");

            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));

            return (FamilyCardOf(page, BreakoutRule.Name), HeavyweightCardOf(page));
        }

        // A night stating no instant, or one before midnight, is read past its evening: the freezes and the answer of the
        // morning after are read on no card of it, the breakout's card the provisional rule's and the heavyweights' the
        // index's own book, holding M2.
        foreach (var before in new string?[] { null, "2026-10-02T22:45:00Z" })
        {
            var (provisional, book) = await CardsReadAt(before);

            Assert.Contains("<span class=\"status-pill provisional\"", provisional, StringComparison.Ordinal);
            Assert.DoesNotContain("family-sweep", provisional, StringComparison.Ordinal);
            Assert.Contains("data-state=\"provisional\"", book, StringComparison.Ordinal);
            Assert.Contains("data-ticker=\"M2\"", book, StringComparison.Ordinal);
        }

        // The night run again at 11:05 read the register as it stood then: the breakout's card is the live rule's, live since
        // the day of its freeze, the answer of 10:00 read and kept at the freeze made on it, and the retirement at 12:00 read
        // on none of it; the heavyweights' card the live rule's own book, holding M1, live since that day.
        var (frozen, frozenBook) = await CardsReadAt("2026-10-03T11:05:00Z");

        Assert.Contains("<span class=\"status-pill live\" title=\"live since 2026-10-03\">Live</span>", frozen, StringComparison.Ordinal);
        Assert.Contains("</details></header>" + SweepLineDrawn, frozen, StringComparison.Ordinal);
        Assert.Contains("data-state=\"live\" data-live-since=\"2026-10-03\"", frozenBook, StringComparison.Ordinal);
        Assert.Contains("data-ticker=\"M1\"", frozenBook, StringComparison.Ordinal);

        // The instant the page reads as of, worked by hand: the second after the night's own where that is past midnight, and
        // midnight otherwise.
        IndexNightRow Night(string? at) => new("MID", new DateOnly(2026, 10, 2), 3, 0.52, true, at is null ? IndexFamilies.Settings("MID") : IndexFamilies.Settings("MID", null, null, DateTimeOffset.Parse(at, CultureInfo.InvariantCulture)), false);

        Assert.Equal(new DateTimeOffset(2026, 10, 3, 11, 5, 1, TimeSpan.Zero), TonightScreen.ReadAsOf(Night("2026-10-03T11:05:00Z"), new DateOnly(2026, 10, 2)));
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero), TonightScreen.ReadAsOf(Night("2026-10-02T22:45:00Z"), new DateOnly(2026, 10, 2)));
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero), TonightScreen.ReadAsOf(Night(null), new DateOnly(2026, 10, 2)));
        Assert.Equal(new DateTimeOffset(2026, 10, 3, 0, 0, 0, TimeSpan.Zero), TonightScreen.ReadAsOf(null, new DateOnly(2026, 10, 2)));
    }

    // Every member's answer under a family stored with the parts given, the market check first, as the night stores them
    // from 20.11.
    static void StoredChecks(TemporaryStore store, string family, Gate market) =>
        store.Execute($"UPDATE index_family_result SET gates = '{FamilyRule.GatesJson([market, new Gate(RuleRows.Setup, false, "no setup", FamilyRule.Values())]).Replace("'", "''", StringComparison.Ordinal)}' WHERE index_code = 'MID' AND family = '{family}';");

    static Gate Check(bool passed, string words, double breadth, double floor) =>
        new(RuleRows.Market, passed, words, FamilyRule.Values(("breadth", FamilyRule.Figure(breadth)), ("floor", FamilyRule.Figure(floor))));

    [Fact]
    public async Task EachFamilysOwnMarketCheckIsReadOffThePartsTheNightStoredOnTheLineTheCardsAndTheRunPage()
    {
        // The S&P 400's night at a breadth of 42 per cent, under the provisional floor of 45, its pullback with no stock held
        // back. Stored before the parts: the index's own check closed every list.
        using var store = UniversesStore();

        store.Execute("UPDATE index_family_night SET breadth = 0.42, market_open = 0 WHERE index_code = 'MID';");
        store.Execute("DELETE FROM index_family_pick WHERE index_code = 'MID' AND family = 'pullback';");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<(string Line, string Pullback, string Drift, string Run, IReadOnlyList<string> Tiles)> Read()
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));
            var run = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{IndexNight}?universe=400"));
            var tiles = Regex.Matches(Assert.Single(Blocks(page, "<nav class=\"setup-tiles\".*?</nav>")), "data-family=\"([^\"]+)\" data-card=\"[^\"]+\" data-count=\"[0-9]+\" data-state=\"([^\"]+)\"").Select(match => $"{match.Groups[1].Value}: {match.Groups[2].Value}");

            return (Assert.Single(Blocks(page, "<p class=\"market-line\".*?</p>")), FamilyCardOf(page, SetupFamilies.Pullback), FamilyCardOf(page, DriftRule.Name), Assert.Single(Blocks(run, "<ul class=\"index-night\".*?</ul>")), [.. tiles]);
        }

        var before = await Read();

        Assert.Contains("data-open=\"false\" data-breadth=\"0.42\" data-floor=\"0.45\"", before.Line, StringComparison.Ordinal);
        Assert.Contains("<b class=\"market-closed\">The lists are closed</b>: the S&P 400's breadth: 42.0% of its members closed above their 200-day average, below its floor of 45%.", before.Line, StringComparison.Ordinal);
        Assert.Contains("<span class=\"family-empty-line\" data-picks=\"0\" data-flagged=\"false\">nothing tonight · the market check closed</span>", before.Pullback, StringComparison.Ordinal);
        Assert.Equal(["pullback: the market check closed", "breakout: listing tonight", "drift: the market check closed", "fundamentals: the market check closed", "heavyweight: rebalanced tonight"], before.Tiles);

        // From 20.11 each family's own check stored: the pullback's on the index's own breadth at 35 per cent and the
        // breakout's on the S&P 500's at 45 open, the drift's at 45 closed. Read alike for none, the line names the drift's
        // list and its check's words, the lists open; the pullback's card says no stock passed and not that the check closed
        // it; the drift's that the check closed; and the Run page names the drift's list.
        StoredChecks(store, SetupFamilies.Pullback, Check(true, "the index's own breadth 42.0% at or above its floor of 35%", 0.42, 0.35));
        StoredChecks(store, BreakoutRule.Name, Check(true, "the S&P 500's breadth 55.0% at or above its floor of 45%", 0.55, 0.45));
        StoredChecks(store, DriftRule.Name, Check(false, "the index's own breadth 42.0% below its floor of 45%", 0.42, 0.45));

        var mixed = await Read();

        Assert.StartsWith("<p class=\"market-line\" data-index=\"S&P 400\" data-open=\"true\" data-breadth=\"0.42\" data-floor=\"none\" data-passed=\"2\" data-members=\"3\" data-buy-points=\"1\" data-setups-listing=\"1\" data-setups=\"4\" data-close=\"0\" data-open-trades=\"0\" data-closed=\"drift\">", mixed.Line, StringComparison.Ordinal);
        Assert.Contains("<b class=\"market-open\">The lists are open</b>: the S&P 400's breadth: 42.0% of its members closed above their 200-day average; the market check closed the earnings drift list, the index's own breadth 42.0% below its floor of 45%, and left the others open.", mixed.Line, StringComparison.Ordinal);
        Assert.Contains("<span class=\"family-empty-line\" data-picks=\"0\" data-flagged=\"false\">nothing tonight</span>", mixed.Pullback, StringComparison.Ordinal);
        Assert.Contains("<p class=\"degraded family-empty\" data-picks=\"0\">No stock passed this setup tonight: of the 3 S&P 400 members read, 2 no setup.</p>", mixed.Pullback, StringComparison.Ordinal);
        Assert.Contains("<span class=\"family-empty-line\" data-picks=\"0\" data-flagged=\"false\">nothing tonight · the market check closed</span>", mixed.Drift, StringComparison.Ordinal);
        Assert.Contains("<li class=\"index-checks\" data-closed=\"drift\">the S&P 400's breadth: 42.0% of its members above their 200-day average; the market check closed the earnings drift list, the index's own breadth 42.0% below its floor of 45%, and left the others open</li>", mixed.Run, StringComparison.Ordinal);

        // Tonight's tiles by each family's own check: the drift's closed, the pullback's open with nothing listed, and the
        // fundamentals-first family's, whose night stored no answer, read by the lists' state, open.
        Assert.Equal(["pullback: nothing tonight", "breakout: listing tonight", "drift: the market check closed", "fundamentals: nothing tonight", "heavyweight: rebalanced tonight"], mixed.Tiles);

        // Every family's check read alike, the index's own breadth at the floor the night stored, closed: the lines as before
        // the parts were stored.
        foreach (var family in new[] { SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name })
        {
            StoredChecks(store, family, Check(false, "the index's own breadth 42.0% below its floor of 45%", 0.42, 0.45));
        }

        var one = await Read();

        Assert.Contains("data-open=\"false\" data-breadth=\"0.42\" data-floor=\"0.45\"", one.Line, StringComparison.Ordinal);
        Assert.DoesNotContain("data-closed", one.Line, StringComparison.Ordinal);
        Assert.Contains("<b class=\"market-closed\">The lists are closed</b>: the S&P 400's breadth: 42.0% of its members closed above their 200-day average, below its floor of 45%.", one.Line, StringComparison.Ordinal);
        Assert.Contains("<span class=\"family-empty-line\" data-picks=\"0\" data-flagged=\"false\">nothing tonight · the market check closed</span>", one.Pullback, StringComparison.Ordinal);
        Assert.Contains("<li>the S&P 400's breadth: 42.0% of its members above their 200-day average, below its floor of 45%, so its swing lists were closed</li>", one.Run, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMarketChipNamesTheFloorItsRuleReadsAndTheDriftsChipsItsEntryAndItsRevenue()
    {
        static string[] Chips(RuleKind kind, IReadOnlyDictionary<string, double> p) => [.. RuleChips.Of(kind, p).Select(chip => chip.Words)];

        var drift = (IndexRuleCandidate)CandidateEvaluators.Find("drift-400")!;
        var pullback = (IndexRuleCandidate)CandidateEvaluators.Find("pullback-400")!;
        var breakout = (IndexRuleCandidate)CandidateEvaluators.Find("breakout-400")!;

        // The S&P 400 drift frozen as the ruling reads it: a 10-session window, its stop at least a move, bought on its first
        // lower close, its check on its own breadth at 45 per cent.
        var midDrift = new Dictionary<string, double>(IndexRules.Provisional(drift))
        {
            [IndexDriftCandidate.WindowSessionsParameter] = 10,
            [IndexDriftCandidate.ReactionMovesParameter] = 0.5,
            [IndexDriftCandidate.VolumeMultipleParameter] = 1.25,
            [IndexDriftCandidate.TargetMultipleParameter] = 3,
            [IndexDriftCandidate.StopFloorParameter] = 1,
            [IndexDriftCandidate.EntryParameter] = 1,
        };

        Assert.Equal(
            ["within 10 sessions", "up 0.5 moves", "volume 1.25 times", "target 3 times the risk", "stop at least 1 move", "bought on its first lower close", "dollar floor 1 times", "close at least $5", "the profit gate", "market check on its own breadth at 45%"],
            Chips(RuleKind.IndexDrift, midDrift));

        // The S&P 600 drift held 63 sessions with its revenue level and no check, and its market variant at 40 per cent,
        // whose tab names the one chip it differs on.
        var smallDrift = new Dictionary<string, double>(midDrift)
        {
            [IndexDriftCandidate.EntryParameter] = 0,
            [IndexDriftCandidate.RevenueGrewParameter] = 1,
            [IndexRuleCandidate.HoldParameter] = 63,
            [IndexRuleCandidate.LargeBreadthParameter] = IndexRuleCandidate.NoMarketCheck,
            [IndexRuleCandidate.BreadthFloorParameter] = 0,
        };
        var smallVariant = new Dictionary<string, double>(smallDrift) { [IndexRuleCandidate.LargeBreadthParameter] = 0, [IndexRuleCandidate.BreadthFloorParameter] = 0.40 };

        Assert.Equal(
            ["within 10 sessions", "up 0.5 moves", "volume 1.25 times", "target 3 times the risk", "stop at least 1 move", "revenue above a year before", "dollar floor 1 times", "close at least $5", "the profit gate", "held 63 sessions", IndexRuleCandidate.MarketCheckOff],
            Chips(RuleKind.IndexDrift, smallDrift));
        Assert.Equal("V1 · market check on its own breadth at 40%", RuleChips.TabWords(1, RuleChips.Differing(RuleChips.Of(RuleKind.IndexDrift, smallDrift), RuleChips.Of(RuleKind.IndexDrift, smallVariant))));

        // The S&P 400 pullback with its own dial off and its check at 35 per cent, and the dial on at the floor said once;
        // the S&P 400 breakout on the S&P 500's breadth at its own floor of 45.
        var midPullback = new Dictionary<string, double>(IndexRules.Provisional(pullback)) { [IndexPullbackCandidate.MarketParameter] = 0, [IndexRuleCandidate.BreadthFloorParameter] = 0.35 };

        Assert.Equal("market check on its own breadth at 35%, its own dial off", Chips(RuleKind.IndexPullback, midPullback)[^1]);
        Assert.Equal("market check on its own breadth at 45%", Chips(RuleKind.IndexPullback, IndexRules.Provisional(pullback))[^1]);
        Assert.Equal("market check on the S&P 500's breadth at 45%", Chips(RuleKind.IndexBreakout, new Dictionary<string, double>(IndexRules.Provisional(breakout)) { [IndexRuleCandidate.LargeBreadthParameter] = 1 })[^1]);
        Assert.Equal("V1 · market check off", RuleChips.TabWords(1, RuleChips.Differing(RuleChips.Of(RuleKind.IndexPullback, midPullback), RuleChips.Of(RuleKind.IndexPullback, new Dictionary<string, double>(midPullback) { [IndexRuleCandidate.LargeBreadthParameter] = IndexRuleCandidate.NoMarketCheck, [IndexRuleCandidate.BreadthFloorParameter] = 0 }))));

        // A provisional rule's chips read the floor its night stored, the sector heavyweights' reading no check.
        var settings = IndexRuleSettings.Read(IndexFamilies.Settings("MID"));

        Assert.Equal("market check on its own breadth at 45%", RuleScreen.ProvisionalChips(DriftRule.Name, settings)[^1].Words);
        Assert.DoesNotContain(RuleScreen.ProvisionalChips(HeavyweightRule.Name, settings), chip => chip.Words.StartsWith("market check", StringComparison.Ordinal));
    }

    [Fact]
    public void EachRulesWordsReadItsOwnCheckAndAFrozenPicksWhyNamesItsLiveRule()
    {
        var settings = IndexRuleSettings.Read(IndexFamilies.Settings("MID"));

        // The provisional swing rule's own check closes its own list; the heavyweights' book reads none.
        Assert.Contains("it is ranked among the S&P 400's own members, and its list closes while the S&P 400's own breadth is under 45%; each trade's result", RuleWords.Drift(settings, "S&P 400"), StringComparison.Ordinal);
        Assert.Contains("it is ranked among the S&P 400's own members, and its book reads no market check; each trade's result", RuleWords.Heavyweights(settings, "S&P 400", "IJH"), StringComparison.Ordinal);
        Assert.DoesNotContain("lists close", RuleWords.Breakout(settings, "S&P 400"), StringComparison.Ordinal);

        // A frozen pick's why names the index's live rule of its family, the breakout's high read off the live rule's own
        // setting, and never the base or the provisional settings.
        var breakout = (IndexRuleCandidate)CandidateEvaluators.Find("breakout-400")!;
        var live = new RegisterRow(1, "the live S&P 400 breakout rule", "rule", "test", "breakout-400", CandidateEvaluator.Write(new Dictionary<string, double>(IndexRules.Provisional(breakout)) { [IndexBreakoutCandidate.HighSessionsParameter] = 251 }), "v", CandidateFamily.Registered, null, new DateTimeOffset(2026, 10, 21, 11, 0, 0, TimeSpan.Zero), null);

        Assert.Equal("Closed above its high of the 251 sessions before on heavy volume tonight, every part of the S&P 400's live breakout rule passing.", TonightScreen.FrozenWhy(BreakoutRule.Name, live, "S&P 400"));
        Assert.Equal("Dipped into a support band and turned back up tonight, every part of the S&P 600's live pullback rule passing.", TonightScreen.FrozenWhy(SetupFamilies.Pullback, live, "S&P 600"));
        Assert.Equal("Its report beat its estimate and the reaction held, every part of the S&P 400's live drift rule passing.", TonightScreen.FrozenWhy(DriftRule.Name, live, "S&P 400"));
    }

    [Fact]
    public async Task PastPicksCallsProvisionalOnlyTheFamiliesNoFreezeWasReadBy()
    {
        using var store = UniversesStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // No freeze: every rule of the index provisional.
        var before = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks?universe=400"));

        Assert.Contains("Every rule of this index is provisional, so no share and no average is drawn", before, StringComparison.Ordinal);
        Assert.Contains("Each listed by a setup on the S&P 400's provisional settings, named with its index.", before, StringComparison.Ordinal);

        // The breakout frozen on the night and read by it: it alone is named frozen, from the day of its freeze, and the
        // three other swing families provisional.
        ReadByTheLiveBreakout(store, FreezeTheBreakout(store, "2026-10-02T12:00:00Z"));

        var frozen = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks?universe=400"));

        Assert.DoesNotContain("Every rule of this index is provisional", frozen, StringComparison.Ordinal);
        Assert.Contains(
            "Breakouts to a new high is frozen, listing by its live rule from the night of its freeze, 2026-10-02, its live rule's record read on the Run page; " +
            "Pullbacks to support, Earnings drift and Improving businesses in an uptrend are provisional, followed and in no record until a freeze. No share and no average is drawn here: each trade is a fact and is followed.",
            frozen,
            StringComparison.Ordinal);
        Assert.Contains("Each listed by a setup on the S&P 400, by its live rule from its freeze and on the provisional settings before it, named with its index.", frozen, StringComparison.Ordinal);
        Assert.Contains("Provisional until its freeze: followed, and in no record until then.", frozen, StringComparison.Ordinal);
    }
}
