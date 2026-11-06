using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Shortlist;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// Tonight and Home over the nights the operator's fix pass names, each part held to its new words against the page as it
// read before: the buys, Home's order, the plan's links, the Fundamentals rule's words, the entry check's line and the paper
// account's dates.
public partial class ReadSurface
{
    static readonly DateOnly FixNight = new(2026, 11, 5);

    static BuyPointView Point(string family, string label, string ticker, decimal buy, decimal stop, decimal target) =>
        new(family, label, new FamilyPickCell(new ListingCell(ticker, FixNight, 1, 0, buy, [ShortlistSeries.AtEntryZone]), 1, buy, stop, target, false, 2m, "why", []));

    static ImprovingRowCell Improver(string ticker, int passed, int applied, string status, decimal? close = null) =>
        new(ticker, ticker + " Inc", passed, applied, passed - 2, applied, "Its sales rose.", status, "line", close);

    static ImprovingCardView FundamentalsCard(IReadOnlyList<ImprovingRowCell> rows) =>
        new("SML", FixNight, true, null, rows.Count(row => row.Status == MarkRenderer.BoughtWords), 0, 0, null, null, null, rows, [], "rule");

    // The S&P 600's buys of the night: LTC's drift and the Fundamentals family's PARR and HCC, where the page read "3 stocks to
    // buy at tomorrow's open, at about tonight's close: these 1 and the fundamentals' 2 on their own card below." with LTC its
    // one row.
    // see: Tonight's buys name every buy on the index, a buy no plan sells drawn with how it sells in its stop's and target's place
    [Fact]
    public void TonightsBuysNameEveryBuyTheFundamentalsInTheirSlotWithHowEachSells()
    {
        var card = FundamentalsCard([Improver("PARR", 8, 9, MarkRenderer.BoughtWords, 21.50m), Improver("HCC", 7, 7, MarkRenderer.BoughtWords, 64.10m)]);
        var points = new[] { Point("drift", "Earnings drift", "LTC", 36.00m, 34.00m, 40.00m), Point("dip", "Short-term dip", "DIPP", 10m, 9m, 12m) };
        var planless = SinglePageApp.PlanlessBuys("600", card, null, null);
        var buys = WebUtility.HtmlDecode(new MarkRenderer().TomorrowsBuys(points, null, night: FixNight, planless: planless));

        Assert.Contains("4 stocks to buy at tomorrow's open, at about tonight's close.", buys, StringComparison.Ordinal);
        Assert.DoesNotContain("on their own card below", buys, StringComparison.Ordinal);
        Assert.DoesNotContain("these ", buys, StringComparison.Ordinal);

        // Every buy a row, the Fundamentals' after the drift's and before the dip's, each in its card's order.
        Assert.Equal(["LTC", "PARR", "HCC", "DIPP"], Regex.Matches(buys, "<tr data-ticker=\"([^\"]+)\" data-family=\"[^\"]+\" data-place=\"[0-9]+\"").Select(match => match.Groups[1].Value));

        // A Fundamentals row says how it sells in the stop's and target's place, its close the buy, its count of the checks
        // that apply its business, and its link opens its row on its own card.
        Assert.Equal(2, Regex.Matches(buys, "<td class=\"r plan-sells\" colspan=\"2\" data-sells>Sells when its filings weaken</td>").Count);
        Assert.Contains("data-buy=\"21.50\"", buys, StringComparison.Ordinal);
        Assert.Contains("<td class=\"business-cell\">7 of 7 apply</td>", buys, StringComparison.Ordinal);
        Assert.Contains("<a class=\"how-sells\" href=\"#/tonight?universe=600&setup=improving&name=HCC\">How it sells</a>", buys, StringComparison.Ordinal);

        // One buy alone is counted as one.
        Assert.Contains("1 stock to buy at tomorrow's open", WebUtility.HtmlDecode(new MarkRenderer().TomorrowsBuys([points[0]], null, night: FixNight)), StringComparison.Ordinal);
    }

