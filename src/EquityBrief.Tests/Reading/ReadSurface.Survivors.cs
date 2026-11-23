using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The survivors on the Improvement loop page, read back over a constructed store: the counts on the index the page reads; the
// queue in the order its survivors passed, each with its line, the nights it has been walked, its record so far worked from
// the stored bars and why it waits, under the rule for making room; a retired survivor with why; another index's absent; and
// a store holding none saying so.
// see: A survivor that has to queue is walked each night as a record drawn among no buys and carries it when a slot opens
public partial class ReadSurface
{
    internal static string[] SurvivorRows =>
    [
        CheckReach.Key("15.20 The Loop page", "Survivors and the queue"),
    ];

    const string WaitingLine = "event breakout(600) · if volume_ratio above tenth 8 · buy next open · stop 2 typical days · take profit whole at 2 risks · sell after 20 sessions · cap 5 a night by strength highest";

    [Fact]
    public async Task TheLoopPageDrawsTheQueueInTheOrderItPassedWithEachRecordSoFarAndWhyItWaits()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO survivor (notebook_id, index_code, family, line, cuts, passed_on, entered_at, run_id) VALUES "
            + $"(1, 'SML', 'breakout', '{WaitingLine}', '{{}}', '2026-11-16', '2026-11-16T23:00:00Z', 'r'), "
            + $"(2, 'SML', 'breakout', '{WaitingLine.Replace("tenth 8", "tenth 7", StringComparison.Ordinal)}', '{{}}', '2026-11-17', '2026-11-17T23:00:00Z', 'r'), "
            + $"(3, 'SML', 'breakout', '{WaitingLine.Replace("tenth 8", "tenth 9", StringComparison.Ordinal)}', '{{}}', '2026-11-10', '2026-11-10T23:00:00Z', 'r'), "
            + $"(4, 'GSPC', 'breakout', '{WaitingLine.Replace("(600)", "(500)", StringComparison.Ordinal)}', '{{}}', '2026-11-16', '2026-11-16T23:00:00Z', 'r');");
        store.Execute(
            "INSERT INTO survivor_state (survivor_id, at, state, candidate, why, run_id) VALUES "
            + "(1, '2026-11-16T23:00:00Z', 'queued', NULL, 'Its setup sells on its own time limit of 63 sessions.', 'r'), "
            + "(2, '2026-11-17T23:00:00Z', 'queued', NULL, 'All 9 slots held; none can make room yet, the nearest having run 40 of the 63 nights.', 'r'), "
            + "(3, '2026-11-10T23:00:00Z', 'queued', NULL, 'Its setup sells on its own time limit of 63 sessions.', 'r'), "
            + "(3, '2026-11-18T23:00:00Z', 'retired', 'the breakout survivor 3', 'Retired to make room for survivor 5.', 'r'), "
            + "(4, '2026-11-16T23:00:00Z', 'queued', NULL, 'An S&P 500 survivor the S&P 600 page does not draw.', 'r');");

        // The first survivor's one pick, walked by hand: the night's close 100 and its typical move 1, bought at the next open
        // of 101 with its stop 2 under at 99, the close of 106 reaching 2 times the risk, 105, sold at the next open of 106:
        // (106 - 101) / 2 = 2.5 risks. The second's pick has one session after it and is still open.
        store.Execute("INSERT INTO survivor_pick (survivor_id, session_date, ticker, last_close, typical_move, run_id) VALUES (1, '2026-11-16', 'AAA', '100', 1.0, 'r'), (2, '2026-11-19', 'BBB', '50', 1.0, 'r');");

        foreach (var (ticker, session, open, close) in new[]
        {
            ("AAA", "2026-11-16", "100", "100"), ("AAA", "2026-11-17", "101", "102"), ("AAA", "2026-11-18", "103", "104"), ("AAA", "2026-11-19", "104", "106"), ("AAA", "2026-11-20", "106", "106"),
            ("BBB", "2026-11-19", "50", "50"), ("BBB", "2026-11-20", "51", "51"),
        })
        {
            store.Execute($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) VALUES ('{ticker}', '{session}', '{open}', '{close}', '{open}', '{close}', 1000, 'eod', '{session}T22:00:00Z');");
        }

        store.Execute("INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) VALUES ('ZZZ', '2026-11-20', '[]', 0, '{}', '[]');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var small = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=600"));
        var survivors = Blocks(small, "<section class=\"card\" data-card=\"loop-survivors\">.*?</section>").Single();

        Assert.Contains("<h2>Ideas that passed, running by themselves</h2>", survivors, StringComparison.Ordinal);
        Assert.Contains("<p data-summary data-survivors=\"count\">On the S&P 600: 0 running in the background, 2 waiting for a slot, 1 retired to make room.</p>", survivors, StringComparison.Ordinal);
        Assert.Contains(SinglePageApp.QueueLine, survivors, StringComparison.Ordinal);

        // The queue in the order it passed, 16 Nov before 17 Nov; walked from the night it passed to the newest night, 20 Nov:
        // five nights for the first and four for the second.
        Assert.Equal(["queued", "queued", "retired"], [.. Regex.Matches(survivors, "<tr data-survivor=\"([a-z]+)\">").Select(match => match.Groups[1].Value)]);
        Assert.Contains($"<td data-h=\"#\">1</td><td data-h=\"Survivor\">", survivors, StringComparison.Ordinal);
        Assert.Contains($"<code class=\"idea-line\">{WaitingLine}</code></td><td data-h=\"Passed\">16 Nov 2026</td><td data-h=\"Nights walked\">5</td><td data-h=\"Its record so far\">1 closed, +$2.50 per $1</td><td data-h=\"Why it waits\">Its setup sells on its own time limit of 63 sessions.</td>", survivors, StringComparison.Ordinal);
        Assert.Contains("<td data-h=\"Passed\">17 Nov 2026</td><td data-h=\"Nights walked\">4</td><td data-h=\"Its record so far\">none closed</td><td data-h=\"Why it waits\">All 9 slots held; none can make room yet, the nearest having run 40 of the 63 nights.</td>", survivors, StringComparison.Ordinal);
        Assert.True(survivors.IndexOf("16 Nov 2026", StringComparison.Ordinal) < survivors.IndexOf("17 Nov 2026", StringComparison.Ordinal));

        // The retired one under the name it ran by, with its record and why.
        Assert.Contains("<td data-h=\"Variant\">the breakout survivor 3<code class=\"idea-line\">", survivors, StringComparison.Ordinal);
        Assert.Contains("<td data-h=\"Its record\">none closed</td><td data-h=\"Why\">Retired to make room for survivor 5.</td>", survivors, StringComparison.Ordinal);
        Assert.DoesNotContain("An S&P 500 survivor", survivors, StringComparison.Ordinal);
        Assert.Contains("None yet.", survivors, StringComparison.Ordinal);

        using var empty = new TemporaryStore().Migrated();
        using var emptyHost = new Host(empty.Root);
        using var emptyClient = emptyHost.CreateClient();

        Assert.Contains("No idea has passed the tester on the S&P 400 yet, so none runs by itself.", WebUtility.HtmlDecode(await emptyClient.GetStringAsync("/screens/loop?universe=400")), StringComparison.Ordinal);
    }
}
