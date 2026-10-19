using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Research;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 19.7: the remaining screens. The Run page's monthly run in the retired loop's place, its setup families
// a row a rule with its verdict, its durations in one form and its trades in one line; the Loop page's summary with each
// strongest proposal's p as a meter against the bar; the Ledger's edge bars and decile strip; the Watch list's nearest
// setup; and the researched list's staleness, move, sections and why, each worked by hand over constructed rows and
// read back off the rendered page.
// see: A registered rule's looks and its futility are monitoring, and a change is adopted only by the operator's approval of a tested proposal
public partial class ReadSurface
{
    // The rows 19.7 adds that this check reaches.
    internal static readonly string[] RemainingScreensRows =
    [
        CheckReach.Key("15.10 Run", "The trades line"),
        CheckReach.Key("15.10 Run", "The monthly run"),
        CheckReach.Key("15.10 Run", "Health and Research"),
        CheckReach.Key("15.10 Run", "How last night went, every length of time on the page in whole minutes and seconds from 19.7"),
        CheckReach.Key("15.10 Run", "The setup families, from 19.7 the registered rules grouped under a heading row a setup in its tint with the level every rule of it shares"),
        CheckReach.Key("15.10 Run", "The setup families, each rule named by its short words with its full name and where its record counts from and why it restarted under the pointer"),
        CheckReach.Key("15.10 Run", "The setup families, its verdict in the newest per-rule test"),
        CheckReach.Key("15.10 Run", "The edge clock, that from 19.7 no look promotes one and a rule changes only through the monthly test and the operator's approval"),
        CheckReach.Key("15.5 The mark vocabulary", "P meter"),
        CheckReach.Key("15.5 The mark vocabulary", "Edge bars"),
        CheckReach.Key("15.5 The mark vocabulary", "Decile strip"),
        CheckReach.Key("15.16 Watch list", "A name's nearest setup"),
        CheckReach.Key("15.16 Watch list", "Its distance and its report"),
        CheckReach.Key("15.16 Watch list", "Add a name, on this page it stands in place of a box of its own"),
        CheckReach.Key("15.16 Watch list", "Add a name, which adds the name it finds while the list holds fewer than twenty and otherwise gives way to a line saying the limit is reached"),
        CheckReach.Key(Scope.LoopPage, "The summary"),
        CheckReach.Key(Scope.LoopPage, "What a change must pass"),
        CheckReach.Key(Scope.LoopPage, "A proposal's row"),
        CheckReach.Key(Scope.LedgerPage, "The business-first family"),
        CheckReach.Key("15.8 Universe", "A report's staleness"),
        CheckReach.Key("15.8 Universe", "The move since written"),
        CheckReach.Key("15.8 Universe", "The sections held"),
        CheckReach.Key("15.8 Universe", "Why it was written"),
        CheckReach.Key("15.8 Universe", "The researched order"),
    ];

    // The rows 19.7 retires: the Run page's trades region, how the system learns and each version at a checkpoint with
    // their three marks, the edge clock's earliest promotion and the Watch list's box of its own, which the phases that
    // landed them still count as landed.
    internal static readonly string[] RetiredRunRows =
    [
        CheckReach.Key("15.10 Run", "How the list's trades are going, the live list's trades still open and those that reached the target or were stopped out or ran out of time"),
        CheckReach.Key("15.10 Run", "How the list's trades are going, a ring of the trades decided against the minimum a share waits on"),
        CheckReach.Key("15.10 Run", "How the list's trades are going, the share and the break-even and the average result together once both minimums are met as Past picks draws them"),
        CheckReach.Key("15.10 Run", "How the list's trades are going, a link opening Past picks"),
        CheckReach.Key("15.10 Run", "How the system learns, the shape clock as the ordinary nights under the open filter version against the sixty its calibration waits on"),
        CheckReach.Key("15.10 Run", "How the system learns, each check's typical pass count against its range drawn dashed until measured"),
        CheckReach.Key("15.10 Run", "How the system learns, the edge clock as a line from today to its two checkpoints with the blocks the live list has gathered"),
        CheckReach.Key("15.10 Run", "How the system learns, each version running beside the live list with what it changes and the stocks it has picked and the share the live list also picked and its blocks against the floor"),
        CheckReach.Key("15.10 Run", "How the system learns, a link comparing tonight's picks"),
        CheckReach.Key("15.10 Run", "At a checkpoint, one row per version on a scale from nought to a hundred"),
        CheckReach.Key("15.10 Run", "At a checkpoint, before its first look a locked dashed outline with its trades and blocks so far"),
        CheckReach.Key("15.10 Run", "At a checkpoint, from its first look the share of its trades that reached the target with the break-even they needed and what no skill scored from the same starts and how far luck alone could move it"),
        CheckReach.Key("15.10 Run", "At a checkpoint, the verdict in words"),
        CheckReach.Key("15.5 The mark vocabulary", "Trades ring"),
        CheckReach.Key("15.5 The mark vocabulary", "Clock lines"),
        CheckReach.Key("15.5 The mark vocabulary", "Checkpoint scale"),
        CheckReach.Key("15.10 Run", "The edge clock, when the earliest promotion can come"),
        CheckReach.Key("15.16 Watch list", "Add a name, a box offering the names of the index"),
        CheckReach.Key("15.16 Watch list", "Add a name, which adds one while the list holds fewer than twenty and otherwise gives way to a line saying the limit is reached"),
    ];

