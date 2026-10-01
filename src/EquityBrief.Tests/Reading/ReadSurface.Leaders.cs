using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;

namespace EquityBrief.Tests.Reading;

// read-surface, 13.4: the sector leaders' card on Tonight, read back off the rendered page over a
// constructed night the lister drew: the pullback's plan as its trade, and why each stock is listed in the
// sector's rank and the stock's place its gates stored.
// see: A sector leader is a stock in the top quarter of a top three sector, bought at the pullback's buy point
public partial class ReadSurface
{
    static void LeaderAnswer(TemporaryStore store, string ticker, int place, string sector, int rank, double over, int placeInSector, int of)
    {
        var gates = FamilyRule.GatesJson(
        [
            new Gate(FamilyRule.Market, true, "breadth at or above its floor", FamilyRule.Values(("breadth", "0.6"), ("floor", "0.5"))),
            new Gate(LeaderRule.Sector, true, "its sector among the top", FamilyRule.Values(("sector", sector), ("rank", FamilyRule.Whole(rank)), ("ranked", "11"))),
            new Gate(LeaderRule.Leader, true, "its return in the top quarter", FamilyRule.Values(("return", FamilyRule.Figure(over)), ("place", FamilyRule.Whole(placeInSector)), ("of", FamilyRule.Whole(of)))),
            new Gate(SwingGates.Setup, true, "the swing filter's setup gate passed tonight", FamilyRule.Values()),
            new Gate(SwingGates.Trigger, true, "the swing filter's trigger gate passed tonight", FamilyRule.Values()),
            new Gate(FamilyRule.Trade, true, "the pullback's plan", FamilyRule.Values(("close", "100"), ("stop", "96"), ("target", "110"), (FamilyRule.RewardToRiskValue, "2.5"))),
        ]);

        store.Execute(
            "INSERT INTO family_result (session_date, ticker, family, passed, missed, place, entry, stop, target, order_by, exclusions, gates) " +
            $"VALUES ('{TheSwitch}', '{ticker}', 'leader', 1, 0, {place}, '100', '96', '110', {-rank}, '[]', '{gates}');");
    }

    [Fact]
    public async Task TheLeadersCardDrawsThePullbacksPlanAndWhyEachStockLeads()
    {
        // The pullback's night as the framework's test reads it, F1 to F5 at places 1 to 5. The leaders pass
        // three: F2, which the pullback lists already; L1, second of 68 in Technology, the first of 11
        // sectors, on a return of 42 per cent; and L2, first of 30 in Energy, the second sector.
        //
        // By hand: F2 is under another on the leaders' card and carries the leader's label on the
        // pullback's; L1 and L2 take the page's sixth and seventh places, each on the pullback's plan,
        // bought at 100 with the stop at 96 and the target at 110, 4 of 14 from the stop, 0.286.
        using var store = await FamilyNightStore();

        Evening(store, TheSwitch, filter: false, [new Member("L1", Trend: false), new Member("L2", Trend: false)]);
        LeaderAnswer(store, "F2", 1, "Technology", 1, 0.55, 1, 68);
        LeaderAnswer(store, "L1", 2, "Technology", 1, 0.42, 2, 68);
        LeaderAnswer(store, "L2", 3, "Energy", 2, 0.31, 1, 30);
        await RedrawTheFamilies(store);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));
        var card = FamilyCardOf(page, LeaderRule.Name);
        var families = SetupFamilies.InPageOrder.Count;

        Assert.StartsWith(FormattableString.Invariant($"<section class=\"family-card\" data-family=\"leader\" data-place=\"4\" data-of=\"{families}\" data-picks=\"2\" data-state=\"provisional\" data-live-since=\"none\" data-variants=\"0\">"), card, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"<div class=\"lbl\">Setup 4 of {families} · Strongest stocks of the strongest sectors</div><h2>Sector leaders</h2>"), page, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"lede\">{SetupFamilies.SectorLeaders.Rule}</p>", page, StringComparison.Ordinal);

        Assert.Equal(
            [("L1", "6"), ("L2", "7")],
            Regex.Matches(card, "<tr data-ticker=\"([^\"]+)\" data-family=\"leader\".*?<td class=\"place\" data-place=\"(\\d+)\">\\2</td>", RegexOptions.Singleline)
                .Select(match => (match.Groups[1].Value, match.Groups[2].Value)));

        var first = FamilyRowOf(card, "L1");

        Assert.Contains("<td class=\"r num plan-buy\" data-buy=\"100\">100.00</td><td class=\"r num plan-stop\" data-stop=\"96\">96.00</td><td class=\"r num plan-target\" data-target=\"110\">110.00</td>", first, StringComparison.Ordinal);
        Assert.Contains("<span class=\"stt\" data-along=\"0.286\"", first, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\" data-reward-to-risk=\"2.5\">2.50</td>", first, StringComparison.Ordinal);
        Assert.Contains("<td class=\"why-tonight\">Its sector, Technology, ranks 1 of 11 by its members' return, and its own return of 42.0% is 2 of the 68 in it. It is at a pullback's buy point tonight.</td>", first, StringComparison.Ordinal);
        Assert.Contains("<td class=\"why-tonight\">Its sector, Energy, ranks 2 of 11 by its members' return, and its own return of 31.0% is 1 of the 30 in it. It is at a pullback's buy point tonight.</td>", FamilyRowOf(card, "L2"), StringComparison.Ordinal);

        // F2, which the pullback and the leaders both pass, is drawn once, on the pullback's card, with the
        // leader's label, and named in a note under the leaders' card.
        Assert.Single(Regex.Matches(page, "<tr data-ticker=\"F2\""));
        Assert.Contains("<span class=\"also-family\" data-also=\"Sector leader\">also a sector leader</span>", FamilyRowOf(FamilyCardOf(page, SetupFamilies.Pullback), "F2"), StringComparison.Ordinal);
        Assert.Equal(
            ["F2 also qualified here tonight and is shown once, under Pullbacks to support, with both labels. One stock is one trade."],
            Regex.Matches(card, "<p class=\"family-note\">(.*?)</p>").Select(match => match.Groups[1].Value));

        // Read back against the store in both directions.
        Assert.Equal(
            Strings(store, $"SELECT ticker FROM family_pick WHERE session_date = '{TheSwitch}' AND family = 'leader' AND state = 'listed' ORDER BY place;"),
            Regex.Matches(card, "<tr data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
    }
}
