using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Returns;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the 12.2 correction on the operator's ruling of 2026-09-27: the Past picks screen, a name's
// "On the list before" and the trade line, read off the rendered pages over constructed stores. A trade is a
// name the swing filter passed on a night it listed, on the plan that night's trade gate read, so the store
// holds a version 2 night reading the nearest bands and a version 3 night reading section 10's plan, each
// row carrying both plans with an outcome on each, a member one gate short with a plan and an outcome, and
// an evening the reasons listed with a row the filter passed.
// see: Every trade the live list recommended is shown, and their share waits for the minimum the reason records wait for
public partial class ReadSurface
{
    // The claims the screen, the region, the mark and section 18's two rows make, which this check reaches
    // and which the phase 12 pair names as landed beyond its prediction.
    internal static readonly string[] PastPicksClaims =
    [
        CheckReach.Key("15.5 The mark vocabulary", "Trade line"),
        CheckReach.Key("15.9 Name", "On the list before"),
        CheckReach.Key("15.17 Past picks", "How the list's picks have done, the trades listed with the nights they were listed on"),
        CheckReach.Key("15.17 Past picks", "How the list's picks have done, how many are still open and how many finished"),
        CheckReach.Key("15.17 Past picks", "How the list's picks have done, how many reached the target and how many were stopped out or ran out of time"),
        CheckReach.Key("15.17 Past picks", "How the list's picks have done, a line saying how many carry no outcome row"),
        CheckReach.Key("15.17 Past picks", "How the list's picks have done, below the minimum a dashed outline stating the trades decided at the target or the stop and their listing nights against the numbers needed with no share"),
        CheckReach.Key("15.17 Past picks", "How the list's picks have done, at or above it a bar of the finished trades in three steps of one neutral hue with a line at the decided trades' average break-even"),
        CheckReach.Key("15.17 Past picks", "How the list's picks have done, the share that reached the target first beside the share needed to break even and the average result in multiples of the risk taken, the three always together"),
        CheckReach.Key("15.17 Past picks", "How the list's picks have done, a key saying how to read it"),
        CheckReach.Key("15.17 Past picks", "Filters, a chip for all and one for each status a trade can stand in with its count"),
        CheckReach.Key("15.17 Past picks", "Filters, none by setup"),
        CheckReach.Key("15.17 Past picks", "Every trade, newest first"),
        CheckReach.Key("15.17 Past picks", "Every trade, a line above the rows stating how many are shown of how many were listed"),
        CheckReach.Key("15.17 Past picks", "Every trade, the night listed"),
        CheckReach.Key("15.17 Past picks", "Every trade, the stock with a link to its page for that night"),
        CheckReach.Key("15.17 Past picks", "Every trade, the buy and the stop and the target"),
        CheckReach.Key("15.17 Past picks", "Every trade, the trade line"),
        CheckReach.Key("15.17 Past picks", "Every trade, the status in words"),
        CheckReach.Key("15.17 Past picks", "Every trade, the sessions held to its resolution or to the newest night"),
        CheckReach.Key("15.17 Past picks", "Every trade, the result as a signed multiple of the risk or open"),
        CheckReach.Key("15.17 Past picks", "Every trade, a key saying how to read the trade line and that only the live list's trades appear with the alternatives hidden until one is promoted"),
        CheckReach.Key(Scope.FailureTable, "No trade listed yet"),
        CheckReach.Key(Scope.FailureTable, "A trade whose outcome row is missing"),
    ];

    const string PicksReasonsEvening = "2026-09-23";
    const string PicksVersionTwoNight = "2026-09-24";
    const string PicksVersionThreeNight = "2026-09-28";
    const string PicksNewest = "2026-09-30";

    // One member's swing filter row on a night: every gate as given, both swing plans entered at the
    // night's close, and the rank among the names passing.
    static void PickGate(TemporaryStore store, string session, string version, string ticker, bool passed, int? rank, string entry, string swingStop, string swingTarget, string clearStop, string clearTarget)
    {
        store.Execute(
            "INSERT OR IGNORE INTO membership (index_code, ticker, joined, \"left\", observed_at, name) " +
            $"VALUES ('GSPC', '{ticker}', '2025-01-02', NULL, '2025-01-02T00:00:00Z', '{ticker} Company');");
        store.Execute(
            "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, family, trigger_pass, trigger_event, trade, " +
            "swing_entry, swing_stop, swing_target, clear_stop, clear_target, exclusions, passed, rank, gates) " +
            $"VALUES ('{ticker}', '{session}', '{version}', 'code', 1, 1, 1, 'pullback', {(passed ? 1 : 0)}, 1, 1, " +
            $"'{entry}', '{swingStop}', '{swingTarget}', '{clearStop}', '{clearTarget}', '[]', {(passed ? 1 : 0)}, {(rank is { } at ? at.ToString(CultureInfo.InvariantCulture) : "NULL")}, '{{\"gates\":[],\"notes\":[]}}');");
    }

