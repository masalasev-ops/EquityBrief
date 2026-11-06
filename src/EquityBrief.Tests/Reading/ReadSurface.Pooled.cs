using System.Net;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// 23.2's verdict across the three indexes: each family's verdict read back off the Loop page's summary from a constructed
// pooled run with the passes luck alone would bring beneath what a change must pass, and the scorecard's part beneath its
// two halves with a variant's same change beside its verdict.
// see: One verdict across the three indices stands beside each index's own, and an approval still changes one index's rule
public partial class ReadSurface
{
    internal static string[] PooledPageRows =>
    [
        CheckReach.Key(Scope.LoopPage, "All three indexes together"),
        CheckReach.Key(Scope.LoopPage, "The passes luck alone would bring"),
        CheckReach.Key("15.21 Paper trading", "An account's verdict across the indexes"),
    ];

    static string PooledRow(string run, string family, string kind, string indices, string rules, int units, double? adjusted, int counted, int better, bool passed, string? carried) =>
        FormattableString.Invariant(
            $"INSERT INTO loop_pooled (run_id, month, family, kind, indices, rules, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, passed, judged_from, carried_by) VALUES ('{run}', '2026-11', '{family}', '{kind}', '{indices}', '{rules}', 'risks', {units}, 14, {(adjusted is { } p ? p.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "NULL")}, {(passed ? 1 : 0)}, {(passed ? 1 : 0)}, {counted}, {better}, 1.5, 1, {(passed ? 1 : 0)}, NULL, {(carried is null ? "NULL" : "'" + carried + "'")});");

    [Fact]
    public async Task TheLoopPagesSummaryDrawsEachSetupAcrossTheThreeIndexesWithThePassesLuckWouldBring()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute("INSERT INTO loop_run (run_id, month, index_code, through, started_at, ended_at, folds) VALUES ('loop-test-GSPC-1', '2026-11', 'GSPC', '2026-11-27', '2026-12-05T12:00:00Z', '2026-12-05T12:01:00Z', 5);");

        using (var host = new Host(store.Root))
        {
            using var client = host.CreateClient();
            var unsummed = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=500"));

            // No pooled run for the month: each setup says so and no line of luck is drawn.
            Assert.Contains("<span class=\"degraded\" data-pooled=\"none\">not summed yet</span>", unsummed, StringComparison.Ordinal);
            Assert.DoesNotContain("pooled-luck", unsummed, StringComparison.Ordinal);
        }

        // Seven setups summed, the pullback passing with the S&P 600 carrying it and the breakout not passing; an older run
        // of the month the page does not read.
        store.Execute(string.Concat(
            PooledRow("loop-pooled-0", "pullback", PooledJudge.Own, "GSPC MID SML", "[]", 10, 0.5, 2, 0, false, null),
            PooledRow("loop-pooled-1", "pullback", PooledJudge.Own, "GSPC MID SML", "[\"a\",\"b\",\"c\"]", 1234, 0.001, 4, 3, true, "SML"),
            PooledRow("loop-pooled-1", "breakout", PooledJudge.Own, "GSPC SML", "[\"d\",\"e\"]", 310, 0.25, 4, 1, false, null),
            PooledRow("loop-pooled-1", "drift", PooledJudge.Own, "GSPC MID SML", "[]", 900, 0.5, 4, 2, false, null),
            PooledRow("loop-pooled-1", "dip", PooledJudge.Own, "GSPC MID SML", "[]", 900, 0.5, 4, 2, false, null),
            PooledRow("loop-pooled-1", "heavyweight", PooledJudge.Own, "GSPC MID SML", "[]", 90, 0.5, 4, 2, false, null),
            PooledRow("loop-pooled-1", "quality", PooledJudge.Own, "GSPC MID SML", "[]", 90, 0.5, 4, 2, false, null),
            PooledRow("loop-pooled-1", "improving", PooledJudge.Own, "GSPC MID SML", "[]", 90, 0.5, 4, 2, false, null),
            PooledRow("loop-pooled-1", "quality", "floor lowered", "GSPC MID", "[]", 90, 0.5, 4, 2, false, null)));

