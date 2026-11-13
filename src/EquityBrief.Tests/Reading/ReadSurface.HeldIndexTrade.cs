using System.Net;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Families;
using Xunit;

namespace EquityBrief.Tests.Reading;

// A stock held on an open trade of the S&P 400's or 600's own pullback, breakout or drift is drawn with that trade first on
// its stock and chart pages, as one an S&P 500 setup or the dip holds is: the card its listing night stored, its fill, its
// stop and its exits. Before this the stock page found a held trade among the S&P 500's swing listings and the dip's alone,
// so SIRI, held on the S&P 400's breakout, drew one line naming an earlier version and no fill, stop or exit.
// see: A stock held on a family's open trade is drawn with that trade first, its short card and its own exits on the plan and the chart
public partial class ReadSurface
{
    // S2, listed by the S&P 600's breakout on 29 Sep and bought at 48.00, is read on the night of 2 Oct with the card its
    // listing night stored. M3, whose S&P 400 pullback trade ended on 1 Oct, and S1, whose S&P 600 drift trade listed on 1 Oct
    // was never filled, each with a card its listing night stored, are held on neither page.
    static TemporaryStore HeldIndexTradeStore()
    {
        var store = UniversesStore();

        store.Execute(
            "INSERT INTO index_family_trade (index_code, family, ticker, session_date, place, entry, stop, target, trail, cap) VALUES " +
            "('SML', 'breakout', 'S2', '2026-09-29', 1, '48', '45', NULL, '3', 63);");
        FillAtTheirOwnBuy(store);
        store.Execute(
            "INSERT INTO index_family_trade (index_code, family, ticker, session_date, place, entry, stop, target, trail, cap) VALUES " +
            "('SML', 'drift', 'S1', '2026-10-01', 1, '40', '36', '48', NULL, 63);");
        store.Execute(
            "INSERT INTO decision_card (index_code, session_date, family, ticker, place, entry, stop, target, rule, settings, lines, record, cap, trail) VALUES " +
            "('SML', '2026-09-29', 'breakout', 'S2', 1, '48', '45', NULL, 'The breakout on the S&P 600', '{}', '[]', NULL, 63, '3'), " +
            "('MID', '2026-09-28', 'pullback', 'M3', 1, '30', '28', '34', 'The pullback on the S&P 400', '{}', '[]', NULL, 20, NULL), " +
            "('SML', '2026-10-01', 'drift', 'S1', 1, '40', '36', '48', 'The drift on the S&P 600', '{}', '[]', NULL, 63, NULL);");

        return store;
    }

    [Fact]
    public async Task AStockHeldOnAnOpenSAndP400Or600TradeIsDrawnWithThatTradeItsCardAndItsExitsOnItsStockAndChartPages()
    {
        using var store = HeldIndexTradeStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // The stock page: the held trade's short card, its setup and night, its fill, its stop, its target as a trailing
        // stop's, its last day, and the plan opening on the trade's own exits.
        var name = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/S2/{IndexNight}"));
        var held = Assert.Single(Blocks(name, "<section class=\"held-card\".*?</section>"));

        Assert.Contains("data-family=\"breakout\" data-night=\"2026-09-29\" data-status=\"open\"", held, StringComparison.Ordinal);
        Assert.Contains("<dt>Fill</dt><dd>48.00</dd>", held, StringComparison.Ordinal);
        Assert.Contains("<dt>Stop</dt><dd>a close below 45.00</dd>", held, StringComparison.Ordinal);
        Assert.Contains("<dt>Target</dt><dd>none, its stop trails the price</dd>", held, StringComparison.Ordinal);
        Assert.Contains("<dt>Last day</dt><dd>after 63 trading days</dd>", held, StringComparison.Ordinal);
        Assert.Matches("<p class=\"picks-exit\" data-family=\"breakout\" data-held=\"2026-09-29\">This stock is held on the trade [^<]* picked it for on 29 Sep 2026, sold at the next open after a close below 45\\.00 or after 63 trading days, whichever comes first\\.", name);

        // The chart page: the breakout's chip says it is held on its open trade, and the brief opens on the trade, its
        // trailing stop and its exits as its card states them.
        var (summary, lines, chips) = BriefOfPage(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/chart/S2")));

        Assert.Contains(chips, chip => chip.StartsWith("breakout|held-trade|", StringComparison.Ordinal) && chip.EndsWith(": held on its open trade", StringComparison.Ordinal));
        Assert.Contains("held on the open trade", summary, StringComparison.Ordinal);
        Assert.Contains("picked it for on 29 Sep 2026, bought at the next open at 48.00.", lines["buy"], StringComparison.Ordinal);
        Assert.Equal("Sell at the next trading day's open after a day that closes below the stop, 45.00 at the buy, raised after each close to that close less 3.00 and never lowered.", lines["stop"]);
        Assert.Equal("No target: the trade is ended by its stop or its time limit. Otherwise sell at the close 63 trading days after the buy.", lines["exit"]);

        // A trade that ended before the night, and one never filled, hold the stock on neither page.
        foreach (var ticker in new[] { "M3", "S1" })
        {
            Assert.DoesNotContain("class=\"held-card\"", WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{ticker}/{IndexNight}")), StringComparison.Ordinal);
            Assert.DoesNotContain("held-trade", string.Join(" ", BriefOfPage(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/chart/{ticker}"))).Chips), StringComparison.Ordinal);
        }
    }

    // Which trade holds the stock, worked by hand on the night of 10 Oct over X's trades: the breakout's of 1 Oct, open; the
    // drift's of 5 Oct, its exit fired on the close of 9 Oct and not yet sold, so still held until the next open sells it; the
    // fundamentals-first family's of 8 Oct, drawn on no page; and the pullback's listed on the night itself, to buy. The
    // drift's is the newest held. Once its sale is priced the breakout's is, and a stock held on none is held on nothing.
    [Fact]
    public void TheHeldIndexTradeIsTheNewestOpenOrFiredAndUnsoldTradeOfTheIndexsOwnSwingSetups()
    {
        var night = new DateOnly(2026, 10, 10);

        IndexTradeRow Trade(string family, DateOnly listed, DateOnly? ended = null, double? result = null, string ticker = "X") =>
            new("MID", family, ticker, listed, 1, 50m, 45m, 60m, null, 63, ended, result, null, 50.5m);

        var breakout = Trade(BreakoutRule.Name, new DateOnly(2026, 10, 1));
        var fired = Trade(DriftRule.Name, new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 9));
        var sold = fired with { Result = 1.2 };
        var business = Trade(FundamentalsRule.Name, new DateOnly(2026, 10, 8));
        var tonight = Trade(SetupFamilies.Pullback, night);
        var other = Trade(DriftRule.Name, new DateOnly(2026, 10, 9), ticker: "Y");

        Assert.Equal(fired, PicksScreen.HeldIndexTrade([breakout, fired, business, tonight, other], "X", night));
        Assert.Equal(breakout, PicksScreen.HeldIndexTrade([breakout, sold, business, tonight, other], "X", night));
        Assert.Null(PicksScreen.HeldIndexTrade([sold, business, tonight, other], "X", night));
    }
}
