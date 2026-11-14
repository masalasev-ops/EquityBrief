using System.Net;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// From the 23.10 correction of 2026-11-13: a live rule's trade under a plan buying at a price is drawn on every page as its
// plan made it, filled only once its price traded, its stop placed from that fill, an order still waiting drawn with its price
// and its last session and one its window passed unfilled as not filled, no trade; a trade bought at the next open is drawn as
// it was. And the orders earlier nights placed still waiting are a group of their own on Home, Tonight and Past picks, every
// count naming its group. Before it every page read the list's own trade bought at the next open, and the counts took every
// order waiting as tonight's to buy.
// see: A live rule's trade under a plan is drawn on every page as its plan made it, and its waiting orders are a group of their own
public partial class ReadSurface
{
    // The S&P 400 breakout frozen on 2 Jan, its live rule trading from 15 Sep a plan that buys 2.22 typical days under the
    // close and stops 1.5 typical days of 2.00 under its fill, each worked by hand: M4, listed 15 Sep at 47.00 for 5 sessions,
    // was never filled; M3, listed 28 Sep at 48.20, filled at 48.20 on 29 Sep, its stop 48.20 - 3.00 = 45.20, open; M2, listed
    // 30 Sep at 47.50 for 10 sessions, waits until 14 Oct, the tenth session after 30 Sep, its stop 44.50; M1, listed on the
    // night, 2 Oct, at 48.00 for 5 sessions, waits until 9 Oct as tonight's new pick, its stop 45.00. The rule's own trade in
    // M2 of 10 Sep, before its plan, was bought at the next open at its buy of 50.00 with its stop of 47.00, as the list's
    // own trade of that night was, and is drawn as it was. The list's own trade in M1 of the night, bought at the next open, is
    // the live rule's plan's from 15 Sep and is drawn as the plan's.
    static TemporaryStore PlanTradesStore()
    {
        var store = UniversesStore();
        var live = FreezeTheBreakout(store, "2026-01-02T12:00:00Z");

        ReadByTheLiveBreakout(store, live);
        store.Execute(
            "INSERT INTO index_family_trade (index_code, family, ticker, session_date, place, entry, stop, target, trail, cap) VALUES " +
            "('MID', 'breakout', 'M2', '2026-09-10', 1, '50', '47', NULL, '3', 63);");
        store.Execute(
            "INSERT INTO index_rule_trade (candidate, index_code, family, ticker, session_date, place, entry, stop, target, trail, cap, risk_moves, reward_to_risk) VALUES " +
            $"('{Doubled(live)}', 'MID', 'breakout', 'M2', '2026-09-10', 1, '50', '47', NULL, '3', 63, 1.5, NULL);");
        FillAtTheirOwnBuy(store);

        static string Plan(double level, int window) =>
            Doubled(new PlanOrder("2.22 typical days under the close, 1.5 typical days under the buy, the rule's sale", "2.22 typical days under the close", level, window, PlanPoints.TypicalStop(1.5), 47, null, 2, null, [], 3, null, 63).Json());

        store.Execute(
            "INSERT INTO index_rule_trade (candidate, index_code, family, ticker, session_date, place, entry, stop, target, trail, cap, risk_moves, reward_to_risk, plan) VALUES " +
            $"('{Doubled(live)}', 'MID', 'breakout', 'M4', '2026-09-15', 1, '50', '47', NULL, '3', 63, 1.5, NULL, '{Plan(47, 5)}'), " +
            $"('{Doubled(live)}', 'MID', 'breakout', 'M3', '2026-09-28', 1, '50', '47', NULL, '3', 63, 1.5, NULL, '{Plan(48.2, 10)}'), " +
            $"('{Doubled(live)}', 'MID', 'breakout', 'M2', '2026-09-30', 1, '50', '47', NULL, '3', 63, 1.5, NULL, '{Plan(47.5, 10)}'), " +
            $"('{Doubled(live)}', 'MID', 'breakout', 'M1', '{IndexNight}', 1, '50', '47', NULL, '3', 63, 1.5, NULL, '{Plan(48, 5)}');");
        store.Execute(
            "INSERT INTO trade_fill (source, index_code, owner, ticker, session_date, filled_on, fill, past, result, benchmark, members, edge, reason, filled_at) VALUES " +
            $"('index_rule_trade', 'MID', '{Doubled(live)}', 'M3', '2026-09-28', '2026-09-29', '48.2', NULL, NULL, NULL, NULL, NULL, NULL, '2026-09-29T21:00:00Z'), " +
            $"('index_rule_trade', 'MID', '{Doubled(live)}', 'M4', '2026-09-15', NULL, NULL, NULL, NULL, NULL, NULL, NULL, '{Doubled(Worker.Fills.TradeFiller.NotFilled)}', '2026-09-22T21:00:00Z');");
        store.Execute(
            "INSERT INTO decision_card (index_code, session_date, family, ticker, place, entry, stop, target, rule, settings, lines, record, cap, trail) VALUES " +
            "('MID', '2026-09-28', 'breakout', 'M3', 1, '50', '47', NULL, 'The breakout on the S&P 400', '{}', '[]', NULL, 63, '3'), " +
            "('MID', '2026-09-10', 'breakout', 'M2', 1, '50', '47', NULL, 'The breakout on the S&P 400', '{}', '[]', NULL, 63, '3');");

        return store;
    }

