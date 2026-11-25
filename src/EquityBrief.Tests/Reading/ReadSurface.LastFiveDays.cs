using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Paper;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// From the operator's correction of 2026-11-25: Home's list of the last five trading days' buys, sales and orders that left
// unbought moves to Paper trading's "Last 5 trading days" tab, Home keeping one line counting them with the way there, and the
// reconciliation holding the line to the tab.
// see: Home counts the last five trading days' buys and sales and orders left unbought in one line, and Paper trading lists them in a tab
public partial class ReadSurface
{
    // HUM's dip order placed no price on 18 and 19 Nov, FERG's order of a revision replaced on 16 Nov cancelled, CSCO bought by
    // the Fundamentals family on 19 Nov at $98.99 and sold on 20 Nov at $90.64. Worked by hand: 1 bought, 1 sold, 1 cancelled
    // and 2 not placed; the line "In the last 5 trading days: 1 bought, 1 sold, 1 cancelled, 2 not placed. See them on Paper
    // trading" linking to the tab; each row with its price and its result.
    [Fact]
    public void HomesLineCountsEachKindOfTheLastFiveDaysAndEachRowHoldsItsPriceAndResult()
    {
        PlannedTradeRow[] planned =
        [
            AgreeOrder("dip", "HUM", new(2026, 11, 18), null, notFilled: true) with { Order = AtLevel(null, 10, "the second zone") },
            AgreeOrder("dip", "HUM", new(2026, 11, 19), null, notFilled: true) with { Order = AtLevel(null, 10, "the second zone") },
            AgreeOrder("drift", "FERG", new(2026, 11, 10), 246.4, notFilled: true) with { Why = PlanOrderWords.Replaced(new DateOnly(2026, 11, 16)), WhyOn = new DateOnly(2026, 11, 25) },
            AgreeOrder("pullback", "EBAY", new(2026, 11, 20), 111.33),
        ];
        var csco = new PaperTrade("CSCO", new DateOnly(2026, 11, 19), 98.99m, 91.55m, new DateOnly(2026, 11, 20), 90.64m, null, 1, Listed: new DateOnly(2026, 11, 18));
        var fundamentals = new PaperAccountSource("list-improving", "improving", PaperAccountView.Live, "Live", "the live Fundamentals rule", null, true, [(csco, -0.084)], new DateOnly(2026, 11, 16));

        var left = RevisionScreen.LeftOf(planned, AgreeNight, AgreeSessions, [fundamentals]);
        var href = (new PaperLink(Universes.Large.Word) { Tab = MarkRenderer.RecentTab }).Href;

        Assert.Equal(
            $"<p class=\"recent-line\" data-bought=\"1\" data-sold=\"1\" data-cancelled=\"1\" data-not-placed=\"2\">In the last 5 trading days: 1 bought, 1 sold, 1 cancelled, 2 not placed. <a href=\"{href.Replace("&", "&amp;", StringComparison.Ordinal)}\">See them on Paper trading</a></p>",
            MarkRenderer.RecentLine(left, href));
        Assert.EndsWith("&tab=recent", href, StringComparison.Ordinal);

        var sold = Assert.Single(left, one => one.Kind == LeftOrderView.SoldKind);
        var bought = Assert.Single(left, one => one.Kind == LeftOrderView.BoughtKind);
        var cancelled = Assert.Single(left, one => one.Kind == LeftOrderView.CancelledKind);

        Assert.Equal((90.64m, "-8.4% on its buy of $98.99."), (sold.Price!.Value, sold.Result));
        Assert.Equal((98.99m, "At the open. Sold since."), (bought.Price!.Value, bought.Result));
        Assert.Equal(("FERG", 246.40m, "Its revision was replaced on 16 Nov."), (cancelled.Ticker, cancelled.Price!.Value, cancelled.Result));
        Assert.All(left.Where(one => one.Kind == LeftOrderView.NotPlacedKind), one => Assert.Equal(("HUM", (decimal?)null), (one.Ticker, one.Price)));

        // Nothing drawn where nothing moved.
        Assert.Equal(string.Empty, MarkRenderer.RecentLine([], href));
    }

