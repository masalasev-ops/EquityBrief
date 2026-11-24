using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Judge;
using EquityBrief.Core.Loop;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Cards;

namespace EquityBrief.Tests.Reading;

// read-surface, the operator's correction of 2026-11-23 to Home's "Met the rules again, already held": a stock met again while
// its earlier order still waits is drawn once, on that order's card in the carousel, with the mark, the line saying its order
// stands and what tonight's rules would have placed, tonight's checklist and the standing order's plan alone; a stock met again
// while a bought trade holds it is a one-line row; "already held" names a bought trade alone; and the AI stock picker's line
// counts a night the earlier version picked across the three indices.
// see: On Home a stock its setup's rules met again is drawn once, on its waiting order's card or as a bought trade's one-line row
public partial class ReadSurface
{
    // M2's order of 30 Sep on the S&P 400's breakout, at 47.50 and not filled, waits on the night of 2 Oct, and the breakout's
    // rules met M2 again that night: its card at place nought, the plan of the night buying at 46.90. Worked by hand: Home draws
    // M2 once, as its order's card, marked, saying "Met the breakout rules again on the night of 2 Oct. Its order of 30 Sep at
    // $47.50 stands; tonight's rules would have placed it at $46.90.", its plan's buy row the order's $47.50, tonight's checklist
    // under "Why it qualified again", and no group of full cards and no "already held".
    [Fact]
    public async Task AWaitingOrderMetAgainIsDrawnOnceOnItsOrdersCardWithTheMarkAndItsStandingPlan()
    {
        using var store = PlanTradesStore();

        var plan = Doubled(new PlanOrder("2.22 typical days under the close, 1.5 typical days under the buy, the rule's sale", "2.22 typical days under the close", 46.9, 5, PlanPoints.TypicalStop(1.5), 47, null, 2, null, [], 3, null, 63).Json());
        const string lines = "[{\"line\":1,\"name\":\"Trend and strength\",\"verdict\":\"pass\",\"words\":\"Passed every step: closed above its 200-day average, 51.00 above 45.00.\"}]";

        store.Execute(
            "INSERT INTO decision_card (index_code, session_date, family, ticker, place, entry, stop, target, rule, settings, lines, record, cap, trail, plan, held_again) VALUES " +
            $"('MID', '{IndexNight}', 'breakout', 'M2', 0, '50', '47', NULL, 'The breakout on the S&P 400', '{{}}', '{lines}', NULL, 63, '3', '{plan}', '{{\"family\":\"breakout\",\"night\":\"2026-09-30\",\"index\":\"MID\"}}');");

        // From the second correction of 2026-11-23 a background version's trade in M2 of the same night, bought at the open and
        // filled, its name sorting before the live rule's: the live rule's order still waiting is the one read.
        Assert.StartsWith(EquityBrief.Core.Candidates.FamilyRecords.LivePrefix, Strings(store, "SELECT candidate FROM index_rule_trade WHERE ticker = 'M2' AND session_date = '2026-09-30';").Single(), StringComparison.Ordinal);
        store.Execute(
            "INSERT INTO index_rule_trade (candidate, index_code, family, ticker, session_date, place, entry, stop, target, trail, cap, risk_moves, reward_to_risk) VALUES " +
            "('a background breakout rule', 'MID', 'breakout', 'M2', '2026-09-30', 1, '50', '47', NULL, '3', 63, 1.5, NULL);");
        store.Execute(
            "INSERT INTO trade_fill (source, index_code, owner, ticker, session_date, filled_on, fill, past, result, benchmark, members, edge, reason, filled_at) VALUES " +
            "('index_rule_trade', 'MID', 'a background breakout rule', 'M2', '2026-09-30', '2026-10-01', '50', NULL, NULL, NULL, NULL, NULL, NULL, '2026-10-01T21:00:00Z');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var home = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/home?universe=400"));
        var card = Assert.Single(Blocks(home, "<article class=\"wo-card fam-edge\" data-family=\"breakout\" data-ticker=\"M2\".*?</article>"));

        Assert.Contains("data-met-again=\"true\"", card, StringComparison.Ordinal);
        Assert.Contains($"<b class=\"wo-again\">{HeldAgainView.AgainMark}</b>", card, StringComparison.Ordinal);
        Assert.Contains("<p class=\"wo-again-line\">Met the breakout rules again on the night of 2 Oct. Its order of 30 Sep at $47.50 stands; tonight's rules would have placed it at $46.90.</p>", card, StringComparison.Ordinal);
        Assert.Contains($"<h5>{MarkRenderer.AgainHeading}</h5>", card, StringComparison.Ordinal);
        Assert.Matches("<div data-plan=\"buy\"><span>Buy if it dips to</span><b class=\"buy\">\\$47\\.50</b>", card);
        Assert.DoesNotContain("$46.90</b>", card, StringComparison.Ordinal);