    // The words a page draws a trade bought at the next open in, any of which beside a planned order is the defect.
    static readonly string[] NextOpenWords = ["to buy at ",PaperAccount.Waiting, TradeFills.Waiting, "bought at the next open", "the open of"];

    // Past picks' row for a stock its family listed on a night, one only.
    static string IndexRowOf(string page, string ticker, string listed) =>
        Assert.Single(Blocks(page, $"<tr data-ticker=\"{ticker}\" data-index=\"S&P 400\" data-family=\"breakout\" data-listed=\"{listed}\".*?</tr>"));

    [Fact]
    public async Task ALiveRulesTradeUnderAPlanIsDrawnAsItsPlanMadeItOnPastPicksPaperTradingAndTheStockAndChartPages()
    {
        using var store = PlanTradesStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // Past picks: the order still waiting at its price with its last session, tonight's new order the same, the filled
        // trade at its fill with the plan's stop from it, the order never filled as no trade, and the trade of 10 Sep bought
        // at the next open as it was. The list's own next-open trade in M1 of the night is drawn no more.
        var picks = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks?universe=400"));

        var waiting = IndexRowOf(picks, "M2", "2026-09-30");
        Assert.Contains("47.50", waiting, StringComparison.Ordinal);
        Assert.Contains("44.50", waiting, StringComparison.Ordinal);
        Assert.Contains("<td data-plan=\"waiting\">buys if it falls to $47.50; cancelled if not filled by 14 Oct 2026</td>", waiting, StringComparison.Ordinal);

        var tonight = IndexRowOf(picks, "M1", IndexNight);
        Assert.Contains("<td data-plan=\"waiting\">buys if it falls to $48.00; cancelled if not filled by 9 Oct 2026</td>", tonight, StringComparison.Ordinal);
        Assert.Contains("45.00", tonight, StringComparison.Ordinal);

        var filled = IndexRowOf(picks, "M3", "2026-09-28");
        Assert.Contains("48.20", filled, StringComparison.Ordinal);
        Assert.Contains("45.20", filled, StringComparison.Ordinal);
        Assert.Contains("<td><b>open</b></td>", filled, StringComparison.Ordinal);

        Assert.Contains("<td data-plan=\"not-filled\">not filled, no trade</td>", IndexRowOf(picks, "M4", "2026-09-15"), StringComparison.Ordinal);

        var before = IndexRowOf(picks, "M2", "2026-09-10");
        Assert.Contains("50.00", before, StringComparison.Ordinal);
        Assert.Contains("47.00", before, StringComparison.Ordinal);
        Assert.Contains("<td><b>open</b></td>", before, StringComparison.Ordinal);

        foreach (var (ticker, listed) in new[] { ("M1", IndexNight), ("M2", "2026-09-30"), ("M3", "2026-09-28"), ("M4", "2026-09-15") })
        {
            Assert.All(NextOpenWords, words => Assert.DoesNotContain(words, IndexRowOf(picks, ticker, listed), StringComparison.Ordinal));
        }

        // Paper trading: the orders waiting on a line of their own with their prices and last sessions, the order never
        // filled not bought as no trade, and none of them to buy at the next open.
        var paper = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper?universe=400"));
        var placed = Assert.Single(Blocks(paper, "<p class=\"paper-waiting\".*?</p>"));

        Assert.Contains("data-waiting=\"2\"", placed, StringComparison.Ordinal);
        Assert.Contains("M1</a> buys if it falls to $48.00; cancelled if not filled by 9 Oct 2026", placed, StringComparison.Ordinal);
        Assert.Contains("M2</a> buys if it falls to $47.50; cancelled if not filled by 14 Oct 2026", placed, StringComparison.Ordinal);
        Assert.Matches("Not bought: <span class=\"pu-reason\" data-count=\"1\">1 pick, not filled, no trade: <a href=\"#/chart/M4\"", paper);
        Assert.DoesNotContain(PaperAccount.Waiting, paper, StringComparison.Ordinal);
        Assert.All(Blocks(paper, "<p class=\"paper-tobuy\".*?</p>"), line => Assert.DoesNotMatch("M[1-4]<", line));

        // The stock page: the trade the plan filled, at its fill on the session its price traded and the plan's stop from
        // that fill; the trade bought at the next open at its own buy and stop as before.
        var heldOnThePlan = Assert.Single(Blocks(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/M3/{IndexNight}")), "<section class=\"held-card\".*?</section>"));

        Assert.Contains("<dt>Fill</dt><dd>48.20, its plan's price traded on 29 Sep 2026</dd>", heldOnThePlan, StringComparison.Ordinal);
        Assert.Contains("<dt>Stop</dt><dd>a close below 45.20</dd>", heldOnThePlan, StringComparison.Ordinal);

        var heldBefore = Assert.Single(Blocks(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/M2/{IndexNight}")), "<section class=\"held-card\".*?</section>"));

        Assert.Contains("data-night=\"2026-09-10\"", heldBefore, StringComparison.Ordinal);
        Assert.Contains("<dt>Fill</dt><dd>50.00</dd>", heldBefore, StringComparison.Ordinal);
        Assert.Contains("<dt>Stop</dt><dd>a close below 47.00</dd>", heldBefore, StringComparison.Ordinal);

        // The chart page's brief: bought when its plan's price traded, its stop the plan's.
        var (_, lines, _) = BriefOfPage(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/chart/M3")));

        Assert.Contains("picked it for on 28 Sep 2026, bought when its plan's price traded at 48.20.", lines["buy"], StringComparison.Ordinal);
        Assert.Contains("45.20", lines["stop"], StringComparison.Ordinal);
        Assert.DoesNotContain("next open", lines["buy"], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheOrdersStillWaitingForTheirPriceAreAGroupOfTheirOwnOnHomeTonightAndPastPicksAndEveryCountNamesItsGroup()
    {
        using var store = PlanTradesStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // The one order earlier nights placed still waiting, M2's, as each page names the group, with its price and last
        // session; tonight's new order, M1's, in none of them.
        static void StillWaitingOnce(string page)
        {
            var group = Assert.Single(Blocks(page, "<div class=\"waiting-orders\".*?</div>"));

            Assert.Contains("data-still-waiting=\"1\"", group, StringComparison.Ordinal);
            Assert.Contains("Waiting to buy: 1 order that buys only if the price falls to a set level. If it does not get there by the last day, nothing is bought.", group, StringComparison.Ordinal);
            Assert.Matches("<li data-ticker=\"M2\" data-family=\"breakout\" data-listed=\"2026-09-30\" data-price=\"47.50\" data-close=\"[^\"]+\" data-last-day=\"2026-10-14\">", group);
            Assert.Contains("buys if it falls to $47.50.", group, StringComparison.Ordinal);
            Assert.Matches("Placed 30 Sep by [a-z ]+; cancelled if not filled by 14 Oct\\.", group);
            Assert.DoesNotContain("data-ticker=\"M1\"", group, StringComparison.Ordinal);
        }

        // Home: the heading counts tonight's one new pick, and the order still waiting is the line beneath it.
        var home = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/home?universe=400"));

        Assert.Matches("<h2>Tonight's ideas: 1 stock to buy [A-Za-z]+ morning</h2></div><div>.*?</div></div><div class=\"card-b\"><div class=\"waiting-orders\"", home);
        StillWaitingOnce(home);

        // Tonight: one new pick, and the order still waiting beneath the open trades.
        var tonight = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));

        Assert.Contains("1 new pick to buy at", tonight, StringComparison.Ordinal);
        Assert.Contains("<div class=\"minute-line\" data-line=\"still-waiting\"><div class=\"waiting-orders\"", tonight, StringComparison.Ordinal);
        StillWaitingOnce(tonight);

        // Past picks: tonight's new order and the one still waiting each counted as its own group, none to buy at the open.
        var picks = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks?universe=400"));

        Assert.Matches("data-to-buy=\"0\" data-new-waiting=\"1\" data-still-waiting=\"1\">Showing 6 of 6 S&P 400 trades, 2 open, 1 new tonight waiting to buy, 1 from earlier nights waiting to buy\\.", picks);
        StillWaitingOnce(picks);

        // The proof holds every page's count to the one function's: tonight's new order is among the new picks, the one
        // still waiting is its own group on each page, and no count fails.
        var proof = await client.GetStringAsync("/proof/counts");
        var mid = Assert.Single(Blocks(proof, "<h2 data-failed=\"\\d+\">S&amp;P 400: .*?</ul>"));

        foreach (var rule in new[]
        {
            "Home's orders still waiting are those earlier nights placed",
            "Tonight's orders still waiting are those earlier nights placed",
            "Past picks' orders still waiting are those earlier nights placed",
            "Past picks' stocks to buy are those waiting on the open",
            "Home's ideas to buy and picks watched are the new picks",
            "Tonight's new picks are those to buy less the reshuffle",
        })
        {
            Assert.Contains($"<li data-verdict=\"pass\">pass: {WebUtility.HtmlEncode(rule)}</li>", mid, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("data-verdict=\"fail\"", mid, StringComparison.Ordinal);
    }

    // The S&P 500's page list on the night of 12 Nov, each trade first as the list bought it at the next open, worked by hand
    // under the live drift rule's plan buying at a price and stopping 1.5 typical days of 2.00 under its fill: TTWO, listed
    // 10 Nov at 97.25 for 10 sessions, waits through 24 Nov, its stop 94.25; FERG, listed on the night, waits as tonight's new
    // order; AKAM, listed 5 Nov, filled at 98.00 on 6 Nov, is open at that fill with its stop at 95.00; NOTF, listed 20 Oct,
    // was never filled. NEXT, whose plan buys at the next open, and OTHR, kept by a variant and not the live rule, are drawn
    // as the list bought them.
    [Fact]
    public void TheSAndP500sPageListIsDrawnAsTheLiveRulesPlanMadeItsTradesAndCountsEachOrderInItsGroup()
    {
        var night = new DateOnly(2026, 11, 12);
        var live = Core.Candidates.FamilyRecords.LivePrefix + "S&P 500 drift rule";

        PickCell Cell(string ticker, DateOnly listed) =>
            new(ticker, null, listed, "drift", 100m, 95m, 110m, PickStatus.Open, 2, null, 1.01, null, null, night, 101m, null, Family: DriftRule.Name, Setup: "Drift", Planned: 100m, FilledOn: listed.AddDays(1));

        PlannedTradeRow Row(string owner, string ticker, DateOnly listed, string entry, double? level, decimal? fill = null, DateOnly? filledOn = null, bool notFilled = false) =>
            new(owner, "GSPC", DriftRule.Name, ticker, listed, 1, 100m, 95m, 110m, null, 63, new PlanOrder("its plan", entry, level, 10, PlanPoints.TypicalStop(1.5), 95, null, 2, 112, [], null, null, 63), filledOn, fill, notFilled, null, null, null);

        const string AtAPrice = "2.22 typical days under the close";
        PickCell[] list = [Cell("TTWO", new(2026, 11, 10)), Cell("FERG", night), Cell("AKAM", new(2026, 11, 5)), Cell("NOTF", new(2026, 10, 20)), Cell("NEXT", new(2026, 11, 10)), Cell("OTHR", new(2026, 11, 10))];
        PlannedTradeRow[] planned =
        [
            Row(live, "TTWO", new(2026, 11, 10), AtAPrice, 97.25),
            Row(live, "FERG", night, AtAPrice, 99),
            Row(live, "AKAM", new(2026, 11, 5), AtAPrice, 98, 98m, new(2026, 11, 6)),
            Row(live, "NOTF", new(2026, 10, 20), AtAPrice, 96, notFilled: true),
            Row(live, "NEXT", new(2026, 11, 10), PlanPoints.NextOpen, null, 100.5m, new(2026, 11, 11)),
            Row("a variant", "OTHR", new(2026, 11, 10), AtAPrice, 97),
        ];

        var drawn = PlanScreen.Cells(list, planned, night).ToDictionary(cell => cell.Ticker, StringComparer.Ordinal);

        Assert.Equal((PickStatus.Waiting, 97.25m, 94.25m, "buys if it falls to $97.25; cancelled if not filled by 24 Nov 2026", (DateOnly?)null), (drawn["TTWO"].Status, drawn["TTWO"].Buy, drawn["TTWO"].Stop, drawn["TTWO"].Unfilled, drawn["TTWO"].FilledOn));
        Assert.Equal((PickStatus.Waiting, 99m, 96m), (drawn["FERG"].Status, drawn["FERG"].Buy, drawn["FERG"].Stop));
        Assert.Equal((PickStatus.Open, 98m, 95m, (DateOnly?)new DateOnly(2026, 11, 6), true), (drawn["AKAM"].Status, drawn["AKAM"].Buy, drawn["AKAM"].Stop, drawn["AKAM"].FilledOn, drawn["AKAM"].UnderPlan));
        Assert.Equal((PickStatus.NotFilled, PlanOrderWords.NotFilled), (drawn["NOTF"].Status, drawn["NOTF"].Unfilled));
        Assert.Equal(list[4], drawn["NEXT"]);
        Assert.Equal(list[5], drawn["OTHR"]);

        // The counts: one new tonight, one from earlier nights, one no trade, and the three bought open.
        var summary = PicksScreen.Summary([.. drawn.Values], night);

        Assert.Equal((0, 1, 1, 1, 3), (summary.ToBuy, summary.PlacedTonight, summary.StillWaiting, summary.NotFilled, summary.Open));

        var counts = WebUtility.HtmlDecode(new MarkRenderer().PicksCounts(summary));

        Assert.Contains("<dt>New tonight, waiting to buy</dt><dd>1</dd>", counts, StringComparison.Ordinal);
        Assert.Contains("<dt>From earlier nights, waiting to buy</dt><dd>1</dd>", counts, StringComparison.Ordinal);
        Assert.Contains("<dt>Not filled, no trade</dt><dd>1</dd>", counts, StringComparison.Ordinal);
        Assert.Contains("<dt>To buy at tomorrow's open</dt><dd>0</dd>", counts, StringComparison.Ordinal);
    }

    // The words and the last session worked by hand: an order of 30 Sep waiting 10 sessions waits through 14 Oct, Columbus
    // Day a session; one of Friday 2 Oct waiting 5 waits through Friday 9 Oct; one of 24 Dec waiting 3 skips Christmas and the
    // weekend to 30 Dec; and an order whose levels placed no price says so.
    [Fact]
    public void AnOrdersLastSessionIsItsWindowsSessionsAfterItsNightAndItsWordsNameItsPrice()
    {
        Assert.Equal(new DateOnly(2026, 10, 14), PlanScreen.LastDay(new DateOnly(2026, 9, 30), 10));
        Assert.Equal(new DateOnly(2026, 10, 9), PlanScreen.LastDay(new DateOnly(2026, 10, 2), 5));
        Assert.Equal(new DateOnly(2026, 12, 30), PlanScreen.LastDay(new DateOnly(2026, 12, 24), 3));
        Assert.Equal("buys if it falls to $47.50; cancelled if not filled by 14 Oct 2026", PlanOrderWords.Waiting(47.5m, new DateOnly(2026, 10, 14)));
        Assert.Equal("buys if it falls to a level its plan could not place tonight, so it will not fill; cancelled by 14 Oct 2026", PlanOrderWords.Waiting(null, new DateOnly(2026, 10, 14)));
        Assert.Equal(", 1 new tonight waiting to buy, 2 from earlier nights waiting to buy", PlanOrderWords.Counts(1, 2));
        Assert.Equal(string.Empty, PlanOrderWords.Counts(0, 0));
        Assert.True(PlanOrderWords.IsWaiting(PlanOrderWords.Waiting(null, new DateOnly(2026, 10, 14))));
        Assert.False(PlanOrderWords.IsWaiting(PlanOrderWords.NotFilled));
        Assert.False(PlanOrderWords.IsWaiting(PaperAccount.Waiting));
    }
}