        using (var host = new Host(store.Root))
        {
            using var client = host.CreateClient();
            var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=500"));

            Assert.Contains("All three indexes together", page, StringComparison.Ordinal);
            Assert.Contains("<span class=\"pill pooled-pill\" data-pooled=\"passed\" data-indices=\"GSPC MID SML\" title=\"adjusted p 0.0010 against the bar 0.0042\">passed</span> <span class=\"loop-better\" data-better=\"3\" data-counted=\"4\">p 0.0010, better in 3 of 4 years, 1,234 trades on the S&P 500, S&P 400 and S&P 600; the S&P 600 carried it</span>", page, StringComparison.Ordinal);
            Assert.Contains("data-pooled=\"did not pass\" data-indices=\"GSPC SML\"", page, StringComparison.Ordinal);
            Assert.Contains("p 0.2500, better in 1 of 4 years, 310 trades on the S&P 500 and S&P 600</span>", page, StringComparison.Ordinal);
            Assert.DoesNotContain("not summed yet", page, StringComparison.Ordinal);

            // Worked by hand: seven setups' own rules summed, 0.05 a setup a year, 0.35, one pass in about three years.
            Assert.Contains("<p class=\"pooled-luck\" data-setups=\"7\" data-false-passes=\"0.35000000000000003\" data-years=\"3\">Passes luck alone would bring: about 0.35 a year across the 7 setups: one pass in about three years may be luck.</p>", page, StringComparison.Ordinal);
            Assert.True(page.IndexOf("pooled-luck", StringComparison.Ordinal) > page.IndexOf("loop-must", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void TheScorecardDrawsTheSetupAcrossTheThreeIndexesBeneathItsHalvesAndAVariantsSameChange()
    {
        var head = new PaperAccountView("live", "quality", PaperAccountView.Live, "the live rule", "the live S&P 400 quality rule", RuleVerdict.WithinLuck, null, "points", [], null, 0, 0, null, new DateOnly(2026, 10, 20));
        var lower = new PaperAccountView("v1", "quality", PaperAccountView.Variant, "the lower floor", "the S&P 400 quality rule at a score of 6 in 9", RuleVerdict.WithinLuck, null, "points", [], null, 0, 0, null, new DateOnly(2026, 10, 20)) { Tested = "mid lower" };
        var other = new PaperAccountView("v2", "quality", PaperAccountView.Variant, "the price unread", "the S&P 400 quality rule, its price not read", RuleVerdict.WithinLuck, null, "points", [], null, 0, 0, null, new DateOnly(2026, 10, 20)) { Tested = "mid unread" };
        LoopPooledRow Pooled(string family, string kind, string[] indices, string[] rules, int units, bool passed, string? carried) =>
            new("loop-pooled-1", "2026-11", family, kind, indices, rules, "points", units, 30, passed ? 0.001 : 0.3, passed, passed, 4, passed ? 3 : 1, 0.5, true, passed, null, carried);

        var page = new PaperPageView([head, lower, other], [], [], [], new PaperNotBought([]), [], null)
        {
            Pooled =
            [
                Pooled("quality", PooledJudge.Own, ["GSPC", "MID", "SML"], ["large live", "mid live", "small live"], 108, true, "MID"),
                Pooled("quality", "floor lowered", ["GSPC", "MID"], ["large lower", "mid lower"], 72, false, null),
                Pooled("pullback", PooledJudge.Own, ["GSPC", "MID", "SML"], [], 900, false, null),
            ],
        };
        var drawn = WebUtility.HtmlDecode(new MarkRenderer().PaperScorecard(page, new PaperLink("400"), "quality"));

        // Beneath the two halves, the head's setup across the three, carried by the S&P 400, and the passes luck alone would
        // bring over the two setups the run summed.
        Assert.True(drawn.IndexOf("<div class=\"score-pooled\" data-pooled=\"passed\">", StringComparison.Ordinal) > drawn.IndexOf("score-half live", StringComparison.Ordinal));
        Assert.Contains("<h4>All three indexes together</h4>", drawn, StringComparison.Ordinal);
        Assert.Contains("Its setup's rules on the S&P 500, S&P 400 and S&P 600, each at its own settings, summed into one test over the same years: it passed, better in 3 of 4 complete years over 108 months. One index carried it: without the S&P 400 the others together gained nothing. Each index's own verdict above stands, and an approval still changes one index's rule.", drawn, StringComparison.Ordinal);
        Assert.Contains("Passes luck alone would bring: about 0.1 a year across the 2 setups: one pass in about ten years may be luck.", drawn, StringComparison.Ordinal);

        // The lower floor's same change across the S&P 500 and 400 beside its verdict, and none beside the variant no run
        // summed.
        Assert.Contains("data-pooled=\"did not pass\" data-indices=\"GSPC MID\">all three indexes together, the same change on the S&P 500 and S&P 400: did not pass</span>", drawn, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(drawn, "the same change on"));

        // A family no run summed says so.
        var none = WebUtility.HtmlDecode(new MarkRenderer().PaperScorecard(page with { Pooled = [] }, new PaperLink("400"), "quality"));

        Assert.Contains("No monthly test has summed this setup across the three indexes yet.", none, StringComparison.Ordinal);
    }
}
