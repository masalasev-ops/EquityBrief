using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Cards;

namespace EquityBrief.Tests.Reading;

// read-surface, 24.46: Tonight redesigned on the operator's order of 2026-11-26. Every plan the rules hold on the night stands
// in one list in three groups, each at its own address in one design; a waiting order's plan is the card the night it was
// placed stored, its checks said as that night's, and none worked out again from tonight's figures; and Home's See the plan
// opens the plan at its address.
// see: Tonight lists every plan the rules hold on the night in one place, each at its own address in one design
public partial class ReadSurface
{
    // The rows 24.46 adds that this check reaches.
    internal static string[] TonightPlanRows =>
    [
        CheckReach.Key("15.7 Tonight", "Tonight at a glance"),
        CheckReach.Key("15.7 Tonight", "Every plan tonight"),
        CheckReach.Key("15.7 Tonight", "A plan"),
        CheckReach.Key("15.7 Tonight", "A waiting order's plan"),
        CheckReach.Key("15.7 Tonight", "The setups' table"),
    ];

    // Each setup's row of the setups' table, its family and its state in words, in the table's order.
    static IReadOnlyList<string> SetupStates(string page) =>
        [.. Regex.Matches(page, "<tr class=\"st-row[^\"]*\" data-setup=\"([^\"]+)\" data-count=\"[0-9]+\" data-state=\"([^\"]+)\">").Select(match => $"{match.Groups[1].Value}: {match.Groups[2].Value}")];

    // The plan a page draws for a stock in a group: its article, whole.
    static string PlanArticle(string page, string group, string ticker) =>
        Assert.Single(Blocks(page, $"<article class=\"pl-plan[^\"]*\" id=\"[^\"]+\" data-plan=\"{Regex.Escape(ticker)}\" data-g=\"{group}\".*?</article>"));

    // M2's card of the night it met the rules again, as the met-again test stores it, its order of 30 Sep at 47.50 waiting.
    static void MetAgainCard(TemporaryStore store)
    {
        var plan = Doubled(new PlanOrder("2.22 typical days under the close, 1.5 typical days under the buy, the rule's sale", "2.22 typical days under the close", 46.9, 5, PlanPoints.TypicalStop(1.5), 47, null, 2, null, [], 3, null, 63).Json());
        const string lines = "[{\"line\":1,\"name\":\"Trend and strength\",\"verdict\":\"pass\",\"words\":\"Passed every step: closed above its 200-day average, 51.00 above 45.00.\"}]";

        store.Execute(
            "INSERT INTO decision_card (index_code, session_date, family, ticker, place, entry, stop, target, rule, settings, lines, record, cap, trail, plan, held_again) VALUES " +
            $"('MID', '{IndexNight}', 'breakout', 'M2', 0, '50', '47', NULL, 'The breakout on the S&P 400', '{{}}', '{lines}', NULL, 63, '3', '{plan}', '{{\"family\":\"breakout\",\"night\":\"2026-09-30\",\"index\":\"MID\"}}');");
    }

    // Worked by hand over the S&P 400's night of 2 Oct: the breakout's new idea, M2's order of 30 Sep waiting, and on the S&P
    // 500's newest night HELD bought on 2 Oct and met again. Each plan's address names it: the page marks it chosen, draws it and
    // hides every other, and opens full screen; with no plan named the list's first is chosen and nothing is full screen.
    [Fact]
    public async Task EveryPlanOpensFromItsAddressForANewIdeaAWaitingOrderAndAHeldStock()
    {
        using (var store = PlanTradesStore())
        {
            MetAgainCard(store);

            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            var plain = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));
            var fresh = Regex.Match(plain, "<a class=\"pl-row[^\"]*\" href=\"([^\"]+)\" data-plan-id=\"[^\"]+\" data-g=\"new\" data-ticker=\"([^\"]+)\"");

            Assert.True(fresh.Success, "The S&P 400's night draws no new idea in its list of plans.");
            Assert.Matches("<section class=\"card tn-plans\" id=\"plans\" data-card=\"plans\" data-plans=\"[0-9]+\" data-new=\"[1-9][0-9]*\" data-waiting=\"[1-9][0-9]*\"", plain);
            Assert.Single(Regex.Matches(plain, "<article class=\"pl-plan[^\"]* on\""));

