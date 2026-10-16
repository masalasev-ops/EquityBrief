using System.Net;
using EquityBrief.Core.Ledger;

namespace EquityBrief.Tests.Reading;

// From 19.10 every screen reads a trade's result from its fill at the next session's open, never from the close-priced
// columns its writer stores. A screen test whose subject is not the fill constructs its trades as their writers store them
// and then gives each a fill at its own plan's buy, on its own night, holding the figures its writer stored, so the
// screen's hand-worked figures read as they did before entries moved; the move itself is held by the fill's own tests and
// by the scan that no read surface draws a close-priced figure. A trade a test already filled keeps its fill.
// see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
public partial class ReadSurface
{
    // Past picks buys each trade at its fill, says a trade waiting on its next session buys at the next open and one whose
    // stock holds no next session why it holds no fill, and Tonight says each pick buys at the next open with its plan.
    [Fact]
    public async Task PastPicksBuysEachTradeAtItsFillAndSaysWhereOneWaitsOrHoldsNoneAndTonightSaysEachPickBuysAtTheNextOpen()
    {
        using var store = PicksStore();

        // PC's open plan of the version 3 night bought at 51 at the open of the session after; PD's not filled yet; and PA's
        // stock holding no bar on its next session.
        store.Execute($"UPDATE trade_fill SET fill = '51', filled_on = '2026-09-29' WHERE source = 'forward_return' AND ticker = 'PC' AND session_date = '{PicksVersionThreeNight}' AND owner = 'clear';");
        store.Execute($"DELETE FROM trade_fill WHERE source = 'forward_return' AND ticker = 'PD' AND session_date = '{PicksVersionThreeNight}';");
        store.Execute($"UPDATE trade_fill SET fill = NULL, filled_on = NULL, result = NULL, reason = '{TradeFills.NoNextSession}' WHERE source = 'forward_return' AND ticker = 'PA' AND session_date = '{PicksVersionTwoNight}';");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks"));

        // PC bought at its fill and its plan's stop and target where its close set them; PD saying it buys at the next
        // open beside its plan's buy; PA saying why it holds no fill, and with no fill no result.
        Assert.Contains("<td class=\"r num\" data-buy=\"51\">51.00</td><td class=\"r num\" data-stop=\"47\">47.00</td><td class=\"r num\" data-target=\"58\">58.00</td>", PickRowOf(page, "PC", PicksVersionThreeNight), StringComparison.Ordinal);
        Assert.Contains($"<td class=\"r num\" data-buy=\"50\"><span class=\"degraded\">{TradeFills.Waiting}</span></td>", PickRowOf(page, "PD", PicksVersionThreeNight), StringComparison.Ordinal);

        var unfilled = PickRowOf(page, "PA", PicksVersionTwoNight);

        Assert.Contains($"<td class=\"r num\" data-buy=\"100\"><span class=\"degraded\">{TradeFills.NoNextSession}</span></td>", unfilled, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r res\" data-result=\"none\"><span class=\"degraded\">none</span></td>", unfilled, StringComparison.Ordinal);

        // Tonight's buy points say once beneath the table that each pick buys at the next open with its plan.
        using var tonightStore = await FamilyPagesStore();
        using var tonightHost = new Host(tonightStore.Root);
        using var tonightClient = tonightHost.CreateClient();

        Assert.Contains(
            $"</table></div><p class=\"buys-next\">Each pick {TradeFills.Waiting}, the open of the session after this night, with its stop and target where this night's close set them.</p>",
            WebUtility.HtmlDecode(await tonightClient.GetStringAsync($"/screens/tonight/{TheSwitch}")),
            StringComparison.Ordinal);
    }

    internal static void FillAtTheirOwnBuy(TemporaryStore store) =>
        store.Execute(Fills);

