using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 11.6: the peers table on a name's page, the name and at most ten members of its group
// by price alone, those sharing its industry first and then the most alike, read back off the page
// against the store.
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
        // Figures running against the order the rows arrive in, the lower likeness before the higher,
        // so a table that ordered its rows by any figure would draw them in another order.
        var on = new DateOnly(2026, 9, 8);
        var year = new PeerYear(new DateOnly(2026, 9, 1), on, [30m, 31m, 29.5m, 30m]);
        PeerCell[] rows =
        [
            new("ZZBB", true, 20m, null, new PeerFigures(on, 21m, 4.76, null, 45), null, Company: "Zed Bee"),
            new("ZZAA", false, 10m, "uptrend", new PeerFigures(on, 20m, 50, -30, 252), null, new PeerLikeness(true, 0.1234, 251), "Zed Ay", year),
            new("ZZCC", false, 30m, "downtrend", null, null, new PeerLikeness(false, 0.5, 251)),
            new("ZZDD", false, 40m, "range", null, null, new PeerLikeness(false, null, 30)),
        ];

        var table = WebUtility.HtmlDecode(new MarkRenderer().PeersTable("ZZBB", new PeersView("sector", "Energy", rows, Others: 5)));
        var drawn = PeerRowsOf(table);

        Assert.Equal(["ZZBB", "ZZAA", "ZZCC", "ZZDD"], drawn.Select(row => row.Ticker));
        Assert.Equal([true, false, false, false], drawn.Select(row => row.Own));
        Assert.Contains("this name", drawn[0].Markup, StringComparison.Ordinal);
        Assert.Contains("ZZBB and 3 of the 5 other members of the Energy sector: those sharing its industry first, then the ones whose daily moves followed ZZBB's most closely over the sessions both hold.", table, StringComparison.Ordinal);

        // No row carries a rank, a place or a number of its own.
        Assert.DoesNotMatch("data-rank|class=\"rank\"|<ol|data-place", table);

        // How closely each moved with the name as it was handed over, to two places with the stored
        // figure whole on its element, a member sharing its industry saying so, one sharing too few
        // sessions saying how many, and the name's own row drawing none.
        Assert.Contains("data-likeness=\"0.1234\" data-sessions=\"251\">0.12</td>", PeerCellOf(drawn[1].Markup, "r num likeness"), StringComparison.Ordinal);
        Assert.Contains("data-likeness=\"0.5\" data-sessions=\"251\">0.50</td>", PeerCellOf(drawn[2].Markup, "r num likeness"), StringComparison.Ordinal);
        Assert.Contains("too few sessions shared, 30", PeerCellOf(drawn[3].Markup, "r num likeness"), StringComparison.Ordinal);
        Assert.Contains("data-likeness=\"\" data-sessions=\"\"></td>", PeerCellOf(drawn[0].Markup, "r num likeness"), StringComparison.Ordinal);
        Assert.Contains("same industry", drawn[1].Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("same industry", drawn[2].Markup, StringComparison.Ordinal);

        // Each member's ticker opens its own page and carries its year beside it, the company named,
        // and the name's own row links nowhere.
        Assert.Contains("<a class=\"peer-link\" href=\"#/name/ZZAA\">ZZAA</a> <span class=\"co\">Zed Ay</span>", drawn[1].Markup, StringComparison.Ordinal);
        Assert.Matches("<span class=\"peer-pop\" role=\"tooltip\"><svg[^>]*class=\"year-line\" data-ticker=\"ZZAA\" data-closes=\"4\">", drawn[1].Markup);
        Assert.Contains("ZZCC holds 0 stored close(s), too few to draw its year.", drawn[2].Markup, StringComparison.Ordinal);
        Assert.DoesNotContain("peer-link", drawn[0].Markup, StringComparison.Ordinal);
        Assert.Contains("<span class=\"co\">Zed Bee</span>", drawn[0].Markup, StringComparison.Ordinal);

        // Each figure as it was handed over, a return the name holds too few bars for saying so
        // with the count, and a name the store holds no readings for saying that.
        Assert.Contains(">50% below 20.00, the high of 252 bars</td>", PeerCellOf(drawn[1].Markup, "below-high"), StringComparison.Ordinal);
        Assert.Contains(">-30%</td>", PeerCellOf(drawn[1].Markup, "peer-return"), StringComparison.Ordinal);
        Assert.Contains("not available, 45 bars", PeerCellOf(drawn[0].Markup, "peer-return"), StringComparison.Ordinal);
        Assert.Contains("no readings stored", PeerCellOf(drawn[2].Markup, "below-high"), StringComparison.Ordinal);
        Assert.Contains(">not classified</td>", PeerCellOf(drawn[0].Markup, "trend-state"), StringComparison.Ordinal);

        // Every member of a group of three drawn, and a row written before the night chose any saying so.
        Assert.Contains(
            "ZZBB and the 3 other members of the Energy sector, those sharing its industry first, then by how closely each one's daily moves followed ZZBB's over the sessions both hold.",
            WebUtility.HtmlDecode(new MarkRenderer().PeersTable("ZZBB", new PeersView("sector", "Energy", rows, Others: 3))),
            StringComparison.Ordinal);
        Assert.Contains(
            "the Energy sector holds 4 other members, and the night has not yet chosen the ones this table draws, which it does from its next run.",
            WebUtility.HtmlDecode(new MarkRenderer().PeersTable("ZZBB", new PeersView("sector", "Energy", [rows[0]], Others: 4, Chosen: false))),
            StringComparison.Ordinal);

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
    public void ThePageDrawsTheMembersTheNightChoseInTheOrderItStoredThemAndNoOther()
    {
        // Four members of the Energy sector, the night having chosen two of them in an order running against
        // their tickers: ZZDD first, then ZZAA. ZZCC is in the group and was not chosen, and ZZEE is in another.
        var on = new DateOnly(2026, 9, 25);
        UniverseRow[] universe =
        [
            new("ZZAA", "Energy", 10m, "uptrend", null, null, null, "Zed Ay", "Oil"),
            new("ZZBB", "Energy", 20m, "range", null, null, null, "Zed Bee", "Gas"),
            new("ZZCC", "Energy", 30m, "range", null, null, null, "Zed Cee", "Coal"),
            new("ZZDD", "Energy", 40m, "downtrend", null, null, null, "Zed Dee", "Gas"),
            new("ZZEE", "Utilities", 50m, "range", null, null, null, "Zed Ee", "Power"),
        ];
        const string Chosen = "[{\"ticker\":\"ZZDD\",\"sameIndustry\":true,\"likeness\":0.1,\"sessions\":251},{\"ticker\":\"ZZAA\",\"sameIndustry\":false,\"likeness\":0.9,\"sessions\":251}]";
        PeerReadingRow[] readings =
        [
            new("ZZBB", on, "sector", "Energy", 25m, 20, 5, 252, Chosen),
            new("ZZAA", on, "sector", "Energy", 12m, 16.7, 2, 252, "[]"),
            new("ZZDD", on, "sector", "Energy", 44m, 9.1, -1, 252, "[]"),
        ];
        CloseRow[] closes = [new("ZZAA", on.AddDays(-1), 9.5m), new("ZZAA", on, 10m), new("ZZDD", on, 40m)];

        var peers = NameScreen.Peers("ZZBB", universe, readings, null, closes)!;

        Assert.Equal(["ZZBB", "ZZDD", "ZZAA"], peers.Rows.Select(row => row.Ticker).ToArray());
        Assert.Equal([true, false, false], peers.Rows.Select(row => row.Own).ToArray());
        Assert.Equal(3, peers.Others);
        Assert.True(peers.Chosen);

        // Each member carries the likeness, the industry mark and the company as stored, and its closes.
        Assert.Equal(new PeerLikeness(true, 0.1, 251), peers.Rows[1].Likeness);
        Assert.Equal(new PeerLikeness(false, 0.9, 251), peers.Rows[2].Likeness);
        Assert.Equal("Zed Ay", peers.Rows[2].Company);
        Assert.Equal([9.5m, 10m], peers.Rows[2].Year!.Closes);
        Assert.Null(peers.Rows[0].Year);

        // A row written before the night chose any draws the name alone and says so.
        var unchosen = NameScreen.Peers("ZZBB", universe, [readings[0] with { Peers = null }], null)!;

        Assert.Equal(["ZZBB"], unchosen.Rows.Select(row => row.Ticker).ToArray());
        Assert.False(unchosen.Chosen);
    }

    [Fact]
    public void TheYearLineDrawsTheClosesWithTheNearestBandsNamedAndSaysSoWhereItHoldsTooFew()
    {
        // Five closes from 100 to 110 with the bands at 96 and 114, so the scale runs 96 to 114 and every
        // height is worked by hand: the plot starts at 30 and is 150 - 30 - 6 = 114 tall, so a price p sits
        // at 30 + 114 * (1 - (p - 96) / 18), which puts 100 at 118.67, 110 at 55.33, 96 at 144 and 114 at 30.
        var marks = new MarkRenderer();
        var year = new PeerYear(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 5), [100m, 104m, 102m, 108m, 110m]);
        var line = marks.YearLine("ZZAA", year, 96m, 114m);

        Assert.Contains("class=\"year-line\" data-ticker=\"ZZAA\" data-closes=\"5\"", line, StringComparison.Ordinal);
        Assert.Contains(">ZZAA, 5 closes to 2026-09-05</text>", line, StringComparison.Ordinal);

        var path = Regex.Match(line, "<path class=\"m-mom\" d=\"([^\"]+)\"/>").Groups[1].Value;
        var points = Regex.Matches(path, "[ML]([0-9.]+) ([0-9.]+)").Select(point => double.Parse(point.Groups[2].Value, CultureInfo.InvariantCulture)).ToArray();

        Assert.Equal(5, points.Length);
        Assert.Equal(118.67, points[0], 2);
        Assert.Equal(55.33, points[^1], 2);

        // Each band across the plot in its own hue at its price's height, named with its price.
        Assert.Matches("<line class=\"m-edge-sup\" data-support=\"96\" x1=\"4\" y1=\"144\"", line);
        Assert.Matches("<line class=\"m-edge-res\" data-resistance=\"114\" x1=\"4\" y1=\"30\"", line);
        Assert.Contains(">support 96.00</text>", line, StringComparison.Ordinal);
        Assert.Contains(">resistance 114.00</text>", line, StringComparison.Ordinal);

        // The last close marked with its stored value.
        Assert.Contains("<circle class=\"m-last\" data-close=\"110\"", line, StringComparison.Ordinal);

        // Two bands sitting close are named a line apart rather than one over the other.
        var close = marks.YearLine("ZZAA", year, 104m, 104.5m);
        var named = Regex.Matches(close, "<text class=\"m-legend-t m-legend-(?:sup|res)\" x=\"[0-9.]+\" y=\"([0-9.]+)\">")
            .Select(label => double.Parse(label.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Equal(2, named.Length);
        Assert.True(Math.Abs(named[1] - named[0]) >= 13 - 1e-9, $"the two names sit {Math.Abs(named[1] - named[0])} apart");

        // One close, or none, draws no picture and says how many it holds.
        var one = marks.YearLine("ZZAA", year with { Closes = [100m] }, 96m, 114m);

        Assert.DoesNotContain("<svg", one, StringComparison.Ordinal);
        Assert.Contains("ZZAA holds 1 stored close(s), too few to draw its year.", one, StringComparison.Ordinal);
        Assert.Contains("ZZAA holds 0 stored close(s), too few to draw its year.", marks.YearLine("ZZAA", null, null, null), StringComparison.Ordinal);
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

            // The name first and marked, then the members the night chose in the order it stored them.
            var drawn = PeerRowsOf(page);

            Assert.Equal(name.Value.EnumerateArray().Select(member => member.GetString()!), drawn.Select(row => row.Ticker));
            Assert.Equal(name.Name, Assert.Single(drawn, row => row.Own).Ticker);
            Assert.True(drawn[0].Own);

            // The key saying how to read it, and what to take from it.
            Assert.Matches("<section class=\"card\"[^>]* data-card=\"peers\">.*?<div class=\"key\">.*?How to read it\\..*?<p class=\"take\"><b>What to take from it\\.</b> The order is how closely each moved with this name, not which is the better company", page.Replace("\n", " ", StringComparison.Ordinal));

            using var picks = System.Text.Json.JsonDocument.Parse(Rows(store, $"SELECT peers FROM peer_reading WHERE ticker = '{name.Name}';")[0][0]);
            var chosen = picks.RootElement.EnumerateArray().ToDictionary(pick => pick.GetProperty("ticker").GetString()!, pick => pick, StringComparer.Ordinal);

            foreach (var (ticker, own, markup) in drawn)
            {
                // A member's likeness whole on its element as the annotator stored it, its ticker opening its
                // own page, and its year line drawn over every close the store holds for it.
                if (!own)
                {
                    var likeness = Regex.Match(PeerCellOf(markup, "r num likeness"), "data-likeness=\"([^\"]*)\" data-sessions=\"([^\"]*)\"");

                    Assert.Equal(chosen[ticker].GetProperty("likeness").GetDouble(), double.Parse(likeness.Groups[1].Value, CultureInfo.InvariantCulture));
                    Assert.Equal(chosen[ticker].GetProperty("sessions").GetInt32().ToString(CultureInfo.InvariantCulture), likeness.Groups[2].Value);
                    Assert.Contains($"<a class=\"peer-link\" href=\"#/name/{ticker}\">{ticker}</a>", markup, StringComparison.Ordinal);
                    Assert.Contains($"class=\"year-line\" data-ticker=\"{ticker}\" data-closes=\"{Rows(store, $"SELECT COUNT(*) FROM bar WHERE ticker = '{ticker}';")[0][0]}\"", markup, StringComparison.Ordinal);
                }

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

    // A name the index does not hold on the night keeps its bars, moves and readings, stored as the
    // annotator stores them for a name it reads no membership row for: a sector unnamed, holding
    // nobody. Its page says it is not a member, beside every move and in its peers region, rather
    // than naming an empty group, and a member's page is unchanged.
    [Fact]
    public async Task ANameTheIndexDoesNotHoldOnTheNightSaysSoBesideItsMovesAndInItsPeersRegion()
    {
        using var store = await FixtureExpectations.WithListings();

        store.Execute(
            "UPDATE membership SET \"left\" = '2026-09-01' WHERE ticker = 'MSFT';"
            + "UPDATE move SET group_kind = 'sector', group_name = NULL, group_members = 0, group_counted = 0, group_median = NULL WHERE ticker = 'MSFT';"
            + "UPDATE peer_reading SET group_kind = 'sector', group_name = NULL WHERE ticker = 'MSFT';");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var left = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/MSFT"));
        var cells = Regex.Matches(left, "<td class=\"group-median\"[^>]*>([^<]*)</td>").Select(cell => cell.Groups[1].Value).ToArray();
        var night = NightIn(store);

        Assert.Contains("data-peers=\"not-a-member\"", left, StringComparison.Ordinal);
        Assert.Contains("MSFT is not a member of the index on the night, so it has no group and no peers are drawn.", left, StringComparison.Ordinal);
        Assert.NotEmpty(cells);
        Assert.All(cells, cell => Assert.Equal($"not a member of the index on {night}, the night its moves' groups were read, so no group is read for this move", cell));
        Assert.DoesNotContain("its membership row does not name", left, StringComparison.Ordinal);
        Assert.DoesNotContain("holds MSFT alone", left, StringComparison.Ordinal);

        // A member's page draws its group as before.
        var member = await client.GetStringAsync("/screens/name/AAPL");

        Assert.DoesNotContain("not-a-member", member, StringComparison.Ordinal);
        Assert.Contains("data-peers=\"group\"", member, StringComparison.Ordinal);
    }

    // A page for an earlier night draws the moves as the newest night read them, groups and all, so
    // whether the name was a member is asked of the newest night's index and the night named is
    // that one. A name that left after the evening a page is for was a member on it, and its moves
    // still say it was not a member on the night their groups were read; a name that joined after
    // it was not a member on it, and its moves still draw the group the newest night read.
    [Fact]
    public async Task ANamesPageForAnEarlierNightNamesTheNightItsMovesGroupsWereReadWhereTheIndexDidNotHoldItThen()
    {
        using var store = await FixtureExpectations.WithListings();

        var night = NightIn(store);
        var earlier = SessionWithoutAnEvening(store, "MSFT", night);
        var sector = Text(store, "SELECT sector FROM membership WHERE ticker = 'MSFT' ORDER BY observed_at DESC LIMIT 1;");

        // MSFT leaves the index on the newest night, its moves stored as the annotator stores them
        // for a name it reads no membership row for; KEYS joins on it. An evening before it is
        // written so the earlier page has one to draw.
        const string Reasons = "[{\"name\":\"at entry zone\",\"fired\":true,\"values\":{\"close\":\"1.00\"}}]";

        store.Execute(
            $"UPDATE membership SET \"left\" = '{night}' WHERE ticker = 'MSFT';"
            + $"UPDATE membership SET joined = '{night}' WHERE ticker = 'KEYS';"
            + "UPDATE move SET group_kind = 'sector', group_name = NULL, group_members = 0, group_counted = 0, group_median = NULL WHERE ticker = 'MSFT';"
            + $"INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) VALUES ('AAPL', '{earlier}', '{Reasons}', 1, '[]', '[]');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/MSFT/{earlier}"));
        var cells = Regex.Matches(page, "<td class=\"group-median\"[^>]*>([^<]*)</td>").Select(cell => cell.Groups[1].Value).ToArray();

        // The page is the earlier evening's, on which the masthead names MSFT's sector.
        Assert.Contains($"data-night=\"{earlier}\"", page, StringComparison.Ordinal);
        Assert.Contains($"href=\"#/universe?sector={Uri.EscapeDataString(sector)}\"", page, StringComparison.Ordinal);

        // Beside every move, the night the groups were read on, which is not the page's own.
        Assert.NotEmpty(cells);
        Assert.All(cells, cell => Assert.Equal($"not a member of the index on {night}, the night its moves' groups were read, so no group is read for this move", cell));
        Assert.Equal(cells.Length, Regex.Matches(page, $"data-read-on=\"{night}\"").Count);
        Assert.DoesNotContain("its membership row does not name", page, StringComparison.Ordinal);

        // KEYS was not a member on the earlier evening and was on the newest night, and its moves
        // draw the group the newest night read; AAPL, a member on both, draws its group as before.
        foreach (var ticker in new[] { "KEYS", "AAPL" })
        {
            var drawn = await client.GetStringAsync($"/screens/name/{ticker}/{earlier}");

            Assert.DoesNotContain("not-a-member", drawn, StringComparison.Ordinal);
            Assert.Contains("<td class=\"group-median\" data-group-kind=", drawn, StringComparison.Ordinal);
        }
    }
}