    // The reconciliation over constructed pages: FERG's order cancelled tonight stands in Paper trading's tab and Home's line
    // counts it: nothing amiss. Planted: Home's line counting two cancelled, named; the tab missing FERG, named on its line and
    // in the counts.
    [Fact]
    public void TheReconciliationHoldsHomesLineToPaperTradingsLastFiveDaysTab()
    {
        PlannedTradeRow[] planned =
        [
            AgreeOrder("pullback", "EBAY", new(2026, 11, 20), 111.33),
            AgreeOrder("drift", "FERG", new(2026, 11, 10), 246.4, notFilled: true) with { Why = PlanOrderWords.Replaced(new DateOnly(2026, 11, 16)), WhyOn = new DateOnly(2026, 11, 25) },
        ];
        static string Card(string family, string ticker, string listed) => $"<article class=\"wo-card fam-edge\" data-family=\"{family}\" data-ticker=\"{ticker}\" data-listed=\"{listed}\" data-price=\"1\">";
        static string Line(int cancelled) => $"<p class=\"recent-line\" data-bought=\"0\" data-sold=\"0\" data-cancelled=\"{cancelled}\" data-not-placed=\"0\">";
        var tonight = Card("pullback", "EBAY", "2026-11-20");
        var nights = new Dictionary<DateOnly, string> { [new DateOnly(2026, 11, 20)] = "<tr data-ticker=\"EBAY\" data-family=\"pullback\" data-place=\"1\">", [AgreeNight] = tonight };
        var paper = new Dictionary<string, string> { ["pullback"] = "<tr data-ticker=\"EBAY\" data-placed=\"2026-11-20\" data-take-profit=\"1\">" };
        const string ferg = "<tr data-ticker=\"FERG\" data-kind=\"cancelled\" data-family=\"drift\" data-on=\"2026-11-24\">";
        static DateOnly? Never(string index, string owner, DateOnly listed) => null;
        (IReadOnlyList<PicksProof.PickLine> Lines, IReadOnlyList<string> Extras) Checked(string home, string recent)
        {
            var lines = PicksProof.Check("GSPC", AgreeNight, new DateOnly(2026, 11, 16), planned, AgreeSessions, Never, new PicksProof.DrawnPages(home, tonight, nights, paper, recent), out var extras);
            return (lines, extras);
        }

        var agreed = Checked(Card("pullback", "EBAY", "2026-11-20") + Line(1), ferg);

        Assert.All(agreed.Lines, line => Assert.Empty(line.Mismatches));
        Assert.Empty(agreed.Extras);
        Assert.Equal("last 5 days", agreed.Lines.Single(line => line.Ticker == "FERG").Paper);

        Assert.Equal(["Home's line counts 2 cancelled in the last 5 trading days, Paper trading's tab lists 1"], Checked(Card("pullback", "EBAY", "2026-11-20") + Line(2), ferg).Extras);

        var missing = Checked(Card("pullback", "EBAY", "2026-11-20") + Line(1), string.Empty);

        Assert.Equal(["Paper trading's last 5 trading days name no move of it"], missing.Lines.Single(line => line.Ticker == "FERG").Mismatches);
        Assert.Equal(["Home's line counts 1 cancelled in the last 5 trading days, Paper trading's tab lists 0"], missing.Extras);
    }

    // Over the family pages' store on the night of 2 Oct: the live pullback and breakout rules bought nine at the open in the
    // last five trading days, and the breakout sold B0 on 30 Sep at $53.00, bought on 25 Sep at $50.00. Worked by hand: Home
    // draws "In the last 5 trading days: 9 bought, 1 sold." and no list, and Paper trading's tab, across every rule, lists
    // those ten over its two pages, newest first.
    [Fact]
    public async Task HomeDrawsTheLineAndPaperTradingsTabListsWhatItCounts()
    {
        using var store = await FamilyPagesStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var home = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/home"));
        var line = Regex.Match(home, "<p class=\"recent-line\" data-bought=\"(\\d+)\" data-sold=\"(\\d+)\" data-cancelled=\"(\\d+)\" data-not-placed=\"(\\d+)\">In the last 5 trading days: [^<]+\\. <a href=\"([^\"]+)\">See them on Paper trading</a></p>");

        Assert.DoesNotContain("class=\"left-notes\"", home, StringComparison.Ordinal);
        Assert.True(line.Success, "Home draws no line of the last five trading days");

        var paper = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper?tab=" + MarkRenderer.RecentTab));
        var tab = new System.Text.StringBuilder();

        for (var page = 1; page < 20; page++)
        {
            var drawn = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/paper?tab={MarkRenderer.RecentTab}&page={page}"));

            tab.Append(Assert.Single(Blocks(drawn, "<table class=\"paper-bought paper-recent\" data-tab=\"recent\".*?</table>")));

            if (Regex.Match(drawn, "Showing \\d+ to (\\d+) of (\\d+)") is not { Success: true } shown || shown.Groups[1].Value == shown.Groups[2].Value)
            {
                break;
            }
        }

        var rows = Regex.Matches(tab.ToString(), "<tr data-ticker=\"[^\"]+\" data-kind=\"([^\"]+)\" data-family=\"[^\"]+\" data-on=\"([^\"]+)\">").ToArray();

        Assert.Contains($"aria-selected=\"true\" aria-pressed=\"true\" data-tab-name=\"{MarkRenderer.RecentTab}\"", paper, StringComparison.Ordinal);
        Assert.Contains("across every rule and every revision on the S&P 500", paper, StringComparison.Ordinal);
        Assert.Contains("data-bought=\"9\" data-sold=\"1\" data-cancelled=\"0\" data-not-placed=\"0\">In the last 5 trading days: 9 bought, 1 sold. <a", line.Value, StringComparison.Ordinal);
        Assert.Equal(10, rows.Length);
        Assert.Matches("<tr data-ticker=\"B0\" data-kind=\"sold\" data-family=\"breakout\" data-on=\"2026-09-30\">.*?>53\\.00<.*?\\+6\\.0.*?on its buy of \\$50\\.00\\.", tab.ToString());

        foreach (var (kind, at) in LeftOrderView.Kinds.Select((kind, at) => (kind, at)))
        {
            Assert.Equal(int.Parse(line.Groups[at + 1].Value, System.Globalization.CultureInfo.InvariantCulture), rows.Count(row => row.Groups[1].Value == kind));
        }

        Assert.Equal(rows.Select(row => row.Groups[2].Value).OrderDescending(StringComparer.Ordinal), rows.Select(row => row.Groups[2].Value));
        Assert.EndsWith("&tab=recent", line.Groups[5].Value, StringComparison.Ordinal);
    }
}
