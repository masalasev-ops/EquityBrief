using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// From the operator's correction of 2026-11-24 at about 11:10 pm: Home's Waiting to buy chosen by setup from a row of chips
// under its heading, "All" first and chosen when the page opens; each card's setup named as Home names it; every card the
// same rows in the same places, so the cards stand one height with their rows lined up; the checks of the night that placed
// the order folded on every card and opened over its lower half; and a take-profit's notes on the stock's own page.
// see: Home's waiting orders are chosen by setup from a row of chips, each card one height with its checks folded
public partial class ReadSurface
{
    // The sixteen orders, eight earnings drift and eight pullback, and two dips standing furthest from their buy price.
    static WaitingOrderView[] EighteenOrders() =>
    [
        .. SixteenOrders(),
        new(DipRule.Name, "D17", new DateOnly(2026, 11, 12), 50m, new DateOnly(2026, 11, 26), "Dip Seventeen Inc.", 62.5m),
        new(DipRule.Name, "D18", new DateOnly(2026, 11, 13), 40m, new DateOnly(2026, 11, 27), null, 52m),
    ];

    // The checks a pullback passed on the night it was placed, five steps, as the night stores its first line.
    static DecisionCardView PlacingCard(WaitingOrderView order) =>
        new("MID", order.Listed, order.Family, order.Ticker, "the pullback rule", order.Price, order.Stop, null,
            [new CardLineView(1, EquityBrief.Core.Cards.CardLines.TrendName, "tick", "Passed every step: market, share above the 200-day average 55.6% at or above 45%; trend and strength, strength 0.96 at or above 0.50; setup, a pullback into an anchored support band; trigger, fired on 2026-11-10; trade, reward against the risk 4.42 at or above 2.")],
            null);

