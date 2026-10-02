using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;

namespace EquityBrief.Tests.Reading;

// read-surface, 13.9: the sector leaders are a variant of the pullback and draw no card on Tonight, a leader's
// row an earlier night stored included, while the page's three cards are drawn in their order.
// see: The sector leaders are a variant of the pullback's starting point and not a family of their own
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
    public async Task TonightDrawsNoLeadersCardAndItsThreeCardsInThePagesOrder()
    {
        // The pullback's night as the framework's test reads it, F1 to F5 at places 1 to 5, with three leader
        // rows a night before the freeze stored. By hand: the page draws the pullbacks', the breakouts' and
        // the earnings drift's cards in that order and no leaders' card, and F2 carries no leader's label.
        using var store = await FamilyNightStore();

        Evening(store, TheSwitch, filter: false, [new Member("L1", Trend: false), new Member("L2", Trend: false)]);
        LeaderAnswer(store, "F2", 1, "Technology", 1, 0.55, 1, 68);
        LeaderAnswer(store, "L1", 2, "Technology", 1, 0.42, 2, 68);
        LeaderAnswer(store, "L2", 3, "Energy", 2, 0.31, 1, 30);
        await RedrawTheFamilies(store);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));

        Assert.Equal(
            [SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name],
            Regex.Matches(page, "<section class=\"family-card\" data-family=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.DoesNotContain("data-family=\"leader\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("also a sector leader", FamilyRowOf(FamilyCardOf(page, SetupFamilies.Pullback), "F2"), StringComparison.Ordinal);
        Assert.Equal(["0"], Strings(store, $"SELECT COUNT(*) FROM family_pick WHERE session_date = '{TheSwitch}' AND family = 'leader';"));
    }
}
