using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Web.App;
using EquityBrief.Worker.Cards;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The operator's ruling of 2026-11-15 on the review of PR 699, item 2: a book's buy draws the stop and the take-profit its
// exit places, worked from the close it was chosen at, on its card on Home and its row of Tonight's buys, and a book's pick
// drawing no profit row fails.
// see: A book's pick draws the stop and the take-profit its exit places, worked from the close it was chosen at
// see: Tonight's buys name every buy on the index, a book's buy drawn with its stop and take-profit where its card placed them
public partial class ReadSurface
{
    [Fact]
    public async Task ABooksBuyDrawsTheStopAndTakeProfitItsExitPlacesFromTheCloseItWasChosenAt()
    {
        using var store = UniversesStore();

        // The S&P 400's Fundamentals family and Quality at a fair price both chose M1 on the night at its close of 50, its
        // typical daily move 2.
        ImprovingNightOn(store);
        store.Execute($"INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ('M1', '{IndexNight}', '{EquityBrief.Core.Indicators.IndicatorSeries.Atr14}', 2.0, 14);");

        using (var before = new Host(store.Root))
        using (var client = before.CreateClient())
        {
            // Before the books' cards are written, each buy draws how it sells, and the guard names both.
            var home = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/home?universe=400"));
            var tonight = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));

            Assert.Equal(["Home improving M1"], SinglePageApp.BookPicksWithNoProfitRow(home));
            Assert.Equal(["Tonight improving M1", "Tonight quality M1"], SinglePageApp.BookPicksWithNoProfitRow(tonight));
        }

        Assert.Equal(2, await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).BooksAsync("book-cards"));

        // The S&P 400's Fundamentals exit is 4 typical moves under the buy and 3 times the risk at the first earlier high that
        // far up, the store's one session holding none: 50 - 4 x 2 = 42 and 50 + 3 x 8 = 74. Quality's is 3 moves and 2 times:
        // 50 - 3 x 2 = 44 and 50 + 2 x 6 = 62.
        Assert.Equal(
            ["improving|M1|1|50|42|74|risk 3", "quality|M1|1|50|44|62|risk 2"],
            Strings(store, $"SELECT family || '|' || ticker || '|' || place || '|' || entry || '|' || stop || '|' || target || '|' || take_profit_from FROM decision_card WHERE index_code = 'MID' AND session_date = '{IndexNight}' AND family IN ('improving', 'quality') ORDER BY family;"));
        Assert.Equal(["ok|2|0 card(s) on the S&P 500; 2 card(s) on the S&P 400; 0 card(s) on the S&P 600"], Strings(store, $"SELECT outcome || '|' || rows_written || '|' || detail FROM run_log WHERE stage = '{DecisionCards.BookStage}';"));

        // The night's own cards, written again, leave the books' cards standing.
        await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 55, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("cards-again");

        Assert.Equal(["improving", "quality"], Strings(store, $"SELECT family FROM decision_card WHERE index_code = 'MID' AND session_date = '{IndexNight}' AND family IN ('improving', 'quality') ORDER BY family;"));

        using var host = new Host(store.Root);
        using var after = host.CreateClient();

        var drawnHome = WebUtility.HtmlDecode(await after.GetStringAsync("/screens/home?universe=400"));
        var drawnTonight = WebUtility.HtmlDecode(await after.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));

        Assert.Empty(SinglePageApp.BookPicksWithNoProfitRow(drawnHome));
        Assert.Empty(SinglePageApp.BookPicksWithNoProfitRow(drawnTonight));

        // Home's Fundamentals card: the buy, the stop with its moves, the take-profit with its multiple, and its filings' rule
        // beside them.
        var card = Read(Regex.Match(drawnHome, "<article class=\"home-idea fam-edge\" data-family=\"improving\" data-ticker=\"M1\">.*?</article>", RegexOptions.Singleline).Value);

        Assert.Contains("Buy at about$50.00", card, StringComparison.Ordinal);
        Assert.Contains("Sell if it closes below$42.004 typical daily moves under the buy.", card, StringComparison.Ordinal);
        Assert.Contains("Take profit at$74.00No earlier high 3 times the risk or more above the buy, so it aims for 3 times the risk: $24.00 up for $8.00 down.", card, StringComparison.Ordinal);
        Assert.Contains("It also sells when a new filing drops its score below 7 in every 9 of the checks that apply, or after 126 trading days.", card, StringComparison.Ordinal);
        Assert.DoesNotContain("No price is set in advance", card, StringComparison.Ordinal);
        Assert.DoesNotContain("its filings weaken", card, StringComparison.Ordinal);

        // Tonight's rows of the two buys: the buy, the stop, the take-profit and what each risks for it, (74 - 50) / (50 - 42) = 3
        // and (62 - 50) / (50 - 44) = 2.
        Assert.Contains("<td class=\"r num plan-buy\" data-buy=\"50\">50.00</td><td class=\"r num plan-stop\" data-stop=\"42\">42.00</td><td class=\"r num plan-target\" data-target=\"74\">74.00</td><td class=\"r num\" data-reward-to-risk=\"3\">$3.00</td>", Row(drawnTonight, "improving"), StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num plan-buy\" data-buy=\"50\">50.00</td><td class=\"r num plan-stop\" data-stop=\"44\">44.00</td><td class=\"r num plan-target\" data-target=\"62\">62.00</td><td class=\"r num\" data-reward-to-risk=\"2\">$2.00</td>", Row(drawnTonight, "quality"), StringComparison.Ordinal);
        Assert.DoesNotContain("data-sells", Row(drawnTonight, "improving"), StringComparison.Ordinal);

        // Each book's own card on Tonight draws M1's stop and take-profit beside it.
        var fundamentals = Read(Assert.Single(Blocks(drawnTonight, "<section class=\"card setup-card\" id=\"setup-improving\".*?</section></div></section>")));
        var quality = Read(Assert.Single(Blocks(drawnTonight, "<section class=\"card setup-card\" id=\"setup-quality\".*?</section></div></section>")));

        Assert.Contains("Sell if it closes below 42.004 typical daily moves under the buy.", fundamentals, StringComparison.Ordinal);
        Assert.Contains("Take profit at 74.00", fundamentals, StringComparison.Ordinal);
        Assert.Contains("Sell if it closes below 44.003 typical daily moves under the buy.", quality, StringComparison.Ordinal);
        Assert.Contains("Take profit at 62.00", quality, StringComparison.Ordinal);

        static string Row(string page, string family) =>
            Regex.Match(page, $"<tr data-ticker=\"M1\" data-family=\"{family}\" data-place=\"\\d+\" data-planless>.*?</tr>", RegexOptions.Singleline).Value;
    }
}
