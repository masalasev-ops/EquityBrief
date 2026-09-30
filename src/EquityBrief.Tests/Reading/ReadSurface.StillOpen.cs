using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Returns;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, the 12.2 correction on the operator's ruling of 2026-09-30: one open trade per stock on the
// pages. Tonight's "Still open" region, the mark on a list row that repeats a trade still open, the mark and the
// counts on Past picks and a name's "On the list before", and section 18's row for a still open trade whose
// outcome row is missing, each read off the rendered page over a constructed store.
// see: A stock holds one open trade on each rule's list, and it is free the night after its trade ends
// see: A repeat listing made before the rule reached the filter is marked and counted once
public partial class ReadSurface
{
    // The claims this correction adds: the Still open row as its nine parts, the two parts of Past picks' rows,
    // and section 18's row.
    internal static readonly string[] OpenTradeClaims =
    [
        CheckReach.Key("15.7 Tonight", "Still open, one row per stock that passed every gate on the night with nothing excluding it but an open trade while a trade the live list recommended for it on an earlier night is still open on this one"),
        CheckReach.Key("15.7 Tonight", "Still open, the night that trade was listed on"),
        CheckReach.Key("15.7 Tonight", "Still open, the trade line"),
        CheckReach.Key("15.7 Tonight", "Still open, where the price stands against that trade's stop and target"),
        CheckReach.Key("15.7 Tonight", "Still open, whether the trade ended at this night's own close and frees the stock from the next night"),
        CheckReach.Key("15.7 Tonight", "Still open, before the rule reaches the filter the stock stands on the list as well and is marked as listed again while that trade is open"),
        CheckReach.Key("15.7 Tonight", "Still open, once the rule reaches the filter the stock is excluded there and drawn here alone"),
        CheckReach.Key("15.7 Tonight", "Still open, a line where there is none"),
        CheckReach.Key("15.7 Tonight", "Still open, a key saying it is not a new trade"),
        CheckReach.Key("15.17 Past picks", "How the list's picks have done, a line saying how many were listed again while an earlier trade was open, drawn and not counted"),
        CheckReach.Key("15.17 Past picks", "Every trade, a repeat listing made while the trade from an earlier night was open marked as listed again and counted once"),
        CheckReach.Key(Scope.FailureTable, "A still open trade whose outcome row is missing"),
    ];

    // The earlier night the trades were listed on, three sessions before the switch night the pages draw.
    const string EarlierNight = "2026-09-29";

    // Both nights listed by the filter under a version reading section 10's plan, every plan entered at 100
    // with its stop at 96 and its target at 110, and each stock's trade of the earlier night in a state of its own:
    //   AG open, undecided;
    //   BS stopped out at the switch night's own close, so still open on that night;
    //   CE reached its target on 2026-10-01, so free on the switch night;
    //   DM with no outcome row stored, read as open until its sessions run out;
    //   EX open, and one gate short on the switch night, so on neither the list nor Still open.
    static TemporaryStore StillOpenStore()
    {
        var store = new TemporaryStore().Migrated();

        OpenCloseVersion(store);

        Evening(store, EarlierNight, filter: true,
        [
            new Member("AG", Passed: true, Rank: 1),
            new Member("BS", Passed: true, Rank: 2),
            new Member("CE", Passed: true, Rank: 3),
            new Member("DM", Passed: true, Rank: 4),
            new Member("EX", Passed: true, Rank: 5),
        ]);
        Evening(store, TheSwitch, filter: true,
        [
            new Member("AG", Passed: true, Rank: 1),
            new Member("BS", Passed: true, Rank: 2),
            new Member("CE", Passed: true, Rank: 3),
            new Member("DM", Passed: true, Rank: 4),
            new Member("EX", Trend: false),
        ]);
        store.Execute("UPDATE gate_result SET clear_stop = '96', clear_target = '110';");

        PickOutcome(store, EarlierNight, "AG", ForwardReturnSeries.Clear, null);
        PickOutcome(store, EarlierNight, "BS", ForwardReturnSeries.Clear, ForwardReturnSeries.Loss, TheSwitch, -5, 28.5714);
        PickOutcome(store, EarlierNight, "CE", ForwardReturnSeries.Clear, ForwardReturnSeries.Win, "2026-10-01", 10, 28.5714);
        PickOutcome(store, EarlierNight, "EX", ForwardReturnSeries.Clear, null);
        // CE's second listing is a trade of its own, open; AG's, BS's and DM's second listings repeat a trade
        // still open, so they carry no outcome row and are scored as no trade.
        PickOutcome(store, TheSwitch, "CE", ForwardReturnSeries.Clear, null);

        foreach (var (ticker, close) in new[] { ("AG", "104"), ("BS", "95"), ("CE", "111"), ("DM", "101"), ("EX", "102") })
        {
            PickBar(store, ticker, EarlierNight, "100");
            PickBar(store, ticker, TheSwitch, close);
        }

        return store;
    }

