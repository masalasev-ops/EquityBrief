using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Core.Returns;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, 13.5: the pages around the setup families, each read back off the rendered page over a
// constructed night the lister drew. Tonight's market line and its one list of stocks close to a buy point
// across every setup; Past picks' setup filter and label, a trailing trade drawn with no target and its
// result in multiples of its risk, and a provisional setup's trades kept out of the share; a name's page
// saying which setup lists it; and the run page's row a setup.
// see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
public partial class ReadSurface
{
    internal static readonly string[] FamilyPagesClaims =
    [
        CheckReach.Key("15.7 Tonight", "The market line, whether the market check left the lists open with the breadth and its floor"),
        CheckReach.Key("15.7 Tonight", "The market line, the buy points the page lists and how many setups list one"),
        CheckReach.Key("15.7 Tonight", "The market line, the stocks close to a buy point"),
        CheckReach.Key("15.7 Tonight", "The market line, the trades still open linking to Past picks"),
        CheckReach.Key("15.7 Tonight", "Close to a buy point across the setups, one row a stock and setup with the setup's label"),
        CheckReach.Key("15.7 Tonight", "Close to a buy point across the setups, the one gate it missed in the gate's words"),
        CheckReach.Key("15.7 Tonight", "Close to a buy point across the setups, the count shown of the count there are"),
        CheckReach.Key("15.9 Name", "The setup the page lists the name under, the setup that lists it with its place and the other setups it qualified under"),
        CheckReach.Key("15.9 Name", "The setup the page lists the name under, a sentence for a setup that passed it and holds it back"),
        CheckReach.Key("15.10 Run", "The setup families, one row a setup in the page's order"),
        CheckReach.Key("15.10 Run", "The setup families, live with the day its rule was registered or provisional"),
        CheckReach.Key("15.10 Run", "The setup families, the variants scored beside it"),
        CheckReach.Key("15.10 Run", "The setup families, what it lists on the night and the trades listed under it so far"),
        CheckReach.Key("15.10 Run", "The setup families, its record against what it waits for or the words saying it starts at the freeze"),
        CheckReach.Key("15.17 Past picks", "The setup filter, one chip a setup with its trades and the counts following the setup chosen"),
        CheckReach.Key("15.17 Past picks", "The setup a trade was listed under, a label on its row"),
        CheckReach.Key("15.17 Past picks", "A trailing trade, drawn with no target and its result in multiples of its risk"),
        CheckReach.Key("15.17 Past picks", "A provisional setup's trades, marked and counted in no share"),
    ];

