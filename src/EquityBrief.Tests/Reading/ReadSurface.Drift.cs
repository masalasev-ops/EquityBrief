using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;

namespace EquityBrief.Tests.Reading;

// read-surface, 13.3: the earnings drift's card on Tonight, read back off the rendered page over a
// constructed night the lister drew: its trade with the stop at the reaction session's low and its target,
// where the buy sits between them, and why each stock is listed in the figures its gates stored.
// see: The earnings drift buys a beat with a strong reaction within five sessions, stopped under the reaction session's low
public partial class ReadSurface
{
    // A member's stored answer under the drift on the night, passing every gate, with the figures the
    // card's words are read from and its trade.
    static void DriftAnswer(TemporaryStore store, string ticker, int place, string report, int back, double surprise, double moves, double multiple, string close, string low, string target, string rewardToRisk)
    {
        var gates = FamilyRule.GatesJson(
        [
            new Gate(FamilyRule.Market, true, "breadth at or above its floor", FamilyRule.Values(("breadth", "0.6"), ("floor", "0.5"))),
            new Gate(DriftRule.Print, true, "a print inside the window", FamilyRule.Values(("report", report), ("back", FamilyRule.Whole(back)))),
            new Gate(DriftRule.Beat, true, "the print beat its estimate", FamilyRule.Values(("surprise", FamilyRule.Figure(surprise)))),
            new Gate(DriftRule.Reaction, true, "the reaction closed up", FamilyRule.Values(("moves", FamilyRule.Figure(moves)))),
            new Gate(DriftRule.Volume, true, "the reaction's volume", FamilyRule.Values(("multiple", FamilyRule.Figure(multiple)))),
            new Gate(DriftRule.Held, true, "the close above the reaction's low", FamilyRule.Values(("close", close), ("low", low))),
            new Gate(FamilyRule.Trade, true, "the stop at the reaction's low", FamilyRule.Values(("close", close), ("stop", low), ("target", target), (FamilyRule.RewardToRiskValue, rewardToRisk))),
        ]);

        store.Execute(
            "INSERT INTO family_result (session_date, ticker, family, passed, missed, place, entry, stop, target, order_by, exclusions, gates) " +
            $"VALUES ('{TheSwitch}', '{ticker}', 'drift', 1, 0, {place}, '{close}', '{low}', '{target}', {FamilyRule.Figure(surprise)}, '[]', '{gates}');");
    }

