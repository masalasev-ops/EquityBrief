using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The week's research on the Improvement loop page, read back over a constructed store: the newest week on the index the page
// reads with its counts; each idea's sentence above its line and what became of it, its verdict where it was tested; each
// setup's failure list over the early years with its shares, and a line where none is listed; another index's ideas absent;
// a week counted failed saying so; and a store holding no week saying the researcher has not run.
// see: The researcher reads each rule's failure list over the early years and the nights since the restart, and the later years as one word
public partial class ReadSurface
{
    internal static string[] WeeklyResearchRows =>
    [
        CheckReach.Key("15.20 The Loop page", "This week's research"),
    ];

    [Fact]
    public async Task TheLoopPageDrawsTheResearchersNewestWeekWithItsIdeasAndWhereEachSetupsLosersGather()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute("INSERT INTO notebook (entered_at, source, index_code, family, words, line, verdict, why, tested_on, run_id) VALUES ('2026-11-29T15:00:00Z', 'the researcher', 'SML', 'pullback', 'w', 'event pullback(600) · if rsi above tenth 3 · buy next open · stop 1.5 typical days · take profit none · sell after 63 sessions · cap 5 a night by strength highest', 'failed', 'Reached 1 in 7, the bar 1 in 1040.', '2026-11-27', 'r');");
        store.Execute(
            "INSERT INTO research_idea (run_id, index_code, family, rank, line, reason, outcome, notebook_id, written_at) VALUES "
            + "('research-2', 'SML', 'pullback', 1, 'event pullback(600) · if rsi above tenth 3 · buy next open · stop 1.5 typical days · take profit none · sell after 63 sessions · cap 5 a night by strength highest', 'Its losers gather where momentum is weakest.', 'tested', 1, '2026-11-29T15:00:00Z'), "
            + "('research-2', 'SML', 'breakout', 1, 'event breakout(600) · if volume_ratio above tenth 7 · buy next open · stop 2 typical days · take profit none · sell after 63 sessions · cap 5 a night by strength highest', 'Heavy volume holds its winners.', 'waiting', NULL, '2026-11-29T15:00:00Z'), "
            + "('research-2', 'SML', 'dip', 1, 'event dip(600) · if rsi below tenth 3 · buy next open · stop 1 typical days · take profit none · sell after 10 sessions · cap 5 a night by rsi lowest', 'The deepest dips recover.', 'held', NULL, '2026-11-29T15:00:00Z'), "
            + "('research-2', 'GSPC', 'pullback', 1, 'event pullback(500) · if rsi above tenth 3 · buy next open · stop 1 typical days · take profit none · sell after 63 sessions · cap 5 a night by rsi lowest', 'An S&P 500 idea the S&P 600 page does not draw.', 'tested', NULL, '2026-11-29T15:00:00Z'), "
            + "('research-1', 'SML', 'drift', 1, 'event drift(600) · if rsi above tenth 3 · buy next open · stop 1 typical days · take profit none · sell after 60 sessions · cap 5 a night by rsi lowest', 'An older week the page does not draw.', 'tested', NULL, '2026-11-22T15:00:00Z');");
        store.Execute("INSERT INTO research_failure (run_id, index_code, family, population, reading, highest, picks, losers, in_band, losers_in_band) VALUES ('research-2', 'SML', 'pullback', 'early', 'rsi', 0, 1000, 500, 300, 210);");
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES "
            + "('research-1', 'research', '2026-11-22T13:00:00Z', '2026-11-22T15:00:00Z', 'ok', 1, 30, 0, '0', 'older'), "
            + "('research-2', 'research', '2026-11-29T13:00:00Z', '2026-11-29T15:00:00Z', 'ok', 5, 34, 0, '0', 'newer');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var research = Blocks(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=600")), "<section class=\"card\" data-card=\"loop-research\">.*?</section>").Single();

        Assert.Contains("<h2>What the researcher proposed</h2>", research, StringComparison.Ordinal);
        Assert.Contains("<p data-summary data-research=\"week\">The week of 29 Nov 2026 on the S&P 600: 3 idea(s), 1 tested, 1 waiting, 1 already in the notebook.</p>", research, StringComparison.Ordinal);

        // The ideas by setup, each sentence above its line, what became of each.
        Assert.Equal(["waiting", "held", "tested"], [.. Regex.Matches(research, "<tr data-research=\"([a-z]+)\">").Select(match => match.Groups[1].Value)]);
        Assert.Contains("Its losers gather where momentum is weakest.<code class=\"idea-line\">event pullback(600) · if rsi above tenth 3", research, StringComparison.Ordinal);
        Assert.Contains("<td data-h=\"What became of it\"><b>Failed</b> Reached 1 in 7, the bar 1 in 1040.</td>", research, StringComparison.Ordinal);
        Assert.Contains("Already in the notebook, so not tested again.", research, StringComparison.Ordinal);
        Assert.Contains("Waits for next week's tests.", research, StringComparison.Ordinal);
        Assert.DoesNotContain("An S&P 500 idea", research, StringComparison.Ordinal);
        Assert.DoesNotContain("An older week", research, StringComparison.Ordinal);

        // The pullback's failure list over the early years: rsi's lowest three tenths hold 30% of its picks and, by hand,
        // 210 of its 500 losers, 42%; its live list and every other setup's say none is listed.
        Assert.Contains("Over 2005 to 2018, of 1,000 closed picks, 500 losers:", research, StringComparison.Ordinal);
        Assert.Contains("<td data-h=\"Reading\">rsi</td><td data-h=\"Band\">its lowest three tenths</td><td data-h=\"Share of its losers\" class=\"r num\">42%</td><td data-h=\"Share of its picks\" class=\"r num\">30%</td><td data-h=\"Picks in the band\" class=\"r num\">300</td>", research, StringComparison.Ordinal);
        Assert.Equal(7, Regex.Matches(research, "data-failures=\"(early|live)\">[^<]*: no reading holds 30 of its closed picks").Count);

        // A week counted failed says so, and a store holding no week says the researcher has not run.
        store.Execute("INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES ('research-3', 'research', '2026-12-06T13:00:00Z', '2026-12-06T13:10:00Z', 'failed', 0, 72, 0, '0', 'failed: no answer');");
        Assert.Contains("The week of 6 Dec 2026 failed: the model answered nothing on either asking, so the week is counted as failed.", WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=600")), StringComparison.Ordinal);

        using var empty = new TemporaryStore().Migrated();
        using var emptyHost = new Host(empty.Root);
        using var emptyClient = emptyHost.CreateClient();

        Assert.Contains("The researcher has not run yet. It runs each Sunday at 13:00 UTC.", WebUtility.HtmlDecode(await emptyClient.GetStringAsync("/screens/loop?universe=400")), StringComparison.Ordinal);
    }
}
