using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Shortlist;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, 12.3: the run page's Calibration region, each figure read back off the page against
// the test's own arithmetic over a constructed store.
public partial class ReadSurface
{
    const int CalibrationMembers = 20;

    // Four nights over twenty members. Each gate passes a run of members from the first, so a count
    // through the funnel is the smallest run so far: the numbers are the members through trend and
    // strength, the setup, the trigger and the trade, in that order. Unusual volume fires on a run of
    // members too, and the fourth night trades at twice its volume.
    static readonly (string Night, int[] Through, int Unusual, double Ratio)[] CalibrationNights =
    [
        ("2026-09-28", [10, 6, 4, 2], 1, 1.0),
        ("2026-09-29", [12, 6, 3, 2], 1, 0.9),
        ("2026-09-30", [8, 4, 2, 1], 1, 0.95),
        ("2026-10-01", [18, 15, 12, 10], 12, 2.0),
    ];

    static TemporaryStore CalibrationStore(string version = ShapeClock.NoVersion)
    {
        var store = new TemporaryStore().Migrated();

        foreach (var (night, through, unusual, ratio) in CalibrationNights)
        {
            for (var member = 0; member < CalibrationMembers; member++)
            {
                var ticker = "T" + member.ToString("00", CultureInfo.InvariantCulture);
                int Flag(int gate) => member < through[gate] ? 1 : 0;

                store.Execute(
                    "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, family, trigger_pass, trigger_event, trade, exclusions, passed, gates) " +
                    $"VALUES ('{ticker}', '{night}', '{version}', 'test', 1, {Flag(0)}, {Flag(1)}, NULL, {Flag(2)}, 0, {Flag(3)}, '[]', {Flag(3)}, '{{\"gates\":[],\"notes\":[]}}');" +
                    "INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) " +
                    $"VALUES ('{ticker}', '{night}', '{CurrentReasons(member < unusual)}', 0, '{{}}', '{{\"candidates\":[],\"skipped\":[]}}');");
            }

            store.Execute(
                "INSERT INTO market_reading (session_date, members, counted, above, breadth, counted_context, above_context, breadth_context, volume_counted, median_volume_ratio) " +
                $"VALUES ('{night}', {CalibrationMembers}, {CalibrationMembers}, 12, 0.6, {CalibrationMembers}, 10, 0.5, {CalibrationMembers}, {ratio.ToString(CultureInfo.InvariantCulture)});");
        }

        return store;
    }

    // A row's six reasons in the form the current rules write, unusual volume fired or not and every
    // other reason not fired.
    static string CurrentReasons(bool unusual) =>
        "[" + string.Join(",", ShortlistSeries.Reasons.Select(reason =>
        {
            var values = reason switch
            {
                ShortlistSeries.EarningsSoon => "{\"next dated event\":\"2026-11-30\",\"horizon\":\"20\"}",
                ShortlistSeries.BreakoutOnVolume => "{\"close\":\"1\",\"previous close\":\"1\"}",
                ShortlistSeries.UnusualVolume => "{\"volume\":\"1\",\"multiple\":\"2\"}",
                _ => "{}",
            };

            return $"{{\"name\":\"{reason}\",\"fired\":{(reason == ShortlistSeries.UnusualVolume && unusual ? "true" : "false")},\"values\":{values}}}";
        })) + "]";

