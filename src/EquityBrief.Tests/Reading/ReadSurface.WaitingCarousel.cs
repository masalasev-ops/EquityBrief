using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Loop;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 23.10: on the operator's answers of 2026-11-16, item 1, the orders earlier nights placed still waiting for
// their price drawn as cards nearest their buy price first, every fact the list carried on its card with the rows of its
// plan; more than three as the row that scrolls with a chip each, three or fewer as the grid; and tonight's ideas counted at
// the open apart from those bought only on a dip, each of the heading's four forms read back by the counts proof.
// see: A live rule's trade under a plan is drawn on every page as its plan made it, and its waiting orders are cards of their own nearest their buy price first
public partial class ReadSurface
{
    static readonly DateOnly CarouselNight = new(2026, 11, 16);

    // An order of a constructed night: its stock, its buy price, the night's close and the night it was placed, with its plan's
    // stop and sale, a take-profit on half for the fourth, none yet for the ninth.
    static WaitingOrderView Waiting(int at, decimal price, decimal close)
    {
        var ticker = "W" + at.ToString("00", CultureInfo.InvariantCulture);
        var stop = price - 2m;
        var taking = at == 9 ? null : new TakeProfitView(price + 6m, TakeProfitSource.Risk, Multiple: 3, Buy: price, Stop: stop) { Half = at == 4 };

        return new WaitingOrderView(at % 2 == 0 ? "drift" : "pullback", ticker, new DateOnly(2026, 11, 10 + at % 3), price, PlanOrderWords.LastDay(new DateOnly(2026, 11, 10 + at % 3), 20), "Company " + ticker + " Inc.", close)
        {
            Stop = stop,
            Sale = new PlanSaleView(PlanPoints.RuleSale, price, stop, null, [], null, null, 63) { TakeProfit = taking },
        };
    }

    // Sixteen orders, each its close a different share above its price: W01 7.5% above, W02 1%, W03 15% and on, stated so the
    // nearest-first order is worked by hand: 1% (W02), 2% (W08), 2.5% (W12), 2.9% (W05), 4% (W14), 5% (W09), 6% (W16), 7.5%
    // (W01), 8% (W11), 9% (W06), 10% (W13), 11% (W04), 12% (W15), 13% (W07), 14% (W10), 15% (W03).
    static readonly (int At, decimal Above)[] Sixteen =
    [
        (1, 0.075m), (2, 0.01m), (3, 0.15m), (4, 0.11m), (5, 0.029m), (6, 0.09m), (7, 0.13m), (8, 0.02m),
        (9, 0.05m), (10, 0.14m), (11, 0.08m), (12, 0.025m), (13, 0.10m), (14, 0.04m), (15, 0.12m), (16, 0.06m),
    ];

    static WaitingOrderView[] SixteenOrders() =>
        [.. Sixteen.Select(one => Waiting(one.At, 100m, Math.Round(100m / (1m - one.Above), 4)))];

