using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;
using Xunit;

namespace EquityBrief.Tests.Reading;

// Never stuck on the Improvement loop page, read back over a constructed store: since the monthly run before the newest, no idea
// passed, so the three nearest misses are named closest first, each with what the researcher tried next or the day it reads it,
// a miss from before that run left out; the researcher's failed week counted beside the day the next runs; an idea passed since
// saying so; and a store holding no monthly run saying there is no month to read.
// see: A month with no idea passed names its nearest misses and what the researcher tried next, and a failed week is counted and never stops the next
public partial class ReadSurface
{
    internal static string[] NeverStuckRows =>
    [
        CheckReach.Key("15.20 The Loop page", "Never stuck"),
    ];

    static string Noted(int id, string index, string family, string words, double? adjusted, string entered) =>
        $"({id}, '{entered}', 'the researcher', '{index}', '{family}', '{words}', 'line {id}', {(adjusted is { } p ? p.ToString(System.Globalization.CultureInfo.InvariantCulture) : "NULL")}, {1.0 / 1040}, 'near miss', 'why', '2026-11-27', 'r')";

    [Fact]
    public async Task TheLoopPageNamesTheNearestMissesWhereNoIdeaPassedAndCountsTheFailedWeeksBesideTheNext()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES "
            + "('loop-month-2026-11', 'loop-month-tester', '2026-11-07T12:00:00Z', '2026-11-07T13:00:00Z', 'ok', 1, 0, 0, '0', 'a'), "
            + "('loop-month-2026-11', 'loop-month-long', '2026-11-07T13:00:00Z', '2026-11-07T15:00:00Z', 'ok', 1, 0, 0, '0', 'a'), "
            + "('loop-month-2026-12', 'loop-month-tester', '2026-12-05T12:00:00Z', '2026-12-05T13:00:00Z', 'ok', 1, 0, 0, '0', 'b'), "
            + "('research-1', 'research', '2026-11-29T13:00:00Z', '2026-11-29T15:00:00Z', 'ok', 5, 30, 0, '0', 'ok'), "
            + "('research-2', 'research', '2026-12-06T13:00:00Z', '2026-12-06T13:10:00Z', 'failed', 0, 72, 0, '0', 'failed');");
        store.Execute(
            "INSERT INTO notebook (id, entered_at, source, index_code, family, words, line, adjusted, bar, verdict, why, tested_on, run_id) VALUES "
            + string.Join(", ",
                Noted(1, "SML", "pullback", "Small cap pullbacks with momentum", 0.002, "2026-11-29T15:00:00Z"),
                Noted(2, "MID", "breakout", "Mid cap breakouts on heavy volume", 0.004, "2026-11-29T15:00:00Z"),
                Noted(3, "GSPC", "drift", "Large cap drift after a strong quarter", 0.003, "2026-11-29T15:00:00Z"),
                Noted(4, "SML", "dip", "Small cap dips at the lows", 0.01, "2026-11-29T15:00:00Z"),
                Noted(5, "SML", "dip", "An older near miss", 0.001, "2026-11-01T15:00:00Z")) + ";");
        store.Execute(
            "INSERT INTO research_idea (run_id, index_code, family, rank, line, reason, outcome, notebook_id, written_at) VALUES "
            + "('research-3', 'SML', 'pullback', 1, 'event pullback(600) · if rsi above tenth 5 · buy next open · stop 1.5 typical days · take profit none · sell after 63 sessions · cap 5 a night by rsi highest', 'Narrow the band where momentum holds.', 'waiting', NULL, '2026-12-06T15:00:00Z');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        string Card(string page) => Blocks(WebUtility.HtmlDecode(page), "<section class=\"card\" data-card=\"loop-stuck\">.*?</section>").Single();

        var card = Card(await client.GetStringAsync("/screens/loop?universe=600"));

        Assert.Contains("<h2>Is the loop finding anything</h2>", card, StringComparison.Ordinal);
        Assert.Contains("<p data-summary data-stuck=\"stuck\">No idea has passed since the monthly run of 7 Nov 2026.</p>", card, StringComparison.Ordinal);

        // The three nearest by hand, 1 in 500, 1 in 333 and 1 in 250; the older miss before the run and the fourth left out.
        Assert.Equal(["1", "3", "2"], [.. Regex.Matches(card, "<li data-miss=\"([0-9]+)\">").Select(match => match.Groups[1].Value)]);
        Assert.Contains("<b>Pullbacks to support, S&P 600</b>: Small cap pullbacks with momentum</p><p class=\"lm-small\">It reached 1 in 500, the bar 1 in 1040.</p>", card, StringComparison.Ordinal);
        Assert.Contains("What the researcher tried next: Narrow the band where momentum holds.</p><code class=\"idea-line\">event pullback(600) · if rsi above tenth 5", card, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(card, "data-next=\"waiting\">The researcher reads it among the setup's tested ideas on [A-Z][a-z]{2} [0-9]{1,2} [A-Z][a-z]{2}.</p>").Count);
        Assert.DoesNotContain("An older near miss", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Small cap dips at the lows", card, StringComparison.Ordinal);

        // The failed week counted beside the next, which runs regardless.
        Assert.Matches("data-stuck=\"failed\">1 of the researcher's 2 week\\(s\\) since failed: each was asked once more before it was counted. The next week runs [A-Z][a-z]{2} [0-9]{1,2} [A-Z][a-z]{2} regardless.</p>", card);

        // An idea passed since: the summary says so and names no miss.
        store.Execute("INSERT INTO survivor (notebook_id, index_code, family, line, cuts, passed_on, entered_at, run_id) VALUES (1, 'SML', 'pullback', 'line 1', '{}', '2026-11-27', '2026-12-06T15:00:00Z', 'r');");

        var passed = Card(await client.GetStringAsync("/screens/loop?universe=600"));

        Assert.Contains("<p data-summary data-stuck=\"passed\">1 idea(s) passed since the monthly run of 7 Nov 2026, each running as its setup's variant or queued for a slot.</p>", passed, StringComparison.Ordinal);
        Assert.DoesNotContain("data-miss=", passed, StringComparison.Ordinal);

        // A store holding no monthly run has no month to read.
        using var empty = new TemporaryStore().Migrated();
        using var emptyHost = new Host(empty.Root);
        using var emptyClient = emptyHost.CreateClient();

        Assert.Contains("No monthly run has been stored yet, so there is no month to read.", Card(await emptyClient.GetStringAsync("/screens/loop?universe=400")), StringComparison.Ordinal);
    }
}
