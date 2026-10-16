using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, 19.12: the Ledger page's table of which readings go with winners drawn from the newest run the tester
// stored for the index and the family chosen, every row as stored and none worked when the page is read: a row whose
// words do not follow from its figures drawn as stored, the readings beyond the shuffle first by their size and the
// rest in the catalogue's order, each with its heading and its correlation in its hue, an older run's rows and another
// index's not drawn, and a line where no run has worked it.
// see: Which readings go with winners is worked by the monthly run and stored, and the words move no rule
public partial class ReadSurface
{
    internal static readonly string[] ReadingLinkRows =
    [
        CheckReach.Key(Scope.LedgerPage, "Which readings go with winners"),
    ];

    static void LinkRow(TemporaryStore store, string run, string index, string reading, double? correlation, double? shuffled, string words) =>
        store.Execute(
            "INSERT INTO loop_link (run_id, month, index_code, family, reading, setups, years, correlation, same_way, shuffled, words) VALUES "
            + FormattableString.Invariant($"('{run}', '2026-10', '{index}', 'pullback', '{reading}', 120, 4, {(correlation is { } held ? held.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "NULL")}, 0.75, {(shuffled is { } bar ? bar.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "NULL")}, '{words}');"));

    [Fact]
    public async Task TheLedgerPageDrawsWhichReadingsGoWithWinnersAsTheNewestRunStoredItAndWorksNoneOfIt()
    {
        using var store = new TemporaryStore().Migrated();

        LedgerYearRow(store, "pullback", 2025, 40, 10, 0, 0, 40, "0.1", "0.05", "-1,-0.5,0,0.25,0.5,1,1.5,2,3");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<string> Ledger() => WebUtility.HtmlDecode(await client.GetStringAsync("/screens/ledger?universe=500&family=pullback"));

        // No run has worked it yet: the card says so and draws no table.
        var none = await Ledger();

        Assert.Contains("<p class=\"degraded\" data-links=\"none\">No monthly run has worked which readings go with winners for this family on this index yet.</p>", none, StringComparison.Ordinal);
        Assert.DoesNotContain("<table class=\"ledger-links\"", none, StringComparison.Ordinal);

        // An older run, then the newest: its rows stored with a correlation of 0.4242 said to go with winners, -0.3 with
        // losers, and 0.9 said to have no link beyond the shuffle though it stands above it, which the page draws as stored;
        // and a newer run on the S&P 400, which the S&P 500's page does not read.
        LinkRow(store, "loop-links-GSPC-20261001T120000Z", "GSPC", "alpha_six", -0.5, 0.1, "goes with losers");
        LinkRow(store, "loop-links-GSPC-20261016T120000Z", "GSPC", "rsi", 0.9, 0.2, "no link beyond the shuffle");
        LinkRow(store, "loop-links-GSPC-20261016T120000Z", "GSPC", "beta", -0.3, 0.2, "goes with losers");
        LinkRow(store, "loop-links-GSPC-20261016T120000Z", "GSPC", "alpha_six", 0.4242, 0.1, "goes with winners");
        LinkRow(store, "loop-links-MID-20261017T120000Z", "MID", "alpha_six", 0.7, 0.1, "goes with winners");

        var page = await Ledger();
        var table = Regex.Match(page, "<table class=\"ledger-links\".*?</table>", RegexOptions.Singleline).Value;

        Assert.Contains("data-run=\"loop-links-GSPC-20261016T120000Z\" data-month=\"2026-10\">Worked by the monthly run for 2026-10: 1 reading going with winners and 1 with losers beyond the shuffle, of 3.", page, StringComparison.Ordinal);
        Assert.Equal(
            ["alpha_six|goes with winners|0.4242|0.1", "beta|goes with losers|-0.3|0.2", "rsi|no link beyond the shuffle|0.9|0.2"],
            Regex.Matches(table, "<tr data-reading=\"([a-z_]+)\" data-words=\"([^\"]+)\" data-correlation=\"([^\"]+)\" data-shuffled=\"([^\"]+)\">")
                .Select(match => $"{match.Groups[1].Value}|{match.Groups[2].Value}|{match.Groups[3].Value}|{match.Groups[4].Value}"));

        // Each row's heading on the chart of what is behind a stock, its correlation in its hue and how many of its four
        // years pointed its way, three of four.
        Assert.Contains("<td>Size and liquidity</td><td class=\"r num\">120</td><td class=\"r num\">4</td><td class=\"r num\"><span class=\"gain\">+0.424</span></td><td class=\"r num\">3 of 4</td>", table, StringComparison.Ordinal);
        Assert.Contains("<td>Volatility</td><td class=\"r num\">120</td><td class=\"r num\">4</td><td class=\"r num\"><span class=\"loss\">-0.300</span></td>", table, StringComparison.Ordinal);
        Assert.Contains("<td>Momentum</td>", table, StringComparison.Ordinal);
        Assert.DoesNotContain("0.7", table, StringComparison.Ordinal);
        Assert.DoesNotContain("-0.500", table, StringComparison.Ordinal);
    }
}
