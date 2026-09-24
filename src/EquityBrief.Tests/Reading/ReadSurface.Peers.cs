using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 11.6: the peers table on a name's page, every member of its group by price alone,
// read back off the page against the store.
public partial class ReadSurface
{
    // Each row of a peers table as drawn: its ticker, whether it is marked as the name's own, and
    // its cells.
    static IReadOnlyList<(string Ticker, bool Own, string Markup)> PeerRowsOf(string page)
    {
        var table = Regex.Match(page, "<table class=\"peers-table\"[^>]*>(.*?)</table>", RegexOptions.Singleline);

        Assert.True(table.Success, "no peers table is drawn");

        return
        [
            .. Regex.Matches(table.Groups[1].Value, "<tr data-ticker=\"([^\"]+)\" data-own=\"(true|false)\">(.*?)</tr>", RegexOptions.Singleline)
                .Select(row => (row.Groups[1].Value, row.Groups[2].Value == "true", row.Groups[3].Value)),
        ];
    }

    static string PeerCellOf(string row, string cell) =>
        Regex.Match(row, $"<td class=\"{cell}\"([^>]*)>(.*?)</td>", RegexOptions.Singleline) is { Success: true } found
            ? found.Value
            : throw new InvalidOperationException($"no {cell} cell in {row}");

