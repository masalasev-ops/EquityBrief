using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Dips;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The short-term dip on Tonight, read back off the rendered page: its card after every swing family's, the market line
// saying the dip reads its own setting, its tile, and its label on an earlier family's row where it gave way.
// see: The dip lists last among the swing families on every index and gives way to every other family's list and open trades
public partial class ReadSurface
{
    // The rows 22.1 adds that this check reaches: the dip's card on Tonight and its trades on Past picks.
    internal static readonly string[] DipPageRows =
    [
        CheckReach.Key("15.7 Tonight", "The short-term dip's card"),
        CheckReach.Key("15.17 Past picks", "The short-term dip's trades"),
    ];

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
            $"('MID', '{IndexNight}', 3, 'off', 1, '{DipFamily.Settings(DipRule.Provisional)}', 3, 1, NULL);");
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

        // Its settings as chips beneath its heading, the S&P 400's dollar floor and least close first, and its tag in its
        // own hue.
        Assert.Equal(
            ["dollar floor 1 times", "close at least $5", "above its 200-session average", "2-session RSI under 10", "no report in 3 sessions or ahead", "stop 4 moves", "sold over its 5-session average", "10 sessions at most", "market setting off"],
            Regex.Matches(Assert.Single(Blocks(dip, "<ul class=\"rule-chips\".*?</ul>")), "<li class=\"rule-chip\">([^<]+)</li>").Select(match => match.Groups[1].Value));
        Assert.Contains(Blocks(dip, "<span class=\"fam-tag\"[^>]*>"), tag => tag.Contains("data-family=\"dip\"", StringComparison.Ordinal));
        // Its pick's exit in words where a target would stand, its reward to risk open, as a trailing pick's is.
        var m2 = Assert.Single(Blocks(dip, "<tr data-ticker=\"M2\" data-card-pick=\"true\">.*?</tr>"));

        Assert.Contains("<td class=\"r num plan-target\" data-target=\"none\">over its 5-session average</td>", m2, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\" data-reward-to-risk=\"none\">open</td>", m2, StringComparison.Ordinal);
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

    // The dip's trades as its step keeps them: on the S&P 400 M2 listed tonight and not bought yet, and M1 listed on
    // 2026-09-24, bought at 48.5 the session after and sold at 51 on 2026-09-30 after its exit's close, 0.5 risks before
    // its cost of 0.02; on the S&P 500 AAA listed on 2026-09-29 and held.
    static void DipTradesOn(TemporaryStore store)
    {
        store.Execute(
            "INSERT INTO dip_trade (rule, index_code, ticker, session_date, place, entry, stop, time_limit, exit_average, risk_moves, bought_on, fill, past, ended_on, sale, end_reason, sessions, result, cost) VALUES " +
            $"('provisional', 'MID', 'M2', '{IndexNight}', 1, '50', '44', 10, 5, 4.0, NULL, NULL, NULL, NULL, NULL, 'open', NULL, NULL, NULL), " +
            "('provisional', 'MID', 'M1', '2026-09-24', 1, '48', '42', 10, 5, 4.0, '2026-09-25', '48.5', NULL, '2026-09-30', '51', 'exit', 4, 0.5, 0.02), " +
            "('provisional', 'GSPC', 'AAA', '2026-09-29', 1, '49', '45', 10, 5, 4.0, '2026-09-30', '49.2', NULL, NULL, NULL, 'open', 2, NULL, NULL);");
    }

    [Fact]
    public async Task TheDipsTradesStandOnPastPicksUnderItsLabelAndFilterItsRunRowAndItsNamePageLineReadBackOffThePages()
    {
        using var store = UniversesStore();

        DipNightOn(store);
        DipTradesOn(store);

        // AAA listed tonight under the S&P 500's pullback, which the dip passed and gave way to.
        store.Execute(
            "INSERT INTO family_pick (session_date, ticker, family, state, place, also, held_family, held_night) VALUES " +
            $"('{IndexNight}', 'AAA', 'pullback', 'listed', 1, '[]', NULL, NULL);");
        store.Execute(
            "INSERT INTO dip_pick (index_code, session_date, ticker, state, place, listed_under, held_index, held_family, held_night) VALUES " +
            $"('GSPC', '{IndexNight}', 'AAA', 'under another', NULL, 'pullback', NULL, NULL, NULL);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // The S&P 400's Past picks: the dip's two trades among the index's, each under its label with the index, the setup
        // chips counting each setup's trades in the page's order, and the key saying how a dip trade is bought and sold.
        var picks = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks?universe=400"));

        Assert.Equal(
            [("pullback", "1"), ("breakout", "1"), ("dip", "2")],
            Regex.Matches(Assert.Single(Blocks(picks, "<nav class=\"universe-filters picks-filters picks-setups\".*?</nav>")), "data-value=\"([a-z]+)\"[^>]*>[^<]+<span class=\"n\">([0-9]+)</span>")
                .Where(match => match.Groups[1].Value != "all")
                .Select(match => (match.Groups[1].Value, match.Groups[2].Value)));
        Assert.Contains("Showing 4 of 4 S&P 400 trades, 2 open", picks, StringComparison.Ordinal);
        Assert.Contains("<td class=\"setup\" data-setup=\"dip\">Short-term dip · S&P 400</td>", Assert.Single(Blocks(picks, "<tr data-ticker=\"M2\" data-index=\"S&P 400\" data-family=\"dip\".*?</tr>")), StringComparison.Ordinal);
        Assert.Contains("data-ended=\"2026-09-30\"", Assert.Single(Blocks(picks, "<tr data-ticker=\"M1\" data-index=\"S&P 400\" data-family=\"dip\".*?</tr>")), StringComparison.Ordinal);
        Assert.Contains("A short-term dip trade is bought at the open of the session after the night that listed it and sold at the open after the first close above its 5-session average of closes or under its stop", picks, StringComparison.Ordinal);

        // The dip chosen keeps its trades alone, the count saying how many of the index's are shown.
        var dipsOnly = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks?universe=400&setup=dip"));

        Assert.Equal(["M2", "M1"], Regex.Matches(Assert.Single(Blocks(dipsOnly, "<table class=\"list-table index-trades\".*?</table>")), "<tr data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.Contains("Showing 2 of 4 S&P 400 trades, 1 open", dipsOnly, StringComparison.Ordinal);

        // The S&P 500's: the dip's card beneath the lists' trades with AAA held, and the dip chosen drawing that card alone.
        var large = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks"));
        var card = Assert.Single(Blocks(large, "<section class=\"card\"[^>]*data-card=\"dip-picks\".*?</section>"));

        Assert.Contains("<p class=\"list-count\" data-dip=\"1\" data-open=\"1\">1 S&P 500 short-term dip trade, 1 open</p>", card, StringComparison.Ordinal);
        Assert.Contains("<td class=\"setup\" data-setup=\"dip\">Short-term dip · S&P 500</td>", card, StringComparison.Ordinal);

        var largeDips = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks?setup=dip"));

        Assert.Single(Blocks(largeDips, "<section class=\"card\"[^>]*data-card=\"dip-picks\".*?</section>"));
        Assert.DoesNotContain("data-card=\"picks-summary\"", largeDips, StringComparison.Ordinal);
        Assert.Contains("data-card=\"picks-summary\"", large, StringComparison.Ordinal);

        // The S&P 400's Run page: the dip's row after every swing family's and before the sector heavyweights', listing one
        // tonight with its two trades, one open and one finished, provisional.
        var run = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{IndexNight}?universe=400"));
        var rows = Regex.Matches(Assert.Single(Blocks(run, "<table class=\"list-table family-run\".*?</table>")), "<tr data-family=\"([^\"]+)\" data-state=\"([^\"]+)\"[^>]*data-listed=\"([0-9]+)\"").Select(match => (match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value)).ToArray();

        Assert.Equal(["pullback", "breakout", "drift", "fundamentals", "dip", "heavyweight"], rows.Select(row => row.Item1));
        Assert.Equal(("dip", "provisional", "1"), rows.Single(row => row.Item1 == "dip"));

        // The name pages: M2 listed under the dip at its place, M3 held back by its pullback trade, and AAA, listed under the
        // S&P 500's pullback, saying it also qualified as a short-term dip.
        var m2 = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/M2/{IndexNight}"));

        Assert.Contains("<p class=\"listed-under\" data-night=\"2026-10-02\" data-family=\"dip\" data-place=\"1\" data-also=\"\">On the page's list for 2026-10-02 under <b>Short dips in an uptrend</b>, at place 1.</p>", m2, StringComparison.Ordinal);

        var m3 = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/M3/{IndexNight}"));

        Assert.Contains("Short dips in an uptrend passed it on 2026-10-02 and does not list it: its trade from 2026-09-30 is still open.", m3, StringComparison.Ordinal);

        var aaa = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/AAA/{IndexNight}"));
        var listedUnder = Assert.Single(Blocks(aaa, "<p class=\"listed-under\".*?</p>"));

        Assert.Contains("data-family=\"pullback\" data-place=\"1\" data-also=\"Short-term dip\"", listedUnder, StringComparison.Ordinal);
        Assert.Contains("it also qualified as a short-term dip.", listedUnder, StringComparison.Ordinal);
    }
}
