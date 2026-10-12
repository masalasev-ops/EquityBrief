using System.Net;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Candidates;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Indices;

namespace EquityBrief.Tests.Reading;

// read-surface, 17.11: on the S&P 400 or 600 a book registered before its family's freeze is listed after the provisional
// rule and, chosen in the link, drawn from its own holdings under the band with the line that it did not pass the
// year-by-year test at the adjusted p the index's newest tester run stored for its design's six settings, the
// twelve-month book's adding that it reads one session short of the search's setting; a run storing no verdict draws no
// line, and an older run's verdict is not read.
// see: An index family's variants may stand registered before its freeze, its provisional rule staying the live one
public partial class ReadSurface
{
    [Fact]
    public async Task ABookRegisteredBeforeAFreezeIsDrawnFromItsOwnHoldingsWithTheLineThatItDidNotPassTheYearByYearTest()
    {
        using var store = UniversesStore();

        var (registrations, refusal) = IndexRules.Variants(
            "heavyweight",
            "MID",
            [
                new Dictionary<string, double> { [IndexRuleCandidate.QualityParameter] = 2 },
                new Dictionary<string, double> { [IndexHeavyweightCandidate.TwelveOneParameter] = 1 },
            ],
            [],
            new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

        Assert.Null(refusal);

        foreach (var (registration, place) in registrations!.Select((one, place) => (one, place)))
        {
            RegisteredRule(store, 400 + place, registration, "2026-10-01T12:00:00Z");
        }

        var (cover, twelve) = (registrations![0].Candidate, registrations[1].Candidate);

        // Each book's own holding at the night's close: the cover's M1 and the twelve-month book's M3.
        store.Execute(
            "INSERT INTO index_heavyweight_rule_holding (candidate, index_code, ticker, entered_on, sector, entry_close, growth, cut, through, ended_on, exit_close, reason, result, cut_return, cost) VALUES " +
            $"('{Doubled(cover)}', 'MID', 'M1', '{IndexNight}', 'Energy', '50', 1.0, '[]', '{IndexNight}', NULL, NULL, NULL, NULL, NULL, NULL), " +
            $"('{Doubled(twelve)}', 'MID', 'M3', '{IndexNight}', 'Energy', '40', 1.0, '[]', '{IndexNight}', NULL, NULL, NULL, NULL, NULL, NULL);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<string> Card(string? chosen) =>
            HeavyweightCardOf(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400{(chosen is null ? string.Empty : "&heavyweight=" + RunScreen.Slug(chosen))}")));

        // No tester run is stored, so a chosen book draws no line.
        Assert.DoesNotContain("variant-untested", await Card(twelve), StringComparison.Ordinal);

        // The newest run read design (a)'s six settings at 0.5999 and did not pass them; an older run that passed them is
        // not read.
        foreach (var (run, month, adjusted, passed) in new[] { ("loop-test-MID-20260905T120000Z", "2026-09", "0.001", 1), ("loop-test-MID-20261011T231515Z", "2026-10", "0.5999", 0) })
        {
            store.Execute($"INSERT INTO loop_run (run_id, month, index_code, through, started_at, ended_at, folds) VALUES ('{run}', '{month}', 'MID', '2026-10-08', '2026-10-09T12:00:00Z', '2026-10-09T12:01:00Z', 5);");
            store.Execute(
                "INSERT INTO loop_proposal (run_id, index_code, family, proposal, words, current_words, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, detectable, stable_folds, passed, finding) "
                + $"VALUES ('{run}', 'MID', 'heavyweight', '{EquityBrief.Core.Loop.SixSettings.DesignA}', NULL, 'the rule today', 'points', 57, 18, {adjusted}, {passed}, {passed}, 4, 2, '0.1', 1, '0.5', 3, {passed}, NULL);");
        }

        // By default the provisional rule, selected, then each book by its number, with no band, no line saying none is
        // registered and no line on a test.
        var page = await Card(null);

        Assert.Contains(
            $"<option value=\"{RuleScreenWords.LiveSlug}\" selected data-variant=\"none\">{RuleScreen.ProvisionalChoice}</option><option value=\"{RunScreen.Slug(cover)}\" data-variant=\"1\">Variant 1: {cover}</option><option value=\"{RunScreen.Slug(twelve)}\" data-variant=\"2\">Variant 2: {twelve}</option>",
            page,
            StringComparison.Ordinal);
        Assert.DoesNotContain("no-variant", page, StringComparison.Ordinal);
        Assert.DoesNotContain("variant-band", page, StringComparison.Ordinal);
        Assert.DoesNotContain("variant-untested", page, StringComparison.Ordinal);

        // The cover's book: its own holding under the band, with the line and no word of the twelve-month strength.
        var covered = await Card(cover);
        const string Untested = "This book did not pass the year-by-year test: the run for 2026-10 read design (a)'s six settings at an adjusted p of 0.60 against the bar of 0.0042, so it does not read as better than the provisional rule.";

        Assert.Contains($"<p class=\"variant-band\" role=\"status\" data-variant=\"1\">{RuleScreenWords.Band(1)}</p><p class=\"variant-untested\" data-adjusted=\"0.5999\">{Untested}</p>", covered, StringComparison.Ordinal);
        Assert.Contains("<tr data-ticker=\"M1\"", covered, StringComparison.Ordinal);
        Assert.DoesNotContain("<tr data-ticker=\"M3\"", covered, StringComparison.Ordinal);

        // The twelve-month book: its own holding, and the line saying it reads one session short of the search's setting.
        var twelveMonth = await Card(twelve);

        Assert.Contains(
            $"<p class=\"variant-untested\" data-adjusted=\"0.5999\">{Untested} It reads twelve months' strength from 251 sessions back, the oldest close the night holds, one session short of the 252 the search read.</p>",
            twelveMonth,
            StringComparison.Ordinal);
        Assert.Contains("<tr data-ticker=\"M3\"", twelveMonth, StringComparison.Ordinal);
        Assert.DoesNotContain("<tr data-ticker=\"M1\"", twelveMonth, StringComparison.Ordinal);
    }
}