    [Fact]
    public void SixteenWaitingOrdersDrawSixteenCardsAndSixteenChipsNearestFirstWithEveryFactTheListCarried()
    {
        var orders = SixteenOrders();
        var html = MarkRenderer.WaitingCarousel(orders, "S&P 400", CarouselNight);
        string[] nearest = ["W02", "W08", "W12", "W05", "W14", "W09", "W16", "W01", "W11", "W06", "W13", "W04", "W15", "W07", "W10", "W03"];

        Assert.Contains("data-carousel=\"true\"", html, StringComparison.Ordinal);
        Assert.Contains("<p class=\"list-count wo-title\">Waiting to buy (16)</p>", html, StringComparison.Ordinal);
        Assert.Contains("1 to 4 of 16", html, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(html, "class=\"wo-arrow\"").Count);
        Assert.Contains(MarkRenderer.WaitingLine, html, StringComparison.Ordinal);

        var cards = Regex.Matches(html, "<article class=\"wo-card[^\"]*\"[^>]*data-ticker=\"([^\"]+)\".*?</article>", RegexOptions.Singleline);
        var chips = Regex.Matches(html, "<button type=\"button\" role=\"listitem\" class=\"wo-chip([^\"]*)\" data-at=\"(\\d+)\" data-ticker=\"([^\"]+)\"");

        Assert.Equal(nearest, cards.Select(card => card.Groups[1].Value));
        Assert.Equal(nearest, chips.Select(chip => chip.Groups[3].Value));
        Assert.Equal(Enumerable.Range(0, 16).Select(at => at.ToString(CultureInfo.InvariantCulture)), chips.Select(chip => chip.Groups[2].Value));

        // Within 3% the chip is filled: W02, W08, W12 and W05; the first four are in view before the page measures.
        Assert.Equal(["W02", "W08", "W12", "W05"], chips.Where(chip => chip.Groups[1].Value.Contains("near", StringComparison.Ordinal)).Select(chip => chip.Groups[3].Value));
        Assert.Equal(["W02", "W08", "W12", "W05"], chips.Where(chip => chip.Groups[1].Value.Contains("in-view", StringComparison.Ordinal)).Select(chip => chip.Groups[3].Value));

        // Every fact the list carried, on its card: the stock and its company, the price, the close and how far above, the
        // night and setup that placed it and its last day; with the trading days left and the plan's rows.
        foreach (var (order, card) in nearest.Select(ticker => (orders.Single(one => one.Ticker == ticker), cards.Single(card => card.Groups[1].Value == ticker).Value)))
        {
            var setup = EquityBrief.Core.Families.SetupFamilies.Named(order.Family)!.Label;

            Assert.Contains($"href=\"#/chart/{order.Ticker}\">{order.Ticker}</a>", card, StringComparison.Ordinal);
            // On the operator's answer of 2026-11-17, item 6, the company in full, as the pick cards draw it.
            Assert.Contains($"<div class=\"home-co\">Company {order.Ticker} Inc.</div>", card, StringComparison.Ordinal);
            Assert.Contains(FormattableString.Invariant($"data-listed=\"{order.Listed:yyyy-MM-dd}\" data-price=\"100.00\" data-close=\"{order.LastClose:0.00}\" data-last-day=\"{order.LastDay:yyyy-MM-dd}\""), card, StringComparison.Ordinal);
            Assert.Contains(System.Net.WebUtility.HtmlEncode(MarkRenderer.WaitingCondition(order)), card, StringComparison.Ordinal);
            Assert.Matches(FormattableString.Invariant($"Buys only if it falls to \\$100\\.00\\. Last close \\${order.LastClose:#,##0.00}, [0-9.]+% above\\."), System.Net.WebUtility.HtmlDecode(card));
            Assert.Contains(FormattableString.Invariant($"Placed {order.Listed:d MMM} by {setup.ToLowerInvariant()}. Cancelled if not filled by {order.LastDay:d MMM}, {PlanOrderWords.SessionsLeft(CarouselNight, order.LastDay)} trading days left."), System.Net.WebUtility.HtmlDecode(card), StringComparison.Ordinal);
            Assert.Contains($"<span class=\"fam-tag\" data-family=\"{order.Family}\">", card, StringComparison.Ordinal);
            Assert.Contains("<span class=\"wo-state\">Waiting to buy</span>", card, StringComparison.Ordinal);
            Assert.Contains("<span>Buy price $100.00</span>", card, StringComparison.Ordinal);
            // From the operator's correction of 2026-11-24 the stock's chart page, where its plan is drawn.
            Assert.Contains($"href=\"#/chart/{order.Ticker}\">See the plan</a>", card, StringComparison.Ordinal);
            Assert.DoesNotContain("setup-record", card, StringComparison.Ordinal);
        }

        // The take-profit on half draws its two rows; the order with none yet says it is added at the next night.
        var half = cards.Single(card => card.Groups[1].Value == "W04").Value;
        var none = cards.Single(card => card.Groups[1].Value == "W09").Value;

        Assert.Contains($"<span>{TakeProfitView.HalfLabel}</span>", half, StringComparison.Ordinal);
        Assert.Contains($"<span>{TakeProfitView.OtherHalfLabel}</span>", half, StringComparison.Ordinal);
        Assert.Contains($"<div data-plan=\"target\"><span>{TakeProfitView.Label}</span><b class=\"aim\">{MarkRenderer.TakeProfitNextNight}</b></div>", none, StringComparison.Ordinal);
        Assert.Contains("<span>Otherwise sell after</span><b class=\"aim\">63 trading days</b>", none, StringComparison.Ordinal);
    }

