using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The pullbacks' search on the Improvement loop page, read back over a constructed store: the newest search on the index the
// page reads, its count of settings tested, passed and near, and its settings best on 2005 to 2018 first, each in words with
// its figures and its verdict; another index's, an older run's and another source's rows absent; and a store holding no
// search saying when it runs.
// see: The pullbacks' search walks the swing filter's dials as grammar lines, chosen on 2005 to 2018 and judged together from 2019
public partial class ReadSurface
{
    internal static string[] PullbackSearchRows =>
    [
        CheckReach.Key("15.20 The Loop page", "The pullbacks' search"),
    ];

    const string Search = Core.Loop.PullbackSearchView.Source;

    static string SearchRow(string run, string index, string source, string words, string early, string late, string verdict, bool filtered = true) =>
        $"('2026-12-05T10:00:00Z', '{source}', NULL, '{index}', 'pullback', '{words}', 'event pullback(500) loose · {(filtered ? "filter " : string.Empty)}{words} · buy next open', 'built', 900, {early}, 700, {late}, 12, 0.01, 0.0000480769, '{verdict}', 'why {words}', '2026-12-04', '{run}')";

    [Fact]
    public async Task TheLoopPageDrawsTheNewestPullbackSearchBestOnTheEarlyYearsFirst()
    {
        using var store = new TemporaryStore().Migrated();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var empty = Blocks(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=500")), "<section class=\"card\" data-card=\"loop-pullbacks\">.*?</section>").Single();

        Assert.Contains("<p class=\"empty\" data-search=\"none\" data-summary>The pullbacks' search has not run on this index yet.", empty, StringComparison.Ordinal);

        store.Execute(
            "INSERT INTO notebook (entered_at, source, source_key, index_code, family, words, line, built_on, early_picks, early_edge, late_picks, late_edge, blocks, adjusted, bar, verdict, why, tested_on, run_id) VALUES "
            + string.Join(", ",
                SearchRow("search-1", "GSPC", Search, "older", "0.50", "0.50", "passed"),
                SearchRow("search-2", "GSPC", Search, "second best", "0.06", "0.08", "near miss"),
                SearchRow("search-2", "GSPC", Search, "best", "0.07", "-0.01", "failed"),
                SearchRow("search-2", "GSPC", Search, "third", "0.02", "0.03", "passed"),
                SearchRow("search-2", "GSPC", Search, "a long search of another kind", "0.97", "0.97", "passed", filtered: false),
                SearchRow("search-2", "SML", Search, "another index", "0.90", "0.90", "passed"),
                SearchRow("search-2", "GSPC", "the researcher", "another source", "0.95", "0.95", "passed")) + ";");

        var search = Blocks(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=500")), "<section class=\"card\" data-card=\"loop-pullbacks\">.*?</section>").Single();

        Assert.Contains("<p data-summary>On 5 Dec 2026 it tested 3 settings: 1 passed and 1 came near the bar. The best on 2005 to 2018 first.</p>", search, StringComparison.Ordinal);
        Assert.Equal(["failed", "near miss", "passed"], [.. Regex.Matches(search, "<tr data-verdict=\"([^\"]+)\"").Select(match => match.Groups[1].Value)]);
        Assert.Contains("<td data-h=\"The setting\">best</td><td data-h=\"Edge to 2018\" class=\"r num\">+0.070 over 900</td><td data-h=\"Edge from 2019\" class=\"r num\">-0.010 over 700</td><td data-h=\"Verdict\">failed: why best</td>", search, StringComparison.Ordinal);
        Assert.DoesNotContain("older", search, StringComparison.Ordinal);
        Assert.DoesNotContain("another index", search, StringComparison.Ordinal);
        Assert.DoesNotContain("another source", search, StringComparison.Ordinal);
        Assert.DoesNotContain("another kind", search, StringComparison.Ordinal);
    }
}
