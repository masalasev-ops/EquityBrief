using System.Net;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Judge;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using Xunit;

namespace EquityBrief.Tests.Reading;

// From 24.15, the operator's word of 2026-11-21: Home's picks carry Tonight's night picker, and on a past night Home draws
// that night's picks as they were listed under a banner, each card with one line on what has happened since, the waiting
// cards and the judge's line as they stood that night; tonight's Home as before.
// see: Home shows a past night's picks as they were listed with what has happened since, and tonight's as before
public partial class ReadSurface
{
    internal static string[] HomePastNightRows =>
    [
        CheckReach.Key("15.24 Home", "A past night's picks"),
        CheckReach.Key("15.24 Home", "What has happened since"),
    ];

    static PlannedTradeRow SinceRow(DateOnly? filledOn = null, decimal? fill = null, bool notFilled = false, DateOnly? endedOn = null, decimal? sale = null, string entry = PlanPoints.NextOpen) =>
        new(
            EquityBrief.Core.Candidates.FamilyRecords.LivePrefix + "the rule", "GSPC", "breakout", "AAA", new DateOnly(2026, 11, 2), 1, 50m, 47m, 56m, null, 63,
            new PlanOrder("a plan", entry, entry == PlanPoints.NextOpen ? null : 48.0, 5, PlanPoints.TypicalStop(1.5), 47, null, 2, null, [], 3, null, 63),
            filledOn, fill, notFilled, endedOn, endedOn is null ? null : 1.0, sale);

    [Fact]
    public void EachLineOnWhatHasHappenedSinceIsWorkedByHandOverATradeInEachState()
    {
        // Listed 2 Nov at an entry of 50 with its stop at 47 and its take-profit at 56: a risk of 3 and an aim of 6, so from a
        // fill of 51 its stop is 48 and its take-profit 57.
        var bought = new DateOnly(2026, 11, 3);
        var sold = new DateOnly(2026, 11, 10);

        // Open, the newest close 52.53: 52.53 / 51 = 1.03, up 3.0%.
        Assert.Equal("Bought on 3 Nov at $51.00 and open, +3.0% so far.", HomeSince.Words(SinceRow(bought, 51m), 52.53m));

        // Sold at 57.00, at its take-profit from the fill: 57 / 51 = 1.1176, up 11.8%.
        Assert.Equal("Bought on 3 Nov at $51.00; took profit on 10 Nov at $57.00, +11.8%.", HomeSince.Words(SinceRow(bought, 51m, endedOn: sold, sale: 57m), null));

        // Sold at 47.50, under its stop from the fill: 47.5 / 51 = 0.9314, down 6.9%.
        Assert.Equal("Bought on 3 Nov at $51.00; stopped out on 10 Nov at $47.50, -6.9%.", HomeSince.Words(SinceRow(bought, 51m, endedOn: sold, sale: 47.5m), null));

        // Sold at 52.00, between the two: its time limit or its give-up price, "sold".
        Assert.Equal("Bought on 3 Nov at $51.00; sold on 10 Nov at $52.00, +2.0%.", HomeSince.Words(SinceRow(bought, 51m, endedOn: sold, sale: 52m), null));

        // An order buying at 48 for 5 sessions from 2 Nov, its last day 9 Nov: never filled, and still waiting.
        Assert.Equal("Waited and was not bought, cancelled on 9 Nov.", HomeSince.Words(SinceRow(notFilled: true, entry: "2.22 typical days under the close"), null));
        Assert.Equal("Still waiting for its price, until 9 Nov.", HomeSince.Words(SinceRow(entry: "2.22 typical days under the close"), null));

        // An idea's line is read under its family and stock, of the night drawn alone.
        var lines = HomeSince.Of([SinceRow(bought, 51m), SinceRow(bought, 51m) with { Listed = new DateOnly(2026, 11, 3), Ticker = "BBB" }], new DateOnly(2026, 11, 2), new Dictionary<string, decimal> { ["AAA"] = 52.53m });

        Assert.Equal(["breakout|AAA"], lines.Keys);
    }