    // The S&P 500's buys of the night: five drifts, the Fundamentals family's AIG, CVS and ODFL and the dip's NUE. Home drew
    // six and named none of the three it left out; it draws Tonight's first six and names the rest with their families.
    // see: Home's ideas are Tonight's buys in Tonight's order, those left out named with their families
    [Fact]
    public void HomesIdeasAreTonightsBuysInTonightsOrderAndTheRestAreNamed()
    {
        BuyPointView[] points =
        [
            .. new[] { "EVRG", "STE", "EW", "APP", "GWW" }.Select(ticker => Point("drift", "Earnings drift", ticker, 50m, 45m, 60m)),
            Point("dip", "Short-term dip", "NUE", 150m, 140m, 160m),
        ];
        var card = FundamentalsCard([Improver("AIG", 6, 6, MarkRenderer.BoughtWords, 80.93m), Improver("CVS", 8, 9, MarkRenderer.BoughtWords), Improver("ODFL", 9, 9, MarkRenderer.BoughtWords)]);
        var order = MarkRenderer.BuysInOrder(points, SinglePageApp.PlanlessBuys("500", card, null, null))
            .Select(buy => buy.Point?.Pick.Row.Ticker ?? buy.Planless!.Ticker)
            .ToArray();

        Assert.Equal(["EVRG", "STE", "EW", "APP", "GWW", "AIG", "CVS", "ODFL", "NUE"], order);

        HomeIdea Idea(string family, string ticker) => new(family, ticker, null, "Why.", 10m, null, null, false, null);

        var left = new[] { Idea("improving", "CVS"), Idea("improving", "ODFL"), Idea("dip", "NUE") };
        var home = WebUtility.HtmlDecode(new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Ideas = [Idea("drift", "EVRG")], IdeasCount = 9, LeftOut = left }, string.Empty));

        Assert.Contains("<p class=\"home-more\" data-more=\"3\">3 more on Tonight: CVS and ODFL (Getting stronger as a business), NUE (Quick rebound), not drawn here to keep this page short.</p>", home, StringComparison.Ordinal);
        Assert.Null(SinglePageApp.LeftOutLine([]));
        Assert.Equal("1 more on Tonight: NUE (Quick rebound), not drawn here to keep this page short.", SinglePageApp.LeftOutLine([Idea("dip", "NUE")]));
    }

    // Home's See the plan opened the stock's chart page; it opens Tonight at the stock's row, and an idea no plan sells opens
    // its row on its own card with How it sells. Tonight's row ran its plan's control and its report's word together, "Open
    // plan report"; the report's label says what it opens.
    // see: See the plan opens Tonight at the stock's row with its plan in view, and a buy no plan sells opens its row on its own card
    [Fact]
    public void SeeThePlanOpensTonightAtTheRowAndABuyNoPlanSellsSaysHowItSells()
    {
        HomeIdea Idea(string family, string ticker) => new(family, ticker, null, "Why.", 10m, family == "drift" ? 9m : null, family == "drift" ? 12m : null, false, null);

        var home = new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Reading = Universes.Mid, Ideas = [Idea("drift", "HL"), Idea("improving", "AVNT"), Idea("quality", "QQ")], IdeasCount = 3 }, string.Empty);

        Assert.Contains("<a class=\"home-plan\" href=\"#/tonight?universe=400&amp;name=HL\">See the plan</a>", home, StringComparison.Ordinal);
        Assert.Contains("<a class=\"home-how\" href=\"#/tonight?universe=400&amp;setup=improving&amp;name=AVNT\">How it sells</a>", home, StringComparison.Ordinal);
        Assert.Contains("<a class=\"home-how\" href=\"#/tonight?universe=400&amp;setup=quality&amp;name=QQ\">How it sells</a>", home, StringComparison.Ordinal);
        Assert.DoesNotContain("#/chart/HL\">See the plan", home, StringComparison.Ordinal);

        var buys = new MarkRenderer().TomorrowsBuys([Point("drift", "Earnings drift", "HL", 50m, 45m, 60m)], null, night: FixNight);

        Assert.Contains(">Report: not written yet</a>", buys, StringComparison.Ordinal);
        Assert.DoesNotContain(">not written</a>", buys, StringComparison.Ordinal);
    }

    // The card opens with its buys named, marks each on its table and never folds one behind the first ten rows.
    [Fact]
    public void TheFundamentalsCardOpensWithItsBuysAndNeverFoldsOneBehindItsShownRows()
    {
        ImprovingRowCell[] rows =
        [
            .. Enumerable.Range(1, 11).Select(at => Improver("R" + at, 6, 9, MarkRenderer.ImprovedWords)),
            Improver("LATE", 8, 9, MarkRenderer.BoughtWords),
        ];
        var drawn = new MarkRenderer().ImprovingCard(FundamentalsCard(rows));
        var fold = drawn.IndexOf("<details class=\"fold improving-more\"", StringComparison.Ordinal);

        Assert.Contains("<p class=\"improving-bought-h\"><b>Bought at tomorrow's open</b></p>", WebUtility.HtmlDecode(drawn), StringComparison.Ordinal);
        Assert.Contains("<li data-ticker=\"LATE\">", drawn, StringComparison.Ordinal);
        Assert.Contains("data-more=\"1\"", drawn, StringComparison.Ordinal);

        // The twelfth row, a buy, stands among the rows drawn open; the eleventh, no buy, behind the fold.
        Assert.True(drawn.IndexOf("<tr class=\"improving-row improving-buy\" data-ticker=\"LATE\"", StringComparison.Ordinal) is var late && late > 0 && late < fold);
        Assert.True(drawn.IndexOf("data-ticker=\"R11\"", StringComparison.Ordinal) > fold);
        Assert.Contains("<span class=\"buy-mark\" data-buy>Buy</span>", drawn, StringComparison.Ordinal);
    }
}