    static string StillOpenRegion(string page) =>
        Assert.Single(Blocks(page, "<section class=\"still-open\".*?</section>"));

    // Tonight's page: Still open holds the stocks that passed every gate while an earlier trade is open, each
    // with where the price stands, the list marks the same rows, and a stock whose trade ended the session
    // before or that is one gate short is on neither.
    [Fact]
    public async Task TonightDrawsEachStockPassedAgainWhileItsEarlierTradeIsStillOpenAndMarksItsRowOnTheList()
    {
        using var store = StillOpenStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));
        var region = StillOpenRegion(page);

        // AG, BS and DM, in the list's own order; not CE, free again since its trade ended the session before,
        // and not EX, one gate short tonight whatever its earlier trade.
        Assert.Contains($"<section class=\"still-open\" data-night=\"{TheSwitch}\" data-count=\"3\">", region, StringComparison.Ordinal);
        Assert.Contains("3 stocks passed the swing filter tonight while a trade from an earlier night is still open.", region, StringComparison.Ordinal);
        Assert.Equal(
            [("AG", "open"), ("BS", "stopped"), ("DM", "missing")],
            Regex.Matches(region, $"<tr data-ticker=\"([A-Z]+)\" data-night=\"{EarlierNight}\" data-status=\"([a-z]+)\">").Select(match => (match.Groups[1].Value, match.Groups[2].Value)));
        Assert.DoesNotContain("data-ticker=\"CE\"", region, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ticker=\"EX\"", region, StringComparison.Ordinal);

        // Where each price stands: AG's close against the open trade's stop and target, with a hollow dot on
        // its trade line at 1.04 of the buy, 10 + 0.08/0.14 of 140, 90; BS ended at this close and is free from
        // the next night; DM's outcome row is missing, so it is read as open until its sessions run out.
        var ag = PickRowOf(region, "AG", EarlierNight);
        var bs = PickRowOf(region, "BS", EarlierNight);
        var dm = PickRowOf(region, "DM", EarlierNight);

        Assert.Contains($"<td class=\"num\">{EarlierNight}</td>", ag, StringComparison.Ordinal);
        Assert.Contains($"<a class=\"name-link\" href=\"#/name/AG/{EarlierNight}\">AG</a>", ag, StringComparison.Ordinal);
        Assert.Contains($"<td class=\"stands\" data-now=\"104\" data-stop=\"96\" data-target=\"110\">104.00 on {TheSwitch}, against a stop of 96.00 and a target of 110.00</td>", ag, StringComparison.Ordinal);
        Assert.Contains("<circle class=\"tl-open\" cx=\"90\" cy=\"15\" r=\"4.5\"/>", ag, StringComparison.Ordinal);
        Assert.Contains("stopped out at this close, against a stop of 96.00 and a target of 110.00; free again from the next night</td>", bs, StringComparison.Ordinal);
        Assert.Contains("<circle class=\"tl-done\"", bs, StringComparison.Ordinal);
        Assert.Contains("no outcome stored, against a stop of 96.00 and a target of 110.00; read as open until its sessions run out</td>", dm, StringComparison.Ordinal);
        Assert.Contains(">no outcome stored</text>", dm, StringComparison.Ordinal);

        // The list still holds every stock the filter passed, the three marked and CE not, and the card's key
        // says the stock is not a new trade.
        var list = Assert.Single(Blocks(page, "<section class=\"tonight-list\" data-list=\"listed\".*?</section>"));

        foreach (var ticker in new[] { "AG", "BS", "DM" })
        {
            Assert.Matches($"<tr data-ticker=\"{ticker}\"[^>]* data-repeat-of=\"{EarlierNight}\">", list);
            Assert.Contains($"<span class=\"listed-again\" data-repeat-of=\"{EarlierNight}\">listed again while the trade from {EarlierNight} is open</span>", PickListRow(list, ticker), StringComparison.Ordinal);
        }

        Assert.DoesNotMatch("<tr data-ticker=\"CE\"[^>]* data-repeat-of=", list);
        Assert.Contains("A stock holds one open trade at a time, so this is not a new trade", page, StringComparison.Ordinal);
        Assert.Contains("Passed again while an earlier trade is still open", page, StringComparison.Ordinal);

        // The earlier night itself holds no trade from before it, so its region says so, and an evening the
        // reasons listed draws no region at all.
        var earlier = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{EarlierNight}"));

        Assert.Contains("<p class=\"degraded\" data-still-open=\"0\">No stock the swing filter passed tonight has a trade from an earlier night still open.</p>", StillOpenRegion(earlier), StringComparison.Ordinal);
    }

    static string PickListRow(string list, string ticker)
    {
        var at = list.IndexOf($"<tr data-ticker=\"{ticker}\"", StringComparison.Ordinal);

        Assert.True(at >= 0, $"no list row for {ticker}");

        return list[at..list.IndexOf("</tr>", at, StringComparison.Ordinal)];
    }

    // Past picks and a name's page: a listing that repeated a trade still open is drawn with its mark, the
    // counts leave it out and say so, and the name page's line counts it apart.
    [Fact]
    public async Task PastPicksMarksARepeatListingAndCountsTheMoveOnce()
    {
        using var store = StillOpenStore();

        // A third night after the switch, so the name page draws the switch night's listings as earlier ones.
        PickListing(store, "AG", "2026-10-05");
        PickBar(store, "AG", "2026-10-05", "105");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks"));

        // Nine listings over the two nights: five on the earlier night, all trades, and four on the switch
        // night, of which AG, BS and DM repeat a trade still open and CE is a trade of its own. So six
        // trades over two nights: AG open, BS stopped out, CE reached its target and CE again open, DM with no
        // outcome row, EX open; three listed again, drawn and not counted.
        Assert.Contains("data-trades=\"6\" data-shown=\"9\" data-status=\"all\">", page, StringComparison.Ordinal);
        Assert.Contains("<dl class=\"counts\" data-listed=\"6\" data-nights=\"2\" data-open=\"3\" data-finished=\"2\" data-target=\"1\" data-stopped=\"1\" data-time=\"0\" data-missing=\"1\">", page, StringComparison.Ordinal);
        Assert.Contains("<p class=\"repeats\" data-repeats=\"3\">3 listings were made again while an earlier trade was open, drawn and not counted.</p>", page, StringComparison.Ordinal);
        Assert.Contains("<p class=\"list-count\" data-shown=\"9\" data-trades=\"6\" data-repeats=\"3\">Showing 9 of 9 rows: 6 trades and 3 listed again while an earlier trade was open</p>", page, StringComparison.Ordinal);

        foreach (var ticker in new[] { "AG", "BS", "DM" })
        {
            var repeat = PickRowOf(page, ticker, TheSwitch);

            Assert.Contains($"data-repeat-of=\"{EarlierNight}\">", repeat, StringComparison.Ordinal);
            Assert.Contains($"<span class=\"listed-again\" data-repeat-of=\"{EarlierNight}\">listed again while the trade from {EarlierNight} was open</span></td>", repeat, StringComparison.Ordinal);
            Assert.DoesNotContain("data-repeat-of", PickRowOf(page, ticker, EarlierNight), StringComparison.Ordinal);
        }

        Assert.DoesNotContain("data-repeat-of", PickRowOf(page, "CE", TheSwitch), StringComparison.Ordinal);

        // The status filter counts the trades alone and draws the rows of its status: three open, AG's first,
        // CE's second and EX's, the three repeats carrying no outcome row and standing in no status.
        var open = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks?status=open"));

        Assert.Contains("data-trades=\"6\" data-shown=\"3\" data-status=\"open\">", open, StringComparison.Ordinal);
        Assert.Contains($"<tr data-ticker=\"AG\" data-night=\"{EarlierNight}\"", open, StringComparison.Ordinal);
        Assert.DoesNotContain("data-repeat-of", open, StringComparison.Ordinal);

        // AG's own page, on 2026-10-05: picked once, still open, and listed again once, drawn and not counted.
        var name = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/AG"));
        var region = Assert.Single(Blocks(name, "<section class=\"card\" id=\"on-the-list-before\".*?</section>"));

        Assert.Contains("<p class=\"picked\" data-ticker=\"AG\" data-picked=\"1\" data-repeats=\"1\">Picked once: still open once; listed again once while an earlier trade was open, drawn and not counted.</p>", region, StringComparison.Ordinal);
        Assert.Contains($"data-repeat-of=\"{EarlierNight}\">", PickRowOf(region, "AG", TheSwitch), StringComparison.Ordinal);
    }

    // Section 18's row: a stock whose earlier trade has no outcome row is read as open until the cap's sessions
    // have passed, so a listing inside the cap stands on Still open and is marked, and one past the cap is a
    // trade of its own.
    [Fact]
    public async Task AStillOpenTradeWhoseOutcomeRowIsMissingIsReadAsOpenUntilItsSessionsRunOut()
    {
        using var store = new TemporaryStore().Migrated();

        OpenCloseVersion(store);

        // RM listed on 2026-06-01 with no outcome row, listed again inside the cap on 2026-06-15 and past it on
        // 2026-10-02: 63 sessions from 2026-06-01 end on 2026-09-01, so the switch night is well past the cap.
        foreach (var night in new[] { "2026-06-01", "2026-06-15", TheSwitch })
        {
            Evening(store, night, filter: true, [new Member("RM", Passed: true, Rank: 1)]);
            PickBar(store, "RM", night, "100");
        }

        store.Execute("UPDATE gate_result SET clear_stop = '96', clear_target = '110';");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var inside = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/tonight/2026-06-15"));
        var region = StillOpenRegion(inside);

        Assert.Contains("data-count=\"1\">", region, StringComparison.Ordinal);
        Assert.Contains("<tr data-ticker=\"RM\" data-night=\"2026-06-01\" data-status=\"missing\">", region, StringComparison.Ordinal);
        Assert.Contains("no outcome stored, against a stop of 96.00 and a target of 110.00; read as open until its sessions run out</td>", region, StringComparison.Ordinal);

        var past = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));

        Assert.Contains("data-still-open=\"0\"", StillOpenRegion(past), StringComparison.Ordinal);

        var picks = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks"));

        Assert.Contains("data-repeat-of=\"2026-06-01\">", PickRowOf(picks, "RM", "2026-06-15"), StringComparison.Ordinal);
        Assert.DoesNotContain("data-repeat-of", PickRowOf(picks, "RM", TheSwitch), StringComparison.Ordinal);
        Assert.Contains("<dl class=\"counts\" data-listed=\"2\"", picks, StringComparison.Ordinal);
    }
}
