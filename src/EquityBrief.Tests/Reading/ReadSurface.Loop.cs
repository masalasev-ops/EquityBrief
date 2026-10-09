using System.Globalization;
using System.Net;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, 17.4: the Loop page under Universe, read off the rendered page over a constructed store. Each family's
// card opens on its rule today in the words its run stored, or says the run tested no procedure for it; each proposal
// states its change or that it proposes none, its four parts each with its figure and whether it held, and its test
// years with the setting each fold chose and each side's totals, a swing family's in risks and a book's in points; the
// month the link names is drawn and the others linked; and an index with no run says so in one line.
// see: A proposal passes the tester on a block sign-flip test of its total edge after costs against the current rule, corrected within a run and held to a fixed bar across runs
public partial class ReadSurface
{
    // The parts of section 15.20's regions this check reaches.
    internal static readonly string[] LoopPageClaims =
    [
        CheckReach.Key(Scope.LoopPage, Scope.LoopRuleToday),
        CheckReach.Key(Scope.LoopPage, Scope.LoopVerdict),
        CheckReach.Key(Scope.LoopPage, Scope.LoopTestYears),
    ];

    // Every row the Loop page adds, named after phase 16's report until phase 17's own pair is checked.
    internal static string[] LoopPageRows => [.. LoopPageClaims];

    const string OctoberRun = "loop-test-MID-20261009T120000Z";

    static void LoopProposalRow(TemporaryStore store, string family, string proposal, string? words, string current, string unit, int units, string? adjusted, int gate, int stable, int counted, int better, string? trimmed, int counts, string detectable, int stableFolds) =>
        store.Execute(
            "INSERT INTO loop_proposal (run_id, index_code, family, proposal, words, current_words, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, detectable, stable_folds, passed) "
            + $"VALUES ('{OctoberRun}', 'MID', '{family}', '{proposal}', {(words is null ? "NULL" : $"'{words}'")}, '{current}', '{unit}', {units}, 19, {adjusted ?? "NULL"}, {gate}, {stable}, {counted}, {better}, {trimmed ?? "NULL"}, {counts}, {detectable}, {stableFolds}, 0);");

    static void LoopTestRow(TemporaryStore store, string family, string proposal, int year, int complete, string? chosen, int currentUnits, int proposedUnits, double currentTotal, double proposedTotal) =>
        store.Execute(
            "INSERT INTO loop_test (run_id, index_code, family, proposal, year, complete, chosen, current_units, proposed_units, current_total, proposed_total) "
            + $"VALUES ('{OctoberRun}', 'MID', '{family}', '{proposal}', {year}, {complete}, {(chosen is null ? "NULL" : $"'{chosen}'")}, {currentUnits}, {proposedUnits}, {currentTotal.ToString(CultureInfo.InvariantCulture)}, {proposedTotal.ToString(CultureInfo.InvariantCulture)});");

