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
        Assert.StartsWith(FormattableString.Invariant($"<section class=\"family-card\" data-family=\"drift\" data-place=\"3\" data-of=\"{families}\" data-picks=\"2\" data-state=\"provisional\" data-live-since=\"none\" data-variants=\"0\" data-rule=\"live\" data-variant=\"none\" data-folded=\"false\">"), card, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"<div class=\"lbl\">Setup 3 of {families} · After a strong report</div>"), page, StringComparison.Ordinal);
        Assert.Contains("<span class=\"fam-swatch\" data-family=\"drift\"></span>Earnings drift</span>", card, StringComparison.Ordinal);
        // Its rule in words, written from the settings it froze at: 3 sessions to buy, a reaction of 0.5 typical moves
        // on twice the volume and a target at 2.5 times the risk, a click away beneath its chips.
        Assert.Contains("<p class=\"rule-words\">A company beats its estimate and the stock closes up at least 0.5 typical moves on 2 times its usual volume. Bought within 3 sessions while it holds above that day's low. Stop at that day's low, target at the next band above or 2.5 times the risk, whichever is nearer.</p>", card, StringComparison.Ordinal);

        // Its picks named by their place among the buy points, E1 sixth and E2 seventh.
        Assert.Equal(["E1", "E2"], Regex.Matches(Assert.Single(Blocks(card, "<p class=\"card-picks\".*?</p>")), "data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.Contains("<td class=\"place\" data-place=\"6\">6</td>", PickRowOf(page, "E1"), StringComparison.Ordinal);
        Assert.Contains("<td class=\"place\" data-place=\"7\">7</td>", PickRowOf(page, "E2"), StringComparison.Ordinal);

        // E1's trade and its reward to risk, as its trade gate stored it, on its buy point's row.
        var first = PickRowOf(page, "E1");

        Assert.Contains("<td class=\"r num plan-buy\" data-buy=\"104\">104.00</td><td class=\"r num plan-stop\" data-stop=\"100.5\">100.50</td><td class=\"r num plan-target\" data-target=\"108\">108.00</td>", first, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\" data-reward-to-risk=\"1.1429\">1.14</td>", first, StringComparison.Ordinal);

        var second = PickRowOf(page, "E2");

        Assert.Contains("data-target=\"109.25\">109.25</td>", second, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\" data-reward-to-risk=\"2.5\">2.50</td>", second, StringComparison.Ordinal);

        // Read back against the store in both directions.
        var points = Assert.Single(Blocks(page, "<table class=\"list-table buy-points\".*?</table>"));

        Assert.Equal(
            Strings(store, $"SELECT ticker FROM family_pick WHERE session_date = '{TheSwitch}' AND family = 'drift' AND state = 'listed' ORDER BY place;"),
            Regex.Matches(points, "<tr data-ticker=\"([^\"]+)\" data-family=\"drift\"").Select(match => match.Groups[1].Value));
        Assert.Equal(
            Strings(store, $"SELECT entry || '|' || stop || '|' || target FROM family_result WHERE session_date = '{TheSwitch}' AND family = 'drift' ORDER BY place;"),
            Regex.Matches(points, "<tr data-ticker=\"[^\"]+\" data-family=\"drift\".*?data-buy=\"([^\"]+)\">[^<]*</td><td class=\"r num plan-stop\" data-stop=\"([^\"]+)\">[^<]*</td><td class=\"r num plan-target\" data-target=\"([^\"]+)\"", RegexOptions.Singleline)
                .Select(match => $"{match.Groups[1].Value}|{match.Groups[2].Value}|{match.Groups[3].Value}"));
    }
}