    // Eighteen orders in three setups: "All 18" chosen, then the earnings drift's 8, the pullback's 8 and the dip's 2 in the
    // order each setup's first order stands nearest its buy price (W02 a drift at 1%, W05 a pullback at 2.9%, D17 a dip at 20%),
    // each named as Home names it, every ticker chip and card carrying its setup so a chip narrows both to it.
    [Fact]
    public void WaitingToBuyDrawsARowOfSetupChipsAllChosenFirstThenEachSetupWithItsCount()
    {
        var orders = EighteenOrders();
        var html = MarkRenderer.WaitingCarousel(orders, "S&P 400", CarouselNight, setupName: SinglePageApp.IdeaTag);
        var bar = Regex.Match(html, "<div class=\"wo-setup-bar\".*?</div>", RegexOptions.Singleline).Value;

        Assert.Equal(
            [("all", "All", "18"), (DriftRule.Name, "Strong earnings", "8"), (SetupFamilies.Pullback, "Bounce in an uptrend", "8"), (DipRule.Name, "Quick rebound", "2")],
            Regex.Matches(bar, "<label class=\"wo-setup-chip\" for=\"wo-setup-sp400-[^\"]+\" data-setup=\"([^\"]+)\"[^>]*>(?:<span class=\"fam-swatch\"[^>]*></span>)?([^<]+)<span class=\"wo-setup-count\">(\\d+)</span></label>").Select(match => (match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value)));
        Assert.Single(Regex.Matches(bar, " checked>"));
        Assert.Contains("id=\"wo-setup-sp400-all\" value=\"all\" checked>", bar, StringComparison.Ordinal);

        // The row stands under the heading and above the line and the ticker chips, apart from the setup switcher's.
        Assert.True(html.IndexOf("wo-title", StringComparison.Ordinal) < html.IndexOf("wo-setup-bar", StringComparison.Ordinal));
        Assert.True(html.IndexOf("wo-setup-bar", StringComparison.Ordinal) < html.IndexOf("class=\"wo-said\"", StringComparison.Ordinal));
        Assert.True(html.IndexOf("wo-setup-bar", StringComparison.Ordinal) < html.IndexOf("class=\"wo-chips\"", StringComparison.Ordinal));
        Assert.DoesNotContain("setup-switch", html, StringComparison.Ordinal);

        // Each setup's chip counts the cards and the ticker chips of that setup, and each card's tag names its setup as Home does.
        var cards = Regex.Matches(html, "<article class=\"wo-card[^\"]*\" data-family=\"([^\"]+)\".*?</article>", RegexOptions.Singleline);
        var chips = Regex.Matches(html, "<button type=\"button\" role=\"listitem\" class=\"wo-chip[^\"]*\" data-at=\"\\d+\" data-ticker=\"[^\"]+\" data-family=\"([^\"]+)\"");

        foreach (var family in new[] { DriftRule.Name, SetupFamilies.Pullback, DipRule.Name })
        {
            var counted = orders.Count(order => order.Family == family);

            Assert.Equal(counted, cards.Count(card => card.Groups[1].Value == family));
            Assert.Equal(counted, chips.Count(chip => chip.Groups[1].Value == family));
        }

        Assert.All(cards, card => Assert.Contains($"<span class=\"fam-swatch\" data-family=\"{card.Groups[1].Value}\"></span>{WebUtility.HtmlEncode(SinglePageApp.IdeaTag(card.Groups[1].Value))}</span>", card.Value, StringComparison.Ordinal));

        // Where no page names them, each setup is named by its label, on its chip and its tags.
        var labelled = MarkRenderer.WaitingCarousel(orders, "S&P 400", CarouselNight);

        Assert.Contains($"<span class=\"fam-swatch\" data-family=\"{DipRule.Name}\"></span>{SetupFamilies.Named(DipRule.Name)!.Label}<span class=\"wo-setup-count\">2</span>", labelled, StringComparison.Ordinal);

        // The page's script narrows the ticker chips and the cards to the setup chosen, counts the heading again and leaves the
        // hidden cards out of the counter and the arrows.
        var shell = new SinglePageApp().Shell("T");

        Assert.Contains("event.target.matches('.wo-setup-input')", shell, StringComparison.Ordinal);
        Assert.Contains("const kept = (one) => chosen.value === 'all' || one.getAttribute('data-family') === chosen.value;", shell, StringComparison.Ordinal);
        Assert.Contains("for (const card of group.querySelectorAll('.wo-card')) { card.hidden = !kept(card);", shell, StringComparison.Ordinal);
        Assert.Contains("for (const chip of group.querySelectorAll('.wo-chip')) { chip.hidden = !kept(chip); }", shell, StringComparison.Ordinal);
        Assert.Contains("title.textContent = 'Waiting to buy (' + shown + ')';", shell, StringComparison.Ordinal);
        Assert.Contains("const cards = woAll(track).filter((card) => !card.hidden);", shell, StringComparison.Ordinal);
        Assert.Contains(".wo-card[hidden],.wo-chip[hidden]{display:none}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains("@media (max-width:600px){.wo-setup-bar{flex-wrap:nowrap;overflow-x:auto;", Stylesheet.Css, StringComparison.Ordinal);
    }

    // Every card the same rows in the same places, whatever it holds: a card with no company, one with no buy price, one with a
    // take-profit on half and one whose stock met the rules again; each row a row of the track's grid, so the cards stand one
    // height with their rows lined up, and no note under a plan row, those standing on the stock's own page.
    [Fact]
    public void EveryWaitingCardDrawsTheSameRowsInTheSamePlacesSoTheCardsStandOneHeight()
    {
        var orders = EighteenOrders().Append(new WaitingOrderView(SetupFamilies.Pullback, "NOPRICE", new DateOnly(2026, 11, 12), null, new DateOnly(2026, 12, 10))).ToArray();
        var met = orders.Single(order => order.Ticker == "W05");
        var again = new DecisionCardView("MID", CarouselNight, met.Family, met.Ticker, "the rule", 100m, 95m, null, [], null) { HeldAgain = new HeldAgainView(met.Family, met.Listed, "MID") };
        var html = MarkRenderer.WaitingCarousel(orders, "S&P 400", CarouselNight, again: [again], setupName: SinglePageApp.IdeaTag);
        var cards = Regex.Matches(html, "<article class=\"wo-card[^\"]*\"[^>]*data-ticker=\"([^\"]+)\"[^>]*>(.*?)</article>", RegexOptions.Singleline);

        Assert.Equal(19, cards.Count);
        Assert.Equal(10, MarkRenderer.WaitingCardRows.Length);

        foreach (Match card in cards)
        {
            // Every row a child of the card, in order, and nothing else beside them.
            var rows = Regex.Matches(card.Groups[2].Value, "<(?:a|div|p) class=\"[^\"]*\" data-row=\"([^\"]+)\"").Select(match => match.Groups[1].Value);

            Assert.Equal(MarkRenderer.WaitingCardRows, rows);
            Assert.DoesNotContain("sale-note", card.Value, StringComparison.Ordinal);
            Assert.DoesNotContain("row-note", card.Value, StringComparison.Ordinal);
        }

        Assert.Contains("<div class=\"home-co wo-co\" data-row=\"company\">&#160;</div>", cards.Single(card => card.Groups[1].Value == "D18").Value, StringComparison.Ordinal);
        Assert.Contains("<div class=\"wo-bar\" data-row=\"bar\"></div>", cards.Single(card => card.Groups[1].Value == "NOPRICE").Value, StringComparison.Ordinal);
        Assert.Contains($"<span class=\"wo-state\">{MarkRenderer.WaitingState}</span><b class=\"wo-again\">{HeldAgainView.AgainMark}</b></div>", cards.Single(card => card.Groups[1].Value == "W05").Value, StringComparison.Ordinal);

        // The stylesheet lays each row on a row of the track's own grid, ten of them, and the company on one line cut short.
        Assert.Contains(".wo-card{display:grid;grid-template-rows:subgrid;grid-row:span 10;", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".waiting-orders .wo-track,.waiting-orders .wo-grid{grid-template-rows:repeat(10,auto)}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".wo-co{white-space:nowrap;overflow:hidden;text-overflow:ellipsis;", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".wo-placed{margin:0;min-height:calc(2em * 1.45);", Stylesheet.Css, StringComparison.Ordinal);

        // The setup switcher's carousels above keep their own cards' layout: the rows apply to the waiting orders' track alone.
        Assert.DoesNotContain(".wo-track{grid-template-rows", Stylesheet.Css, StringComparison.Ordinal);
    }

    // The revision as one short chip: "Revision 7 · since 16 Nov" for the running one, "Revision 3 · ran 10 to 13 Nov" for one
    // no longer running, its month named once, and across a month's end both months; the full words its title.
    [Fact]
    public void AWaitingCardNamesItsRevisionAsOneShortChip()
    {
        Assert.Equal("Revision 7 · since 16 Nov", RevisionTags.ShortWords(new RevisionView(7, new DateOnly(2026, 11, 16), null, null)));
        Assert.Equal("Revision 3 · ran 10 to 13 Nov", RevisionTags.ShortWords(new RevisionView(3, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 13), null)));
        Assert.Equal("Revision 2 · ran 28 Oct to 3 Nov", RevisionTags.ShortWords(new RevisionView(2, new DateOnly(2026, 10, 28), new DateOnly(2026, 11, 3), null)));

        var revisions = new Dictionary<string, IReadOnlyList<RevisionView>>(StringComparer.Ordinal)
        {
            [DriftRule.Name] = [new RevisionView(3, new DateOnly(2026, 11, 10), new DateOnly(2026, 11, 13), null), new RevisionView(4, new DateOnly(2026, 11, 16), null, null)],
        };

        using var scope = RevisionTags.Use("500", revisions);
        var html = WebUtility.HtmlDecode(MarkRenderer.WaitingCarousel(
            [new(DriftRule.Name, "FERG", new DateOnly(2026, 11, 10), 240.92m, new DateOnly(2026, 12, 9), "Ferguson Enterprises Inc.", 251.23m)],
            "S&P 500",
            CarouselNight));

        Assert.Contains("title=\"Placed by revision 3, ran 10 Nov to 13 Nov 2026\"", html, StringComparison.Ordinal);
        Assert.Contains(">Revision 3 · ran 10 to 13 Nov</a></div>", html, StringComparison.Ordinal);
    }

    // The checks of the night that placed the order, on every card, folded under one line counting them from the card that
    // night stored; opened, they stand in a panel laid over the card's lower half, so no card changes height. A card whose
    // stock met the rules again folds the placing night's checks alone, the sentence and tonight's checks standing on its card
    // among tonight's ideas from the operator's correction of 2026-11-25.
    [Fact]
    public void EveryWaitingCardFoldsTheChecksOfTheNightThatPlacedItAndOpensThemOverItsLowerHalf()
    {
        var orders = SixteenOrders();
        var placed = orders.Single(order => order.Ticker == "W01");
        var met = orders.Single(order => order.Ticker == "W07");
        var again = new DecisionCardView("MID", CarouselNight, met.Family, met.Ticker, "the rule", 100m, 95m, null,
            [new CardLineView(1, EquityBrief.Core.Cards.CardLines.TrendName, "tick", "Passed every step: market, share above the 200-day average 51.0% at or above 45%; trigger, fired on 2026-11-16.")],
            null) { HeldAgain = new HeldAgainView(met.Family, met.Listed, "MID") };
        WaitingOrderView[] drawn = [.. orders.Select(order => order.Ticker is "W01" or "W07" ? order with { Placing = PlacingCard(order) } : order)];
        var html = WebUtility.HtmlDecode(MarkRenderer.WaitingCarousel(drawn, "S&P 400", CarouselNight, again: [again]));
        string Card(string ticker) => Regex.Match(html, $"<article class=\"wo-card[^\"]*\"[^>]*data-ticker=\"{ticker}\".*?</article>", RegexOptions.Singleline).Value;

        // W01's pullback, placed on 11 Nov, passed five checks: the count is the card's own, worked by hand from its line.
        Assert.Equal(5, MarkRenderer.AgainChecks(PlacingCard(placed)));
        Assert.Contains("<details class=\"wo-fold\" data-checks=\"5\"><summary>Why it qualified on 11 Nov (5 checks)</summary><div class=\"wo-panel\"><button type=\"button\" class=\"wo-close\" aria-label=\"Close the checks\">Close</button><section class=\"card-why\"", Card("W01"), StringComparison.Ordinal);
        Assert.Equal(5, Regex.Matches(Card("W01"), "<li class=\"card-tick\"").Count);
        Assert.Contains("<h5>On 11 Nov, when its order was placed</h5>", Card("W01"), StringComparison.Ordinal);

        // An order whose night stored no card says so in its fold and counts nothing.
        Assert.Contains("<summary>Why it qualified on 10 Nov</summary>", Card("W03"), StringComparison.Ordinal);
        Assert.Contains("<p class=\"wo-none\">The night of 10 Nov stored no checks for this order.</p>", Card("W03"), StringComparison.Ordinal);

        // W07 met the rules again on 16 Nov: its mark stands, and its one fold holds 11 Nov's checks alone, neither the sentence
        // nor tonight's checks, which stand on its card among tonight's ideas.
        var metCard = Card("W07");

        Assert.Contains($"<b class=\"wo-again\">{HeldAgainView.AgainMark}</b>", metCard, StringComparison.Ordinal);
        Assert.Contains("<summary>Why it qualified on 11 Nov (5 checks)</summary>", metCard, StringComparison.Ordinal);
        Assert.Contains("<h5>On 11 Nov, when its order was placed</h5>", metCard, StringComparison.Ordinal);
        Assert.DoesNotContain("wo-again-line", metCard, StringComparison.Ordinal);
        Assert.DoesNotContain("16 Nov", metCard, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(metCard, "<details "));

        // Opened, the panel is laid over the card's lower half out of its flow, and its own button closes it.
        Assert.Contains(".wo-panel{position:absolute;left:0;right:0;bottom:0;top:45%;overflow:auto;", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".wo-card{display:grid;grid-template-rows:subgrid;grid-row:span 10;row-gap:8px;align-items:start;position:relative;", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains("const close = event.target.closest ? event.target.closest('.wo-close') : null;", new SinglePageApp().Shell("T"), StringComparison.Ordinal);
    }

    // Every class Waiting to buy's setup row and its cards' new rows write is selected in the stylesheet only inside its own
    // marked block, so a class shared with a rule meant for something else fails here, as the setup switch's once did.
    [Fact]
    public void EveryClassTheWaitingSetupRowAndItsCardsRowsWriteIsSelectedOnlyInItsOwnBlockOfTheStylesheet()
    {
        var html = MarkRenderer.WaitingCarousel([.. SixteenOrders().Select(order => order with { Placing = PlacingCard(order) })], "S&P 400", CarouselNight);
        var bar = Regex.Match(html, "<div class=\"wo-setup-bar\".*?</div>", RegexOptions.Singleline).Value;
        var fold = Regex.Match(html, "<div class=\"wo-why\".*?</details></div>", RegexOptions.Singleline).Value;

        string[] Classes(string markup) => [.. Regex.Matches(markup, "class=\"([^\"]+)\"").SelectMany(match => match.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))];

        // The row's own classes, and the card's new ones: its company's, its revision's, its fold's and its panel's; the
        // checklist inside the panel keeps the card's own classes.
        string[] own = [.. Classes(bar).Concat(Classes(fold)).Concat(["wo-co", "wo-rev"])
            .Where(name => name is not ("fam-swatch" or "card-why" or "card-ticks" or "card-tick" or "card-mark" or "card-words"))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];

        Assert.Equal(["wo-close", "wo-co", "wo-fold", "wo-panel", "wo-rev", "wo-setup-bar", "wo-setup-chip", "wo-setup-count", "wo-setup-input", "wo-why"], own);

        var begin = Stylesheet.Css.IndexOf("/* waiting by setup: begin", StringComparison.Ordinal);
        var end = Stylesheet.Css.IndexOf("/* waiting by setup: end */", StringComparison.Ordinal);

        Assert.True(begin >= 0 && end > begin, "The stylesheet holds no marked block for Waiting to buy's setup row.");

        var outside = Stylesheet.Css[..begin] + Stylesheet.Css[end..];
        var inside = Stylesheet.Css[begin..end];

        foreach (var name in own.Append("wo-rev-chip").Append("wo-none"))
        {
            var selected = new Regex($@"\.{Regex.Escape(name)}(?![\w-])");

            Assert.True(selected.IsMatch(inside), $"Waiting to buy's class '{name}' has no rule of its own.");
            Assert.False(selected.IsMatch(outside), $"Waiting to buy's class '{name}' is selected by a rule outside its block, one meant for something else.");
        }
    }

    // A waiting order's notes on how it takes profit leave its card on Home for its plan on Tonight, which See the plan opens,
    // under its take-profit; the stock's own page keeps them under its trade's take-profit.
    [Fact]
    public void AWaitingOrdersTakeProfitNotesStandOnItsPlanOnTonight()
    {
        var order = Waiting(4, 100m, 104m);
        var note = Assert.IsType<string>(MarkRenderer.WaitingTargetNote(order));
        var none = new TradeCountView(0, 0, 0, 0, 0, 0, null);
        var groups = new TradeGroupsView(null, null, none, none, none, 0) { StillWaitingOrders = [order] };
        var entries = SinglePageApp.PlanEntries([], [], [], groups, new Dictionary<string, DecisionCardView>());
        var plan = WebUtility.HtmlDecode(new MarkRenderer().EveryPlan(entries, CarouselNight, "400", order.Ticker));

        Assert.Matches($"<div data-figure=\"target\"><dt>Take profit at</dt><dd class=\"target\">[^<]*</dd><small>{Regex.Escape(note)}</small></div>", plan);
        Assert.DoesNotContain(WebUtility.HtmlEncode(note), MarkRenderer.WaitingCarousel([order], "S&P 400", CarouselNight), StringComparison.Ordinal);
        Assert.DoesNotContain(note, MarkRenderer.WaitingCarousel([order], "S&P 400", CarouselNight), StringComparison.Ordinal);

        var page = MarkRenderer.NameTradesCard("EBAY", [new NameTradeView(SetupFamilies.Pullback, "Revision 7, running since 16 Nov 2026", true, new DateOnly(2026, 11, 20), null, 111.33m, 96.24m, 141.50m, 18, 113.77m, "The last close stands 2.2% above its buy price.") { TakeProfitNote = note }]);

        Assert.Contains($"<td class=\"r num\">$141.50<span class=\"row-note\">{WebUtility.HtmlEncode(note)}</span></td>", page, StringComparison.Ordinal);
    }
}