    [Fact]
    public void APeersTableDrawsItsRowsAsTheyArriveMarksTheNameAndRanksNone()
    {
        // Figures running against the tickers, so a table that ordered its rows by any figure
        // would draw them in another order than the one they arrive in, which is ticker order.
        var on = new DateOnly(2026, 9, 8);
        PeerCell[] rows =
        [
            new("ZZAA", false, 10m, "uptrend", new PeerFigures(on, 20m, 50, -30, 252), null),
            new("ZZBB", true, 20m, null, new PeerFigures(on, 21m, 4.76, null, 45), null),
            new("ZZCC", false, 30m, "downtrend", null, null),
        ];

        var table = WebUtility.HtmlDecode(new MarkRenderer().PeersTable("ZZBB", new PeersView("sector", "Energy", rows)));
        var drawn = PeerRowsOf(table);

        Assert.Equal(["ZZAA", "ZZBB", "ZZCC"], drawn.Select(row => row.Ticker));
        Assert.Equal([false, true, false], drawn.Select(row => row.Own));
        Assert.Contains("this name", drawn[1].Markup, StringComparison.Ordinal);
        Assert.Contains("ZZBB and the 2 other members of the Energy sector, in ticker order.", table, StringComparison.Ordinal);

        // No row carries a rank, a place or a number of its own.
        Assert.DoesNotMatch("data-rank|class=\"rank\"|<ol|data-place", table);

        // Each figure as it was handed over, a return the name holds too few bars for saying so
        // with the count, and a name the store holds no readings for saying that.
        Assert.Contains(">50% below 20.00, the high of 252 bars</td>", PeerCellOf(drawn[0].Markup, "below-high"), StringComparison.Ordinal);
        Assert.Contains(">-30%</td>", PeerCellOf(drawn[0].Markup, "peer-return"), StringComparison.Ordinal);
        Assert.Contains("not available, 45 bars", PeerCellOf(drawn[1].Markup, "peer-return"), StringComparison.Ordinal);
        Assert.Contains("no readings stored", PeerCellOf(drawn[2].Markup, "below-high"), StringComparison.Ordinal);
        Assert.Contains(">not classified</td>", PeerCellOf(drawn[1].Markup, "trend-state"), StringComparison.Ordinal);

        // A group holding nobody, a name the store holds no readings for, and a page for an
        // earlier night, each saying so rather than drawing an empty table.
        Assert.Contains(
            "the Communication Services sector holds no other member, so the table holds ZZDD alone.",
            WebUtility.HtmlDecode(new MarkRenderer().PeersTable("ZZDD", new PeersView("sector", "Communication Services", [new PeerCell("ZZDD", true, 5m, null, null, null)]))),
            StringComparison.Ordinal);
        Assert.Contains("No readings are stored for ZZEE", new MarkRenderer().PeersTable("ZZEE", new PeersView(null, null, [])), StringComparison.Ordinal);
        Assert.Contains("kept for the newest night alone", new MarkRenderer().PeersTable("ZZFF", new PeersView(null, null, [], Kept: false)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachNamesPeersTableIsDrawnFromTheStoreOverTheFixture()
    {
        var groups = Expected("peers").GetProperty("groups");

        using var store = await FixtureReplay.ReplayedAsync();
        using var host = new PassHost(store.Root);
        using var client = host.CreateClient();

        var universe = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/universe"));
        var read = 0;

        foreach (var name in groups.EnumerateObject().Where(entry => entry.Name != "note"))
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{name.Name}"));

            // Beneath the table of the biggest moves: the peers card is the card after it.
            var cards = Regex.Matches(page, "<section class=\"card\"[^>]* data-card=\"([^\"]+)\">").Select(card => card.Groups[1].Value).ToList();

            Assert.Equal("peers", cards[cards.IndexOf("how-it-got-here") + 1]);

            // Every member of the group and the name, in ticker order, the name's own row marked.
            var drawn = PeerRowsOf(page);

            Assert.Equal(name.Value.EnumerateArray().Select(member => member.GetString()!), drawn.Select(row => row.Ticker));
            Assert.Equal(drawn.Select(row => row.Ticker).Order(StringComparer.Ordinal), drawn.Select(row => row.Ticker));
            Assert.Equal(name.Name, Assert.Single(drawn, row => row.Own).Ticker);

            // The key saying how to read it, and what to take from it.
            Assert.Matches("<section class=\"card\"[^>]* data-card=\"peers\">.*?<div class=\"key\">.*?How to read it\\..*?<p class=\"take\"><b>What to take from it\\.</b> The table lists and ranks none", page.Replace("\n", " ", StringComparison.Ordinal));

            foreach (var (ticker, _, markup) in drawn)
            {
                var stored = Assert.Single(Rows(store, $"SELECT year_high, printf('%.6f', below_high_pct), CASE WHEN return_pct IS NULL THEN '' ELSE printf('%.6f', return_pct) END, bars FROM peer_reading WHERE ticker = '{ticker}';"));
                var close = Rows(store, $"SELECT close FROM bar WHERE ticker = '{ticker}' ORDER BY session_date DESC LIMIT 1;")[0][0];
                var trend = Rows(store, $"SELECT trend_state FROM ladder WHERE ticker = '{ticker}' ORDER BY as_of DESC LIMIT 1;").Select(row => row[0]).FirstOrDefault() ?? "not classified";

                // The close and the trend state as the store holds them, the close whole on its
                // element and drawn at the places a price is read at.
                Assert.Contains($"<td class=\"r num\" data-close=\"{decimal.Parse(close, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}\">{Figures.Price(decimal.Parse(close, CultureInfo.InvariantCulture))}</td>", markup, StringComparison.Ordinal);
                Assert.Contains($"<td class=\"trend-state\">{trend.Replace('_', ' ')}</td>", markup, StringComparison.Ordinal);

                // The two readings and the bars they rest on, as the annotator stored them.
                var below = Regex.Match(PeerCellOf(markup, "below-high"), "data-below-high=\"([^\"]*)\" data-year-high=\"([^\"]*)\" data-bars=\"([^\"]*)\"");

                Assert.True(below.Success);
                Assert.InRange(Math.Abs(double.Parse(stored[1], CultureInfo.InvariantCulture) - double.Parse(below.Groups[1].Value, CultureInfo.InvariantCulture)), 0, 0.005 + 1e-9);
                Assert.Equal(decimal.Parse(stored[0], CultureInfo.InvariantCulture), decimal.Parse(below.Groups[2].Value, CultureInfo.InvariantCulture));
                Assert.Equal(stored[3], below.Groups[3].Value);

                var back = Regex.Match(PeerCellOf(markup, "peer-return"), "data-return=\"([^\"]*)\" data-bars=\"([^\"]*)\"");

                Assert.True(back.Success);
                Assert.Equal(stored[3], back.Groups[2].Value);

                if (stored[2].Length == 0)
                {
                    Assert.Equal(string.Empty, back.Groups[1].Value);
                }
                else
                {
                    Assert.InRange(Math.Abs(double.Parse(stored[2], CultureInfo.InvariantCulture) - double.Parse(back.Groups[1].Value, CultureInfo.InvariantCulture)), 0, 0.005 + 1e-9);
                }

                // The distance row mark, the very mark the universe table draws for the ticker.
                var mark = Regex.Match(markup, "<svg class=\"distance-row\".*?</svg>", RegexOptions.Singleline);

                Assert.True(mark.Success, $"no distance row mark on {ticker}'s row");
                Assert.Contains(mark.Value, universe, StringComparison.Ordinal);

                read++;
            }
        }

        // Three rows on each Technology page and one on NFLX's.
        Assert.Equal(10, read);
    }
}