    [Fact]
    public async Task TheLoopPageDrawsEachFamilysRuleTodayAndEachProposalsVerdictAndTestYearsReadBackAgainstTheStore()
    {
        using var store = new TemporaryStore().Migrated();

        // Two runs on the S&P 400, September's with nothing tested and October's, which the page draws by default.
        store.Execute("INSERT INTO loop_run (run_id, month, index_code, through, started_at, ended_at, folds) VALUES ('loop-test-MID-20260905T120000Z', '2026-09', 'MID', '2026-09-04', '2026-09-05T12:00:00Z', '2026-09-05T12:01:00Z', 5);");
        store.Execute($"INSERT INTO loop_run (run_id, month, index_code, through, started_at, ended_at, folds) VALUES ('{OctoberRun}', '2026-10', 'MID', '2026-10-08', '2026-10-09T12:00:00Z', '2026-10-09T12:01:00Z', 5);");

        // October's: the breakout's grid choosing no setting, the drift's choosing one that fails the gate, and the
        // heavyweights' design (a) in points.
        LoopProposalRow(store, "breakout", "the grid", null, "the breakout rule today", "risks", 351, "1", 0, 0, 2, 0, "0", 1, "0.141", 0);
        LoopProposalRow(store, "drift", "the grid", "the drift rule within 3 sessions", "the drift rule today", "risks", 1181, "0.1752", 0, 1, 4, 3, "12.5", 1, "0.09", 3);
        LoopProposalRow(store, "heavyweight", "the six settings, design (a)", "design (a), the profit gate and the interest cover", "the heavyweights rule today", "points", 57, "0.5999", 0, 0, 4, 2, "0.012", 1, "0.004", 3);
        LoopTestRow(store, "drift", "the grid", 2022, 1, "the drift rule within 3 sessions", 64, 83, 13.58, 1.498);
        LoopTestRow(store, "drift", "the grid", 2023, 1, "the drift rule within 5 sessions", 233, 213, -27.655, 22.788);
        LoopTestRow(store, "drift", "the grid", 2026, 0, null, 210, 210, -56.081, -56.081);
        LoopTestRow(store, "heavyweight", "the six settings, design (a)", 2024, 1, "design (a), the profit gate and the interest cover", 12, 12, -0.039, 0.099);
        LoopTestRow(store, "heavyweight", "the six settings, design (a)", 2025, 1, "design (a), the profit gate and the interest cover", 12, 11, 0.086, -0.034);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=400"));

        // October's run drawn and September's linked.
        Assert.Contains($"data-month=\"2026-10\" data-run=\"{OctoberRun}\" data-proposals=\"3\"", page, StringComparison.Ordinal);
        Assert.Contains("<b data-month-chosen=\"2026-10\">2026-10</b>", page, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#/loop?universe=400&month=2026-09\" data-month-link=\"2026-09\">2026-09</a>", page, StringComparison.Ordinal);

        // A family's rule today: each card opens on the words its run stored, and the pullback, which the run tested no
        // procedure for, says so.
        Assert.Contains("data-card=\"loop-pullback\"", page, StringComparison.Ordinal);
        Assert.Contains("<p class=\"degraded\" data-loop-rule=\"none\" data-family=\"pullback\">No procedure was tested for this family in this run, so no rule is read here.</p>", page, StringComparison.Ordinal);
        Assert.Contains("<p class=\"loop-rule\" data-family=\"breakout\" data-rule=\"the breakout rule today\"><b>The rule today.</b> The breakout rule today</p>", page, StringComparison.Ordinal);
        Assert.Contains("<p class=\"loop-rule\" data-family=\"drift\" data-rule=\"the drift rule today\"><b>The rule today.</b> The drift rule today</p>", page, StringComparison.Ordinal);
        Assert.Contains("<p class=\"loop-rule\" data-family=\"heavyweight\" data-rule=\"the heavyweights rule today\"><b>The rule today.</b> The heavyweights rule today</p>", page, StringComparison.Ordinal);

        // A proposal's verdict: the breakout's proposes nothing; the drift's change and its four parts, the gate over the
        // bar and the other three held; the heavyweights' trimmed total in points.
        Assert.Contains("<p class=\"loop-change degraded\" data-words=\"none\"><b>No change.</b> The procedure run on all finished data chose no setting that met the floors", page, StringComparison.Ordinal);
        Assert.Contains("<h4 class=\"loop-name\">The grid: did not pass the tester</h4><p class=\"loop-change\" data-words=\"the drift rule within 3 sessions\"><b>Proposed, run on all finished data.</b> The drift rule within 3 sessions</p>", page, StringComparison.Ordinal);
        Assert.Contains("<li data-part=\"gate\" data-adjusted=\"0.1752\" data-held=\"0\">The gate: an adjusted p-value of 0.1752 over 19 blocks of 63 sessions, against the bar of 0.0042; over it.</li>", page, StringComparison.Ordinal);
        Assert.Contains("<li data-part=\"stable\" data-counted=\"4\" data-better=\"3\" data-held=\"1\">Stability: better in 3 of the 4 complete year(s) holding 50 trades a side, against three in five with the latest complete year counted and better; held.</li>", page, StringComparison.Ordinal);
        Assert.Contains("<li data-part=\"trimmed\" data-trimmed=\"12.5\" data-held=\"1\">With the 5 largest results by size left out of each side, the proposal's total less the rule's: +12.50 risks; above nothing.</li>", page, StringComparison.Ordinal);
        Assert.Contains("<li data-part=\"counts\" data-units=\"1181\" data-held=\"1\">The count: 1181 trade(s) over the test years, against the 300 trades a swing family is judged on; enough.</li>", page, StringComparison.Ordinal);
        Assert.Contains("<li data-part=\"trimmed\" data-trimmed=\"0.012\" data-held=\"1\">With the 5 largest results by size left out of each side, the proposal's total less the rule's: +1.20 points; above nothing.</li>", page, StringComparison.Ordinal);
        Assert.Contains("<li data-part=\"counts\" data-units=\"57\" data-held=\"1\">The count: 57 month(s) over the test years, against the 36 months a book is judged on; enough.</li>", page, StringComparison.Ordinal);
        Assert.Contains("data-detectable=\"0.09\">At this bar the gate detects a difference of about +0.09 risks a trade four times in five", page, StringComparison.Ordinal);
        Assert.Contains("<p class=\"loop-folds\" data-stable-folds=\"3\" data-folds=\"3\">3 of the 3 folds chose within a grid step of the proposal on their own years before.</p>", page, StringComparison.Ordinal);

        // A proposal's test years: each with the setting its fold chose, each side's total over its units, and whether
        // the year is counted and better; the partial year and a book's year short of twelve months not counted.
        Assert.Contains("<tr data-year=\"2023\" data-complete=\"1\" data-current-units=\"233\" data-proposed-units=\"213\" data-current-total=\"-27.655\" data-proposed-total=\"22.788\"><td class=\"num\">2023</td><td data-chosen=\"the drift rule within 5 sessions\">The drift rule within 5 sessions</td><td class=\"r num\">-27.66 risks over 233 trade(s)</td><td class=\"r num\">+22.79 risks over 213 trade(s)</td><td class=\"r\" data-better=\"yes\">yes</td></tr>", page, StringComparison.Ordinal);
        Assert.Contains("<td class=\"num\">2026 to date</td><td data-chosen=\"none\" class=\"degraded\">no setting met the floors on the years before, so the year reads the rule today</td>", page, StringComparison.Ordinal);
        Assert.Contains("data-better=\"not counted, to date\">not counted, to date</td>", page, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\">-3.90 points over 12 month(s)</td><td class=\"r num\">+9.90 points over 12 month(s)</td><td class=\"r\" data-better=\"yes\">yes</td>", page, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\">+8.60 points over 12 month(s)</td><td class=\"r num\">-3.40 points over 11 month(s)</td><td class=\"r\" data-better=\"not counted, too few\">not counted, too few</td>", page, StringComparison.Ordinal);

        // Each card's key says the rule today was chosen on these test years.
        Assert.Contains("The rule today was chosen on these same test years, so a proposal reads understated against it.", page, StringComparison.Ordinal);

        // September's run named in the link, its families each saying none was tested.
        var september = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=400&month=2026-09"));

        Assert.Contains("data-month=\"2026-09\" data-run=\"loop-test-MID-20260905T120000Z\" data-proposals=\"0\"", september, StringComparison.Ordinal);
        Assert.Equal(4, september.Split("data-loop-rule=\"none\"").Length - 1);

        // An index with no run says so in one line.
        var small = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=600"));

        Assert.Contains("<p class=\"degraded\" data-loop=\"none\">No tester run is stored for this index.", small, StringComparison.Ordinal);
        Assert.DoesNotContain("data-card=\"loop-breakout\"", small, StringComparison.Ordinal);
    }
}
