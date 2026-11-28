using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Cards;

namespace EquityBrief.Tests.Reading;

// read-surface, the operator's order of 2026-11-14, item 1f: a guard over the family pages' constructed night of Friday
// 2026-10-02, on which HELD met the pullback's rules again while its trade from the night before was still open. Home, Tonight
// and a stock's page draw none of the words the order retired; nothing names tomorrow's open on a Friday; HELD's card is drawn
// in a group of its own on Tonight, and from the operator's correction of 2026-11-23 a row of its own on Home; and HELD reaches
// no pick count and no paper trade. Worked by hand: the pullback
// lists F1 to F5 and the breakout K1 and K2, seven new picks to buy at Monday's open; HELD was listed on 2026-10-01.
// see: A stock a setup's rules met again while a trade holds it is drawn as a full card of its own, bought again by nothing and counted in nothing
public partial class ReadSurface
{
    // The words the order retired from Home, Tonight and a stock's page, and from the operator's review of PR 699 "No fixed target"
    // and "Aim to sell at".
    // see: A plan selling on a rising give-up price draws that price and the price at which it can no longer lose, and no card says it has no fixed target
    internal static readonly string[] RetiredWords = ["some setup", "in none of the trades", "this version's", "waiting for its price", "No fixed target", "Aim to sell at", "Sell if it reaches"];

    [Fact]
    public async Task OnAFridayWithAHeldStockMetAgainTheRetiredWordsAreGoneAndItsCardStandsApartCountedInNothing()
    {
        using var store = await FamilyPagesStore();

        // HELD's open trade under the pullback's live rule, listed 1 Oct at 98.00 with its stop at 94.00 and target at 106.00,
        // filled at 98.50 on 2 Oct.
        store.Execute(
            "INSERT INTO family_trade (candidate, ticker, session_date, family, place, entry, stop, target, risk_moves, reward_to_risk, cap, ended_on, result, benchmark, members) VALUES " +
            $"('{EquityBrief.Core.Candidates.FamilyRecords.LivePrefix}S&P 500 pullback rule', 'HELD', '2026-10-01', 'pullback', 1, '98', '94', '106', 2.0, 2.0, 63, NULL, NULL, NULL, NULL);");
        store.Execute(
            "INSERT INTO trade_fill (source, index_code, owner, ticker, session_date, filled_on, fill, filled_at) VALUES " +
            $"('family_trade', 'GSPC', '{EquityBrief.Core.Candidates.FamilyRecords.LivePrefix}S&P 500 pullback rule', 'HELD', '2026-10-01', '2026-10-02', '98.5', '2026-10-02T23:00:00Z');");

        await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("cards-held-again");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var tonight = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));
        var home = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/home"));
        var held = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/HELD/{TheSwitch}"));

        // None of the retired words on any of the three pages.
        foreach (var (name, page) in new[] { ("Tonight", tonight), ("Home", home), ("HELD's page", held) })
        {
            Assert.All(RetiredWords, words => Assert.False(page.Contains(words, StringComparison.Ordinal), $"{name} draws \"{words}\""));

            // On the operator's ruling of 2026-11-15 on the review of PR 699 a book's pick with no profit row fails.
            Assert.Empty(EquityBrief.Web.App.SinglePageApp.BookPicksWithNoProfitRow(page));
        }

        // A Friday's next session is Monday's, so no page names tomorrow's open.
        Assert.Equal("Monday's", MarkRenderer.OpenDay(new DateOnly(2026, 10, 2)));
        Assert.All(new[] { tonight, home, held }, page => Assert.DoesNotContain("tomorrow's open", page, StringComparison.Ordinal));

        // From 24.48 HELD as one line under the pullback's panel, held since its fill traded on 2 Oct, up (100.00 - 98.50) /
        // 98.50 = 1.52% since bought, the line opening its plan, and no note repeating it.
        var group = Assert.Single(Blocks(tonight, "<div class=\"sp-again\" data-held-again=\"1\">.*?</ul></div>"));

        Assert.Contains($"<p class=\"sp-h\">{MarkRenderer.MetAgainHeading} (1)</p>", group, StringComparison.Ordinal);
        Assert.Contains($"<a class=\"sp-line\" data-plan-line data-ticker=\"HELD\" href=\"{MarkRenderer.PlanHref("HELD", "500")}\"><b class=\"sp-t\">HELD</b><span class=\"sp-why\">already held since 2 Oct</span><span class=\"sp-p\">+1.5% since bought</span></a>", group, StringComparison.Ordinal);
        Assert.DoesNotContain("HELD qualified again tonight but", tonight, StringComparison.Ordinal);

        // Its plan holds its line and its open trade: from the operator's correction of 2026-11-23 the day it bought it is
        // the day its fill traded, 2 Oct, at 98.50, tonight's close 100.00.
        var plan = Assert.Single(Blocks(tonight, "<article class=\"pl-plan[^\"]*\" id=\"[^\"]+\" data-plan=\"HELD\" data-g=\"held\".*?</article>"));

        Assert.Contains("HELD met the pullback rules again tonight. Pullback bought it on 2 Oct and that trade is still open, so paper trading will not buy it again until it ends.", plan, StringComparison.Ordinal);
        Assert.Contains("Bought on 2 Oct at $98.50. Tonight's close was $100.00.", plan, StringComparison.Ordinal);

        // HELD reaches no pick count: the seven new picks stand, and HELD is no row of Monday's buys.
        Assert.Contains("7 new picks to buy at Monday's open", tonight, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex("<tr data-ticker=\"HELD\" data-family=\"pullback\" data-place="), tonight);

        // And no paper trade: no rule's trade or pick holds HELD on the Friday, which paper trading reads, and the night's cards
        // wrote its card at place nought.
        Assert.Equal(["0"], Strings(store, $"SELECT (SELECT COUNT(*) FROM family_trade WHERE ticker = 'HELD' AND session_date = '{TheSwitch}') + (SELECT COUNT(*) FROM rule_pick WHERE ticker = 'HELD' AND session_date = '{TheSwitch}');"));
        Assert.Equal(["0"], Strings(store, $"SELECT place FROM decision_card WHERE ticker = 'HELD' AND session_date = '{TheSwitch}' AND held_again IS NOT NULL;"));
    }
}