    [Fact]
    public async Task TheDriftsCardDrawsItsTradeToItsTargetAndWhyEachStockIsListed()
    {
        // The pullback's night as the framework's test reads it, F1 to F5 at places 1 to 5, and no breakout.
        // The drift passes two: E1, a beat of 12.5 per cent reported on 2026-09-28 whose reaction, three
        // sessions back, closed up 1.4 typical moves on 2.1 times its volume, bought at 104 with the stop at
        // the reaction's low of 100.5 and the target at 108; and E2, a beat of 5 per cent whose reaction was
        // tonight, bought at 103 with the stop at 100.5 and the target at 109.25.
        //
        // By hand: E1 and E2 take the page's sixth and seventh places. E1's buy sits 3.5 of the 7.5 from its
        // stop to its target, 0.467 of the way, at a reward to risk of 1.1429; E2's sits 2.5 of 8.75, 0.286,
        // at 2.5.
        using var store = await FamilyNightStore();

        Evening(store, TheSwitch, filter: false, [new Member("E1", Trend: false), new Member("E2", Trend: false)]);
        DriftAnswer(store, "E1", 1, "2026-09-28", 3, 12.5, 1.4, 2.1, "104", "100.5", "108", "1.1429");
        DriftAnswer(store, "E2", 2, "2026-10-02", 0, 5.0, 1.0, 1.5, "103", "100.5", "109.25", "2.5");
        await RedrawTheFamilies(store);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));
        var card = FamilyCardOf(page, DriftRule.Name);
        var families = SetupFamilies.InPageOrder.Count;

        // The card: the third of the page's families, two picks, on provisional settings.
        Assert.Contains(FormattableString.Invariant($"<section class=\"family-card\" data-family=\"drift\" data-place=\"3\" data-of=\"{families}\" data-picks=\"2\" data-state=\"provisional\" data-live-since=\"none\" data-variants=\"0\" data-rule=\"live\" data-variant=\"none\" data-folded=\"false\">"), card, StringComparison.Ordinal);
        Assert.Contains("<b>Strong earnings</b><small>Earnings drift</small>", card, StringComparison.Ordinal);
        // Its rule in words, written from the settings it froze at: 3 sessions to buy, a reaction of 0.5 typical moves
        // on twice the volume and a target at 2.5 times the risk, from 22.6 in the reader's words behind its fold.
        Assert.Contains("<p class=\"rule-words\">A company beats its estimate and the stock closes up at least 0.5 typical days' moves on 2 times its usual volume. Bought within 3 trading days while it holds above that day's low. Stop at that day's low, target the nearer of the next price band about two typical days' moves up, or 2.5 times the risk.</p>", card, StringComparison.Ordinal);

        // Its picks drawn as lines of its own, E1 and E2, each opening its plan, and among the buy points E1 sixth and E2
        // seventh.
        Assert.Equal(["E1", "E2"], CardPicksOf(card));
        Assert.Contains($"href=\"{EquityBrief.Web.Marks.MarkRenderer.PlanHref("E1", "500")}\"><b class=\"sp-t\">E1</b>", CardPickRowOf(card, "E1"), StringComparison.Ordinal);
        Assert.Equal(5, NewIdeasOf(page).ToList().IndexOf("E1"));
        Assert.Equal(6, NewIdeasOf(page).ToList().IndexOf("E2"));

        // E1's trade, as its trade gate stored it, on its line.
        var first = PickRowOf(page, "E1");

        Assert.Contains("<span class=\"sp-p\" data-buy=\"104\">Buy $104.00</span><span class=\"sp-p\" data-stop=\"100.5\">Stop $100.50</span><span class=\"sp-p\" data-target=\"108\">Target $108.00</span>", first, StringComparison.Ordinal);

        var second = PickRowOf(page, "E2");

        Assert.Contains("data-target=\"109.25\">Target $109.25</span>", second, StringComparison.Ordinal);

        // From 22.6 why each qualified, the drift's template worked by hand off the figures its gates stored: E1 beat by
        // 12.5 per cent on its report of 28 September, its reaction 1.4 typical days' moves on 2.1 times its volume, its
        // close of 104 above the reaction's low of 100.5; E2 by 5 per cent on 2 October, 1 move on 1.5 times. The same
        // sentence on its buy point's row and its card's own row.
        const string WhyE1 = "Beat its estimate by 12.5% on 28 Sep, jumped 1.4 typical days' moves on 2.10 times its usual volume, and is still above that day's low of 100.50.";
        const string WhyE2 = "Beat its estimate by 5.0% on 2 Oct, jumped 1.0 typical days' moves on 1.50 times its usual volume, and is still above that day's low of 100.50.";

        Assert.Contains($"<span class=\"sp-why\">{WhyE1}</span>", first, StringComparison.Ordinal);
        Assert.Contains($"<span class=\"sp-why\">{WhyE2}</span>", second, StringComparison.Ordinal);

        // Read back against the store in both directions: the drift's plans in the list of every plan, and its panel's lines.
        var points = Assert.Single(Blocks(card, "<ul class=\"sp-rows\" data-rows=\"picks\".*?</ul>"));

        Assert.Equal(
            Strings(store, $"SELECT ticker FROM family_pick WHERE session_date = '{TheSwitch}' AND family = 'drift' AND state = 'listed' ORDER BY place;"),
            Regex.Matches(page, "<a class=\"pl-row[^\"]*\" href=\"[^\"]+\" data-plan-id=\"[^\"]+\" data-g=\"new\" data-ticker=\"([^\"]+)\" data-family=\"drift\"").Select(match => match.Groups[1].Value));
        Assert.Equal(
            Strings(store, $"SELECT entry || '|' || stop || '|' || target FROM family_result WHERE session_date = '{TheSwitch}' AND family = 'drift' ORDER BY place;"),
            Regex.Matches(points, "<a class=\"sp-line\" data-plan-line data-ticker=\"[^\"]+\".*?data-buy=\"([^\"]+)\">[^<]*</span><span class=\"sp-p\" data-stop=\"([^\"]+)\">[^<]*</span><span class=\"sp-p\" data-target=\"([^\"]+)\"", RegexOptions.Singleline)
                .Select(match => $"{match.Groups[1].Value}|{match.Groups[2].Value}|{match.Groups[3].Value}"));
    }
}
