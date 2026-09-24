using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 11.7: a name's earnings reaction record beside its earnings setups, read back off
// the page against the store.
public partial class ReadSurface
{
    static IReadOnlyList<(string ReportDate, string Markup)> ReactionRowsOf(string page)
    {
        var table = Regex.Match(page, "<table class=\"reactions-table\"[^>]*>(.*?)</table>", RegexOptions.Singleline);

        Assert.True(table.Success, "no reaction record is drawn");

        return
        [
            .. Regex.Matches(table.Groups[1].Value, "<tr data-report-date=\"([^\"]+)\"[^>]*>(.*?)</tr>", RegexOptions.Singleline)
                .Select(row => (row.Groups[1].Value, row.Groups[2].Value)),
        ];
    }

    [Fact]
    public void AReactionRecordDrawsEachPrintAsStoredAndAPrintWithNoEstimateAsNoneFiledAndNoSurprise()
    {
        ReactionCell[] prints =
        [
            new(new DateOnly(2026, 3, 3), "before", new DateOnly(2026, 3, 3), "1.00", "1.10", 10, 10),
            new(new DateOnly(2026, 3, 3), "after", new DateOnly(2026, 3, 4), "1.00", "0.90", -10, -10),
            new(new DateOnly(2026, 3, 6), "unstated", new DateOnly(2026, 3, 6), "2.00", "2.50", null, 21.21),
            new(new DateOnly(2026, 3, 9), "before", new DateOnly(2026, 3, 9), null, "0.75", null, -25),
        ];

        var table = WebUtility.HtmlDecode(new MarkRenderer().ReactionsTable("ZZAA", prints));
        var rows = ReactionRowsOf(table);

        Assert.Equal(4, rows.Count);
        Assert.Contains("<td>before the open</td><td>2026-03-03</td>", rows[0].Markup, StringComparison.Ordinal);
        Assert.Contains("<td>after the close</td><td>2026-03-04</td>", rows[1].Markup, StringComparison.Ordinal);
        Assert.Contains("<td>timing not filed</td><td>2026-03-06</td>", rows[2].Markup, StringComparison.Ordinal);
        Assert.Contains("data-surprise=\"10\">+10%</td>", rows[0].Markup, StringComparison.Ordinal);
        Assert.Contains("data-move=\"-10\">-10%</td>", rows[1].Markup, StringComparison.Ordinal);

        // An estimate filed with no surprise beside it says the provider filed none.
        Assert.Contains("data-surprise=\"\"><span class=\"degraded\">not filed</span>", rows[2].Markup, StringComparison.Ordinal);

        // No estimate: the actual stands, the estimate says none was filed, and no surprise is drawn,
        // so the print is never read as having met one.
        Assert.Contains("data-estimate=\"\"><span class=\"degraded\">none was filed</span>", rows[3].Markup, StringComparison.Ordinal);
        Assert.Contains("data-actual=\"0.75\">0.75</td>", rows[3].Markup, StringComparison.Ordinal);
        Assert.Contains("none, with no estimate to measure against", rows[3].Markup, StringComparison.Ordinal);
        Assert.DoesNotMatch("data-surprise=\"0\"|>0%<|>\\+0%<", rows[3].Markup);

        // A record holding no print says so.
        Assert.Contains("No print over the calendar's year behind is stored for ZZBB", new MarkRenderer().ReactionsTable("ZZBB", []), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachNamesReactionRecordIsDrawnBesideItsSetupsFromTheStoreOverTheFixture()
    {
        using var store = await FixtureReplay.ReplayedAsync();
        using var host = new PassHost(store.Root);
        using var client = host.CreateClient();

        var read = 0;

        foreach (var ticker in new[] { "AAPL", "KEYS", "MSFT", "NFLX" })
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{ticker}"));

            // Beside the earnings setups, which close the plan's card: the record is the card after it.
            var cards = Regex.Matches(page, "<section class=\"card\"[^>]* data-card=\"([^\"]+)\">").Select(card => card.Groups[1].Value).ToList();

            Assert.Equal("reactions", cards[cards.IndexOf("plan") + 1]);
            Assert.Matches("<section class=\"card\"[^>]* data-card=\"reactions\">.*?<div class=\"key\">.*?How to read it\\..*?<p class=\"take\"><b>What to take from it\\.</b> A record of what past reports did", page.Replace("\n", " ", StringComparison.Ordinal));

            var stored = Rows(store, $"SELECT report_date, timing, reaction_session, IFNULL(estimate, ''), IFNULL(actual, ''), CASE WHEN surprise_pct IS NULL THEN '' ELSE printf('%.6f', surprise_pct) END, printf('%.6f', move_pct) FROM earnings_reaction WHERE ticker = '{ticker}' ORDER BY report_date;");
            var drawn = ReactionRowsOf(page);

            Assert.Equal(stored.Select(row => row[0]), drawn.Select(row => row.ReportDate));

            foreach (var (row, (_, markup)) in stored.Zip(drawn))
            {
                Assert.Contains($"data-timing=\"{row[1]}\" data-session=\"{row[2]}\"", page, StringComparison.Ordinal);
                Assert.Contains($"data-estimate=\"{row[3]}\"", markup, StringComparison.Ordinal);
                Assert.Contains($"data-actual=\"{row[4]}\"", markup, StringComparison.Ordinal);

                var surprise = Regex.Match(markup, "data-surprise=\"([^\"]*)\"").Groups[1].Value;
                var move = Regex.Match(markup, "data-move=\"([^\"]*)\"").Groups[1].Value;

                if (row[5].Length == 0)
                {
                    Assert.Equal(string.Empty, surprise);
                }
                else
                {
                    Assert.InRange(Math.Abs(double.Parse(row[5], CultureInfo.InvariantCulture) - double.Parse(surprise, CultureInfo.InvariantCulture)), 0, 0.005 + 1e-9);
                }

                Assert.InRange(Math.Abs(double.Parse(row[6], CultureInfo.InvariantCulture) - double.Parse(move, CultureInfo.InvariantCulture)), 0, 0.005 + 1e-9);

                read++;
            }
        }

        // The fixture's sixteen prints, four a name.
        Assert.Equal(16, read);
    }
}