    static string Fills => @"
            INSERT OR IGNORE INTO trade_fill (source, index_code, owner, ticker, session_date, filled_on, fill, past, result, benchmark, members, edge, reason, filled_at)
            SELECT 'family_trade', 'GSPC', candidate, ticker, session_date, session_date, entry, NULL, result, benchmark, members, result - benchmark, NULL, '2026-01-01T00:00:00Z' FROM family_trade;
            INSERT OR IGNORE INTO trade_fill (source, index_code, owner, ticker, session_date, filled_on, fill, past, result, benchmark, members, edge, reason, filled_at)
            SELECT 'index_family_trade', index_code, family, ticker, session_date, session_date, entry, NULL, result, benchmark, members, result - benchmark, NULL, '2026-01-01T00:00:00Z' FROM index_family_trade;
            INSERT OR IGNORE INTO trade_fill (source, index_code, owner, ticker, session_date, filled_on, fill, past, result, benchmark, members, edge, reason, filled_at)
            SELECT 'index_rule_trade', index_code, candidate, ticker, session_date, session_date, entry, NULL, result, benchmark, members, result - benchmark, NULL, '2026-01-01T00:00:00Z' FROM index_rule_trade;
            INSERT OR IGNORE INTO trade_fill (source, index_code, owner, ticker, session_date, filled_on, fill, past, result, benchmark, members, edge, reason, filled_at)
            SELECT 'rule_pick', index_code, rule, ticker, session_date, session_date, entry, NULL, result, NULL, NULL, NULL, NULL, '2026-01-01T00:00:00Z' FROM rule_pick;
            INSERT OR IGNORE INTO trade_fill (source, index_code, owner, ticker, session_date, filled_on, fill, past, result, benchmark, members, edge, reason, filled_at)
            SELECT 'heavyweight_holding', 'GSPC', '', ticker, entered_on, entered_on, entry_close, NULL, result, cut_return, NULL, result - cut_return, NULL, '2026-01-01T00:00:00Z' FROM heavyweight_holding;
            INSERT OR IGNORE INTO trade_fill (source, index_code, owner, ticker, session_date, filled_on, fill, past, result, benchmark, members, edge, reason, filled_at)
            SELECT 'heavyweight_rule_holding', 'GSPC', candidate, ticker, entered_on, entered_on, entry_close, NULL, result, cut_return, NULL, result - cut_return, NULL, '2026-01-01T00:00:00Z' FROM heavyweight_rule_holding;
            INSERT OR IGNORE INTO trade_fill (source, index_code, owner, ticker, session_date, filled_on, fill, past, result, benchmark, members, edge, reason, filled_at)
            SELECT 'index_heavyweight_holding', index_code, '', ticker, entered_on, entered_on, entry_close, NULL, result, cut_return, NULL, result - cut_return, NULL, '2026-01-01T00:00:00Z' FROM index_heavyweight_holding;
            INSERT OR IGNORE INTO trade_fill (source, index_code, owner, ticker, session_date, filled_on, fill, past, result, benchmark, members, edge, reason, filled_at)
            SELECT 'index_heavyweight_rule_holding', index_code, candidate, ticker, entered_on, entered_on, entry_close, NULL, result, cut_return, NULL, result - cut_return, NULL, '2026-01-01T00:00:00Z' FROM index_heavyweight_rule_holding;

            " + ScoredPlans;

    // The page list's scored plans, each at the night's close its return is measured from, its result its return over
    // its plan's risk as a share of that buy, as Past picks read it before: the swing filter's plans off the row's own
    // plan by the horizon it was scored under, every other family's off its stored answer by its family's horizon.
    static readonly string ScoredPlans = @"
            INSERT OR IGNORE INTO trade_fill (source, index_code, owner, ticker, session_date, filled_on, fill, past, result, benchmark, members, edge, reason, filled_at)
            SELECT 'forward_return', 'GSPC', f.horizon, f.ticker, f.session_date, f.session_date,
                   COALESCE((SELECT b.close FROM bar b WHERE b.ticker = f.ticker AND b.session_date = f.session_date), g.swing_entry), NULL,
                   CASE WHEN f.return_pct IS NOT NULL AND CAST(g.swing_entry AS REAL) > CAST(CASE WHEN f.horizon LIKE 'clear%' THEN g.clear_stop ELSE g.swing_stop END AS REAL)
                        THEN f.return_pct / ((CAST(g.swing_entry AS REAL) - CAST(CASE WHEN f.horizon LIKE 'clear%' THEN g.clear_stop ELSE g.swing_stop END AS REAL)) / CAST(g.swing_entry AS REAL) * 100)
                   END,
                   NULL, NULL, NULL, NULL, '2026-01-01T00:00:00Z'
            FROM forward_return f
            JOIN gate_result g ON g.ticker = f.ticker AND g.session_date = f.session_date
            WHERE f.horizon IN ('swing', 'swing-20', 'clear', 'clear-20', '" + EquityBrief.Core.Returns.ForwardReturnSeries.MenuExit + @"');
            INSERT OR IGNORE INTO trade_fill (source, index_code, owner, ticker, session_date, filled_on, fill, past, result, benchmark, members, edge, reason, filled_at)
            SELECT 'forward_return', 'GSPC', pf.horizon, p.ticker, p.session_date, p.session_date,
                   COALESCE((SELECT b.close FROM bar b WHERE b.ticker = p.ticker AND b.session_date = p.session_date), pr.entry), NULL,
                   CASE WHEN pf.return_pct IS NOT NULL AND CAST(pr.entry AS REAL) > CAST(pr.stop AS REAL)
                        THEN pf.return_pct / ((CAST(pr.entry AS REAL) - CAST(pr.stop AS REAL)) / CAST(pr.entry AS REAL) * 100)
                   END,
                   NULL, NULL, NULL, NULL, '2026-01-01T00:00:00Z'
            FROM family_pick p
            JOIN family_result pr ON pr.session_date = p.session_date AND pr.ticker = p.ticker AND pr.family = p.family
            LEFT JOIN gate_result pg ON pg.ticker = p.ticker AND pg.session_date = p.session_date
            LEFT JOIN filter_version pv ON pv.version = pg.version
            JOIN forward_return pf
                ON pf.ticker = p.ticker AND pf.session_date = p.session_date
                AND pf.horizon = " + EquityBrief.Core.Families.SetupFamilies.HorizonIn("p.family", "pv.settings") + @";";
}
