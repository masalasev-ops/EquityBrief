using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Filter;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Reading;

// read-surface, 12.1: the swing readings on a name's page and in the universe table, and the
// night's breadth on tonight's header and the run page, each read back off the page against the
// store the fixture's replay wrote.
public partial class ReadSurface
{
    const string SwingNight = "2026-09-08";

    // Rows as the store holds them, each figure whole and in the invariant culture, and an empty
    // string where the store holds none.
    static IReadOnlyList<string[]> SwingRows(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<string[]>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add([
                .. Enumerable.Range(0, reader.FieldCount).Select(field => reader.GetValue(field) switch
                {
                    DBNull => string.Empty,
                    double value => value.ToString("R", CultureInfo.InvariantCulture),
                    long value => value.ToString(CultureInfo.InvariantCulture),
                    var other => Convert.ToString(other, CultureInfo.InvariantCulture)!,
                }),
            ]);
        }

        return rows;
    }

    // A stored statistic as the page carries it whole, and the word it carries where none is stored.
    static string WholeOf(string stored) =>
        stored.Length == 0 ? "none" : double.Parse(stored, CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture);

    [Fact]
    public async Task EachNamesSwingReadingsAreDrawnOnItsPageAsTheStoreHoldsThem()
    {
        using var store = await FixtureReplay.ReplayedAsync();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var stored = SwingRows(
            store,
            "SELECT ticker, bars, return_short, return_long, place_short, place_long, " +
            "recent_high, high_session, pullback_sessions, depth, dry_up, " +
            $"tightness, note FROM swing_reading WHERE session_date = '{SwingNight}' ORDER BY ticker;");

        // Every member of the fixture, the one the night read nothing for among them.
        Assert.Equal(4, stored.Count);

        var read = 0;
        var noted = 0;

        foreach (var row in stored)
        {
            var ticker = row[0];
            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{ticker}"));
            var card = Assert.Single(Blocks(page, "<section class=\"card\"[^>]* data-card=\"swing\">.*?</section>"));
            var region = Regex.Match(card, "<div class=\"swing-readings\" data-ticker=\"([^\"]+)\" data-session=\"([^\"]+)\" data-bars=\"(\\d+)\">");

            Assert.True(region.Success, $"no swing readings region on {ticker}'s page");
            Assert.Equal((ticker, SwingNight, row[1]), (region.Groups[1].Value, region.Groups[2].Value, region.Groups[3].Value));

            // The key saying how to read it, and what to take from it.
            Assert.Matches("<div class=\"key\">.*?How to read it\\..*?<p class=\"take\"><b>What to take from it\\.</b> These are facts about the chart\\.", card.Replace("\n", " ", StringComparison.Ordinal));

            // Its refusal to rank is the one wording the code holds, and the page states the refusal in no
            // other words: the page's opening and this key are the two places it is drawn.
            // see: A page ranks no company as an investment, and a rank it draws is a return's place among the members' returns
            Assert.Contains("this page " + SinglePageApp.RankRefusal + ".", card, StringComparison.Ordinal);
            Assert.Equal(2, Regex.Matches(page, Regex.Escape(SinglePageApp.RankRefusal)).Count);
            Assert.Equal(2, Regex.Matches(page, "as an investment").Count);

            if (row[12].Length > 0)
            {
                // A name the night read nothing for says why, in the reason the store holds, and draws no figure.
                Assert.Contains($"no swing readings for this night: {row[12]}", card, StringComparison.Ordinal);
                Assert.DoesNotContain("<table class=\"swing-table\"", card, StringComparison.Ordinal);

                noted++;

                continue;
            }

            string Cell(string reading) =>
                Regex.Match(card, $"<tr data-reading=\"{reading}\"><td>[^<]*</td>(<td[^>]*>.*?</td>)</tr>", RegexOptions.Singleline) is { Success: true } found
                    ? found.Groups[1].Value
                    : throw new InvalidOperationException($"no {reading} row on {ticker}'s page");

            // Each return whole with its place, and drawn with its sign and the place in per cent.
            Assert.Contains($"data-value=\"{WholeOf(row[2])}\" data-place=\"{WholeOf(row[4])}\"", Cell("return-short"), StringComparison.Ordinal);
            Assert.Contains($"data-value=\"{WholeOf(row[3])}\" data-place=\"{WholeOf(row[5])}\"", Cell("return-long"), StringComparison.Ordinal);
            Assert.Contains(
                string.Create(CultureInfo.InvariantCulture, $"{double.Parse(row[2], CultureInfo.InvariantCulture):+0.00;-0.00;0.00}% over {SwingReadings.ReturnShortSessions} sessions, above {double.Parse(row[4], CultureInfo.InvariantCulture) * 100:0.0}% of the other members' returns"),
                Cell("return-short"),
                StringComparison.Ordinal);

            // The recent high, the session it was made on and the sessions since, each whole.
            Assert.Contains(
                $"data-value=\"{decimal.Parse(row[6], CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)}\" data-session=\"{row[7]}\" data-since=\"{row[8]}\"",
                Cell("recent-high"),
                StringComparison.Ordinal);
            Assert.Contains($"{Figures.Price(decimal.Parse(row[6], CultureInfo.InvariantCulture))} on {row[7]}, {row[8]} session(s) ago", Cell("recent-high"), StringComparison.Ordinal);

            // The pullback, the volume while it came down and the tightness, each whole and at two places.
            foreach (var (reading, column) in new[] { ("depth", 9), ("dry-up", 10), ("tightness", 11) })
            {
                Assert.Contains($"data-value=\"{WholeOf(row[column])}\"", Cell(reading), StringComparison.Ordinal);
                Assert.Contains(
                    double.Parse(row[column], CultureInfo.InvariantCulture).ToString("0.00", CultureInfo.InvariantCulture),
                    Cell(reading),
                    StringComparison.Ordinal);
            }

            read++;
        }

        Assert.Equal((3, 1), (read, noted));
    }

    [Fact]
    public async Task TheUniverseTableDrawsEachMembersFourSwingReadingsAsTheStoreHoldsThem()
    {
        using var store = await FixtureReplay.ReplayedAsync();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/universe"));

        Assert.Contains("<th>Strength</th><th>Pullback</th><th>Dry-up</th><th>Tightness</th>", page, StringComparison.Ordinal);

        var stored = SwingRows(
            store,
            "SELECT ticker, strength, depth, dry_up, tightness " +
            $"FROM swing_reading WHERE session_date = '{SwingNight}' ORDER BY ticker;");
        var drawn = 0;
        var notRead = 0;

        foreach (var row in stored)
        {
            var markup = Regex.Match(page, $"<tr data-ticker=\"{row[0]}\"[^>]*>(.*?)</tr>", RegexOptions.Singleline);

            Assert.True(markup.Success, $"no universe row for {row[0]}");

            string SwingCellOf(string attribute) =>
                Regex.Match(markup.Groups[1].Value, $"<td class=\"num swing\" data-{attribute}=\"([^\"]*)\">(.*?)</td>") is { Success: true } found
                    ? found.Value
                    : throw new InvalidOperationException($"no {attribute} cell on {row[0]}'s row");

            Assert.Contains($"data-strength=\"{WholeOf(row[1])}\"", SwingCellOf("strength"), StringComparison.Ordinal);
            Assert.Contains($"data-depth=\"{WholeOf(row[2])}\"", SwingCellOf("depth"), StringComparison.Ordinal);
            Assert.Contains($"data-dry-up=\"{WholeOf(row[3])}\"", SwingCellOf("dry-up"), StringComparison.Ordinal);
            Assert.Contains($"data-tightness=\"{WholeOf(row[4])}\"", SwingCellOf("tightness"), StringComparison.Ordinal);

            if (row[1].Length == 0)
            {
                // A member the night read nothing for draws a word in each of the four, never a zero.
                Assert.Contains(">not read</span>", SwingCellOf("strength"), StringComparison.Ordinal);
                notRead++;
            }
            else
            {
                Assert.Contains(
                    string.Create(CultureInfo.InvariantCulture, $">{double.Parse(row[1], CultureInfo.InvariantCulture) * 100:0}%</td>"),
                    SwingCellOf("strength"),
                    StringComparison.Ordinal);
            }

            drawn++;
        }

        Assert.Equal((4, 1), (drawn, notRead));
    }

    [Fact]
    public async Task TheNightsBreadthIsDrawnOnTonightsHeaderAndTheRunPageAsTheStoreHoldsIt()
    {
        using var store = await FixtureReplay.ReplayedAsync();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var market = Assert.Single(SwingRows(
            store,
            "SELECT members, counted, above, breadth, counted_context, breadth_context, volume_counted, median_volume_ratio " +
            $"FROM market_reading WHERE session_date = '{SwingNight}';"));
        var breadth = double.Parse(market[3], CultureInfo.InvariantCulture);
        var context = double.Parse(market[5], CultureInfo.InvariantCulture);

        // Tonight's header: the breadth whole on its element with its counts, and in words.
        var tonight = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{SwingNight}"));
        var line = Assert.Single(Blocks(tonight, "<p class=\"breadth\"[^>]*>.*?</p>"));

        Assert.Contains($"data-breadth=\"{WholeOf(market[3])}\" data-counted=\"{market[1]}\" data-members=\"{market[0]}\" data-breadth-context=\"{WholeOf(market[5])}\"", line, StringComparison.Ordinal);
        Assert.Contains(
            string.Create(CultureInfo.InvariantCulture, $"breadth: {breadth * 100:0.0}% of the {market[1]} members read close above their own {SwingReadings.BreadthAverageSessions}-day average, and {context * 100:0.0}% above their {SwingReadings.ContextAverageSessions}-day average, as context"),
            line,
            StringComparison.Ordinal);

        // The run page's market card: its three parts, each whole on its row and in words.
        var run = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{SwingNight}"));
        var card = Assert.Single(Blocks(run, "<section class=\"card\"[^>]* data-card=\"market\">.*?</section>"));

        Assert.Contains($"<tr data-part=\"breadth\" data-breadth=\"{WholeOf(market[3])}\" data-counted=\"{market[1]}\" data-above=\"{market[2]}\">", card, StringComparison.Ordinal);
        Assert.Contains(
            string.Create(CultureInfo.InvariantCulture, $"{breadth * 100:0.0}% of the {market[1]} members read close above their own {SwingReadings.BreadthAverageSessions}-day average, of {market[0]} in the index"),
            card,
            StringComparison.Ordinal);
        Assert.Contains($"<tr data-part=\"context\" data-breadth-context=\"{WholeOf(market[5])}\" data-counted-context=\"{market[4]}\">", card, StringComparison.Ordinal);
        Assert.Contains($"<tr data-part=\"volume\" data-median-volume-ratio=\"{WholeOf(market[7])}\" data-volume-counted=\"{market[6]}\">", card, StringComparison.Ordinal);
        Assert.Contains(
            string.Create(CultureInfo.InvariantCulture, $"{double.Parse(market[7], CultureInfo.InvariantCulture):0.00} over the {market[6]} members trading"),
            card,
            StringComparison.Ordinal);

        // A night the store holds no market reading for says so on both pages rather than drawing a zero.
        var earlier = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-09-03"));

        Assert.Contains("no market reading is stored for this night", Assert.Single(Blocks(earlier, "<section class=\"card\"[^>]* data-card=\"market\">.*?</section>")), StringComparison.Ordinal);
    }

    [Fact]
    public void ABreadthReadOverTooFewMembersSaysSoWithItsCounts()
    {
        // Two of five members held both a close and the long average: fewer than half, so no share,
        // and both pages say how many were held of how many.
        var view = new MarketView(new DateOnly(2026, 9, 8), 5, 2, 1, null, 2, 2, null, 0, null);
        var marks = new MarkRenderer();

        var header = WebUtility.HtmlDecode(marks.NightHeader(new DateOnly(2026, 9, 8), 5, 0, null, null, market: view));
        var region = WebUtility.HtmlDecode(marks.MarketReading(view));

        Assert.Contains("breadth: not available, 2 of the 5 members hold a close and a 200-day average, fewer than half", header, StringComparison.Ordinal);
        Assert.Contains("not available: 2 of the 5 members hold a close and a 200-day average, fewer than half", region, StringComparison.Ordinal);
        Assert.Contains("data-breadth=\"none\"", region, StringComparison.Ordinal);
        Assert.Contains("breadth: no market reading is stored for this night", marks.NightHeader(new DateOnly(2026, 9, 8), 5, 0, null, null), StringComparison.Ordinal);
    }

    [Fact]
    public void ASwingReadingShortOfItsWindowSaysSoWithItsBarsAndAHighMadeOnTheNightHasNoDryUp()
    {
        // Forty bars: no return over either span, no tightness, a high and its pullback read, and the
        // high made on the night itself so no session has come down from it.
        var on = new DateOnly(2026, 9, 8);
        var table = WebUtility.HtmlDecode(new MarkRenderer().SwingTable(
            "ZZAA",
            new SwingReadingsView(on, 40, null, null, null, null, 25m, on, 0, 0.5, null, null, null)));

        Assert.Contains("data-bars=\"40\"", table, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(table, "not available, 40 bars").Count);
        Assert.Contains("the high was made on the night, so no session has come down from it", table, StringComparison.Ordinal);
        Assert.Contains("0 session(s) ago", table, StringComparison.Ordinal);

        // A lone member's return has no other to be placed among and says so rather than drawing a place.
        var alone = WebUtility.HtmlDecode(new MarkRenderer().SwingTable(
            "ZZBB",
            new SwingReadingsView(on, 252, 4.0, 8.0, null, null, 25m, on, 0, 0.5, null, 0.9, null)));

        Assert.Contains("+4.00% over 63 sessions, with no other member's return to place it among", alone, StringComparison.Ordinal);
    }

    [Fact]
    public void EachReturnIsDrawnBesideItsOwnPlace()
    {
        // The two places differ, so a page drawing either return beside the other's place reads wrong.
        var on = new DateOnly(2026, 9, 8);
        var table = WebUtility.HtmlDecode(new MarkRenderer().SwingTable(
            "ZZCC",
            new SwingReadingsView(on, 252, 4.0, -8.0, 0.25, 0.75, 25m, on, 0, 0.5, null, 0.9, null)));

        Assert.Contains("<td data-value=\"4\" data-place=\"0.25\">+4.00% over 63 sessions, above 25.0% of the other members' returns</td>", table, StringComparison.Ordinal);
        Assert.Contains("<td data-value=\"-8\" data-place=\"0.75\">-8.00% over 126 sessions, above 75.0% of the other members' returns</td>", table, StringComparison.Ordinal);
    }
}