    const string MonthlyOctober = "loop-month-2026-10-20261003T120000Z";

    const string LargeTested = "loop-test-GSPC-20261003T120000Z";

    static void Tested(TemporaryStore store, string run, string index, string family, string proposal, double adjusted, bool passed, string? change, int better = 3, int counted = 4) =>
        store.Execute(
            "INSERT INTO loop_proposal (run_id, index_code, family, proposal, words, current_words, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, detectable, stable_folds, passed, finding, change) "
            + FormattableString.Invariant($"VALUES ('{run}', '{index}', '{family}', '{Literal(proposal)}', 'the change', 'the {family} rule today', 'risks', 400, 19, {adjusted.ToString("R", CultureInfo.InvariantCulture)}, {(passed ? 1 : 0)}, 1, {counted}, {better}, 1.5, 1, 0.1, 3, {(passed ? 1 : 0)}, NULL, {(change is null ? "NULL" : $"'{change}'")});"));

    static void RuleTested(TemporaryStore store, string family, string rule, string standing, bool passed, string? why = null) =>
        store.Execute(
            "INSERT INTO loop_rule (run_id, month, index_code, family, rule, standing, words, against, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, passed, why) "
            + $"VALUES ('loop-rules-GSPC-20261003T120000Z', '2026-10', 'GSPC', '{family}', '{Literal(rule)}', '{standing}', 'the rule', 'its benchmark', 'risks', 400, 9, {(passed ? "0.001" : "0.3")}, {(passed ? 1 : 0)}, 1, 3, 3, 1.0, 1, {(passed ? 1 : 0)}, {(why is null ? "NULL" : $"'{why}'")});");

    [Fact]
    public async Task TheRunPagesMonthlyRunStandsInTheRetiredLoopsPlaceWithWhatEachFamilyTestedAndHowEachRuleRead()
    {
        using var store = RunTopStore();

        // The monthly run for October: its facts and its history steps held, started at noon on 2026-10-03.
        foreach (var (stage, minute) in new[] { ("facts", "00"), ("history", "20") })
        {
            store.Execute(
                "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) "
                + $"VALUES ('{MonthlyOctober}', 'loop-month-{stage}', '2026-10-03T12:{minute}:00Z', '2026-10-03T12:{minute}:30Z', 'ok', 0, 0, 0, '0', 'held');");
        }

        // The S&P 500's tester run: the breakout's two proposals, the stronger passing with a change and so put; the
        // drift's one, which did not pass. The per-rule test: the breakout's live rule passed, its two variants within luck.
        store.Execute("INSERT INTO loop_run (run_id, month, index_code, through, started_at, ended_at, folds) VALUES ('" + LargeTested + "', '2026-10', 'GSPC', '2026-10-02', '2026-10-03T12:30:00Z', '2026-10-03T12:40:00Z', 5);");
        Tested(store, LargeTested, "GSPC", "breakout", "the grid", 0.002, true, Exit(7));
        Tested(store, LargeTested, "GSPC", "breakout", "the autopsy's exit, ranked 1", 0.4, false, null);
        Tested(store, LargeTested, "GSPC", "drift", "the grid", 0.6, false, null);
        RuleTested(store, "breakout", "the live breakout rule", "live", true);
        RuleTested(store, "breakout", "a variant", "variant", false);
        RuleTested(store, "breakout", "another variant", "variant", false);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-09-10"));
        var monthly = Card(page, "monthly-run");
        var luck = RuleVerdict.Of(false, null, 0.3, false, true, 1.0, true);

        Assert.Contains("<div class=\"monthly-run\" data-state=\"held\" data-month=\"2026-10\" data-families=\"2\">", monthly, StringComparison.Ordinal);
        Assert.Contains("The monthly run for 2026-10 started at 12:00 UTC on 2026-10-03, and every step it ran held.", monthly, StringComparison.Ordinal);
        Assert.Contains("<tr data-family=\"breakout\" data-tested=\"2\" data-passed=\"1\" data-put=\"the grid\">", monthly, StringComparison.Ordinal);
        Assert.Contains("<tr data-family=\"drift\" data-tested=\"1\" data-passed=\"0\" data-put=\"none\">", monthly, StringComparison.Ordinal);
        Assert.Contains($"the rule today: {RuleVerdict.Passed}</span>; <span data-variants=\"2\">variants: 2 {luck}</span>", monthly, StringComparison.Ordinal);
        Assert.Contains("<span class=\"degraded\" data-head=\"none\">the rule today: not tested yet</span>", Regex.Match(monthly, "<tr data-family=\"drift\".*?</tr>").Value, StringComparison.Ordinal);
        Assert.Contains($"href=\"{SinglePageApp.LoopRoute}?universe=500\"", monthly, StringComparison.Ordinal);
        Assert.Contains($"href=\"{SinglePageApp.PaperRoute}?universe=500\"", monthly, StringComparison.Ordinal);
        Assert.Matches("<p class=\"mr-shape\" data-ordinary=\"\\d+\" data-wanted=\"60\">The shape clock: \\d+ of the 60 ordinary nights", monthly);

        // Above the detail, beneath the Health heading and its night, with none of the retired loop's words anywhere.
        Assert.True(page.IndexOf("data-card=\"monthly-run\"", StringComparison.Ordinal) < page.IndexOf("<section class=\"run-detail\">", StringComparison.Ordinal));

        foreach (var retired in new[] { "How the system learns", "At a checkpoint", "Checkpoint 1", "about two years", "stays hidden until", "unlocks at checkpoint" })
        {
            Assert.DoesNotContain(retired, page, StringComparison.Ordinal);
        }

        // A run that stopped says where and why; one whose passing proposal an earlier decline holds back puts none.
        var stopped = RunScreen.Monthly(
            [new RunStageRow(MonthlyOctober, "loop-month-history", new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 3, 12, 5, 0, TimeSpan.Zero), "failed", 0, 0, 0, "0", "history did not hold")],
            [new LoopProposalRow("breakout", "the grid", "the change", "today", "risks", 400, 19, 0.002, true, true, 4, 3, 1.5, true, 0.1, 3, true, null, Exit(7))],
            _ => true,
            [],
            null);