    // The night the pages are read for. The pullback lists F1 to F5 and holds HELD back, as the framework's
    // night does. The breakout passes K1 and K2, and K3 is one gate short of it, its volume. The night
    // before, the page listed a breakout, B0, bought at 50 with the stop at 47: it closed at 51 and then at
    // 49.9, under its trail of 48 raised to... its stop of 47 raised by the close of 51 to 48, so 49.9 did
    // not sell it and it is still open on the night.
    static async Task<TemporaryStore> FamilyPagesStore()
    {
        var store = await FamilyNightStore();

        // K1 to K3 miss two of the pullback's gates, so none is a single gate short of a pullback.
        Evening(store, TheSwitch, filter: false, [new Member("K1", Trend: false, Setup: false), new Member("K2", Trend: false, Setup: false), new Member("K3", Trend: false, Setup: false), new Member("K4", Trend: false, Setup: false)]);
        BreakoutAnswer(store, "K1", 1, "50", "49.5", 2.1, 0.8, "47");
        BreakoutAnswer(store, "K2", 2, "80.25", "80", 1.6, 1, "76.25");
        BreakoutAnswer(store, "K3", null, "30", "29", 1.2, 0.7, "28", volume: false);

        // K4 is two gates short of a breakout, its high and its volume, so it is close to no buy point.
        BreakoutAnswer(store, "K4", null, "40", "45", 1.1, 0.7, "38", newHigh: false, volume: false);

        // Each trade the page lists holds an outcome row not yet decided, as the filler writes one the night
        // a trade is listed: the pullback's five tonight and HELD's the night before under the plan clear
        // of the noise, and the breakout's two tonight under its own horizon.
        foreach (var (ticker, session, horizon) in new[]
        {
            ("F1", TheSwitch, "clear"), ("F2", TheSwitch, "clear"), ("F3", TheSwitch, "clear"), ("F4", TheSwitch, "clear"), ("F5", TheSwitch, "clear"),
            ("HELD", BeforeTheSwitch, "clear"), ("K1", TheSwitch, "breakout"), ("K2", TheSwitch, "breakout"),
        })
        {
            store.Execute(
                "INSERT INTO forward_return (ticker, session_date, horizon, outcome, resolved_on, return_pct, base_rate, break_even) " +
                $"VALUES ('{ticker}', '{session}', '{horizon}', NULL, NULL, NULL, NULL, NULL);");
        }

        // The breakout the page listed five nights before, on a night the families drew, sold under its
        // trail three sessions later at 53 from a buy at 50 with the stop at 47: 6 per cent on 6 at risk.
        const string Earlier = "2026-09-25";

        store.Execute($"INSERT INTO family_night (session_date, families) VALUES ('{Earlier}', '[\"pullback\",\"breakout\"]');");
        store.Execute(
            "INSERT INTO family_result (session_date, ticker, family, passed, missed, place, entry, stop, target, order_by, exclusions, gates) " +
            $"VALUES ('{Earlier}', 'B0', 'breakout', 1, 0, 1, '50', '47', NULL, 2.0, '[]', '{{\"gates\":[]}}');");
        store.Execute(
            "INSERT INTO family_pick (session_date, ticker, family, state, place, also, held_family, held_night) " +
            $"VALUES ('{Earlier}', 'B0', 'breakout', 'listed', 1, '[]', NULL, NULL);");
        store.Execute(
            "INSERT INTO forward_return (ticker, session_date, horizon, outcome, resolved_on, return_pct, base_rate, break_even, planned_risk) " +
            $"VALUES ('B0', '{Earlier}', 'breakout', '{TrailingExit.Trailed}', '2026-09-30', 6.0, NULL, NULL, 6.0);");
        PickBar(store, "B0", Earlier, "50");

        // The night's breadth as the market reader stored it, 60 per cent, over the floor of 50 the gates read.
        store.Execute(
            "INSERT INTO market_reading (session_date, members, counted, above, breadth, counted_context, above_context, breadth_context, volume_counted, median_volume_ratio) " +
            $"VALUES ('{TheSwitch}', 10, 10, 6, 0.6, 10, 5, 0.5, 10, 0.9);");

        await RedrawTheFamilies(store);

        return store;
    }

