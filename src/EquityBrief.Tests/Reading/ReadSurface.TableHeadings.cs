using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Shortlist;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 5.8: each column heading of tonight's list, a peers table and a Past picks table says what its
// column holds while the pointer is over it or it has the focus, read back off each table word for word against
// sentences written here, with every window and threshold they name stated as a number.
public partial class ReadSurface
{
    // Each heading of the first row of the table named, as drawn: its name, its classes, the words it shows and
    // what it says, and how many headings the row draws in all, so a heading drawn without a sentence shows.
    static (IReadOnlyList<(string Heading, string Classes, string Shown, string Says)> Tipped, int All) TableHeadingsOf(string markup, string table)
    {
        var row = WebUtility.HtmlDecode(Regex.Match(markup, $"<table class=\"{table}\"[^>]*>(?:<thead>)?<tr>(.*?)</tr>", RegexOptions.Singleline).Groups[1].Value);

        return (
            [
                .. Regex.Matches(row, "<th class=\"([^\"]*)\" tabindex=\"0\" data-heading=\"([^\"]+)\"><span class=\"th-t\">([^<]+)</span><span class=\"head-tip\" role=\"tooltip\">([^<]+)</span></th>")
                    .Select(heading => (heading.Groups[2].Value, heading.Groups[1].Value, heading.Groups[3].Value, heading.Groups[4].Value)),
            ],
            Regex.Matches(row, "<th[ >]").Count);
    }

    // What a reason's column says after when it fires, alike for every reason.
    const string AMarkMeans = " A mark on a row means it fired for the stock that evening, and holding the pointer on the mark shows the values it fired on. Where the swing filter listed the evening, the reasons are context and chose no row.";

    static string SaidOf(string heading) => SaidOfEachColumn.Single(column => column.Heading == heading).Says;

    [Fact]
    public void TonightsListSaysInEachHeadingWhatItsColumnHoldsOnAnEveningOfEitherRule()
    {
        var night = new DateOnly(2026, 9, 18);
        var marks = new MarkRenderer();

        (string Heading, string Classes, string Shown, string Says)[] fixedColumns =
        [
            ("#", "place tipped", "#", "The row's place in the order the list is drawn in, counted from one."),
            ("Name", "tipped", "Name", "The ticker selects the row and draws its plan and levels beneath the list. Beside it, report opens the stock's written report, or not written opens its page where no report is written yet, and the company is named beneath."),
            ("Close", "r tipped", "Close", "The stock's closing price on the evening."),
            ("Day", "r tipped", "Day", "How far the close moved on the day against the close before it, in per cent."),
            ("Trend", "tipped", "Trend", SaidOf("Trend") + " Beside it, where the night read one, the state the company's reported quarters gave it, with what its numbers say under the pointer."),
            ("Distance to levels", "c tipped", "Distance to levels", SaidOf("Distance")),
            ("Reward to risk", "r tipped", "Reward to risk", "How far the plan's target sits above its buy against how far its stop sits below it, so 2.00 means twice as much to gain as to lose. It is a fact about the chart and not a chance of anything. Where the plan states none, the row says why."),
        ];

        (string Heading, string Classes, string Shown, string Says)[] reasonColumns =
        [
            ("at entry zone", "rz tipped", "entry", "At entry zone fires when the close is inside one of the plan's buying zones." + AMarkMeans),
            ("crossed a level", "rz tipped", "crossed", "Crossed a level fires when the close moved through a band's edge it was on the other side of the session before." + AMarkMeans),
            ("breakout on volume", "rz tipped", "breakout", "Breakout on volume fires when the close is above a band that sat at or above the previous close, on volume above the 50-day average." + AMarkMeans),
            ("trend state changed", "rz tipped", "trend", "Trend state changed fires when the trend's word differs from the night before." + AMarkMeans),
            ("unusual volume", "rz tipped", "volume", "Unusual volume fires when the day's volume is above 2 times the 50-day average." + AMarkMeans),
            ("earnings soon", "rz tipped", "earnings", "Earnings soon fires when the next earnings report is within 20 sessions on the exchange's calendar." + AMarkMeans),
        ];

        (string Heading, string Classes, string Shown, string Says) news = ("News", "tipped", "News", MarkRenderer.NewsSays);

        (string Heading, string Classes, string Shown, string Says) gates =
            ("Gates", "tipped", "Gates", "How the swing filter passed the stock: the setup's family, the session its trigger arrived on, and the plan the trade gate read with its reward to risk and how far its stop sits below the entry in typical days' moves. Hold the pointer on the cell for each gate's reason.");

        // An evening the reasons listed draws no gates' column, and one the swing filter listed draws it after
        // the reward to risk; every heading either way carries its sentence, one to each column a row draws.
        foreach (var (list, expected) in new[]
        {
            (marks.TonightList(FiredNight(night, 3), SinglePageApp.TonightDrawn, []), (IReadOnlyList<(string, string, string, string)>)[.. fixedColumns, news, .. reasonColumns]),
            (marks.TonightList(FiredNight(night, 3), SinglePageApp.TonightDrawn, [], new ListRuleView(ListRules.Filter, true, 0.6, 0.45, [])), [.. fixedColumns, news, gates, .. reasonColumns]),
        })
        {
            var (tipped, all) = TableHeadingsOf(list, "list-table");

            Assert.Equal(expected, tipped);
            Assert.Equal(expected.Count, all);
            Assert.Equal(all, Regex.Matches(Regex.Match(list, "<tr data-ticker=\"N00\".*?</tr>", RegexOptions.Singleline).Value, "<td[ >]").Count);
        }

        // Every reason the list draws a column for is one the sentences name.
        Assert.Equal(ShortlistSeries.Reasons, reasonColumns.Select(column => column.Heading));
    }