        Assert.Equal((WorryItem.Failed, "the run for 2026-10 stopped at its history step: history did not hold"), (stopped.State, stopped.Why));
        Assert.Equal((1, 1, (string?)null), (stopped.Families[0].Tested, stopped.Families[0].Passed, stopped.Families[0].Put));
        Assert.Equal((0, 60), (stopped.Ordinary, stopped.Wanted));
    }

    [Fact]
    public void EveryLengthOfTimeIsWholeMinutesAndSecondsByOneFunction()
    {
        // Worked by hand: a second short of a minute, a minute, 31 minutes 32.4 seconds and an hour, and nothing below nought.
        Assert.Equal(
            ["0 min 0 s", "0 min 59 s", "1 min 0 s", "31 min 32 s", "60 min 0 s", "0 min 0 s"],
            new[] { 0, 59.9, 60, 1892.4, 3600, -4 }.Select(Figures.Duration));

        // The stage table and the night's total read in that form, and the stage time in seconds is gone.
        var header = WebUtility.HtmlDecode(new MarkRenderer().OperationalHeader(
            new DateOnly(2026, 9, 10),
            [new StageRow("fetch", new DateTimeOffset(2026, 9, 10, 23, 0, 0, TimeSpan.Zero), 125, 1, 0, 1, "0", "ok", "fetched")],
            null));

        Assert.Contains("<td>2 min 5 s</td>", header, StringComparison.Ordinal);
        Assert.Contains("1 stage(s) of the night, 2 min 5 s of stage time", header, StringComparison.Ordinal);
        Assert.DoesNotContain("second(s) of stage time", header, StringComparison.Ordinal);
        Assert.Contains("the night took 4 min 2 s", new MarkRenderer().NightOps(new DateOnly(2026, 9, 10), "00:04:02", null), StringComparison.Ordinal);
    }

    [Fact]
    public void TheSetupFamiliesAreARowARuleUnderItsFamilysHeadingInItsTintWithItsVerdict()
    {
        // Two breakout rules at one level and two heavyweights rules at two, the variant restarted at its registration.
        FamilyRecordRow[] rows =
        [
            new("breakout", "Breakouts", "the live breakout rule", true, 4, 2, 0.12, 1, 8, 0.0167, "words", new DateOnly(2026, 10, 2), null, "Live", RuleVerdict.Passed),
            new("breakout", "Breakouts", "the breakout rule at a 251-session high", false, 3, 1, -0.05, 1, 8, 0.0167, "words", new DateOnly(2026, 10, 5), "a trade differed", "V1 · 251-session high", RuleVerdict.NotTested),
            new("heavyweight", "Sector heavyweights", "the live sector heavyweights rule", true, 5, 0, null, 0, 8, 0.025, "words", new DateOnly(2026, 10, 7), null, "Live", null),
            new("heavyweight", "Sector heavyweights", "the sector heavyweights rule at every company", false, 4, 0, null, 0, 8, 0.0125, "words", new DateOnly(2026, 10, 7), null, "V1 · every company", null),
        ];
        var table = WebUtility.HtmlDecode(new MarkRenderer().FamilyRecords(rows));

        // The breakout's heading carries its one level; the heavyweights' two levels stand on their rows.
        Assert.Contains("<tr class=\"family-head\" data-family=\"breakout\" data-level=\"0.0167\">", table, StringComparison.Ordinal);
        Assert.Contains("· its looks read at a level of 0.0167</th>", table, StringComparison.Ordinal);
        Assert.Contains("<tr class=\"family-head\" data-family=\"heavyweight\" data-level=\"differs\">", table, StringComparison.Ordinal);
        Assert.Contains("0 of 8, level 0.025</td>", table, StringComparison.Ordinal);
        Assert.Contains("0 of 8, level 0.0125</td>", table, StringComparison.Ordinal);

        // A rule by its short words in its tint, its full name, its start and its restart under the pointer, its verdict.
        Assert.Contains("title=\"the breakout rule at a 251-session high. Its record counts from 2026-10-05. It restarted at its registration, since a trade differed.\"><span class=\"fam-tag\" data-family=\"breakout\">V1 · 251-session high</span></td>", table, StringComparison.Ordinal);
        Assert.Contains($"data-verdict=\"{RuleVerdict.Passed}\"", table, StringComparison.Ordinal);
        Assert.Contains("<td class=\"degraded\">not read</td>", table, StringComparison.Ordinal);
        Assert.DoesNotContain("record-from", table, StringComparison.Ordinal);
    }

    [Fact]
    public void ThePMeterDrawsAnAdjustedPAgainstTheBarOnALogScale()
    {
        // On a scale 160 wide from a ten-thousandth to one, a p of a hundredth sits at 160 times two quarters, 80; the bar
        // of 0.05 over 12 at 160 times log10(0.0041667) plus 4 over 4, 64.79; a ten-thousandth and anything under it at 0.
        var hundredth = MarkRenderer.PMeter(0.01);
        var bar = (160 * (Math.Log10(0.05 / 12) + 4) / 4).ToString("0.##", CultureInfo.InvariantCulture);

        Assert.Equal("64.79", bar);
        Assert.Contains("data-adjusted=\"0.01\" data-held=\"false\"", hundredth, StringComparison.Ordinal);
        Assert.Contains("<rect class=\"pm-fill loss\" x=\"80\" y=\"3\" width=\"80\"", hundredth, StringComparison.Ordinal);
        Assert.Contains($"<line class=\"pm-bar\" x1=\"{bar}\"", hundredth, StringComparison.Ordinal);
        Assert.Contains("<rect class=\"pm-fill gain\" x=\"0\" y=\"3\" width=\"160\"", MarkRenderer.PMeter(0.00001), StringComparison.Ordinal);
        Assert.Contains("data-held=\"true\"", MarkRenderer.PMeter(LoopGate.Bar), StringComparison.Ordinal);
        Assert.Contains("data-adjusted=\"none\"", MarkRenderer.PMeter(null), StringComparison.Ordinal);
        Assert.DoesNotContain("pm-fill", MarkRenderer.PMeter(null), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLoopPageOpensOnASummaryAFamilyWithEachStrongestProposalsMeterAndTheOnePutToYou()
    {
        using var store = new TemporaryStore().Migrated();

        // The S&P 400's October run: the pullback's two, the stronger at a ten-thousandth passing with a change; the
        // breakout's one at 0.05, which did not pass; nothing for the drift or the heavyweights.
        Run(store, "loop-test-MID-20261009T120000Z", "MID", "2026-10");
        Tested(store, "loop-test-MID-20261009T120000Z", "MID", "pullback", "winners against losers, ranked 1", 0.0001, true, Condition("rsi", 60));
        Tested(store, "loop-test-MID-20261009T120000Z", "MID", "pullback", "the grid", 0.5, false, null);
        Tested(store, "loop-test-MID-20261009T120000Z", "MID", "breakout", "the grid", 0.05, false, Exit(7), better: 2, counted: 5);
        Tested(store, "loop-test-MID-20261009T120000Z", "MID", "fundamentals", "the grid", 0.3, false, EquityBrief.Core.Loop.LoopChange.OfSetting([0, 0, 2], "growth 0|margins either|cash 1.2", 0).Json);
        store.Execute("INSERT INTO loop_test (run_id, index_code, family, proposal, year, complete, chosen, current_units, proposed_units, current_total, proposed_total) VALUES ('loop-test-MID-20261009T120000Z', 'MID', 'pullback', 'winners against losers, ranked 1', 2024, 1, 'rsi above 60', 60, 55, 2.5, 4.0);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=400"));
        var summary = Card(page, "loop-summary");

        // A row a family in the one order, the business-first family among them.
        Assert.Equal(
            ["pullback", "breakout", "drift", "fundamentals", "heavyweight"],
            Regex.Matches(summary, "<tr data-family=\"([^\"]+)\" data-tested").Select(match => match.Groups[1].Value));
        Assert.Contains("<tr data-family=\"pullback\" data-tested=\"2\" data-strongest=\"winners against losers, ranked 1\" data-put=\"winners against losers, ranked 1\">", summary, StringComparison.Ordinal);
        Assert.Contains("data-adjusted=\"0.0001\" data-held=\"true\"", summary, StringComparison.Ordinal);
        Assert.Contains("p 0.0001, better in 3 of 4 years", summary, StringComparison.Ordinal);
        Assert.Contains("class=\"loop-press\"", Regex.Match(summary, "<tr data-family=\"pullback\".*?</tr>", RegexOptions.Singleline).Value, StringComparison.Ordinal);

        // The breakout at 0.05: 160 times log10(0.05) plus 4 over 4 is 107.96, over the bar and drawn in red.
        Assert.Contains("data-adjusted=\"0.05\" data-held=\"false\"", summary, StringComparison.Ordinal);
        Assert.Contains("<rect class=\"pm-fill loss\" x=\"107.96\"", summary, StringComparison.Ordinal);
        Assert.Contains("Its one proposal did not pass with a change to put to you; the next monthly run is on 2026-11-07.", summary, StringComparison.Ordinal);

        // 2026-11-01 is a Sunday, so the first Saturday after the run's month is the seventh.
        Assert.Equal(new DateOnly(2026, 11, 7), SinglePageApp.NextMonthlyRun("2026-10"));
        Assert.Equal(new DateOnly(2026, 12, 5), SinglePageApp.NextMonthlyRun("2026-11"));
        Assert.Contains("Nothing was tested for this setup in the run for 2026-10; the next monthly run is on 2026-11-07.", summary, StringComparison.Ordinal);

        // From 19.9 the business-first family is a row and a card as every family is, its one proposal drawn from the run
        // and nothing said of 19.9.
        Assert.Contains("<tr data-family=\"fundamentals\" data-tested=\"1\" data-strongest=\"the grid\" data-put=\"none\">", summary, StringComparison.Ordinal);
        Assert.Contains("data-card=\"loop-fundamentals\"", page, StringComparison.Ordinal);
        Assert.Single(Blocks(page, "<details class=\"loop-row\" data-family=\"fundamentals\" data-proposal=\"the grid\">.*?</details>"));
        Assert.DoesNotContain("19.9", page, StringComparison.Ordinal);

        // The line on what a change must pass, each figure its constant.
        Assert.Contains(
            FormattableString.Invariant($"an adjusted p at or under the bar of {LoopGate.Bar:0.0000}; better in 3 of 5 complete test years with the latest complete year better; its total less the rule's above nothing with the 5 largest results on each side left out; and 300 trades, or 36 months for a book, over the test years."),
            summary,
            StringComparison.Ordinal);

        // Each proposal a folded row, the one put pointing to the summary's presses, its year as a pair of bars.
        var row = Assert.Single(Blocks(page, "<details class=\"loop-row\" data-family=\"pullback\" data-proposal=\"winners against losers, ranked 1\">.*?</details>"));

        Assert.Contains("<span class=\"lr-years\">better in 3 of 4</span>", row, StringComparison.Ordinal);
        Assert.Contains("<span class=\"lr-count\">400 trade(s) of 300</span>", row, StringComparison.Ordinal);
        Assert.Contains("<g data-year=\"2024\" data-current=\"2.5\" data-proposed=\"4\">", row, StringComparison.Ordinal);
        Assert.Contains("Its presses stand in the summary above.", row, StringComparison.Ordinal);
        Assert.True(page.IndexOf("data-card=\"loop-summary\"", StringComparison.Ordinal) < page.IndexOf("data-card=\"loop-pullback\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheLoopSummaryDrawsTheBusinessFirstFamilysRuleTodayFromTheSettingItsNightStoredOnEveryIndex()
    {
        // From the 19.7 correction of 2026-10-18: no rule of the business-first family is registered, so its rule today is
        // the setting each index's night stored, read back from its short form; the S&P 400 at the provisional setting and
        // the S&P 500 at an approved one, each chip worked by hand from the setting's words.
        Assert.Equal(EquityBrief.Core.Families.FundamentalsRule.Provisional, EquityBrief.Core.Families.FundamentalsSetting.FromShort("growth 0|margins either|cash 1.0"));
        Assert.All(EquityBrief.Core.Families.FundamentalsRule.Grid, setting => Assert.Equal(setting, EquityBrief.Core.Families.FundamentalsSetting.FromShort(setting.Short)));
        Assert.Null(EquityBrief.Core.Families.FundamentalsSetting.FromShort("growth 0|margins 1|cash 1.0"));
        Assert.Null(EquityBrief.Core.Families.FundamentalsSetting.FromShort("growth 0|cash 1.0"));
        Assert.Null(EquityBrief.Core.Families.FundamentalsSetting.FromShort(null));

        static string NightSettings(string setting) =>
            "{\"floors\":{\"price\":5,\"dollarVolume\":10000000,\"sessions\":50},\"marketFloor\":0.45,\"pullback\":{\"setting\":\"strength 0.5\",\"cap\":63},"
            + "\"fundamentals\":{\"setting\":\"" + setting + "\",\"words\":\"the setting in words\",\"cap\":63}}";

        using var store = new TemporaryStore().Migrated();

        Run(store, "loop-test-MID-20261009T120000Z", "MID", "2026-10");
        Run(store, "loop-test-GSPC-20261009T120000Z", "GSPC", "2026-10");
        store.Execute("INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) VALUES ('KEYS', '2026-10-16', '[]', 0, '{}', '[]');");
        store.Execute(
            "INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced) VALUES "
            + $"('MID', '2026-10-16', 400, 0.5, 1, '{NightSettings("growth 0|margins either|cash 1.0")}', 0), "
            + $"('GSPC', '2026-10-16', 503, 0.5, 1, '{NightSettings("growth 0.05|margins both|cash 1.2")}', 0);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        static string RuleToday(string page) =>
            Regex.Match(Card(WebUtility.HtmlDecode(page), "loop-summary"), "<tr data-family=\"fundamentals\".*?</td><td.*?</td>", RegexOptions.Singleline).Value;

        Assert.EndsWith(
            "<td><span class=\"rule-chips\"><span class=\"chip\">revenue up more than 0%</span><span class=\"chip\">gross or operating margin up</span><span class=\"chip\">cash at least 1 times income</span></span></td>",
            RuleToday(await client.GetStringAsync("/screens/loop?universe=400")),
            StringComparison.Ordinal);
        Assert.EndsWith(
            "<td><span class=\"rule-chips\"><span class=\"chip\">revenue up more than 5%</span><span class=\"chip\">gross and operating margins up</span><span class=\"chip\">cash at least 1.2 times income</span></span></td>",
            RuleToday(await client.GetStringAsync("/screens/loop?universe=500")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheLearnedScoreIsNamedByTheMonthItWasFittedWithItsHashUnderThePointer()
    {
        var proposals = new[] { new LoopProposalRow("pullback", RidgeScore.Proposal + ", ranked 1", "the score", "today", "risks", 400, 19, 0.5, false, false, 4, 1, 0.1, true, 0.1, 2, false) };
        var drawn = new MarkRenderer().LoopScore("pullback", proposals, new LoopModelRow("pullback", new DateOnly(2026, 9, 1), 1200, "a1b2c3d4e5f6", "the weights"), [], "2026-10");

        Assert.Contains("data-hash=\"a1b2c3d4e5f6\" title=\"a1b2c3d4e5f6\"><b>The learned score, fitted October 2026.</b>", drawn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLedgerDrawsTheBusinessFirstFamilysCardAndEachYearsEdgeAndTheDecilesAsMarks()
    {
        // The edge a year: 0.2 the largest, drawn 28 high above the line at 30; -0.1 14 beneath it; a year with none a gap.
        LedgerYear Year(int year, double? edge) => new("GSPC", "breakout", year, 10, 4, 0, 0, edge is null ? 0 : 5, 0.1, edge, null, null);

        var bars = MarkRenderer.LedgerEdgeBars("breakout", [Year(2023, 0.2), Year(2024, -0.1), Year(2025, null)]);

        Assert.Contains("data-years=\"2\"", bars, StringComparison.Ordinal);
        Assert.Contains("<rect class=\"gain\" data-year=\"2023\" data-edge=\"0.2\" x=\"3\" y=\"2\" width=\"16\" height=\"28\">", bars, StringComparison.Ordinal);
        Assert.Contains("<rect class=\"loss\" data-year=\"2024\" data-edge=\"-0.1\" x=\"25\" y=\"30\" width=\"16\" height=\"14\">", bars, StringComparison.Ordinal);
        Assert.DoesNotContain("data-year=\"2025\" data-edge", bars, StringComparison.Ordinal);

        // The strip over nine cut points from -0.4 to 0.4: on a line 300 wide from 6 to 294, -0.4 at 6, nought at 150, the
        // median's dot drawn larger.
        var strip = MarkRenderer.LedgerDecileStrip(new LedgerYear("GSPC", "breakout", 2025, 10, 4, 0, 0, 9, 0.1, 0.05, [0, 0, 0, 0, 0, 0, 0, 0, 0], [-0.4, -0.3, -0.2, -0.1, 0, 0.1, 0.2, 0.3, 0.4]));

        Assert.Contains("<circle class=\"loss\" data-cut=\"1\" data-edge=\"-0.4\" cx=\"6\" cy=\"13\" r=\"3\"/>", strip, StringComparison.Ordinal);
        Assert.Contains("<circle class=\"gain\" data-cut=\"5\" data-edge=\"0\" cx=\"150\" cy=\"13\" r=\"5\"/>", strip, StringComparison.Ordinal);
        Assert.Contains("<line class=\"ds-zero\" x1=\"150\"", strip, StringComparison.Ordinal);
        Assert.Equal(string.Empty, MarkRenderer.LedgerDecileStrip(Year(2025, null)));

        // From 19.9 the ledger writes the business-first family's setups, so the page carries no line saying it holds none,
        // on an index holding no setup and on one holding the family's: its card stands after the drift's in the one order,
        // named as every page names it, over summary rows stored in the reverse order.
        using var store = new TemporaryStore().Migrated();

        LedgerYearRow(store, "fundamentals", 2026, 12, 3, 12, 2, 4, "0.3", "0.05", "-1,-0.5,0,0.25,0.5,1,1.5,2,3");
        LedgerYearRow(store, "drift", 2026, 20, 5, 20, 4, 8, "0.1", "0.02", "-1,-0.5,0,0.25,0.5,1,1.5,2,3");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();
        var empty = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/ledger?universe=600"));
        var held = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/ledger?universe=500"));

        Assert.Contains("data-ledger=\"none\"", empty, StringComparison.Ordinal);

        foreach (var page in new[] { empty, held })
        {
            Assert.DoesNotContain("ledger-business", page, StringComparison.Ordinal);
            Assert.DoesNotContain("19.9", page, StringComparison.Ordinal);
        }

        Assert.True(held.IndexOf("data-card=\"ledger-drift\"", StringComparison.Ordinal) < held.IndexOf("data-card=\"ledger-fundamentals\"", StringComparison.Ordinal));
        Assert.Contains($"{EquityBrief.Core.Families.SetupFamilies.FundamentalsFirst.Label} setups", Card(held, "ledger-fundamentals"), StringComparison.Ordinal);
        Assert.Contains("data-families=\"2\"", held, StringComparison.Ordinal);
    }

    [Fact]
    public void AWatchedNamesNearestSetupIsAFamilyThatPassedThenTheFewestGatesMissedATieInTheOneOrder()
    {
        var night = new DateOnly(2026, 10, 9);

        GateResultRow Gate(bool passed, bool trend, bool setup) => new(
            "AAA", night, "5", true, trend, setup, setup ? "pullback" : null, setup, setup, setup, null, null, null, null, null, null, null, [], passed, passed ? 1 : null, null, null,
            "{\"gates\":[{\"gate\":\"trend and strength\",\"passed\":" + (trend ? "true" : "false") + ",\"reason\":\"its close sits under its 200-day average\",\"values\":{}},{\"gate\":\"setup\",\"passed\":" + (setup ? "true" : "false") + ",\"reason\":\"no pullback to a band\",\"values\":{}}],\"notes\":[]}");

        FamilyResultRow Family(string family, bool passed, int missed) => new(
            night, "AAA", family, passed, missed, null, null, null, null, null, [],
            "{\"gates\":[{\"gate\":\"volume\",\"passed\":" + (missed == 0 ? "true" : "false") + ",\"reason\":\"volume under 1.5 times its average\",\"values\":{}}]}");

        // The pullback passed: it is the nearest, whatever else missed.
        Assert.Equal(new NearestSetup("pullback", 0, null, null), TonightScreen.Nearest("AAA", Gate(true, true, true), [Family("breakout", false, 1)]));

        // The pullback missing two gates and the breakout one: the breakout, with its first missed gate in its own words.
        Assert.Equal(new NearestSetup("breakout", 1, "volume", "volume under 1.5 times its average"), TonightScreen.Nearest("AAA", Gate(false, false, false), [Family("breakout", false, 1), Family("drift", false, 2)]));

        // The breakout and the drift each one short: the breakout, first in the one order.
        Assert.Equal("breakout", TonightScreen.Nearest("AAA", null, [Family("drift", false, 1), Family("breakout", false, 1)])!.Family);

        // A family that passed beats every family that missed, and a name no family answered has none.
        Assert.Equal("drift", TonightScreen.Nearest("AAA", Gate(false, false, true), [Family("drift", true, 0)])!.Family);
        Assert.Null(TonightScreen.Nearest("AAA", null, [Family("drift", false, 1) with { Ticker = "BBB" }]));

        // Drawn as a tag in its tint with its gate's chip, the words under the pointer.
        var cell = new WatchCell(new ListingCell("AAA", night, 0, 0, 10m, []), "Alpha", night, "stopped", false, new NearestSetup("breakout", 1, "volume", "volume under 1.5 times its average"), null);
        var region = WebUtility.HtmlDecode(new SinglePageApp().WatchRegion(night, [cell], 20, new MarkRenderer()));

        Assert.Contains("data-nearest=\"breakout\" data-missed=\"1\"", region, StringComparison.Ordinal);
        Assert.Contains("<span class=\"chip\" data-gate=\"volume\" title=\"volume under 1.5 times its average\">1 short: volume</span>", region, StringComparison.Ordinal);
        Assert.Contains("<span class=\"degraded\">no date</span>", region, StringComparison.Ordinal);
        Assert.Contains("Search a name in the bar above to add it here; 19 places left.", region, StringComparison.Ordinal);
        Assert.DoesNotContain("watch-add\"", region, StringComparison.Ordinal);

        // The header's search adds the name it finds on the watch list's own route and opens the name elsewhere.
        var shell = new SinglePageApp().Shell("EquityBrief");

        Assert.Contains($"if (ticker && location.hash.startsWith('{SinglePageApp.WatchRoute}'))", shell, StringComparison.Ordinal);
        Assert.Contains($"const response = await fetch('{SinglePageApp.WatchPostRoute}' + encodeURIComponent(ticker), {{", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void AReportIsStaleOnTheLaterSideOfEachReasonAndStandsOnTheEarlier()
    {
        var night = new DateOnly(2026, 9, 30);
        SectionStanding[] written = [new("What the company sells", new DateOnly(2026, 9, 10), Staleness.Accepted)];

        StalenessVerdict Judged(DateOnly? filing = null, EarningsEvent? earning = null, IReadOnlyList<PulseSession>? pulse = null) =>
            Staleness.Judge(night, written, filing, earning is null ? [] : [earning], pulse ?? [], refresh: false);

        // A filing dated the day after the section was written makes it stale; one dated that day does not.
        Assert.Equal(ResearchState.Stale, Judged(filing: new DateOnly(2026, 9, 11)).State);
        Assert.Equal(ResearchState.Stands, Judged(filing: new DateOnly(2026, 9, 10)).State);

        // A report after the close on the day written has passed since; one before the open that day had not.
        Assert.Equal(ResearchState.Stale, Judged(earning: new EarningsEvent(new DateOnly(2026, 9, 10), "after")).State);
        Assert.Equal(ResearchState.Stands, Judged(earning: new EarningsEvent(new DateOnly(2026, 9, 10), Staleness.BeforeTheOpen)).State);

        // The news: thirty quiet sessions of one story and five loud ones of ten. The five sessions' sums before the loud
        // ones are 5 and their median 5, so a sum is above at 15: the first loud session's is 4 + 10 = 14 and the second's
        // 3 + 20 = 23, the run beginning on the second. Stale where the section was written before it and standing where
        // written on it.
        var sessions = Enumerable.Range(0, 35).Select(at => new DateOnly(2026, 8, 1).AddDays(at)).ToArray();
        PulseSession[] loud = [.. sessions.Select((session, at) => new PulseSession(session, at < 30 ? 1 : 10))];
        var began = Staleness.Pulse(night, loud).Since!.Value;

        Assert.Equal(sessions[31], began);
        Assert.Equal(ResearchState.Stale, Staleness.Judge(night, [written[0] with { AsOf = began.AddDays(-1) }], null, [], loud, refresh: false).State);
        Assert.Equal(ResearchState.Stands, Staleness.Judge(night, [written[0] with { AsOf = began }], null, [], loud, refresh: false).State);

        // A stale report is drawn with what made it so, and one that stands says so.
        var region = WebUtility.HtmlDecode(new SinglePageApp().ResearchedRegion(
            [
                new ResearchedCell("AAA", "Alpha", "Industrials", new DateOnly(2026, 9, 10), 8, true, ["a filing dated 2026-09-11 arrived after it was written"], 0.052, 10, ResearchedScreen.AskedByYou),
                new ResearchedCell("BBB", "Beta", "Utilities", new DateOnly(2026, 9, 12), 10, false, [], -0.0004, 10, ResearchedScreen.ByHand),
            ]));

        Assert.Contains("<span class=\"status-pill failed\">stale</span> a filing dated 2026-09-11 arrived after it was written</td>", region, StringComparison.Ordinal);
        Assert.Contains("<span class=\"status-pill live\">stands</span>", region, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num gain\">+5.2%</td>", region, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num flat\">0.0%</td>", region, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\" data-of=\"10\">8 of 10</td>", region, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachResearchedReportSaysWhyItWasWrittenHowFarItsStockMovedAndSortsByDayOrStaleness()
    {
        var written = new DateOnly(2026, 9, 10);

        WrittenRequestRow Request(string from, bool refresh, int day) => new("AAA", from, refresh, new DateTimeOffset(2026, 9, day, 23, 0, 0, TimeSpan.Zero));

        // Settled the day written or the day after reads its asker; two days after reads none of them.
        Assert.Equal(ResearchedScreen.TonightsList, ResearchedScreen.Why(written, [Request("night", false, 10)]));
        Assert.Equal(ResearchedScreen.AskedByYou, ResearchedScreen.Why(written, [Request("name", false, 11)]));
        Assert.Equal(ResearchedScreen.Regenerated, ResearchedScreen.Why(written, [Request("name", true, 10)]));
        Assert.Equal(ResearchedScreen.ByHand, ResearchedScreen.Why(written, [Request("name", false, 12)]));
        Assert.Equal(ResearchedScreen.ByHand, ResearchedScreen.Why(written, []));

        // The move: the close on the Friday before a Sunday written, 40, against the newest, 42, is a twentieth; none
        // with no close on or before the day written.
        var closes = new Dictionary<(string, DateOnly), decimal> { [("AAA", new DateOnly(2026, 9, 11))] = 40m, [("AAA", new DateOnly(2026, 9, 18))] = 42m };

        Assert.Equal(0.05, ResearchedScreen.Move(new DateOnly(2026, 9, 13), closes, "AAA")!.Value, 9);
        Assert.Null(ResearchedScreen.Move(new DateOnly(2026, 9, 10), closes, "AAA"));

        // Sorted by the day written, or with a stale report first.
        ResearchedCell[] cells = [new("OLD", null, null, new DateOnly(2026, 9, 1), 1, true), new("NEW", null, null, new DateOnly(2026, 9, 9), 1, false)];

        Assert.Equal(["NEW", "OLD"], ResearchedScreen.Sorted(cells, ResearchedScreen.ByWritten).Select(cell => cell.Ticker));
        Assert.Equal(["OLD", "NEW"], ResearchedScreen.Sorted(cells, ResearchedScreen.ByStaleness).Select(cell => cell.Ticker));

        // Over a store: a report the operator asked for on its stock's page, its sections out of ten, its move off its
        // closes, and the order kept in the link.
        using var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO research_section (ticker, section, version, as_of, model, status, prose, source_ids, reject_reason) VALUES "
            + "('AAA', 'What the company sells', 1, '2026-09-10', 'm', 'accepted', 'p', '[]', NULL), ('AAA', 'The two cases', 1, '2026-09-10', 'm', 'accepted', 'p', '[]', NULL);"
            + "INSERT INTO research_request (ticker, asked_at, asked_from, lane, state, settled_at, run_id, reason, refresh) VALUES ('AAA', '2026-09-10T20:00:00Z', 'name', 'paid', 'written', '2026-09-10T21:00:00Z', 'pass-1', NULL, 0);");
        PickBar(store, "AAA", "2026-09-10", "50");
        PickBar(store, "AAA", "2026-09-18", "55");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/researched?sort=stale"));

        Assert.Contains($"data-why=\"{ResearchedScreen.AskedByYou}\" data-move=\"{0.1.ToString("R", CultureInfo.InvariantCulture)}\"", page, StringComparison.Ordinal);
        Assert.Contains($"<td class=\"r num\" data-of=\"{ClaimRules.Sections.Length}\">2 of {ClaimRules.Sections.Length}</td>", page, StringComparison.Ordinal);
        Assert.Contains("<nav class=\"researched-sort\" data-sort=\"stale\">", page, StringComparison.Ordinal);
        Assert.Contains($"href=\"{SinglePageApp.ResearchedRoute}?universe=500&sort=written\"", page, StringComparison.Ordinal);
    }
}