            foreach (var (group, ticker) in new[] { ("new", fresh.Groups[2].Value), ("waiting", "M2") })
            {
                Assert.Contains($"href=\"{MarkRenderer.PlanHref(ticker, "400")}\" data-plan-id=", plain, StringComparison.Ordinal);

                var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400&plan={ticker}"));
                var article = PlanArticle(page, group, ticker);

                Assert.Contains($"data-chosen=\"{ticker}\"", page, StringComparison.Ordinal);
                Assert.Contains("<section class=\"card tn-plans sheet\"", page, StringComparison.Ordinal);
                Assert.Matches("^<article class=\"pl-plan fam-edge on\" [^>]*data-card-night=\"[^\"]+\">", article);
                Assert.Single(Regex.Matches(page, "<article class=\"pl-plan[^\"]* on\""));
                Assert.Equal(Regex.Matches(page, "<article class=\"pl-plan").Count - 1, Regex.Matches(page, "<article class=\"pl-plan[^>]* hidden>").Count);
                Assert.Contains($"<a href=\"#/chart/{ticker}\">Chart</a>", article, StringComparison.Ordinal);
                Assert.Contains(group == "new" ? "data-state=\"new\">New tonight" : "data-state=\"waiting\">Waiting since 30 Sep · met the rules again tonight", article, StringComparison.Ordinal);
            }

            Assert.DoesNotContain("tn-plans sheet", plain, StringComparison.Ordinal);

            // From 24.48 the setups' table opens no row with no setup in the link, and the one the link names otherwise, every
            // other row closed, the opened one holding its setup's panel.
            static string[] Opened(string page) =>
                [.. Regex.Matches(page, "<tr class=\"st-open\" data-for=\"([^\"]+)\">").Select(match => match.Groups[1].Value)];

            Assert.Empty(Opened(plain));

            var breakout = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400&setup=breakout"));