    [Fact]
    public void APeersTableSaysInEachHeadingWhatItsColumnHolds()
    {
        var on = new DateOnly(2026, 9, 8);
        PeerCell[] rows =
        [
            new("ZZBB", true, 20m, null, new PeerFigures(on, 21m, 4.76, null, 45), null, Company: "Zed Bee"),
            new("ZZAA", false, 10m, "uptrend", new PeerFigures(on, 20m, 50, -30, 252), null, new PeerLikeness(true, 0.1234, 251), "Zed Ay"),
        ];

        var table = new MarkRenderer().PeersTable("ZZBB", new PeersView("sector", "Energy", rows, Others: 5));
        var (tipped, all) = TableHeadingsOf(table, "peers-table");

        Assert.Equal(
            [
                ("Name", "tipped", "Name", "The first row is this page's stock, then at most ten of its group, those sharing its industry first. Each ticker opens its own page, and holding the pointer over it draws its year of closes with its nearest support and resistance."),
                ("Moved with it", "r tipped", "Moved with it", "How closely the stock's daily moves followed this page's stock over the sessions both hold: 1 is in step every day, 0 is no relation, and a negative figure moved the other way. A pair sharing fewer than 60 sessions says how many instead."),
                ("Close", "tipped", "Close", "The stock's last stored close."),
                ("Below the year's high", "tipped", "Below the year's high", "How far the close sits below the highest price among the bars the store holds for it, in per cent, with that high and how many bars it was read over."),
                ("Return over 60 sessions", "tipped", "Return over 60 sessions", "The change in the close over the last 60 sessions, in per cent."),
                ("Trend", "tipped", "Trend", SaidOf("Trend")),
                ("Distance", "tipped", "Distance", SaidOf("Distance")),
            ],
            tipped);
        Assert.Equal(7, all);
        Assert.Equal(all, Regex.Matches(Regex.Match(table, "<tr data-ticker=\"ZZAA\".*?</tr>", RegexOptions.Singleline).Value, "<td[ >]").Count);
    }

    [Fact]
    public void APastPicksTableSaysInEachHeadingWhatItsColumnHoldsWithTheStocksColumnOrWithout()
    {
        const string Bought = "The evening the live list recommended the trade. It is taken as bought at that evening's close.";

        (string Heading, string Classes, string Shown, string Says)[] after =
        [
            ("Business that night", "tipped", "Business that night", "The state the company's reported quarters gave it on the evening it was listed, or not read that night where none was stored yet."),
            ("Setup", "tipped", "Setup", "The setup the page listed the trade under. A trade listed before the page drew setups was the pullback's."),
            ("Buy", "r tipped", "Buy", "The price the plan buys at."),
            ("Stop", "r tipped", "Stop", "The price the plan sells at to cut the loss, or the first level of a stop that trails the price."),
            ("Target", "r tipped", "Target", "The price the plan takes its gain at. A setup that trails its stop names none."),
            ("Trade", "tipped", "Trade", "The line runs from the stop on the left, in green, to the target on the right, in orange, with the buy marked between them. The dot is where the price is now, hollow while the trade is open and filled where it finished."),
            ("Status", "tipped", "Status", "What became of the trade: open, reached target, stopped out, or ran out of time where its holding limit passed before either."),
            ("Sessions held", "r tipped", "Sessions held", "Trading sessions from the night listed to the session it finished on, or to the night drawn while it is still open."),
            ("Result", "r tipped", "Result", "What the trade came to in multiples of the risk it took, the fall from the buy to the stop: +2.00 made twice that risk and -1.00 lost it, and a close through the stop can read below -1. Open while the trade runs."),
        ];

        var marks = new MarkRenderer();

        // The Past picks screen's table, naming the stock, and a name page's, whose night opens that night's page.
        foreach (var (named, expected) in new[]
        {
            (true, (IReadOnlyList<(string, string, string, string)>)[("Night listed", "tipped", "Night listed", Bought), ("Stock", "tipped", "Stock", "The stock, which opens its page as it stood on the night listed."), .. after]),
            (false, [("Night listed", "tipped", "Night listed", Bought.TrimEnd('.') + ", and the date opens this stock's page as it stood that night."), .. after]),
        })
        {
            var (tipped, all) = TableHeadingsOf(marks.PicksTable([], named), "picks-table");

            Assert.Equal(expected, tipped);
            Assert.Equal(expected.Count, all);
        }
    }

    [Fact]
    public async Task TonightsListAndAPeersTableDrawEveryHeadingWithWhatItsColumnHoldsOnTheirScreens()
    {
        using var store = await FixtureExpectations.WithListings();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var tonight = await client.GetStringAsync("/screens/tonight");
        var name = await client.GetStringAsync($"/screens/name/{FiredNamesOn(store, NightIn(store))[0]}");

        // Every heading of both tables carries a sentence, and the reasons' columns the six in their set order.
        foreach (var (screen, table, least) in new[] { (tonight, "list-table", 13), (name, "peers-table", 7) })
        {
            var (tipped, all) = TableHeadingsOf(screen, table);

            Assert.True(all >= least, $"the {table} draws {all} headings");
            Assert.Equal(all, tipped.Count);
            Assert.All(tipped, heading => Assert.True(heading.Says.Length > 20, $"the {heading.Heading} heading says \"{heading.Says}\""));
        }

        Assert.Equal(ShortlistSeries.Reasons, TableHeadingsOf(tonight, "list-table").Tipped.Where(heading => heading.Classes == "rz tipped").Select(heading => heading.Heading));
    }
}
