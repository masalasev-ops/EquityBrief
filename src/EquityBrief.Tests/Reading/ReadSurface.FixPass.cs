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