        // Once on Home: no other card or row of M2, no group of full cards, and nothing on the page calls it held or bought.
        Assert.Single(Regex.Matches(home, "data-ticker=\"M2\" data-listed=\"2026-09-30\""));
        Assert.DoesNotContain("class=\"held-card\"", home, StringComparison.Ordinal);
        Assert.DoesNotContain("data-held-again=", home, StringComparison.Ordinal);
        Assert.DoesNotContain(HeldAgainView.Label, home, StringComparison.Ordinal);
        Assert.DoesNotContain("M2 met the breakout rules again tonight. Breakout bought", home, StringComparison.Ordinal);

        // Tonight keeps its card under the breakout, labelled and said as an order not filled, never as bought or held.
        var tonight = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));
        var group = Assert.Single(Blocks(tonight, "<div class=\"held-card\" data-ticker=\"M2\".*?</p><p class=\"held-line\">.*?</p>"));

        Assert.Contains($"<b>{HeldAgainView.WaitingLabel}</b>", group, StringComparison.Ordinal);
        Assert.Contains("M2 met the breakout rules again tonight. Breakout placed its order on 30 Sep and it has not been filled, so paper trading places no second one.", group, StringComparison.Ordinal);
        Assert.Contains($"<h5>{HeldAgainView.NoteGroup} (1)</h5>", tonight, StringComparison.Ordinal);
    }

    // More than three orders draw the chips: the chip of the order whose stock met the rules again carries the mark and says so
    // to a screen reader, and no other chip does.
    [Fact]
    public void AWaitingOrdersChipCarriesTheMarkWhereItsStockMetTheRulesAgain()
    {
        var orders = SixteenOrders()[..5];
        var met = orders[2];
        var again = new DecisionCardView("MID", CarouselNight, met.Family, met.Ticker, "the rule", 100m, 95m, null, [], null)
        {
            HeldAgain = new HeldAgainView(met.Family, met.Listed, "MID"),
        };
        var html = MarkRenderer.WaitingCarousel(orders, "S&P 400", CarouselNight, again: [again]);
        var marked = Regex.Matches(html, "<button type=\"button\" role=\"listitem\" class=\"wo-chip[^\"]*\"[^>]*data-ticker=\"([^\"]+)\"[^>]*data-met-again=\"true\"[^>]*>.*?</button>");

        Assert.Equal(met.Ticker, Assert.Single(marked).Groups[1].Value);
        Assert.Contains($"<b class=\"wo-again\" title=\"{HeldAgainView.AgainMark}\">{HeldAgainView.AgainMark}</b></button>", marked[0].Value, StringComparison.Ordinal);
        Assert.Contains(", met the rules again\"", marked[0].Value, StringComparison.Ordinal);
        Assert.Equal(4, Regex.Matches(html, "class=\"wo-chip[^\"]*\"[^>]*data-met-again=\"false\"").Count);

        // An order whose card names another night is not marked.
        var elsewhere = again with { HeldAgain = new HeldAgainView(met.Family, met.Listed.AddDays(-1), "MID") };

        Assert.DoesNotContain("data-met-again=\"true\"", MarkRenderer.WaitingCarousel(orders, "S&P 400", CarouselNight, again: [elsewhere]), StringComparison.Ordinal);
    }

    // HELD's trade under the pullback, listed 1 Oct and bought at 98.50 on 2 Oct, still open when the pullback's rules met HELD
    // again on the night of 2 Oct: Home draws one row, "HELD", the pullback and the day it bought it, "Already held, not bought
    // again" and the way to its trade on Paper trading, under the heading counting one; no full card.
    [Fact]
    public async Task ABoughtTradeMetAgainIsAOneLineRowOnHome()
    {
        using var store = await FamilyPagesStore();

        store.Execute(
            "INSERT INTO family_trade (candidate, ticker, session_date, family, place, entry, stop, target, risk_moves, reward_to_risk, cap, ended_on, result, benchmark, members) VALUES " +
            $"('{EquityBrief.Core.Candidates.FamilyRecords.LivePrefix}S&P 500 pullback rule', 'HELD', '2026-10-01', 'pullback', 1, '98', '94', '106', 2.0, 2.0, 63, NULL, NULL, NULL, NULL);");
        store.Execute(
            "INSERT INTO trade_fill (source, index_code, owner, ticker, session_date, filled_on, fill, filled_at) VALUES " +
            $"('family_trade', 'GSPC', '{EquityBrief.Core.Candidates.FamilyRecords.LivePrefix}S&P 500 pullback rule', 'HELD', '2026-10-01', '2026-10-02', '98.5', '2026-10-02T23:00:00Z');");

        await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("cards-held-again");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var home = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/home"));
        var rows = Assert.Single(Blocks(home, "<div class=\"held-again held-rows\".*?</ul></div>"));
        var row = Assert.Single(Blocks(rows, "<li class=\"held-row\".*?</li>"));

        Assert.Contains($"<h5>{HeldAgainView.Group} (1)</h5>", rows, StringComparison.Ordinal);
        Assert.Contains("data-ticker=\"HELD\" data-family=\"pullback\" data-held-by=\"pullback\" data-bought=\"true\"", row, StringComparison.Ordinal);
        Assert.Contains("<span class=\"held-by\">Pullback, bought 2 Oct</span>", row, StringComparison.Ordinal);
        Assert.Contains($"<b class=\"held-label\">{HeldAgainView.Label}</b>", row, StringComparison.Ordinal);
        Assert.Contains($"<a class=\"held-go\" href=\"{SinglePageApp.PaperRoute}\">See its trade on Paper trading</a>", row, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"held-card\"", home, StringComparison.Ordinal);

        // With no stock met again the region is not drawn.
        Assert.Equal(string.Empty, SinglePageApp.HeldAgainRows([], EquityBrief.Web.App.Universes.Large, new Dictionary<string, string>()));
    }

    // The earlier version picked five across the three indices on 20 Nov and no index's own version has picked since: Home's
    // line counts the five. Once the S&P 500's own version picks two on 23 Nov, the line counts its two.
    [Fact]
    public void ThePickersHomeLineCountsTheNightsPicksWhicheverVersionMadeThem()
    {
        var across = new DateOnly(2026, 11, 20);
        var own = new DateOnly(2026, 11, 23);
        JudgePickRow Pick(DateOnly night, int rank, string? runIndex, string runId) =>
            new(night, rank, "GSPC", "T" + rank, JudgeMenu.Holds[0].Words, JudgeMenu.Whole, "its reason", 100m, 2.0, 98m, 104m, null, null, null, null, false) { RunIndex = runIndex, RunId = runId };
        JudgeView Built(IReadOnlyList<JudgeRunRow> runs, IReadOnlyList<JudgePickRow> picks) =>
            JudgeScreen.Build("GSPC", runs, picks, new Dictionary<string, IReadOnlyList<(DateOnly, double, double, double)>>(), new Dictionary<(string, DateOnly), double>(), new Dictionary<DateOnly, IReadOnlyList<(string, string)>>(), [], [], [], EquityBrief.Core.Cards.AccountSettings.Example, (_, _, _) => null, draws: 0);

        JudgeRunRow[] acrossRuns = [new("judge-across", across, "picked", string.Empty, 1500, 5, false)];
        JudgePickRow[] acrossPicks = [.. Enumerable.Range(1, 5).Select(rank => Pick(across, rank, null, "judge-across"))];
        var before = Built(acrossRuns, acrossPicks);

        Assert.Equal((across, 5), before.AcrossNight);
        Assert.Contains("<b>AI stock picker:</b> picked 5 stocks from all 1,500 on the night of 20 Nov, before it picked within each index. Bought at today's open in its own paper account.", SinglePageApp.JudgeHomeLine(before), StringComparison.Ordinal);
        Assert.DoesNotContain("Picked 0", SinglePageApp.JudgeHomeLine(before), StringComparison.Ordinal);

        JudgeRunRow[] withOwn = [.. acrossRuns, new("judge-gspc", own, "picked", string.Empty, 503, 2, false) { Index = "GSPC" }];
        JudgePickRow[] ownPicks = [.. acrossPicks, Pick(own, 1, "GSPC", "judge-gspc"), Pick(own, 2, "GSPC", "judge-gspc")];
        var after = Built(withOwn, ownPicks);

        Assert.Null(after.AcrossNight);
        Assert.Contains("<b>AI stock picker</b> Picked 2 stocks tonight", SinglePageApp.JudgeHomeLine(after), StringComparison.Ordinal);
    }
}