    [Fact]
    public async Task TheCalibrationRegionStatesTheShapeClockAsTheTestsOwnArithmeticGivesIt()
    {
        using var store = CalibrationStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // Worked by hand. The fourth night trades at 2.0 and is an event, as it is too for unusual volume
        // at 12 of 20 against a median of 1 in 20, for the trigger at 12 of 20 against a median of 3.5,
        // and for the trade at 10 of 20 against a median of 2; trend and strength's median share is 0.55
        // and the setup's 0.3, above a quarter, so neither is read for it. Over the three ordinary nights
        // the medians are 10 through trend and strength, 6 through the setup, 3 through the trigger, 2
        // through the trade and 2 on the list, and unusual volume's share is 1 in 20 on each.
        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-09-30"));
        var region = Assert.Single(Blocks(page, "<section class=\"calibration\".*?</section>"));

        Assert.Contains("data-version=\"none\" data-window=\"4\" data-ordinary=\"3\" data-wanted=\"60\" data-crossed=\"false\"", region, StringComparison.Ordinal);
        Assert.Contains("No filter version is open, so no night counts toward the 60 yet: 3 ordinary night(s) of 4 are stored under section 17's proposed values.", region, StringComparison.Ordinal);
        Assert.Contains("The night of 2026-09-30 is an ordinary night", region, StringComparison.Ordinal);

        (string Measure, double Median, int Low, int High)[] expected =
        [
            (SwingGates.Trend, 10, 38, 347),
            (SwingGates.Setup, 6, 14, 126),
            (SwingGates.Trigger, 3, 6, 56),
            (SwingGates.Trade, 2, 1, 12),
            (ShapeClock.ListMeasure, 2, 1, 9),
        ];

        foreach (var (measure, median, low, high) in expected)
        {
            Assert.Contains(
                $"<tr data-measure=\"{measure}\" data-median=\"{median.ToString("R", CultureInfo.InvariantCulture)}\" data-low=\"{low}\" data-high=\"{high}\" data-measured=\"false\">",
                region,
                StringComparison.Ordinal);
        }

        // Until the trigger every median says it is not yet measured.
        Assert.Equal(5, Regex.Matches(region, ", not yet measured</td>").Count);

        Assert.Contains(
            "Event nights, left out of every median: 2026-10-01, trigger at 60.0% of the index against its median of 17.5% and trade at 50.0% of the index against its median of 10.0% and unusual volume at 60.0% of the index against its median of 5.0% and the index trading at 2.00 times its fifty-day volume.",
            region,
            StringComparison.Ordinal);

        Assert.Contains(
            $"<tr data-reason=\"{ShortlistSeries.UnusualVolume}\" data-share=\"0.05\" data-median=\"0.05\">",
            region,
            StringComparison.Ordinal);

        // No due line before the trigger, and the event night's own page says it is one.
        Assert.DoesNotContain("data-due=\"shape\"", page, StringComparison.Ordinal);
        Assert.Contains(
            "The night of 2026-10-01 is an event night:",
            WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-10-01")),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SixtyOrdinaryNightsUnderAnOpenVersionPutTheDueLineAtTheTopOfTheRunPage()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute("INSERT INTO filter_version (version, settings, opened_at, closed_at, evidence) " +
            $"VALUES ('v1', '{FilterSettings.Proposed.Write()}', '2026-06-01T00:00:00Z', NULL, 'the operator''s ruling');");

        var sessions = Enumerable.Range(0, 200)
            .Select(day => new DateOnly(2026, 6, 1).AddDays(day))
            .Where(day => day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            .Take(ShapeClock.CalibrationNights)
            .Select(day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToArray();

        foreach (var night in sessions)
        {
            store.Execute(
                "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, family, trigger_pass, trigger_event, trade, exclusions, passed, gates) " +
                $"VALUES ('T00', '{night}', 'v1', 'test', 1, 1, 1, 'pullback', 1, 1, 1, '[]', 1, '{{\"gates\":[],\"notes\":[]}}');");
        }

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // Fifty-nine nights in, nothing is due; on the sixtieth the page opens with the line.
        var before = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{sessions[^2]}"));

        store.Execute($"DELETE FROM gate_result WHERE session_date = '{sessions[^1]}';");
        Assert.DoesNotContain("data-due=\"shape\"", WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{sessions[^2]}")), StringComparison.Ordinal);

        store.Execute(
            "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, family, trigger_pass, trigger_event, trade, exclusions, passed, gates) " +
            $"VALUES ('T00', '{sessions[^1]}', 'v1', 'test', 1, 1, 1, 'pullback', 1, 1, 1, '[]', 1, '{{\"gates\":[],\"notes\":[]}}');");

        var due = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{sessions[^1]}"));
        var line = Regex.Match(due, "<p class=\"due\" data-due=\"shape\">(.*?)</p>");

        Assert.True(line.Success, "no due line on the run page once the trigger is crossed");
        Assert.Equal("Shape calibration is due: 60 ordinary nights under filter version v1 are stored, against the 60 it waits on.", line.Groups[1].Value);
        Assert.True(line.Index < due.IndexOf("data-card=\"operational\"", StringComparison.Ordinal), "the due line is not at the top of the page");
        Assert.Contains("data-crossed=\"true\"", due, StringComparison.Ordinal);
        Assert.DoesNotContain(", not yet measured</td>", due, StringComparison.Ordinal);
        Assert.Contains("data-crossed=\"true\"", before, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRegionOpensWithTheSentenceSectionThirteenStatesAndCountsWhatElseIsWaiting()
    {
        using var store = CalibrationStore();

        // Two nights the clock chose the session for closed, and one run by hand for a named session and one
        // that stopped do not count; the first of the two replayed a merge distance version and four others,
        // the second only the merge distance; two research passes carry a recorded cost; and the version
        // whose new label holds two nights scored three nights since its window opened, a night written
        // in sample before it not among them.
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES " +
            "('night-20260910T233000Z', 'close', '2026-09-10T23:40:00Z', '2026-09-10T23:41:00Z', 'ok', 1, 0, 0, '0', '')," +
            "('night-20260911T233000Z', 'close', '2026-09-11T23:40:00Z', '2026-09-11T23:41:00Z', 'ok', 1, 0, 0, '0', '')," +
            "('night-20260912T010000Z-for-2026-09-11', 'close', '2026-09-12T01:10:00Z', '2026-09-12T01:11:00Z', 'ok', 1, 0, 0, '0', '')," +
            "('night-20260914T233000Z', 'close', '2026-09-14T23:40:00Z', '2026-09-14T23:41:00Z', 'stopped', 0, 0, 0, '0', '')," +
            "('night-20260910T233000Z', 'rule-versions', '2026-09-10T23:39:00Z', '2026-09-10T23:40:00Z', 'ok', 1, 0, 0, '0', '8 open version(s), 5 replayed with 1 of the merge distance, over 503 name(s)')," +
            "('night-20260911T233000Z', 'rule-versions', '2026-09-11T23:39:00Z', '2026-09-11T23:40:00Z', 'ok', 1, 0, 0, '0', '2 open version(s), 1 replayed with 1 of the merge distance, over 503 name(s)')," +
            "('research-20260910T010000Z-KEYS', 'research call: The short version', '2026-09-10T01:00:00Z', '2026-09-10T01:01:00Z', 'ok', 1, 1, 0, '0.012', '')," +
            "('research-20260910T010000Z-KEYS', 'research call: The risks', '2026-09-10T01:01:00Z', '2026-09-10T01:02:00Z', 'ok', 1, 1, 0, '0.02', '')," +
            "('research-20260911T010000Z-MSFT', 'research call: The short version', '2026-09-11T01:00:00Z', '2026-09-11T01:01:00Z', 'ok', 1, 1, 0, '0.015', '');" +
            "INSERT INTO rule_version (rule, version, parameters, parameters_hash, code_version, opened_at, closed_at, replaced_by, evidence) VALUES " +
            "('the trend rule', 'the new label holds two nights', '{\"downtrendFromAverages\": 0, \"nightsTheNewLabelHolds\": 2}', 'h', 'c', '2026-09-10T00:00:00Z', NULL, NULL, '');" +
            "INSERT INTO version_score (ticker, session_date, rule, version, opened_at, plan, sample) VALUES " +
            "('T00', '2026-09-10', 'the trend rule', 'the new label holds two nights', '2026-09-10T00:00:00Z', '{}', 'scored')," +
            "('T01', '2026-09-10', 'the trend rule', 'the new label holds two nights', '2026-09-10T00:00:00Z', '{}', 'scored')," +
            "('T00', '2026-09-11', 'the trend rule', 'the new label holds two nights', '2026-09-10T00:00:00Z', '{}', 'scored')," +
            "('T00', '2026-09-14', 'the trend rule', 'the new label holds two nights', '2026-09-10T00:00:00Z', '{}', 'scored')," +
            "('T00', '2026-09-09', 'the trend rule', 'the new label holds two nights', '2026-09-10T00:00:00Z', '{}', 'in_sample');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var region = Assert.Single(Blocks(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-09-30")), "<section class=\"calibration\".*?</section>"));

        // The sentence, held once in code, opens the region and opens section 13.8 word for word.
        Assert.Contains($"<p class=\"timeline\">{ShapeClock.Timeline}</p>", region, StringComparison.Ordinal);

        var thirteen = Regex.Match(WebUtility.HtmlDecode(EquityBrief.Tests.Checks.Corpus.Read("docs/ARCHITECTURE.html")), "<h3>13\\.8 [^<]*</h3>\\s*<p>(.*?)</p>", RegexOptions.Singleline);

        Assert.True(thirteen.Success, "section 13.8 carries no paragraph");
        Assert.StartsWith(ShapeClock.Timeline + " ", thirteen.Groups[1].Value, StringComparison.Ordinal);

        var lines = Regex.Matches(region, "<li data-trigger=\"([^\"]+)\" data-count=\"(\\d+)\" data-of=\"(\\d+)\">(.*?)</li>")
            .ToDictionary(match => match.Groups[1].Value, match => (Count: match.Groups[2].Value, Of: match.Groups[3].Value, Text: match.Groups[4].Value), StringComparer.Ordinal);

        Assert.Equal(["wall clock", "spend cap", "version bound", "event setups", "trend confirmation"], lines.Keys);
        Assert.Equal(("2", "5"), (lines["wall clock"].Count, lines["wall clock"].Of));
        Assert.Equal(("2", "20"), (lines["spend cap"].Count, lines["spend cap"].Of));
        Assert.Equal(("1", "5"), (lines["version bound"].Count, lines["version bound"].Of));
        Assert.Equal(("0", "250"), (lines["event setups"].Count, lines["event setups"].Of));
        Assert.Equal(("3", "60"), (lines["trend confirmation"].Count, lines["trend confirmation"].Of));
        Assert.Contains("no stage scores an event-book setup yet, so nothing counts toward it", lines["event setups"].Text, StringComparison.Ordinal);
        Assert.Contains("scored under 'the new label holds two nights' since its window opened", lines["trend confirmation"].Text, StringComparison.Ordinal);
    }
}