    [Fact]
    public async Task TonightOpensOnTheMarketLineAndDrawsOneListOfStocksCloseToABuyPointAcrossEverySetup()
    {
        using var store = await FamilyPagesStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));
        var families = SetupFamilies.InPageOrder.Count;

        // The line. The market gate every row stored is open at a breadth of 60 per cent over a floor of 50.
        // The page lists seven buy points, F1 to F5 and K1 and K2, across two setups. Two stocks are a single
        // gate short: N1 under the pullback, its trade, and K3 under the breakout, its volume. One trade is
        // still open, HELD's from the night before; B0's finished on 2026-09-30.
        var line = Assert.Single(Blocks(page, "<p class=\"market-line\".*?</p>"));

        Assert.Equal(
            FormattableString.Invariant($"<p class=\"market-line\" data-open=\"true\" data-breadth=\"0.6\" data-floor=\"0.5\" data-buy-points=\"7\" data-setups-listing=\"2\" data-setups=\"{families}\" data-close=\"2\" data-open-trades=\"1\">"),
            line[..(line.IndexOf('>') + 1)]);
        Assert.Contains("<b class=\"market-open\">The lists are open</b>: 60.0% of the members closed above their 200-day average, at or above its floor of 50%.", line, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"7 buy points tonight across 2 of {families} setups · 2 close to a buy point · <a href=\"#/picks?status=open\">1 open trade</a>"), line, StringComparison.Ordinal);

        // Read back against the store: the buy points are the listed rows, and the open trade is the one
        // listed trade before the night with no outcome decided by it.
        Assert.Equal(["7"], Strings(store, $"SELECT COUNT(*) FROM family_pick WHERE session_date = '{TheSwitch}' AND state = 'listed';"));

        // The one list across the setups, in the page's order: the pullback's row and then the breakout's,
        // each with its setup's label and the gate it missed.
        var close = Assert.Single(Blocks(page, "<table class=\"list-table close-across\".*?</table>"));

        Assert.Equal(
            [("N1", "pullback", "Pullback", "trade"), ("K3", "breakout", "Breakout", "volume")],
            Regex.Matches(close, "<tr data-ticker=\"([^\"]+)\" data-family=\"([^\"]+)\" data-gate=\"([^\"]+)\">.*?<td class=\"setup\" data-setup=\"[^\"]+\">([^<]+)</td>", RegexOptions.Singleline)
                .Select(match => (match.Groups[1].Value, match.Groups[2].Value, match.Groups[4].Value, match.Groups[3].Value)));
        Assert.Contains("<td class=\"missed-gate\"><b>volume</b>: the volume against its average</td>", close, StringComparison.Ordinal);
        Assert.Contains("<p class=\"list-count\" data-shown=\"2\" data-close=\"2\">Showing 2 of 2 close to a buy point</p>", page, StringComparison.Ordinal);

        // And the other way: every member the breakout stored as one gate short with nothing excluding it
        // is a row of the list.
        Assert.Equal(
            Strings(store, $"SELECT ticker FROM family_result WHERE session_date = '{TheSwitch}' AND passed = 0 AND missed = 1 AND exclusions = '[]' ORDER BY ticker;"),
            Regex.Matches(close, "<tr data-ticker=\"([^\"]+)\" data-family=\"breakout\"").Select(match => match.Groups[1].Value));

        // A night the market check closed says so on the line, with both figures, and lists nothing.
        using var closed = new TemporaryStore().Migrated();

        OpenCloseVersion(closed);
        Evening(closed, TheSwitch, filter: true, [.. Enumerable.Range(0, 6).Select(at => new Member($"M{at}", Market: false))]);
        closed.Execute(
            "INSERT INTO market_reading (session_date, members, counted, above, breadth, counted_context, above_context, breadth_context, volume_counted, median_volume_ratio) " +
            $"VALUES ('{TheSwitch}', 6, 6, 2, 0.4, 6, 3, 0.5, 6, 0.9);");
        await DrawTheFamilies(closed, TheSwitch);

        using var closedHost = new Host(closed.Root);
        using var closedClient = closedHost.CreateClient();

        var shut = Assert.Single(Blocks(WebUtility.HtmlDecode(await closedClient.GetStringAsync($"/screens/tonight/{TheSwitch}")), "<p class=\"market-line\".*?</p>"));

        Assert.Contains("data-open=\"false\" data-breadth=\"0.4\" data-floor=\"0.5\" data-buy-points=\"0\" data-setups-listing=\"0\"", shut, StringComparison.Ordinal);
        Assert.Contains("<b class=\"market-closed\">The lists are closed</b>: 40.0% of the members closed above their 200-day average, below its floor of 50%.", shut, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PastPicksFiltersBySetupLabelsEachTradeAndKeepsAProvisionalSetupsTradesOutOfTheShare()
    {
        using var store = await FamilyPagesStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // Every trade the page listed: the pullback's F1 to F5 tonight and HELD the night before, six; and
        // the breakout's K1 and K2 tonight and B0 on 2026-09-25, three. Nine in all.
        var all = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks"));

        Assert.Contains("data-trades=\"9\" data-shown=\"9\"", all, StringComparison.Ordinal);
        Assert.Equal(
            [("all", "9"), ("pullback", "6"), ("breakout", "3")],
            Regex.Matches(all, "<a class=\"chip\" data-filter=\"setup\" data-value=\"([^\"]+)\"[^>]*>[^<]+<span class=\"n\">(\\d+)</span>").Select(match => (match.Groups[1].Value, match.Groups[2].Value)));

        // The breakout's trades are a provisional setup's: three, marked, and in no share.
        Assert.Contains("<p class=\"provisional-count\" data-provisional=\"3\">3 of them were listed by a setup not yet frozen: followed like any other, and in no share and no average until its freeze.</p>", all, StringComparison.Ordinal);

        // The filter keeps exactly the breakout's trades, newest first, and the counts follow: three listed,
        // two open and one finished.
        var breakouts = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks?setup=breakout"));

        Assert.Contains("data-trades=\"3\" data-shown=\"3\"", breakouts, StringComparison.Ordinal);
        Assert.Equal(
            ["K1", "K2", "B0"],
            Regex.Matches(breakouts, "<tr data-ticker=\"([^\"]+)\" data-night=").Select(match => match.Groups[1].Value));
        Assert.Equal(
            Strings(store, "SELECT ticker FROM family_pick WHERE family = 'breakout' AND state = 'listed' ORDER BY session_date DESC, place;"),
            Regex.Matches(breakouts, "<tr data-ticker=\"([^\"]+)\" data-night=").Select(match => match.Groups[1].Value));
        Assert.Contains("<a class=\"chip\" data-filter=\"setup\" data-value=\"breakout\" aria-pressed=\"true\" href=\"#/picks?setup=breakout\">Breakout<span class=\"n\">3</span></a>", breakouts, StringComparison.Ordinal);
        Assert.Contains("href=\"#/picks?setup=breakout&status=open\"", breakouts, StringComparison.Ordinal);

        // Each row carries its setup's label, marked provisional; a pullback's row carries the pullback's.
        var sold = breakouts[breakouts.IndexOf("<tr data-ticker=\"B0\"", StringComparison.Ordinal)..];

        sold = sold[..sold.IndexOf("</tr>", StringComparison.Ordinal)];

        Assert.Contains("<td class=\"setup\" data-setup=\"breakout\" data-provisional=\"true\">Breakout <span class=\"provisional\">provisional</span></td>", sold, StringComparison.Ordinal);
        Assert.Contains("<td class=\"setup\" data-setup=\"pullback\" data-provisional=\"false\">Pullback</td>", all, StringComparison.Ordinal);

        // B0, the trailing trade: bought at 50 with the stop at 47 and no target, sold at its trailing stop
        // 6 per cent up on the 6 per cent it risked, a result of +1.00 times its risk.
        Assert.Contains("data-buy=\"50\">50.00</td><td class=\"r num\" data-stop=\"47\">47.00</td><td class=\"r num\" data-target=\"none\">trailing</td>", sold, StringComparison.Ordinal);
        Assert.Contains("trailing stop, no target", sold, StringComparison.Ordinal);
        Assert.Contains("data-result=\"1\">+1.00 ×</td>", sold, StringComparison.Ordinal);
        Assert.Equal(TrailingExit.Trailed, Strings(store, "SELECT outcome FROM forward_return WHERE ticker = 'B0';").Single());

        // The pullback's filter keeps its six.
        var pullbacks = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks?setup=pullback"));

        Assert.Contains("data-trades=\"6\" data-shown=\"6\"", pullbacks, StringComparison.Ordinal);
        Assert.DoesNotContain("provisional-count", pullbacks, StringComparison.Ordinal);

        // The share's own count, by hand over two constructed trades that each reached a target with a
        // break-even of 40 per cent: the pullback's is decided, and the drift's, listed by a setup handed in
        // as provisional, is counted as listed and finished and is no decided trade.
        EquityBrief.Api.Reading.PickRow Won(string ticker, string family) => new(
            ticker, new DateOnly(2026, 9, 1), "clear", 100m, 96m, 110m, true, ForwardReturnSeries.Win, new DateOnly(2026, 9, 10), 10.0, 40.0,
            null, 100m, new DateOnly(2026, 9, 30), 111m, Family: family);

        var counted = EquityBrief.Api.Reading.PicksScreen.Summary(EquityBrief.Api.Reading.PicksScreen.Cells(
            [Won("PW", SetupFamilies.Pullback), Won("DW", DriftRule.Name)],
            new DateOnly(2026, 9, 30),
            new HashSet<string>(StringComparer.Ordinal) { DriftRule.Name }));

        Assert.Equal((2, 2, 1, 1), (counted.Listed, counted.Finished, counted.Decided, counted.Provisional));
    }

    [Fact]
    public async Task ANamesPageSaysWhichSetupListsItAndTheRunPageDrawsARowASetup()
    {
        using var store = await FamilyPagesStore();

        // F1 qualifies as a breakout as well, so the pullback lists it with the breakout's label.
        BreakoutAnswer(store, "F1", 3, "100", "99.75", 1.55, 0.9, "96");
        await RedrawTheFamilies(store);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // A name's page: F1 is listed first, under the pullback, and also qualified as a breakout; HELD was
        // passed by the pullback and is held back by its trade from the night before; K1 is the breakout's.
        var listed = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/F1/{TheSwitch}"));

        Assert.Contains($"<p class=\"listed-under\" data-night=\"{TheSwitch}\" data-family=\"pullback\" data-place=\"1\" data-also=\"Breakout\">On the page's list for {TheSwitch} under <b>Pullbacks to support</b>, at place 1; it also qualified as a breakout.</p>", listed, StringComparison.Ordinal);

        var held = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/HELD/{TheSwitch}"));

        Assert.Contains($"<p class=\"listed-under\" data-night=\"{TheSwitch}\" data-family=\"none\" data-place=\"none\" data-also=\"\">Pullbacks to support passed it on {TheSwitch} and does not list it: its trade from {BeforeTheSwitch} is still open.</p>", held, StringComparison.Ordinal);

        var breakout = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/K1/{TheSwitch}"));

        Assert.Contains("data-family=\"breakout\" data-place=\"6\" data-also=\"\">On the page's list for " + TheSwitch + " under <b>Breakouts to a new high</b>, at place 6.</p>", breakout, StringComparison.Ordinal);

        // A name no setup passed says nothing of the list.
        Assert.DoesNotContain("class=\"listed-under\"", WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/N1/{TheSwitch}")), StringComparison.Ordinal);

        // The run page: a row a setup in the page's order. The pullback is live since the day its candidate
        // was registered, with two variants, five listed tonight and six trades, all open; the breakout is
        // provisional, lists two tonight and has three trades, two open and one finished, and its record is
        // the ruling's words.
        var run = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{TheSwitch}"));
        var table = Assert.Single(Blocks(run, "<table class=\"list-table family-run\".*?</table>"));

        Assert.Equal(
            SetupFamilies.InPageOrder.Select(family => family.Name),
            Regex.Matches(table, "<tr data-family=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.Equal(
            [
                "<tr data-family=\"pullback\" data-state=\"live\" data-live-since=\"2026-09-25\" data-variants=\"2\" data-listed=\"5\" data-trades=\"6\" data-open=\"6\" data-finished=\"0\">",
                "<tr data-family=\"breakout\" data-state=\"provisional\" data-live-since=\"none\" data-variants=\"0\" data-listed=\"2\" data-trades=\"3\" data-open=\"2\" data-finished=\"1\">",
            ],
            Regex.Matches(table, "<tr data-family=\"(?:pullback|breakout)\"[^>]*>").Select(match => match.Value));
        Assert.Contains("<td class=\"family-record\">provisional: not yet frozen; its record starts at the freeze</td>", table, StringComparison.Ordinal);
        Assert.Contains(
            FormattableString.Invariant($"<td class=\"family-record\">0 decided of the {ReasonVerdict.MinimumResolved} its record waits for, on 0 of {ReasonVerdict.MinimumSessions} nights</td>"),
            table,
            StringComparison.Ordinal);
        Assert.Contains("<td>live since 2026-09-25</td>", table, StringComparison.Ordinal);
    }
}
