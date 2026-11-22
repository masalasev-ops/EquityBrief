using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The notebook on the Improvement loop page, read back over a constructed store: the ideas on the index the page reads and the
// operator's own, newest first under the count of each verdict, each in plain words above its grammar line with its figures,
// its verdict and why; another index's ideas absent; and a store holding none saying so.
// see: Every idea from any source is tested by the one tester and kept in a notebook nothing is deleted from
public partial class ReadSurface
{
    internal static string[] NotebookRows =>
    [
        CheckReach.Key("15.20 The Loop page", "The notebook"),
    ];

    [Fact]
    public async Task TheLoopPageDrawsTheNotebooksIdeasOnItsIndexNewestFirstWithTheirCountsAndLines()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO notebook (entered_at, source, index_code, family, words, line, early_picks, early_edge, late_picks, late_edge, blocks, adjusted, bar, verdict, why, tested_on, run_id) VALUES "
            + "('2026-11-22T10:00:00Z', 'the researcher', 'SML', 'pullback', 'Pullbacks to support, S&P 600, only where sector_month in its highest 5 tenths.', 'event pullback(600) · if sector_month above tenth 5 · buy next open · stop 1.5 typical days · take profit whole at 2 risks · sell after 20 sessions', 400, 0.11, 300, 0.05, 30, 0.0016, 0.000962, 'near miss', 'Reached 1 in 610, the bar 1 in 1040.', '2026-11-20', 'idea-test-1'), "
            + "('2026-11-15T10:00:00Z', 'the monthly run', 'SML', 'dip', 'the exits menu: take profit at 1.5 times the risk, against the rule today.', NULL, NULL, NULL, NULL, NULL, 12, 0.2, 0.004167, 'failed', 'Fell short on its gate.', '2026-11-07', 'fill-1'), "
            + "('2026-11-15T10:00:00Z', 'a search over the long history', 'GSPC', 'breakout', 'An S&P 500 idea the S&P 600 page does not draw.', NULL, NULL, NULL, NULL, NULL, 20, 0.001, 0.004167, 'passed', 'Passed all four parts of its test.', '2026-11-06', 'fill-1'), "
            + "('2026-11-15T10:00:00Z', 'the operator', NULL, NULL, 'Keep the last part instead of selling it all.', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'waiting', 'Waiting for its shape: the grammar has no take-profit shape that writes it yet.', NULL, 'fill-1');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var small = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=600"));
        var notebook = Blocks(small, "<section class=\"card\" data-card=\"loop-notebook\">.*?</section>").Single();

        Assert.Contains("<h2>Every idea ever tested</h2>", notebook, StringComparison.Ordinal);
        Assert.Contains("<p data-summary data-notebook=\"count\">3 ideas on the S&P 600 and your own: 1 near misses, 1 failed, 1 waiting. Showing 3 of 3, newest first.</p>", notebook, StringComparison.Ordinal);

        // Newest first by the day each was tested, or entered where no test dates it: the researcher's tested 20 Nov, the
        // operator's entered 15 Nov, the monthly run's tested 7 Nov.
        Assert.Equal(["near miss", "waiting", "failed"], [.. Regex.Matches(notebook, "<tr data-verdict=\"([a-z ]+)\">").Select(match => match.Groups[1].Value)]);
        Assert.Contains("<td data-h=\"Tested\" class=\"num\">20 Nov 2026</td><td data-h=\"From\">The researcher</td>", notebook, StringComparison.Ordinal);
        Assert.Contains("only where sector_month in its highest 5 tenths.<code class=\"idea-line\">event pullback(600) · if sector_month above tenth 5 · buy next open", notebook, StringComparison.Ordinal);
        Assert.Contains("<td data-h=\"2005 to 2018\" class=\"r num\">+$0.11</td><td data-h=\"From 2019\" class=\"r num\">+$0.05</td><td data-h=\"Verdict and why\"><b>Near miss</b> Reached 1 in 610, the bar 1 in 1040.</td>", notebook, StringComparison.Ordinal);
        Assert.Contains("<td data-h=\"From\">You</td><td data-h=\"The idea\">Keep the last part instead of selling it all.</td><td data-h=\"2005 to 2018\" class=\"r num\">not yet</td>", notebook, StringComparison.Ordinal);
        Assert.Contains("<td data-h=\"2005 to 2018\" class=\"r num\">-</td><td data-h=\"From 2019\" class=\"r num\">-</td><td data-h=\"Verdict and why\"><b>Failed</b> Fell short on its gate.</td>", notebook, StringComparison.Ordinal);
        Assert.DoesNotContain("An S&P 500 idea", notebook, StringComparison.Ordinal);

        // The S&P 500's page draws its own idea and the operator's, and an index holding none draws only the operator's.
        var large = Blocks(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=500")), "<section class=\"card\" data-card=\"loop-notebook\">.*?</section>").Single();

        Assert.Contains("2 ideas on the S&P 500 and your own: 1 passed, 1 waiting. Showing 2 of 2, newest first.", large, StringComparison.Ordinal);

        using var empty = new TemporaryStore().Migrated();
        using var emptyHost = new Host(empty.Root);
        using var emptyClient = emptyHost.CreateClient();

        Assert.Contains("No idea on the S&P 400 is in the notebook yet.", WebUtility.HtmlDecode(await emptyClient.GetStringAsync("/screens/loop?universe=400")), StringComparison.Ordinal);
    }
}
