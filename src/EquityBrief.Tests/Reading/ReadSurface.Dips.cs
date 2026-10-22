using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Worker.Dips;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The short-term dip on Tonight, read back off the rendered page: its card after every swing family's, the market line
// saying the dip reads its own setting, its tile, and its label on an earlier family's row where it gave way.
// see: The dip lists last among the swing families on every index and gives way to every other family's list and open trades
public partial class ReadSurface
{
    // A dip answer's gates as its step stores them, every one passed.
    static string DipGatesPassed() => FamilyRule.GatesJson(
    [
        DipRule.NoMarket,
        .. DipRule.Gates.Skip(1).Select(gate => new Gate(gate, true, string.Empty, FamilyRule.Values())),
    ]).Replace("'", "''", StringComparison.Ordinal);

    // The dip's own night on the universes' store's S&P 400, its market setting off: M2 passing first and listed at its
    // close of 50 with its stop at 44; M1 passing and listed tonight under the breakout, so the dip gives way; and M3
    // passing and held back by its pullback trade on the S&P 500's list from 2026-09-30.
    static void DipNightOn(TemporaryStore store)
    {
        store.Execute(
            "INSERT INTO dip_night (index_code, session_date, members, market, market_open, settings, passed, listed, fault) VALUES " +
            $"('MID', '{IndexNight}', 3, 'off', 1, '{DipNight.Settings(DipRule.Provisional)}', 3, 1, NULL);");
        store.Execute(
            "INSERT INTO dip_result (index_code, session_date, ticker, passed, place, entry, stop, time_limit, rsi, reason, gates) VALUES " +
            $"('MID', '{IndexNight}', 'M2', 1, 1, '50', '44', 10, 3.5, NULL, '{DipGatesPassed()}'), " +
            $"('MID', '{IndexNight}', 'M1', 1, 2, '50', '45', 10, 6.0, NULL, '{DipGatesPassed()}'), " +
            $"('MID', '{IndexNight}', 'M3', 1, 3, '30', '26', 10, 8.0, NULL, '{DipGatesPassed()}');");
        store.Execute(
            "INSERT INTO dip_pick (index_code, session_date, ticker, state, place, listed_under, held_index, held_family, held_night) VALUES " +
            $"('MID', '{IndexNight}', 'M2', 'listed', 1, NULL, NULL, NULL, NULL), " +
            $"('MID', '{IndexNight}', 'M1', 'under another', NULL, 'breakout', NULL, NULL, NULL), " +
            $"('MID', '{IndexNight}', 'M3', 'open trade', NULL, NULL, 'GSPC', 'pullback', '2026-09-30');");
    }

    [Fact]
    public async Task OnANightTheMarketCheckClosedTheOtherSwingCardsCloseAndTheDipsCardListsReadBackOffThePage()
    {
        // The S&P 400's night at a breadth of 42 per cent, under its floor of 45, so the swing families' check closed their
        // lists; the breakout's pick of M1 stored before the check, as the universes' store keeps it.
        using var store = UniversesStore();

        store.Execute("UPDATE index_family_night SET breadth = 0.42, market_open = 0 WHERE index_code = 'MID';");
        store.Execute("DELETE FROM index_family_pick WHERE index_code = 'MID' AND family = 'pullback';");
        DipNightOn(store);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));
        var line = Assert.Single(Blocks(page, "<p class=\"market-line\".*?</p>"));

        // The line names the check that closed the swing lists and says the dip read its own setting, which left its list
        // open.
        Assert.Contains("data-dip=\"open\"", line, StringComparison.Ordinal);
        Assert.Contains(
            "<b class=\"market-closed\">The lists are closed but the short-term dip's</b>: the S&P 400's breadth: 42.0% of its members closed above their 200-day average, below its floor of 45%; the short-term dip reads its own market setting, none while it is provisional, and its list is open.",
            line,
            StringComparison.Ordinal);

        // The pullback's card closed by the check, and the dip's card drawn last of the swing families, listing M2 on its
        // rule and its provisional marker, with the notes on M1 and M3.
        Assert.Contains("nothing tonight · the market check closed", FamilyCardOf(page, SetupFamilies.Pullback), StringComparison.Ordinal);

        var dip = FamilyCardOf(page, DipRule.Name);

        Assert.Equal(["M2"], CardPicksOf(dip));
        Assert.Contains(SetupFamilies.ProvisionalStatus, dip, StringComparison.Ordinal);
        Assert.Contains("M1 also qualified here tonight and is shown once, under Breakouts to a new high, with both labels. One stock is one trade.", dip, StringComparison.Ordinal);
        Assert.Contains("M3 qualified tonight but its pullback trade from 2026-09-30 on the S&P 500's list is still open, so it is not listed. One stock, one trade.", dip, StringComparison.Ordinal);

        // The cards in the page's order, the dip's after the fundamentals-first family's, each numbered among them.
        Assert.Equal(
            ["pullback", "breakout", "drift", "fundamentals", "dip"],
            Regex.Matches(page, "<section class=\"family-card\" data-family=\"([^\"]+)\"").Select(match => match.Groups[1].Value).Distinct());
        Assert.Contains("Setup 5 of 5 · Sharp drop in an uptrend", page, StringComparison.Ordinal);

        // The tiles: every swing family's by the check, the dip's by its own setting, listing with M3 held back.
        var tiles = Regex.Matches(Assert.Single(Blocks(page, "<nav class=\"setup-tiles\".*?</nav>")), "data-family=\"([^\"]+)\" data-card=\"[^\"]+\" data-count=\"[0-9]+\" data-state=\"([^\"]+)\"").Select(match => $"{match.Groups[1].Value}: {match.Groups[2].Value}");
        Assert.Equal(["pullback: the market check closed", "breakout: listing tonight", "drift: the market check closed", "fundamentals: the market check closed", "dip: 1 held back by a trade still open", "heavyweight: rebalanced tonight"], tiles);

        // The breakout's buy point of M1, the stock the dip gave way on, carries the dip's label beside the breakout's own in
        // tonight's table of buy points, and M1 is listed once there.
        var points = Assert.Single(Blocks(page, "<table class=\"list-table buy-points\".*?</table>"));
        var m1 = Assert.Single(Blocks(points, "<tr data-ticker=\"M1\".*?</tr>"));

        Assert.Contains("<span class=\"also-family\" data-also=\"Short-term dip\">", m1, StringComparison.Ordinal);
        Assert.Contains("data-family=\"breakout\"", m1, StringComparison.Ordinal);
    }
}