    // A plan's forward return on one horizon: an outcome with the session it resolved on, the return from the
    // buy and the bar the plan set, or none of them while the trade is open.
    static void PickOutcome(TemporaryStore store, string session, string ticker, string horizon, string? outcome, string? resolvedOn = null, double? returned = null, double? breakEven = null) =>
        store.Execute(
            "INSERT INTO forward_return (ticker, session_date, horizon, outcome, resolved_on, return_pct, base_rate, break_even) " +
            $"VALUES ('{ticker}', '{session}', '{horizon}', {Quoted(outcome)}, {Quoted(resolvedOn)}, {Real(returned)}, NULL, {Real(breakEven)});");

    static void PickBar(TemporaryStore store, string ticker, string session, string close) =>
        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
            $"VALUES ('{ticker}', '{session}', '{close}', '{close}', '{close}', '{close}', 1000, 'test', '{session}T21:00:00Z', '{close}');");

    static void PickListing(TemporaryStore store, string ticker, string session) =>
        store.Execute(
            "INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons, band_strength) " +
            $"VALUES ('{ticker}', '{session}', '{ReasonsJson(0)}', 0, '{{}}', '{{\"candidates\":[],\"skipped\":[]}}', 0);");

    static string Quoted(string? text) => text is null ? "NULL" : $"'{text}'";

    static string Real(double? value) => value is { } held ? held.ToString("R", CultureInfo.InvariantCulture) : "NULL";

    // The constructed store. Version 2 reads the plan at the nearest bands and version 3 section 10's plan.
    //
    // The version 2 night, every plan entered at 100 with the nearest bands at 96 and 110 and section 10's at
    // 98 and 108: PA first on the list, reaching the nearest bands' target on 2026-09-28 at +10%, and PB second,
    // stopped out on 2026-09-25 at -5%, each with section 10's plan decided the other way, which is a
    // candidate's plan on that night; and PF, one gate short, whose plan reached its target.
    //
    // The version 3 night, every plan entered at 50 with the nearest bands at 48 and 56 and section 10's at 47
    // and 58: PC first, open, closing at 52 on the newest night; PD second, run out of time on 2026-09-30 at +2%;
    // and PE third, whose section 10 outcome row is missing. Each carries a decided outcome on the nearest
    // bands' plan, which is a candidate's plan from version 3.
    //
    // And PG, which the filter passed on an evening the reasons listed, whose plan reached its target.
    static TemporaryStore PicksStore()
    {
        var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO filter_version (version, settings, opened_at, closed_at, evidence) VALUES " +
            "('2', '{\"trade\":\"swing\"}', '2026-09-20T00:00:00Z', '2026-09-27T00:00:00Z', 'test'), " +
            "('3', '{\"trade\":\"clear\"}', '2026-09-27T00:00:00Z', NULL, 'test');");
        store.Execute($"INSERT INTO list_rule (session_date, rule) VALUES ('{PicksVersionTwoNight}', 'filter'), ('{PicksVersionThreeNight}', 'filter');");

        PickGate(store, PicksVersionTwoNight, "2", "PA", passed: true, 1, "100", "96", "110", "98", "108");
        PickGate(store, PicksVersionTwoNight, "2", "PB", passed: true, 2, "100", "96", "110", "98", "108");
        PickGate(store, PicksVersionTwoNight, "2", "PF", passed: false, null, "100", "96", "110", "98", "108");
        PickOutcome(store, PicksVersionTwoNight, "PA", ForwardReturnSeries.Swing, ForwardReturnSeries.Win, "2026-09-28", 10, 28.5714);
        PickOutcome(store, PicksVersionTwoNight, "PA", ForwardReturnSeries.Clear, ForwardReturnSeries.Loss, "2026-09-25", -2, 25);
        PickOutcome(store, PicksVersionTwoNight, "PB", ForwardReturnSeries.Swing, ForwardReturnSeries.Loss, "2026-09-25", -5, 28.5714);
        PickOutcome(store, PicksVersionTwoNight, "PB", ForwardReturnSeries.Clear, ForwardReturnSeries.Win, "2026-09-28", 8, 25);
        PickOutcome(store, PicksVersionTwoNight, "PF", ForwardReturnSeries.Swing, ForwardReturnSeries.Win, "2026-09-28", 10, 28.5714);

        PickGate(store, PicksVersionThreeNight, "3", "PC", passed: true, 1, "50", "48", "56", "47", "58");
        PickGate(store, PicksVersionThreeNight, "3", "PD", passed: true, 2, "50", "48", "56", "47", "58");
        PickGate(store, PicksVersionThreeNight, "3", "PE", passed: true, 3, "50", "48", "56", "47", "58");
        PickOutcome(store, PicksVersionThreeNight, "PC", ForwardReturnSeries.Clear, null);
        PickOutcome(store, PicksVersionThreeNight, "PC", ForwardReturnSeries.Swing, ForwardReturnSeries.Loss, "2026-09-29", -4, 25);
        PickOutcome(store, PicksVersionThreeNight, "PD", ForwardReturnSeries.Clear, ForwardReturnSeries.Unresolved, "2026-09-30", 2, 27.2727);
        PickOutcome(store, PicksVersionThreeNight, "PD", ForwardReturnSeries.Swing, ForwardReturnSeries.Win, "2026-09-30", 12, 25);
        PickOutcome(store, PicksVersionThreeNight, "PE", ForwardReturnSeries.Swing, ForwardReturnSeries.Win, "2026-09-29", 12, 25);

        PickGate(store, PicksReasonsEvening, "2", "PG", passed: true, 1, "100", "96", "110", "98", "108");
        PickOutcome(store, PicksReasonsEvening, "PG", ForwardReturnSeries.Swing, ForwardReturnSeries.Win, "2026-09-25", 10, 28.5714);

        foreach (var (ticker, session, close) in new[]
        {
            ("PA", PicksVersionTwoNight, "100"), ("PA", "2026-09-25", "103"), ("PA", PicksVersionThreeNight, "111"), ("PA", PicksNewest, "112"),
            ("PB", PicksVersionTwoNight, "100"), ("PC", PicksVersionThreeNight, "50"), ("PC", PicksNewest, "52"),
            ("PD", PicksVersionThreeNight, "50"), ("PE", PicksVersionThreeNight, "50"),
        })
        {
            PickBar(store, ticker, session, close);
        }

        foreach (var session in new[] { PicksVersionTwoNight, "2026-09-25", PicksVersionThreeNight, PicksNewest })
        {
            PickListing(store, "PA", session);
        }

        return store;
    }

    // One row of a table the screen or the region draws, from its opening tag to its close.
    static string PickRowOf(string page, string ticker, string night)
    {
        var at = page.IndexOf($"<tr data-ticker=\"{ticker}\" data-night=\"{night}\"", StringComparison.Ordinal);

        Assert.True(at >= 0, $"no row for {ticker} listed on {night}");

        return page[at..page.IndexOf("</tr>", at, StringComparison.Ordinal)];
    }

    // The screen over the constructed store, every row and figure read off the rendered page and worked by
    // hand above each assertion.
    [Fact]
    public async Task ThePastPicksScreenFollowsEveryTradeTheLiveListRecommendedOnThePlanItsNightTraded()
    {
        using var store = PicksStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks"));

        // Five trades over two nights: PA and PB on the version 2 night and PC, PD and PE on the version 3
        // night. PF was one gate short and PG was passed on an evening the reasons listed, so neither is a
        // trade the list recommended.
        Assert.Contains($"<section class=\"picks\" data-night=\"{PicksNewest}\" data-universe=\"500\" data-trades=\"5\" data-shown=\"5\" data-status=\"all\">", page, StringComparison.Ordinal);
        Assert.Contains("<div class=\"screen-mast\" data-title=\"Past picks: S&P 500\"><span class=\"m-screen\">Past picks: S&P 500</span>", page, StringComparison.Ordinal);
        Assert.Contains($"Every trade the S&P 500's lists recommended, as of the close of {PicksNewest}", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ticker=\"PF\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ticker=\"PG\"", page, StringComparison.Ordinal);

        // The counts: PC open; PA reached its target, PB stopped out and PD ran out of time, three finished;
        // PE carries no outcome row, so it is listed and in no status.
        Assert.Contains("<dl class=\"counts\" data-listed=\"5\" data-nights=\"2\" data-open=\"1\" data-finished=\"3\" data-target=\"1\" data-stopped=\"1\" data-time=\"1\" data-missing=\"1\">", page, StringComparison.Ordinal);
        Assert.Contains("<div><dt>Trades listed</dt><dd>5<span class=\"grp\">over 2 nights</span></dd></div>", page, StringComparison.Ordinal);

        foreach (var (words, count) in new[] { ("Still open", 1), ("Finished", 3), ("Reached target", 1), ("Stopped out", 1), ("Ran out of time", 1) })
        {
            Assert.Contains($"<div><dt>{words}</dt><dd>{count}</dd></div>", page, StringComparison.Ordinal);
        }

        Assert.Contains("<p class=\"degraded\" data-missing=\"1\">1 trade has no outcome row stored, so it counts as listed and in no status.</p>", page, StringComparison.Ordinal);

        // Two decided at the target or the stop, PA and PB, both listed on the one version 2 night: far below
        // both minimums, so the dashed outline and no share.
        Assert.Contains($"<p class=\"not-yet-rate\" data-decided=\"2\" data-needed=\"{ReasonVerdict.MinimumResolved}\" data-decided-nights=\"1\" data-nights-needed=\"{ReasonVerdict.MinimumSessions}\">", page, StringComparison.Ordinal);
        Assert.Contains($"<b>2</b> trades decided at the target or the stop of the <b>{ReasonVerdict.MinimumResolved}</b> needed, over <b>1</b> of the <b>{ReasonVerdict.MinimumSessions}</b> listing nights needed.", page, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"rate\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("Reached target first", page, StringComparison.Ordinal);

        // The chips: all and the four statuses, each with its count, and no other.
        Assert.Equal(
            ["all", PickStatus.Open, PickStatus.Target, PickStatus.Stopped, PickStatus.Time],
            Regex.Matches(page, "<a class=\"chip\" data-filter=\"([^\"]+)\" data-value=\"([^\"]+)\"").Select(match => match.Groups[1].Value == "status" ? match.Groups[2].Value : "not a status"));
        Assert.Contains("<a class=\"chip\" data-filter=\"status\" data-value=\"all\" aria-pressed=\"true\" href=\"#/picks\">All<span class=\"n\">5</span></a>", page, StringComparison.Ordinal);

        foreach (var (status, words) in new[] { (PickStatus.Open, "Still open"), (PickStatus.Target, "Reached target"), (PickStatus.Stopped, "Stopped out"), (PickStatus.Time, "Ran out of time") })
        {
            Assert.Contains($"<a class=\"chip\" data-filter=\"status\" data-value=\"{status}\" aria-pressed=\"false\" href=\"#/picks?status={status}\">{words}<span class=\"n\">1</span></a>", page, StringComparison.Ordinal);
        }

        Assert.Contains("<p class=\"list-count\" data-shown=\"5\" data-trades=\"5\">Showing 5 of 5 trades</p>", page, StringComparison.Ordinal);

        // Newest night first and the list's own order within it.
        Assert.Equal(
            [("PC", PicksVersionThreeNight), ("PD", PicksVersionThreeNight), ("PE", PicksVersionThreeNight), ("PA", PicksVersionTwoNight), ("PB", PicksVersionTwoNight)],
            Regex.Matches(page, "<tr data-ticker=\"([^\"]+)\" data-night=\"([^\"]+)\"").Select(match => (match.Groups[1].Value, match.Groups[2].Value)));

        // Each row on the plan its night's trade gate read: section 10's on the version 3 night and the
        // nearest bands' on the version 2 night, never the other.
        foreach (var (ticker, night, plan, prices) in new[]
        {
            ("PC", PicksVersionThreeNight, "clear", "data-buy=\"50\">50.00</td><td class=\"r num\" data-stop=\"47\">47.00</td><td class=\"r num\" data-target=\"58\">58.00</td>"),
            ("PD", PicksVersionThreeNight, "clear", "data-buy=\"50\">50.00</td><td class=\"r num\" data-stop=\"47\">47.00</td><td class=\"r num\" data-target=\"58\">58.00</td>"),
            ("PE", PicksVersionThreeNight, "clear", "data-buy=\"50\">50.00</td><td class=\"r num\" data-stop=\"47\">47.00</td><td class=\"r num\" data-target=\"58\">58.00</td>"),
            ("PA", PicksVersionTwoNight, "swing", "data-buy=\"100\">100.00</td><td class=\"r num\" data-stop=\"96\">96.00</td><td class=\"r num\" data-target=\"110\">110.00</td>"),
            ("PB", PicksVersionTwoNight, "swing", "data-buy=\"100\">100.00</td><td class=\"r num\" data-stop=\"96\">96.00</td><td class=\"r num\" data-target=\"110\">110.00</td>"),
        })
        {
            var row = PickRowOf(page, ticker, night);

            Assert.Contains($"data-plan=\"{plan}\"", row, StringComparison.Ordinal);
            Assert.Contains($"<td class=\"num\">{night}</td><td class=\"c-nm\"><a class=\"nm\" href=\"#/name/{ticker}/{night}\"><span class=\"tk\">{ticker}</span><span class=\"co\">{ticker} Company</span></a></td>", row, StringComparison.Ordinal);
            Assert.Contains("<td class=\"r num\" " + prices, row, StringComparison.Ordinal);
        }

        // What became of each, the sessions each was held on the exchange's calendar and its result as the
        // stored return over the risk as a share of the buy:
        // PC open from 2026-09-28 to the newest night, 2026-09-29 and 2026-09-30, 2 sessions;
        // PD ran out of time on 2026-09-30, 2 sessions, +2% over a risk of 3 in 50, 6%, is +0.33;
        // PE has no outcome row, held 2 sessions to the newest night and no result;
        // PA reached its target on 2026-09-28, 2 sessions, +10% over a risk of 4 in 100, 4%, is +2.50;
        // PB stopped out on 2026-09-25, 1 session, -5% over 4% is -1.25, a close through the stop.
        foreach (var (ticker, night, status, sessions, result) in new[]
        {
            ("PC", PicksVersionThreeNight, "Open", 2, "<td class=\"r res open\" data-result=\"open\">open</td>"),
            ("PD", PicksVersionThreeNight, "Ran out of time", 2, "+0.33 ×</td>"),
            ("PE", PicksVersionThreeNight, "No outcome stored", 2, "<td class=\"r res\" data-result=\"none\"><span class=\"degraded\">none</span></td>"),
            ("PA", PicksVersionTwoNight, "Reached target", 2, "+2.50 ×</td>"),
            ("PB", PicksVersionTwoNight, "Stopped out", 1, "-1.25 ×</td>"),
        })
        {
            var row = PickRowOf(page, ticker, night);

            Assert.Contains($"<td class=\"status\">{status}</td>", row, StringComparison.Ordinal);
            Assert.Contains($"<td class=\"r num\" data-sessions=\"{sessions}\">{sessions}</td>", row, StringComparison.Ordinal);
            Assert.Contains(result, row, StringComparison.Ordinal);
        }

        // The trade lines, each placed by ratios to the buy along the line from 10 at the stop to 150 at the
        // target. PC: stop 47 and target 58 on a buy of 50 are 0.94 and 1.16, the buy sits at 10 + 0.06/0.22 of
        // 140, 48.18, and its close of 52 on the newest night over the listing's 50 is 1.04, at 73.64, hollow.
        // PD finished at +2%, 1.02, at 60.91, filled. PA finished at +10%, its target, at 150, and PB at -5%,
        // below its stop at 0.96, at 10, both filled. PE draws the line and no dot and says why.
        var lines = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var ticker in new[] { "PC", "PD", "PE", "PA", "PB" })
        {
            lines[ticker] = Assert.Single(Blocks(PickRowOf(page, ticker, ticker is "PA" or "PB" ? PicksVersionTwoNight : PicksVersionThreeNight), "<svg class=\"trade-line\".*?</svg>"));
        }

        Assert.Contains("<line class=\"tl-buy\" x1=\"48.18\"", lines["PC"], StringComparison.Ordinal);
        Assert.Contains("data-dot=\"open\"", lines["PC"], StringComparison.Ordinal);
        Assert.Contains("<circle class=\"tl-open\" cx=\"73.64\" cy=\"15\" r=\"4.5\"/>", lines["PC"], StringComparison.Ordinal);
        Assert.Contains($"<title>Stop 47.00, buy 50.00, target 58.00; the close of {PicksNewest}, 52.00</title>", lines["PC"], StringComparison.Ordinal);
        Assert.Contains("<circle class=\"tl-done\" cx=\"60.91\" cy=\"15\" r=\"4.5\"/>", lines["PD"], StringComparison.Ordinal);
        Assert.Contains("<title>Stop 47.00, buy 50.00, target 58.00; ran out of time on 2026-09-30, +2.00% from the buy</title>", lines["PD"], StringComparison.Ordinal);
        Assert.Contains("<circle class=\"tl-done\" cx=\"150\" cy=\"15\" r=\"4.5\"/>", lines["PA"], StringComparison.Ordinal);
        Assert.Contains("<circle class=\"tl-done\" cx=\"10\" cy=\"15\" r=\"4.5\"/>", lines["PB"], StringComparison.Ordinal);
        Assert.Contains("data-dot=\"none\"", lines["PE"], StringComparison.Ordinal);
        Assert.DoesNotContain("<circle", lines["PE"], StringComparison.Ordinal);
        Assert.Contains(">no outcome stored</text>", lines["PE"], StringComparison.Ordinal);

        foreach (var line in lines.Values)
        {
            Assert.Contains("<line class=\"tl-stop\" x1=\"10\"", line, StringComparison.Ordinal);
            Assert.Contains("<line class=\"tl-target\" x1=\"150\"", line, StringComparison.Ordinal);
        }

        // Both keys, each closing on what to take from it.
        Assert.Contains("<b>How to read it.</b> A trade is listed on the night the live list drew it", page, StringComparison.Ordinal);
        Assert.Contains("<b>How to read the trade line.</b> The line runs from the stop on the left, in green, to the target on the right, in orange, with the buy marked between them.", page, StringComparison.Ordinal);
        Assert.Contains("<p class=\"take\"><b>What to take from it.</b> Only the live list's trades appear here, each under the setup that listed it. The alternatives being tested in the background stay hidden until one of them is promoted.</p>", page, StringComparison.Ordinal);
        Assert.Contains($"<span class=\"stamp computed\">Computed for {PicksNewest}</span>", page, StringComparison.Ordinal);

        // A status in the hash lights its chip and draws its trades alone, and the line counts them.
        var stopped = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks?status=stopped"));

        Assert.Contains("data-trades=\"5\" data-shown=\"1\" data-status=\"stopped\"", stopped, StringComparison.Ordinal);
        Assert.Equal(["PB"], Regex.Matches(stopped, "<tr data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.Contains("<a class=\"chip\" data-filter=\"status\" data-value=\"stopped\" aria-pressed=\"true\"", stopped, StringComparison.Ordinal);
        Assert.Contains("Showing 1 of 5 trades", stopped, StringComparison.Ordinal);

        // The masthead offers the screen between the universe and the researched names, and the shell asks
        // for it with the hash's filter.
        var shell = await client.GetStringAsync("/");

        Assert.Contains($"<a href=\"{SinglePageApp.UniverseRoute}\" data-view=\"universe\">Universe</a><a href=\"{SinglePageApp.PicksRoute}\" data-view=\"picks\">Past picks</a><a href=\"{SinglePageApp.ResearchedRoute}\" data-view=\"researched\">Researched</a>", shell, StringComparison.Ordinal);
        Assert.Contains("const picks = await fetch('/screens/picks' + (query ? '?' + query : ''));", shell, StringComparison.Ordinal);
    }

    // A store of decided trades spread over the nights named, each night listed by the filter under a
    // version reading the nearest bands, every plan entered at 100 with its stop at 96 and its target at 110.
    // Two in every five decided reach the target at +8% on a bar of 25 and the rest stop out at -4% on a bar
    // of 35, and twenty more on the first night run out of time at +1%.
    static TemporaryStore MinimumStore(int nights, int decided)
    {
        var store = new TemporaryStore().Migrated();
        var sessions = new List<DateOnly>();

        for (var day = new DateOnly(2026, 1, 2); sessions.Count < nights; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && ExchangeClosures.IsSession(day))
            {
                sessions.Add(day);
            }
        }

        string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        store.Execute("INSERT INTO filter_version (version, settings, opened_at, closed_at, evidence) VALUES ('2', '{\"trade\":\"swing\"}', '2026-01-01T00:00:00Z', NULL, 'test');");
        store.Execute("INSERT INTO list_rule (session_date, rule) VALUES " + string.Join(", ", sessions.Select(day => $"('{Day(day)}', 'filter')")) + ";");

        var gates = new StringBuilder();
        var outcomes = new StringBuilder();

        for (var at = 0; at < decided + 20; at++)
        {
            var night = Day(at < decided ? sessions[at % nights] : sessions[0]);
            var (outcome, returned, bar) = at >= decided ? ("unresolved", "1", "30") : at % 5 < 2 ? ("win", "8", "25") : ("loss", "-4", "35");

            gates.Append(gates.Length == 0 ? string.Empty : ", ").Append(CultureInfo.InvariantCulture, $"('T{at:000}', '{night}', '2', 'code', 1, 1, 1, 1, 1, '100', '96', '110', '[]', 1, {at + 1}, '{{}}')");
            outcomes.Append(outcomes.Length == 0 ? string.Empty : ", ").Append(CultureInfo.InvariantCulture, $"('T{at:000}', '{night}', 'swing', '{outcome}', '{night}', {returned}, NULL, {bar})");
        }

        store.Execute("INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, trigger_pass, trade, swing_entry, swing_stop, swing_target, exclusions, passed, rank, gates) VALUES " + gates + ";");
        store.Execute("INSERT INTO forward_return (ticker, session_date, horizon, outcome, resolved_on, return_pct, base_rate, break_even) VALUES " + outcomes + ";");
        PickListing(store, "T000", Day(sessions[^1]));

        return store;
    }

    // The share, the bar and the average result wait for both of the run page's minimums, read from the
    // same constants: one decided trade short of the count draws the dashed outline and no share, one night
    // short does too, and both met draw the three figures together, each worked by hand.
    [Fact]
    public async Task ThePastPicksShareIsDrawnOnlyOnceTheDecidedTradesAndTheirNightsReachTheRunPagesMinimum()
    {
        // The arithmetic below is worked at the two figures section 17 states.
        Assert.Equal((250, 60), (ReasonVerdict.MinimumResolved, ReasonVerdict.MinimumSessions));

        foreach (var (nights, decided) in new[] { (60, 249), (59, 250) })
        {
            using var shortStore = MinimumStore(nights, decided);
            using var shortHost = new Host(shortStore.Root);
            using var shortClient = shortHost.CreateClient();

            var below = WebUtility.HtmlDecode(await shortClient.GetStringAsync("/screens/picks"));

            Assert.Contains($"<b>{decided}</b> trades decided at the target or the stop of the <b>250</b> needed, over <b>{nights}</b> of the <b>60</b> listing nights needed.", below, StringComparison.Ordinal);
            Assert.DoesNotContain("class=\"rate\"", below, StringComparison.Ordinal);
            Assert.DoesNotContain("Reached target first", below, StringComparison.Ordinal);
        }

        using var store = MinimumStore(60, 250);
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks"));

        // 250 decided over 60 nights: 100 reached the target and 150 stopped out, and 20 ran out of time.
        // The share is 100 of the 250, 40.0%, the time-outs in no share. The bar each needed is 25 for the 100
        // and 35 for the 150, a mean of 31.0. Each result is the return over the 4% risked: +2 for a target,
        // -1 for a stop and +0.25 for a time-out, a mean over the 270 finished of 55/270, +0.20.
        Assert.DoesNotContain("not-yet-rate", page, StringComparison.Ordinal);
        Assert.Contains("<dl class=\"counts\" data-listed=\"270\" data-nights=\"60\" data-open=\"0\" data-finished=\"270\" data-target=\"100\" data-stopped=\"150\" data-time=\"20\" data-missing=\"0\">", page, StringComparison.Ordinal);
        Assert.Contains("<div><dt>Reached target first</dt><dd>40.0%</dd><small>100 of the 250 decided at the target or the stop</small></div>", page, StringComparison.Ordinal);
        Assert.Contains("<div><dt>Needed to break even</dt><dd>31.0%</dd><small>the mean of each decided trade's own bar</small></div>", page, StringComparison.Ordinal);
        Assert.Contains("<div><dt>Average result</dt><dd>+0.20 × risk</dd><small>per finished trade</small></div>", page, StringComparison.Ordinal);

        // The bar over 620: the 100 at 620 × 100/270, 229.63, the 150 at 620 × 150/270, 344.44, the 20 after
        // them, and the line at 31% of the 574.07 the decided trades fill, 177.96.
        Assert.Contains("<rect class=\"rb-t\" x=\"0\" y=\"8\" width=\"229.63\" height=\"18\"/>", page, StringComparison.Ordinal);
        Assert.Contains("<rect class=\"rb-s\" x=\"229.63\" y=\"8\" width=\"344.44\" height=\"18\"/>", page, StringComparison.Ordinal);
        Assert.Contains("<rect class=\"rb-o\" x=\"574.07\" y=\"8\" width=\"45.93\" height=\"18\"/>", page, StringComparison.Ordinal);
        Assert.Contains("<line class=\"rb-even\" x1=\"177.96\" y1=\"2\" x2=\"177.96\" y2=\"32\"/>", page, StringComparison.Ordinal);
        Assert.Contains("break-even 31.0%</text>", page, StringComparison.Ordinal);
    }

    // A name's own page: the region after the plan for a name the live list picked before the page's night,
    // each trade as it stood on that night, and none for a name never picked before it.
    [Fact]
    public async Task ANamesPageDrawsTheNightsTheLiveListPickedItBeforeAsTheyStoodOnThePagesNight()
    {
        using var store = PicksStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // Tonight, 2026-09-30: PA's pick of 2026-09-24 reached its target on 2026-09-28.
        var tonight = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/PA"));
        var region = Assert.Single(Blocks(tonight, "<section class=\"card\" id=\"on-the-list-before\".*?</section>"));

        Assert.Contains("<p class=\"picked\" data-ticker=\"PA\" data-picked=\"1\" data-repeats=\"0\">Picked once: reached target once.</p>", region, StringComparison.Ordinal);

        var row = PickRowOf(region, "PA", PicksVersionTwoNight);

        Assert.Contains($"<td class=\"num\"><a href=\"#/name/PA/{PicksVersionTwoNight}\">{PicksVersionTwoNight}</a></td>", row, StringComparison.Ordinal);
        Assert.Contains("<td class=\"status\">Reached target</td>", row, StringComparison.Ordinal);
        Assert.Contains("+2.50 ×</td>", row, StringComparison.Ordinal);
        Assert.Contains("<p class=\"take\"><b>What to take from it.</b>", region, StringComparison.Ordinal);

        // It stands after the plan and its earnings reactions in the contents as on the page.
        var titles = ContentsTitles(tonight);

        Assert.True(Array.IndexOf(titles, "On the list before") > Array.IndexOf(titles, "Entry and exit plan"), string.Join(", ", titles));
        Assert.True(tonight.IndexOf("id=\"on-the-list-before\"", StringComparison.Ordinal) > tonight.IndexOf("id=\"plan\"", StringComparison.Ordinal));

        // On 2026-09-25 the same pick was open, one session held, its close of 103 over the listing's 100 at
        // 1.03 on a line from 0.96 to 1.10, at 10 + 0.07/0.14 of 140, 80, hollow.
        var earlier = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/PA/2026-09-25"));
        var then = PickRowOf(Assert.Single(Blocks(earlier, "<section class=\"card\" id=\"on-the-list-before\".*?</section>")), "PA", PicksVersionTwoNight);

        Assert.Contains("Picked once: still open once.", earlier, StringComparison.Ordinal);
        Assert.Contains("<td class=\"status\">Open</td>", then, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\" data-sessions=\"1\">1</td>", then, StringComparison.Ordinal);
        Assert.Contains("<circle class=\"tl-open\" cx=\"80\" cy=\"15\" r=\"4.5\"/>", then, StringComparison.Ordinal);

        // No region on the night of the pick itself, which is the page's subject, nor for a name one gate short
        // or passed only on an evening the reasons listed.
        Assert.DoesNotContain("on-the-list-before", await client.GetStringAsync($"/screens/name/PA/{PicksVersionTwoNight}"), StringComparison.Ordinal);
        Assert.DoesNotContain("on-the-list-before", await client.GetStringAsync("/screens/name/PF"), StringComparison.Ordinal);
        Assert.DoesNotContain("on-the-list-before", await client.GetStringAsync("/screens/name/PG"), StringComparison.Ordinal);
    }

    // The eighth mark over a full input and over each input it degrades on.
    [Fact]
    public void TheTradeLineDrawsAStopABuyATargetAndADotAndSaysWhatItLacks()
    {
        var marks = new MarkRenderer();
        var night = new DateOnly(2026, 9, 24);

        PickCell Trade(string status, decimal? buy = 100m, decimal? stop = 96m, decimal? target = 110m, double? along = 1.02) =>
            new("T", null, night, "clear", buy, stop, target, status, 1, null, along, null, null, night, 102m, null);

        // A stop at 0.96 and a target at 1.10 of the buy: the buy at 10 + 0.04/0.14 of 140, 50, and a price
        // at 1.02 at 70. Open is hollow and finished filled, whatever it finished as.
        var open = marks.TradeLine(Trade(PickStatus.Open));

        Assert.Contains("data-buy=\"100\" data-stop=\"96\" data-target=\"110\" data-along=\"1.02\" data-dot=\"open\">", open, StringComparison.Ordinal);
        Assert.Contains("<line class=\"tl-stop\" x1=\"10\" y1=\"6\" x2=\"10\" y2=\"24\"/>", open, StringComparison.Ordinal);
        Assert.Contains("<line class=\"tl-target\" x1=\"150\" y1=\"6\" x2=\"150\" y2=\"24\"/>", open, StringComparison.Ordinal);
        Assert.Contains("<line class=\"tl-buy\" x1=\"50\" y1=\"10\" x2=\"50\" y2=\"20\"/>", open, StringComparison.Ordinal);
        Assert.Contains("<circle class=\"tl-open\" cx=\"70\" cy=\"15\" r=\"4.5\"/>", open, StringComparison.Ordinal);

        foreach (var status in new[] { PickStatus.Target, PickStatus.Stopped, PickStatus.Time })
        {
            Assert.Contains("<circle class=\"tl-done\" cx=\"70\" cy=\"15\" r=\"4.5\"/>", marks.TradeLine(Trade(status)), StringComparison.Ordinal);
        }

        // A price past either end sits at that end.
        Assert.Contains("<circle class=\"tl-open\" cx=\"150\"", marks.TradeLine(Trade(PickStatus.Open, along: 1.3)), StringComparison.Ordinal);
        Assert.Contains("<circle class=\"tl-done\" cx=\"10\"", marks.TradeLine(Trade(PickStatus.Stopped, along: 0.9)), StringComparison.Ordinal);

        // The line and no dot where the outcome row is missing or an open trade has no close to place.
        var missing = marks.TradeLine(Trade(PickStatus.Missing));
        var unplaced = marks.TradeLine(Trade(PickStatus.Open, along: null));

        Assert.DoesNotContain("<circle", missing + unplaced, StringComparison.Ordinal);
        Assert.Contains("<line class=\"tl-track\"", missing, StringComparison.Ordinal);
        Assert.Contains(">no outcome stored</text>", missing, StringComparison.Ordinal);
        Assert.Contains(">no close stored to place</text>", unplaced, StringComparison.Ordinal);

        // No line where the plan lacks a price or its prices are out of order, the dashed outline saying which.
        foreach (var (cell, says) in new[]
        {
            (Trade(PickStatus.Open, stop: null), "no stop stored"),
            (Trade(PickStatus.Open, target: null), "no target stored"),
            (Trade(PickStatus.Open, buy: null), "no buy stored"),
            (Trade(PickStatus.Open, stop: 101m), "the stop, buy and target are not in order"),
        })
        {
            var drawn = marks.TradeLine(cell);

            Assert.Contains("<rect class=\"m-absent\"", drawn, StringComparison.Ordinal);
            Assert.Contains($">{says}</text>", drawn, StringComparison.Ordinal);
            Assert.DoesNotContain("tl-track", drawn, StringComparison.Ordinal);
            Assert.DoesNotContain("<circle", drawn, StringComparison.Ordinal);
        }
    }

    // A store whose filter has passed no name draws the one line section 18 states and no count, share or
    // table.
    [Fact]
    public async Task ThePastPicksScreenOverAStoreWithNoTradeSaysNoneHasBeenListed()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute($"INSERT INTO list_rule (session_date, rule) VALUES ('{PicksVersionTwoNight}', 'filter');");
        PickGate(store, PicksVersionTwoNight, "2", "PF", passed: false, null, "100", "96", "110", "98", "108");
        PickListing(store, "PF", PicksVersionTwoNight);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/picks"));

        Assert.Contains("<p class=\"degraded\" data-picks=\"none\">No trade has been listed yet. The live list recommends a trade on each night the swing filter passes a name, and every one it recommends is followed here from that night on.</p>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"counts\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("picks-table", page, StringComparison.Ordinal);
        Assert.DoesNotContain("not-yet-rate", page, StringComparison.Ordinal);
    }
}