    [Fact]
    public async Task HomeOnAPastNightDrawsItsPicksUnderTheBannerWithWhatHappenedSinceAndTonightsAsBefore()
    {
        // The S&P 400's night of 2 Oct as PlanTradesStore lists it, a night of 5 Oct after it: M1, listed on 2 Oct to buy at
        // 48.00, filled at 48.00 on 5 Oct and closing there at 49.20, up 49.2 / 48 = 2.5% so far. The judge picked one stock
        // on 2 Oct and two on 5 Oct.
        using var store = PlanTradesStore();

        store.Execute("INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) VALUES ('ZZZ', '2026-10-05', '[]', 0, '{}', '[]');");
        store.Execute(
            "INSERT INTO trade_fill (source, index_code, owner, ticker, session_date, filled_on, fill, past, result, benchmark, members, edge, reason, filled_at) " +
            "SELECT 'index_rule_trade', 'MID', candidate, 'M1', session_date, '2026-10-05', '48', NULL, NULL, NULL, NULL, NULL, NULL, '2026-10-05T21:00:00Z' " +
            $"FROM index_rule_trade WHERE ticker = 'M1' AND session_date = '{IndexNight}';");
        store.Execute("INSERT OR REPLACE INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) VALUES ('M1', '2026-10-05', '48.5', '49.5', '47.8', '49.2', 1000, 'eod', '2026-10-05T22:00:00Z');");
        store.Execute(
            "INSERT INTO judge_run (run_id, session_date, fact_version, pin_id, seed, outcome, started_at, ended_at, screened, kept, asks, asks_failed, detail, index_code) VALUES "
            + "('judge-pick-1', '2026-10-02', 'v', 1, 1, 'picked', '2026-10-03T01:00:00Z', '2026-10-03T01:10:00Z', 4, 1, 2, 0, '', 'MID'), "
            + "('judge-pick-2', '2026-10-05', 'v', 1, 2, 'picked', '2026-10-06T01:00:00Z', '2026-10-06T01:10:00Z', 4, 2, 2, 0, '', 'MID');");
        store.Execute(
            "INSERT INTO judge_pick (session_date, rank, run_id, index_code, ticker, hold, take_profit, reason, last_close, typical_move, stop, target, give_up_distance) VALUES "
            + $"('2026-10-02', 1, 'judge-pick-1', 'MID', 'M5', '1 week', '{JudgeMenu.Whole}', 'A reason.', '100', 2.0, '98', '104', NULL), "
            + $"('2026-10-05', 1, 'judge-pick-2', 'MID', 'M6', '1 week', '{JudgeMenu.Whole}', 'A reason.', '100', 2.0, '98', '104', NULL), "
            + $"('2026-10-05', 2, 'judge-pick-2', 'MID', 'M7', '1 week', '{JudgeMenu.Whole}', 'A reason.', '100', 2.0, '98', '104', NULL);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();
        var past = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/home/{IndexNight}?universe=400"));

        // The banner with its way back, and the night picker on the night drawn.
        Assert.Contains($"<p class=\"home-past-night\" data-past-night=\"{IndexNight}\">Showing the picks of the night of 2 Oct 2026, not tonight's. <a href=\"{SinglePageApp.HomeRoute}\">Back to tonight</a></p>", past, StringComparison.Ordinal);
        Assert.Contains($"<span class=\"night-picker\" data-route=\"{SinglePageApp.HomeNightRoute}\" data-night=\"{IndexNight}\">", past, StringComparison.Ordinal);

        // M1's card as it was listed, with what happened since.
        var m1 = Assert.Single(Blocks(past, "<article class=\"home-idea fam-edge\" data-family=\"breakout\" data-ticker=\"M1\">.*?</article>"));

        Assert.Contains("<p class=\"home-since\" data-since><b>Since then:</b> Bought on 5 Oct at $48.00 and open, +2.5% so far.</p>", m1, StringComparison.Ordinal);

        // The waiting cards as they stood that night: M2's order still waiting, M1 tonight's new one and in none of them.
        Assert.Contains("<p class=\"list-count wo-title\" data-wo-title>Waiting to buy (1)</p>", past, StringComparison.Ordinal);
        Assert.Matches("<article class=\"wo-card fam-edge\" data-family=\"breakout\" data-ticker=\"M2\" data-listed=\"2026-09-30\"", past);

        // The judge's line names that night's one pick, not the newest night's two.
        Assert.Contains("<b>AI stock picker</b> Picked 1 stock that night in its own paper account.", past, StringComparison.Ordinal);

        // Tonight's Home on the same store: no banner and no line on what happened since, the picker on the newest night.
        var tonight = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/home?universe=400"));

        Assert.DoesNotContain("data-past-night", tonight, StringComparison.Ordinal);
        Assert.DoesNotContain("data-since", tonight, StringComparison.Ordinal);
        Assert.Contains("data-night=\"2026-10-05\"", tonight, StringComparison.Ordinal);

        // M1's order left the waiting list when it filled on 5 Oct: from the operator's correction of 2026-11-25 the newest night's
        // line counts its buy and Paper trading's last five trading days list it, and the night of 2 Oct, drawn as it stood
        // then, counts no buy.
        Assert.Matches("<p class=\"recent-line\" data-bought=\"1\" ", tonight);
        Assert.DoesNotMatch("<p class=\"recent-line\" data-bought=\"[1-9]", past);
        Assert.Contains("<tr data-ticker=\"M1\" data-kind=\"bought\" data-family=\"breakout\" data-on=\"2026-10-05\">", WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper?universe=400&tab=" + EquityBrief.Web.Marks.MarkRenderer.RecentTab)), StringComparison.Ordinal);

        // And on the store as PlanTradesStore lists it, Home tonight as before: the night of 2 Oct with no banner and no line.
        using var before = PlanTradesStore();
        using var beforeHost = new Host(before.Root);
        using var beforeClient = beforeHost.CreateClient();
        var asBefore = WebUtility.HtmlDecode(await beforeClient.GetStringAsync("/screens/home?universe=400"));

        Assert.Contains($"<section class=\"home\" data-universe=\"400\" data-night=\"{IndexNight}\"", asBefore, StringComparison.Ordinal);
        Assert.DoesNotContain("data-past-night", asBefore, StringComparison.Ordinal);
        Assert.DoesNotContain("data-since", asBefore, StringComparison.Ordinal);
        Assert.Contains("<h2>Tonight's ideas: 1 new, each bought only if its price dips to the buy price</h2>", asBefore, StringComparison.Ordinal);
    }
}