    [Fact]
    public void TwoWaitingOrdersDrawThePlainGridWithNoArrowsAndNoChips()
    {
        var html = MarkRenderer.WaitingCarousel([Waiting(1, 100m, 104m), Waiting(2, 50m, 51m)], "S&P 600", CarouselNight);

        Assert.Contains("data-carousel=\"false\"", html, StringComparison.Ordinal);
        Assert.Contains("<div class=\"wo-grid\">", html, StringComparison.Ordinal);
        Assert.DoesNotContain("wo-arrow", html, StringComparison.Ordinal);
        Assert.DoesNotContain("wo-chip", html, StringComparison.Ordinal);
        Assert.DoesNotContain("data-wo-track", html, StringComparison.Ordinal);
        Assert.Equal(["W02", "W01"], Regex.Matches(html, "<article class=\"wo-card[^\"]*\"[^>]*data-ticker=\"([^\"]+)\"").Select(card => card.Groups[1].Value));
        Assert.Equal(string.Empty, MarkRenderer.WaitingCarousel([], "S&P 600", CarouselNight));

        // A plan row carrying a note under its price wraps the note onto a line of its own, so the label keeps its line: read
        // back on 2026-11-17, "Take profit at" stood on three lines beside the rules restart's note.
        Assert.Contains(".home-prices.wo-plan div[data-plan]{flex-wrap:wrap}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".home-prices .sale-note{flex-basis:100%", Stylesheet.Css, StringComparison.Ordinal);
    }

    [Fact]
    public void NoWaitingCardLacksABuyPriceAStopAndATakeProfitRow()
    {
        var html = MarkRenderer.WaitingCarousel([.. SixteenOrders(), Waiting(17, 80m, 82m) with { Sale = null }], "S&P 500", CarouselNight);

        Assert.Equal(17, Regex.Matches(html, "<article class=\"wo-card").Count);
        Assert.Empty(MarkRenderer.WaitingCardsLackingARow(html));

        // The guard finds a card whose take-profit row is taken out.
        var planted = Regex.Replace(html, "(<article class=\"wo-card[^\"]*\"[^>]*data-ticker=\"W02\".*?)<div data-plan=\"target\">", "$1<div data-plan=\"none\">", RegexOptions.Singleline);

        Assert.Equal(["W02"], MarkRenderer.WaitingCardsLackingARow(planted));
    }

    // The heading's four forms, as Home draws them and as the counts proof reads them back.
    [Theory]
    [InlineData(3, 0, "Tonight's ideas: 3 stocks to buy at Tuesday's open")]
    [InlineData(1, 0, "Tonight's ideas: 1 stock to buy at Tuesday's open")]
    [InlineData(0, 2, "Tonight's ideas: 2 new, each bought only if its price dips to the buy price")]
    [InlineData(2, 1, "Tonight's ideas: 2 to buy at Tuesday's open, 1 more bought only on a dip")]
    [InlineData(0, 0, "Tonight's ideas: 0 stocks to buy tomorrow morning")]
    public void HomesHeadingCountsTheIdeasAtTheOpenApartFromThoseBoughtOnlyOnADipAndTheProofReadsEachForm(int atOpen, int onDip, string heading)
    {
        HomeIdea Idea(int at, bool dip) => new("drift", "I" + at.ToString(CultureInfo.InvariantCulture), null, "why", 10m, 9m, 12m, false, null) { Zone = dip ? new ZoneBuy(9.5m, 20, 10m) : null };

        var view = new EquityBrief.Web.App.HomeView(
            Universes.Of("400"), CarouselNight, false, null, null, true, null, [], null, null,
            [.. Enumerable.Range(0, atOpen).Select(at => Idea(at, false)), .. Enumerable.Range(atOpen, onDip).Select(at => Idea(at, true))],
            atOpen + onDip, "tomorrow", null, null);

        Assert.Equal(heading, EquityBrief.Web.App.SinglePageApp.IdeasTitle(view));
        Assert.Equal(atOpen + onDip, CountsProof.HomeIdeas($"<h2>{heading}</h2><p>Each idea comes with what to pay, where to give up if it goes wrong, and how it sells.</p>"));
    }

    [Fact]
    public void ANewPickThatWaitsForAPriceSaysInItsBoxItIsBoughtOnlyIfThePriceFallsToItByItsLastDay()
    {
        var said = MarkRenderer.ZoneBox(new ZoneBuy(376.21m, 20, 389.59m), CarouselNight);

        Assert.Equal("Not bought unless the price falls to $376.21 by 15 Dec 2026. Tonight's close was $389.59, 3.4% above. If it rises from here, there is no trade.", said);
    }
}
