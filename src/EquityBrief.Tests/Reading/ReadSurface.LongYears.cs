using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.History;
using EquityBrief.Tests.Harness;
using Xunit;

namespace EquityBrief.Tests.Reading;

// 23.6's History page: each family by year read back over a constructed store off `long_reading`, the trades, the share at
// target and the mean edge after costs on each index and the three pooled by the trades each settled, the last price's beside
// it, 2008 marked, the years the settings were chosen on told from those they never saw, the drift's survivors line, the S&P
// 400's and 600's early years labelled lower coverage, and a store the long build has not written saying so.
// see: The long ledger is read over the long spans and the long prices with the store's own closes after them, each setup under the rule as ruled and with a stock whose prices stop ended at its last price
public partial class ReadSurface
{
    internal static string[] LongYearsRows => [CheckReach.Key("15.25 History", "Each family by year")];

    [Fact]
    public async Task TheHistoryPageDrawsEachFamilyByYearOnEachIndexAndPooledWithTheLastPriceBesideIt()
    {
        using var store = new TemporaryStore().Migrated();

        using (var host = new Host(store.Root))
        {
            using var client = host.CreateClient();
            var empty = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/history"));

            Assert.Contains("The long ledger has not been written.", empty, StringComparison.Ordinal);
        }

        // The pullback in 2008 on the S&P 500 and 400, 30 trades at 0.10 and 10 at 0.30, pooled to 0.15 over the 40, and at the
        // last price 32 at 0.06 and 10 at 0.30, pooled to 4.92 over 42, 0.12; in 2019 on the S&P 500 alone; and the drift in 2008.
        store.Execute(@"
            INSERT INTO long_reading (index_code, family, year, reading, setups, settled, targets, edge_mean, edge_after_cost_mean, crossing, edge_after_cost_without, read_through, run_id) VALUES
                ('GSPC', 'pullback', 2008, 'as ruled', 40, 30, 12, 0.12, 0.10, 1, 0.11, '2026-11-06', 'r'),
                ('GSPC', 'pullback', 2008, 'last price', 40, 32, 12, 0.08, 0.06, 1, 0.07, '2026-11-06', 'r'),
                ('MID', 'pullback', 2008, 'as ruled', 12, 10, 4, 0.32, 0.30, 0, 0.30, '2026-11-06', 'r'),
                ('MID', 'pullback', 2008, 'last price', 12, 10, 4, 0.32, 0.30, 0, 0.30, '2026-11-06', 'r'),
                ('GSPC', 'pullback', 2019, 'as ruled', 20, 20, 8, 0.05, 0.04, 0, 0.04, '2026-11-06', 'r'),
                ('GSPC', 'drift', 2008, 'as ruled', 5, 5, 1, 0.2, 0.2, 0, 0.2, '2026-11-06', 'r');");

        using var written = new Host(store.Root);
        using var reader = written.CreateClient();
        var page = WebUtility.HtmlDecode(await reader.GetStringAsync("/screens/history"));

        Assert.Contains("data-card=\"long-years\"", page, StringComparison.Ordinal);
        Assert.Contains("read through 2026-11-06", page, StringComparison.Ordinal);

        var pullback = Regex.Match(page, "<details data-family=\"pullback\">(.*?)</details>", RegexOptions.Singleline).Groups[1].Value;
        var crisis = Regex.Match(pullback, "<tr data-year=\"2008\" data-seen=\"never seen\" data-crisis=\"2008\">(.*?)</tr>", RegexOptions.Singleline).Groups[1].Value;
        var cells = Regex.Matches(crisis, "<td class=\"n\"[^>]*>(.*?)</td>", RegexOptions.Singleline).Select(match => Regex.Replace(match.Groups[1].Value, "<[^>]+>", string.Empty)).ToArray();

        Assert.Contains("<summary>Pullback: 60 trades, 2008 to 2019</summary>", pullback, StringComparison.Ordinal);
        Assert.Equal(
        [
            "30", "40%", "+0.10 R last price +0.06 R",
            "10", "40%", "+0.30 R last price +0.30 R",
            "0", "none", "none last price none",
            "40", "40%", "+0.15 R last price +0.12 R",
        ], cells);
        Assert.Contains("<tr data-year=\"2019\" data-seen=\"chosen on\">", pullback, StringComparison.Ordinal);
        Assert.Contains($"title=\"{LongFloors.LowerLabel}\" data-note=\"lower\"", crisis, StringComparison.Ordinal);
        Assert.DoesNotContain("data-note=\"survivors\"", pullback, StringComparison.Ordinal);

        var drift = Regex.Match(page, "<details data-family=\"drift\">(.*?)</details>", RegexOptions.Singleline).Groups[1].Value;

        Assert.Contains($"<p class=\"read\" data-note=\"survivors\">{LongFirstYears.SurvivorsNote}</p>", drift, StringComparison.Ordinal);
    }
}