            Assert.Equal(["breakout"], Opened(breakout));
            Assert.Contains("<tr class=\"st-open\" data-for=\"breakout\"><td colspan=\"6\"><div class=\"st-panel fam-edge\" data-family=\"breakout\"><section class=\"card setup-card\" id=\"setup-breakout\" data-card=\"family\"", breakout, StringComparison.Ordinal);
            Assert.Contains("<button type=\"button\" class=\"btn-2\" data-setup-toggle=\"breakout\" aria-expanded=\"true\">Close</button>", breakout, StringComparison.Ordinal);
        }

        using (var store = await FamilyPagesStore())
        {
            store.Execute(
                "INSERT INTO family_trade (candidate, ticker, session_date, family, place, entry, stop, target, risk_moves, reward_to_risk, cap, ended_on, result, benchmark, members) VALUES " +
                $"('{EquityBrief.Core.Candidates.FamilyRecords.LivePrefix}S&P 500 pullback rule', 'HELD', '2026-10-01', 'pullback', 1, '98', '94', '106', 2.0, 2.0, 63, NULL, NULL, NULL, NULL);");
            store.Execute(
                "INSERT INTO trade_fill (source, index_code, owner, ticker, session_date, filled_on, fill, filled_at) VALUES " +
                $"('family_trade', 'GSPC', '{EquityBrief.Core.Candidates.FamilyRecords.LivePrefix}S&P 500 pullback rule', 'HELD', '2026-10-01', '2026-10-02', '98.5', '2026-10-02T23:00:00Z');");

            await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("cards-held-again");

            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/tonight?plan=HELD"));
            var held = PlanArticle(page, "held", "HELD");

            Assert.Contains("data-chosen=\"HELD\"", page, StringComparison.Ordinal);
            Assert.Matches("^<article class=\"pl-plan fam-edge on\"", held);
            Assert.Contains("data-state=\"held\">Held since 2 Oct · met the rules again tonight", held, StringComparison.Ordinal);
            Assert.Contains("Bought on 2 Oct at $98.50.", held, StringComparison.Ordinal);
            Assert.Contains("<span class=\"lad-m buy\" data-buy=\"98.5\"", held, StringComparison.Ordinal);

            // The page's own route opens a plan's address by asking for tonight with the plan named.
            var shell = await client.GetStringAsync("/");

            Assert.Contains($"path.startsWith('{MarkRenderer.PlanRoute}')", shell, StringComparison.Ordinal);
            Assert.Contains("'plan=' + encodeURIComponent(planned)", shell, StringComparison.Ordinal);
        }
    }

    // M2's order of 30 Sep, waiting on the night of 2 Oct. Worked by hand: with no card stored on 30 Sep its plan says so and draws
    // the order as stored, its buy $47.50 and never the $46.90 tonight's rules would have placed, with no tab of checks; with the
    // card 30 Sep stored, its plan is that card, its checks under "The rule's checks on the night of 30 Sep 2026" and every word of
    // them said as that night's.
    // see: A waiting order's plan is the one stored on the night it was placed, its checks said as that night's
    [Fact]
    public async Task AWaitingOrdersPlanIsTheCardItsPlacingNightStoredItsChecksSaidAsThatNights()
    {
        using var store = PlanTradesStore();

        MetAgainCard(store);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var before = PlanArticle(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400&plan=M2")), "waiting", "M2");

        Assert.Contains("data-card-night=\"none\"", before, StringComparison.Ordinal);
        Assert.Contains("<p class=\"pp-none\" data-no-placed-plan>The store holds no plan from the night this order was placed, 30 Sep 2026.", before, StringComparison.Ordinal);
        Assert.Contains("<div data-figure=\"buy\"><dt>Buy if it dips to</dt><dd class=\"buy\">$47.50</dd>", before, StringComparison.Ordinal);
        Assert.DoesNotContain("46.90", before, StringComparison.Ordinal);
        Assert.DoesNotContain("pp-tabs", before, StringComparison.Ordinal);

        const string placed = "[{\"line\":1,\"name\":\"Trend and strength\",\"verdict\":\"pass\",\"words\":\"Passed every step tonight: closed above its 200-day average, 52.00 above 45.00.\"},{\"line\":2,\"name\":\"Market and sector\",\"verdict\":\"note\",\"words\":\"Tonight its sector stands 3rd of 11.\"}]";

        store.Execute(
            "INSERT INTO decision_card (index_code, session_date, family, ticker, place, entry, stop, target, rule, settings, lines, record, cap, trail) VALUES " +
            $"('MID', '2026-09-30', 'breakout', 'M2', 1, '51', '47.5', NULL, 'The breakout on the S&P 400', '{{}}', '{placed}', NULL, 63, '3');");

        var after = PlanArticle(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400&plan=M2")), "waiting", "M2");
        var checks = Regex.Match(after, "<section class=\"pp-panel\" data-p=\"checks\">(.*?)</section><section class=\"pp-panel\"", RegexOptions.Singleline).Groups[1].Value;

        Assert.Contains("data-card-night=\"2026-09-30\"", after, StringComparison.Ordinal);
        Assert.DoesNotContain("data-no-placed-plan", after, StringComparison.Ordinal);
        Assert.Contains("<div class=\"decision-card\" data-index=\"MID\" data-family=\"breakout\" data-ticker=\"M2\" data-night=\"2026-09-30\">", after, StringComparison.Ordinal);
        Assert.Contains("data-checks-when=\"2026-09-30\">The rule's checks on the night of 30 Sep 2026, when its order was placed, each against its floor.", checks, StringComparison.Ordinal);
        Assert.Contains("52.00 above 45.00", checks, StringComparison.Ordinal);
        Assert.Contains("That night its sector stands 3rd of 11.", checks, StringComparison.Ordinal);
        Assert.DoesNotContain("tonight", checks, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("51.00 above 45.00", after, StringComparison.Ordinal);
        Assert.Contains("<div data-figure=\"buy\"><dt>Buy if it dips to</dt><dd class=\"buy\">$47.50</dd>", after, StringComparison.Ordinal);

        // A night whose rule placed the order while another setup held the stock stored its card as one met again: it is still
        // that night's card, and the plan reads it.
        store.Execute("UPDATE decision_card SET held_again = '{\"family\":\"pullback\",\"night\":\"2026-09-29\",\"index\":\"MID\",\"ordered\":\"47.50\"}' WHERE index_code = 'MID' AND session_date = '2026-09-30' AND ticker = 'M2';");

        var ordered = PlanArticle(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400&plan=M2")), "waiting", "M2");

        Assert.Contains("data-card-night=\"2026-09-30\"", ordered, StringComparison.Ordinal);
        Assert.DoesNotContain("data-no-placed-plan", ordered, StringComparison.Ordinal);
    }

    // The plans of a constructed night worked by hand: a card stored under another night is never a waiting order's plan, the
    // waiting orders stand nearest their buy price first, a stock met again while its order waits is marked on its order, and a
    // bought stock met again stands among the held.
    [Fact]
    public void ThePlansStandInTheirGroupsTheWaitingNearestFirstEachFromItsPlacingNightsCard()
    {
        var night = new DateOnly(2026, 11, 25);
        WaitingOrderView Order(string ticker, decimal price, decimal close) =>
            new("pullback", ticker, new DateOnly(2026, 11, 20), price, new DateOnly(2026, 12, 18), ticker + " Inc", close);
        DecisionCardView Card(string ticker, DateOnly on) => new("GSPC", on, "pullback", ticker, "the pullback rule", 100m, 90m, null, [], null);

        var none = new TradeCountView(0, 0, 0, 0, 0, 0, null);
        var groups = new TradeGroupsView(null, null, none, none, none, 0)
        {
            StillWaitingOrders = [Order("FAR", 100m, 110m), Order("NEAR", 100m, 101m), Order("MID", 100m, 105m)],
        };
        var placed = new Dictionary<string, DecisionCardView>
        {
            [SinglePageApp.TonightExtras.Key("pullback", "NEAR", new DateOnly(2026, 11, 20))] = Card("NEAR", new DateOnly(2026, 11, 20)),
            [SinglePageApp.TonightExtras.Key("pullback", "FAR", new DateOnly(2026, 11, 21))] = Card("FAR", new DateOnly(2026, 11, 21)),
        };
        var met = Card("MID", night) with { HeldAgain = new HeldAgainView("pullback", new DateOnly(2026, 11, 20), "GSPC") };
        var bought = Card("HOLD", night) with { HeldAgain = new HeldAgainView("pullback", new DateOnly(2026, 11, 18), "GSPC") { Fill = 50m, BoughtOn = new DateOnly(2026, 11, 19) } };
        var family = new FamilyCardView("pullback", "Pullback", "h", "e", "r", 1, 1, null, 0, [], [], null) { HeldAgain = [met, bought] };

        var entries = SinglePageApp.PlanEntries([family], [], [], groups, placed);

        Assert.Equal(["NEAR", "MID", "FAR", "HOLD"], entries.Select(entry => entry.Ticker));
        Assert.Equal(["waiting", "waiting", "waiting", "held"], entries.Select(entry => entry.Group));
        Assert.Equal(new DateOnly(2026, 11, 20), entries[0].Card?.Night);
        Assert.Null(entries[2].Card);
        Assert.Equal([false, true, false, true], entries.Select(entry => entry.MetAgain));

        var html = new MarkRenderer().EveryPlan(entries, night, "500", "FAR");

        Assert.Contains("data-chosen=\"FAR\"", html, StringComparison.Ordinal);
        Assert.Contains("4 plans tonight: 0 new, 3 waiting to buy and 1 already held that met the rules again.", html, StringComparison.Ordinal);
        Assert.Contains("<span class=\"r-fig\">1.0% to go<br><small>", html, StringComparison.Ordinal);
    }

    // Home's See the plan, on a waiting order's card and on a bought stock met again, opens its plan on Tonight at its address.
    [Fact]
    public void HomesWaitingOrdersAndHeldStocksOpenTheirPlansOnTonight()
    {
        var orders = SixteenOrders()[..2];
        var carousel = MarkRenderer.WaitingCarousel(orders, "S&P 400", CarouselNight);

        Assert.All(orders, order => Assert.Contains($"<a class=\"home-plan\" data-row=\"see-plan\" href=\"{MarkRenderer.PlanHref(order.Ticker, "400")}\">See the plan</a>", carousel, StringComparison.Ordinal));
        Assert.DoesNotContain("href=\"#/chart/" + orders.First().Ticker + "\">See the plan", carousel, StringComparison.Ordinal);

        var held = new DecisionCardView("GSPC", CarouselNight, "pullback", "HELD", "the rule", 100m, 95m, null, [], null)
        {
            HeldAgain = new HeldAgainView("pullback", CarouselNight, "GSPC") { Fill = 98.5m, BoughtOn = CarouselNight },
        };

        Assert.Contains($"<a class=\"home-plan held-plan\" href=\"{MarkRenderer.PlanHref("HELD", "500")}\">See the plan</a>", SinglePageApp.HeldAgainRows([held], Universes.Large, new Dictionary<string, string>()), StringComparison.Ordinal);
    }
}
