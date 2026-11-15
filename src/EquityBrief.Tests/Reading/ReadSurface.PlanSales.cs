using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// From the 23.10 correction of 2026-11-13: a pick's card whose plan buys at a price draws the sale that plan was registered with,
// worked from the price it waits for, on Home, Tonight's row and its folded card, the stock page and the read-only copy, and a
// watched card's better plan the same; what it risks for what it aims at reads from that sale. Before it each card drew its buy,
// its stop and its time limit, and the sale was missing.
// see: A pick's card draws the sale its traded plan was registered with, worked from the price the plan buys at
public partial class ReadSurface
{
    // An order as the night stores it on a card: buying at 40.00 within 20 sessions, a typical day of 1.00 and the rule's stop of
    // 37.00, the stop and the sale its words name, as the plan point test registered it.
    static string Ordered(string stop, string sale, double? target = null, double[]? staged = null, int cap = 60) =>
        new PlanOrder($"1 typical days under the close within 20 sessions, {stop}, {sale}", "1 typical days under the close", 40, 20, stop, 37, null, 1, target, staged ?? [], null, null, cap).Json();

    // Each plan's sale worked by hand from that order, the lines in the order a card draws them, each its label, its figure and
    // its note run together as a reader's eye reads them, and what it risks for what it aims at: the rule's target of 45.00 at a
    // stop 2 typical days under the buy, 38.00, risks 2.00 to make 5.00, $2.50; the first band above at 46.00 at the rule's stop
    // risks 3.00 to make 6.00, $2.00; the staged sales at 42.00 and 44.00, a level of 39.00 under the buy dropped, sell a third
    // at each and aim at their mean of 43.00, $1.50; and the time limit alone of 5 trading days aims at nothing.
    public static TheoryData<string, string, string[], string?> RegisteredSales => new()
    {
        { "the rule's target", Ordered(PlanPoints.TypicalStop(2), PlanPoints.RuleSale, target: 45), ["Aim to sell at$45.00", "Otherwise sell after60 trading days"], "2.50" },
        { "the first band above", Ordered(PlanPoints.RuleStop, PlanPoints.FirstBand, target: 46), ["Aim to sell at$46.00the first price above the buy where it has turned down before", "Otherwise sell after60 trading days"], "2.00" },
        { "the first band above, none above the buy", Ordered(PlanPoints.RuleStop, PlanPoints.FirstBand), ["No price above tonight's close where it has turned down before:it holds for 60 trading days unless the stop sells it first."], null },
        { "the staged sales", Ordered(PlanPoints.TypicalStop(2), PlanPoints.StagedSales, staged: [44, 39, 42]), ["Sell a third at$42.00", "Sell a third at$44.00", "Keep the last thirduntil a close below the stop", "Otherwise sell after60 trading days"], "1.50" },
        { "the staged sales, none above the buy", Ordered(PlanPoints.TypicalStop(2), PlanPoints.StagedSales, staged: [39]), ["No price level above tonight's close to sell at:it is near its highs for the past year, so it holds for 60 trading days unless the stop sells it first."], null },
        { "the time limit alone", Ordered(PlanPoints.RuleStop, PlanPoints.TimeLimit, cap: 5), ["No price target:this rule sells after 5 trading days"], null },
    };

    // A card's lines of its plan, each tile or row read as a reader reads it.
    static IReadOnlyList<string> Tiles(string html, string pattern) =>
        [.. Regex.Matches(html, pattern, RegexOptions.Singleline).Select(match => Read(match.Value))];

    [Theory]
    [MemberData(nameof(RegisteredSales))]
    public void EveryCardDrawsTheSaleItsTradedPlanWasRegisteredWithWorkedFromThePriceItBuysAt(string sale, string plan, string[] lines, string? reward)
    {
        var records = new SetupRecords([new SetupLongRecord("MID", DriftRule.Name, 0.05, 400, 2019, 2026) { LiveMove = 0.0123, LiveUp = 0.5432, LiveFills = 1200 }]);
        var row = new DecisionCardRow("MID", EntryNight, DriftRule.Name, "MAT", 1, 41.30m, 37m, 47m, "the rule", "{}", "[]", null, Cap: 60) { Plan = plan };
        var view = CardScreen.View(row, CardContext.None with { LongRecords = records });
        var zone = Assert.IsType<ZoneBuy>(view.Zone);

        Assert.Equal(40m, zone.Price);

        // The stock page's card, Tonight's folded card and the read-only copy's: the plan's tiles after the buy and the stop are
        // the sale's lines, and the reward reads from them.
        foreach (var drawn in new[] { new MarkRenderer().DecisionCard(view), new MarkRenderer { Copying = true }.DecisionCard(view) })
        {
            var tiles = Tiles(drawn, "<div class=\"plan-tile\" data-tile=\"(?:target|stage|keep|limit)\".*?</div>");

            Assert.True(lines.SequenceEqual(tiles), $"{sale}: the card drew [{string.Join(" | ", tiles)}]");
        }

        Assert.Equal(reward is null ? 0 : 1, Regex.Matches(new MarkRenderer().DecisionCard(view), $"You risk \\$1 to make \\${Regex.Escape(reward ?? "x")}\\.").Count);

        // Home's card: the same lines beneath the buy and the stop, and what it risks for what it aims at.
        var idea = new HomeIdea(DriftRule.Name, "MAT", null, "Beat.", zone.Price, view.Stop, view.Target, false, null, LongRecord: records.Of("MID", DriftRule.Name), Zone: zone);
        var home = new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Reading = Universes.Mid, Ideas = [idea], IdeasCount = 1 }, string.Empty);
        var card = Regex.Match(home, "<article class=\"home-idea.*?</article>", RegexOptions.Singleline).Value;
        var homeLines = Tiles(card, "<div data-sale=\"[a-z]+\">.*?</div>");

        Assert.True(lines.SequenceEqual(homeLines), $"{sale}: Home drew [{string.Join(" | ", homeLines)}]");
        Assert.Equal(reward is null ? null : "Risks $1 to make about $" + reward, Regex.Match(Read(card), "Risks \\$1 to make about \\$[0-9.]+") is { Success: true } said ? said.Value : null);

        // Tonight's row: the price it waits for, the plan's stop, the sale in the target's column and the reward from it.
        var point = new BuyPointView(DriftRule.Name, "Earnings drift", new FamilyPickCell(new ListingCell("MAT", EntryNight, 1, 0, 41.30m, []), 1, 41.30m, 37m, 47m, false, 3m, "why", [], view));
        var buys = WebUtility.HtmlDecode(new MarkRenderer().TomorrowsBuys([point], null, night: EntryNight));
        var tonight = Regex.Match(buys, "<tr data-ticker=\"MAT\".*?</tr>", RegexOptions.Singleline).Value;

        Assert.Contains("data-buy=\"40", tonight, StringComparison.Ordinal);
        Assert.Contains($"data-stop=\"{view.Stop!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)}\"", tonight, StringComparison.Ordinal);
        Assert.Contains(reward is null ? "data-reward-to-risk=\"none\"" : $">${reward}</td>", tonight, StringComparison.Ordinal);
        Assert.DoesNotContain("data-reward-to-risk=\"3", tonight, StringComparison.Ordinal);
    }

    // The time limit alone draws the rule's record beneath its line, the mean move of its plan's fills from 2019 by the time
    // they sold and the share of them up, as the rule's record and not the stock's chance; until the test has written it, that
    // it is being worked out. Worked by hand: a move of +0.0123 is up about 1.2%, -0.0087 down about 0.9%, and 0.5432 up 54%.
    [Fact]
    public void ACardSellingOnItsTimeLimitAloneDrawsItsRulesRecordOrSaysItIsBeingWorkedOut()
    {
        DecisionCardView Card(SetupRecords records) =>
            CardScreen.View(new DecisionCardRow("MID", EntryNight, DipRule.Name, "HALO", 1, 41.30m, 37m, null, "the rule", "{}", "[]", null, Cap: 5) { Plan = Ordered(PlanPoints.RuleStop, PlanPoints.TimeLimit, cap: 5) }, CardContext.None with { LongRecords = records });

        SetupRecords Moved(double move) => new([new SetupLongRecord("MID", DipRule.Name, -0.01, 900, 2019, 2026) { LiveMove = move, LiveUp = 0.5432, LiveFills = 900 }]);

        const string Up = "Over 2019 to 2026, picks like this were up about 1.2% on average by then, and 54% of them were up. That is the rule's record, not this stock's chance.";
        const string Down = "Over 2019 to 2026, picks like this were down about 0.9% on average by then, and 54% of them were up. That is the rule's record, not this stock's chance.";

        foreach (var (records, said) in new[] { (Moved(0.0123), Up), (Moved(-0.0087), Down), (SetupRecords.None, MarkRenderer.RecordBeingWorkedOut), (new SetupRecords([new SetupLongRecord("MID", DipRule.Name, -0.01, 900, 2019, 2026)]), MarkRenderer.RecordBeingWorkedOut) })
        {
            var view = Card(records);

            foreach (var drawn in new[] { new MarkRenderer().DecisionCard(view), new MarkRenderer { Copying = true }.DecisionCard(view) })
            {
                Assert.Equal(said, Read(Assert.Single(Tiles(drawn, "<p class=\"time-record\".*?</p>"))));
                Assert.DoesNotContain("You risk $1", Read(drawn), StringComparison.Ordinal);
            }

            var home = new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Reading = Universes.Mid, Ideas = [new HomeIdea(DipRule.Name, "HALO", null, "A fall.", view.Zone!.Price, view.Stop, null, false, null, LongRecord: records.Of("MID", DipRule.Name), Zone: view.Zone)], IdeasCount = 1 }, string.Empty);

            Assert.Equal(said, Read(Assert.Single(Tiles(home, "<p class=\"home-cond\" data-time-record>.*?</p>"))));
            Assert.DoesNotContain("Risks $1", Read(home), StringComparison.Ordinal);
            Assert.DoesNotContain("its 2-day RSI", Read(new MarkRenderer().DecisionCard(view)), StringComparison.Ordinal);
        }
    }

    // From the second 23.10 correction of 2026-11-13: a staged sale whose levels place no price above the buy, as the S&P 400
    // drift's BHF of 2026-11-12, and the first band above with no band above it, each draw one line saying so and how long it
    // holds, and beneath it the record of picks like it, as the time limit alone draws the dip's; a staged sale with a level
    // above its buy draws no such line. Worked by hand: a move of +0.0123 is up about 1.2% and 0.5432 is 54% up.
    [Fact]
    public void ACardWhoseLevelsPlaceNoPriceAboveItsBuySaysSoAndDrawsTheRecordOfPicksLikeIt()
    {
        const string Up = "Over 2019 to 2026, picks like this were up about 1.2% on average by then, and 54% of them were up. That is the rule's record, not this stock's chance.";

        DecisionCardView Card(string family, string plan, SetupRecords records) =>
            CardScreen.View(new DecisionCardRow("MID", EntryNight, family, "BHF", 1, 44.10m, 37m, null, "the rule", "{}", "[]", null, Cap: 60) { Plan = plan }, CardContext.None with { LongRecords = records });

        SetupRecords Moved(string family) => new([new SetupLongRecord("MID", family, 0.05, 400, 2019, 2026) { LiveMove = 0.0123, LiveUp = 0.5432, LiveFills = 300 }]);

        foreach (var (family, plan, line) in new[]
        {
            (DriftRule.Name, Ordered(PlanPoints.TypicalStop(2), PlanPoints.StagedSales, staged: [39]), "No price level above tonight's close to sell at:it is near its highs for the past year, so it holds for 60 trading days unless the stop sells it first."),
            (BreakoutRule.Name, Ordered(PlanPoints.RuleStop, PlanPoints.FirstBand), "No price above tonight's close where it has turned down before:it holds for 60 trading days unless the stop sells it first."),
        })
        {
            foreach (var (records, said) in new[] { (Moved(family), Up), (SetupRecords.None, MarkRenderer.RecordBeingWorkedOut) })
            {
                var view = Card(family, plan, records);

                foreach (var drawn in new[] { new MarkRenderer().DecisionCard(view), new MarkRenderer { Copying = true }.DecisionCard(view) })
                {
                    Assert.Equal([line], Tiles(drawn, "<div class=\"plan-tile\" data-tile=\"(?:target|stage|keep|limit)\".*?</div>"));
                    Assert.Equal(said, Read(Assert.Single(Tiles(drawn, "<p class=\"time-record\".*?</p>"))));
                    Assert.DoesNotContain("Sell a share at", Read(drawn), StringComparison.Ordinal);
                    Assert.DoesNotContain("Aim to sell at", Read(drawn), StringComparison.Ordinal);
                }

                var home = new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Reading = Universes.Mid, Ideas = [new HomeIdea(family, "BHF", null, "Beat.", view.Zone!.Price, view.Stop, null, false, null, LongRecord: records.Of("MID", family), Zone: view.Zone)], IdeasCount = 1 }, string.Empty);

                Assert.Equal([line], Tiles(home, "<div data-sale=\"[a-z]+\">.*?</div>"));
                Assert.Equal(said, Read(Assert.Single(Tiles(home, "<p class=\"home-cond\" data-time-record>.*?</p>"))));
            }
        }

        var staged = Card(DriftRule.Name, Ordered(PlanPoints.TypicalStop(2), PlanPoints.StagedSales, staged: [44, 42]), Moved(DriftRule.Name));

        Assert.Empty(Tiles(new MarkRenderer().DecisionCard(staged), "<p class=\"time-record\".*?</p>"));

        // Tonight's row of the staged sale with no level above says the same in its columns.
        var held = Card(DriftRule.Name, Ordered(PlanPoints.TypicalStop(2), PlanPoints.StagedSales, staged: [39]), Moved(DriftRule.Name));
        var point = new BuyPointView(DriftRule.Name, "Earnings drift", new FamilyPickCell(new ListingCell("BHF", EntryNight, 1, 0, 44.10m, []), 1, 44.10m, 37m, null, false, 3m, "why", [], held));
        var row = Regex.Match(WebUtility.HtmlDecode(new MarkRenderer().TomorrowsBuys([point], null, night: EntryNight)), "<tr data-ticker=\"BHF\".*?</tr>", RegexOptions.Singleline).Value;

        Assert.Contains(">none above tonight's close: holds for 60 trading days</td>", row, StringComparison.Ordinal);
        Assert.Contains(">No price above tonight's close: it holds to its time limit</td>", row, StringComparison.Ordinal);
        Assert.DoesNotContain("none above the buy", row, StringComparison.Ordinal);
    }

    // The record of picks like a plan naming no price above its buy counts only the fills its sale placed no price above, worked
    // by hand: fills moving +0.04 and -0.02 with no price above, and one moving +0.05 sold at a staged level above it, are a mean of
    // +0.01 over two fills, one of them up; a pick never filled counts in none of it. Which sales place no price above a fill of
    // 40.00: the time limit alone; the staged sales at 39.00 alone and not at 39.00 and 42.00; the first band above with none and
    // not with one at 46.00; and never the rule's sale.
    [Fact]
    public void TheRecordOfPicksLikeAPlanNamingNoPriceAboveCountsOnlyTheFillsItsSalePlacedNoneAbove()
    {
        var combination = new PlanCombination("1 typical days under the close", 20, PlanPoints.TypicalStop(2), PlanPoints.StagedSales);
        Worker.Loop.PlanPointTest.Worked Fill(double move, bool none, bool filled = true) =>
            new(combination, 40, filled ? 40 : null, 38, "at 42.00", new PlanPick(2021, "2019 on", "calm", filled, filled ? move * 10 : null, null, 10, filled ? "sold" : null, 0, null), "worked") { Move = filled ? move : null, NoPriceAbove = none };

        var moved = Worker.Loop.PlanPointTest.MovesOver([Fill(0.04, true), Fill(-0.02, true), Fill(0.05, false), Fill(0.09, true, filled: false)])!;

        Assert.Equal((0.01, 0.5, 2), (Math.Round(moved.Move, 10), moved.Up, moved.Fills));
        Assert.Null(Worker.Loop.PlanPointTest.MovesOver([Fill(0.05, false)]));

        Assert.True(Worker.Loop.PlanPointTest.NoPriceAboveOf(PlanPoints.TimeLimit, new PlanSale(null, null, []), 40));
        Assert.True(Worker.Loop.PlanPointTest.NoPriceAboveOf(PlanPoints.StagedSales, new PlanSale(null, null, [39]), 40));
        Assert.False(Worker.Loop.PlanPointTest.NoPriceAboveOf(PlanPoints.StagedSales, new PlanSale(null, null, [39, 42]), 40));
        Assert.True(Worker.Loop.PlanPointTest.NoPriceAboveOf(PlanPoints.FirstBand, new PlanSale(null, null, []), 40));
        Assert.False(Worker.Loop.PlanPointTest.NoPriceAboveOf(PlanPoints.FirstBand, new PlanSale(46, null, []), 40));
        Assert.False(Worker.Loop.PlanPointTest.NoPriceAboveOf(PlanPoints.RuleSale, new PlanSale(null, null, []), 40));
    }

    // A watched card's better plan draws its sale as a pick's card does: its staged levels above the buy, a third each, the
    // last kept, and its time limit.
    [Fact]
    public void AWatchedCardsBetterPlanDrawsItsSaleAsAPicksCardDoes()
    {
        var plan = new AlternativePlan("the second zone within 20 sessions, 2 typical days under the buy, the staged sales", 51.30m, 49.20m, 20, 47.20m, null, [52.10m, 50.40m], null, false, 63, 2019, 2026, 900, 0.08, 0.02);
        var block = SinglePageApp.AlternativeBlock(plan, Universes.Large);

        Assert.Equal(["Sell a third at$50.40", "Sell a third at$52.10", "Keep the last thirduntil a close below the stop", "Otherwise sell after63 trading days"], Tiles(block, "<div data-sale=\"[a-z]+\">.*?</div>"));
    }

    // The read hands each family's record the live plan's moves off its newest run, none where that run wrote none, and the
    // test's arithmetic, worked by hand: moves of +0.06, -0.02 and +0.02 are a mean of +0.02 with two of the three up; none
    // over no move.
    [Fact]
    public async Task TheRecordCarriesTheLivePlansMovesFromItsNewestRunAndTheTestWorksThemByHand()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute(@"
            INSERT INTO plan_point (run_id, index_code, family, through, setups, picks, traded_later_edge_per_pick, traded_later_picks, live_later_move, live_later_up, live_later_fills) VALUES
                ('old', 'MID', 'dip', '2026-11-06', 10, 5, -0.01, 900, 0.5, 0.9, 10),
                ('new', 'MID', 'dip', '2026-11-12', 10, 5, -0.01, 900, 0.0123, 0.5432, 1200),
                ('new', 'MID', 'drift', '2026-11-12', 10, 5, 0.02, 800, NULL, NULL, NULL);");

        var records = await Api(store).SetupRecordsAsync();

        Assert.Equal(((double?)0.0123, (double?)0.5432, (int?)1200, 2026), (records.Of("MID", DipRule.Name)!.LiveMove, records.Of("MID", DipRule.Name)!.LiveUp, records.Of("MID", DipRule.Name)!.LiveFills, records.Of("MID", DipRule.Name)!.Through));
        Assert.Null(records.Of("MID", DriftRule.Name)!.LiveMove);

        var moved = Worker.Loop.PlanPointTest.Moves([0.06, -0.02, 0.02])!;

        Assert.Equal((0.02, 2.0 / 3, 3), (Math.Round(moved.Move, 10), moved.Up, moved.Fills));
        Assert.Null(Worker.Loop.PlanPointTest.Moves([]));
    }

    // M1, listed tonight by the S&P 400 breakout's live rule under its plan buying at 48.00 within 5 sessions, its stop 1.5
    // typical days of 2.00 under that price, 45.00, and the rule's own sale trailing 3.00 behind the highest close: its chart
    // page's brief reads the plan's buy, its stop and its sale, and never a buy at the next open.
    [Fact]
    public async Task AStockListedTonightUnderAPlanReadsOnItsChartPagesBriefAsThePlanBuysAndSellsIt()
    {
        using var store = PlanTradesStore();
        var plan = new PlanOrder("2.22 typical days under the close within 5 sessions, 1.5 typical days under the buy, the rule's sale", "2.22 typical days under the close", 48, 5, PlanPoints.TypicalStop(1.5), 47, null, 2, null, [], 3, null, 63).Json();

        store.Execute(
            "INSERT INTO decision_card (index_code, session_date, family, ticker, place, entry, stop, target, rule, settings, lines, record, cap, trail, plan) VALUES " +
            $"('MID', '{IndexNight}', 'breakout', 'M1', 1, '50', '47', NULL, 'The breakout on the S&P 400', '{{}}', '[]', NULL, 63, '3', '{Doubled(plan)}');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var (_, lines, _) = BriefOfPage(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/chart/M1")));

        Assert.EndsWith("buy only if it dips to about 48.00 within 5 trading days; if it never does, there is no trade.", lines["buy"], StringComparison.Ordinal);
        Assert.DoesNotContain("next open", lines["buy"], StringComparison.Ordinal);
        Assert.Equal("Sell at the next trading day's open after a day that closes below the stop, 45.00 at the buy, raised after each close to that close less 3.00 and never lowered.", lines["stop"]);
        Assert.Equal("Otherwise sell at the close 63 trading days after the buy.", lines["exit"]);
    }

    // Home's line above its cards names how each idea sells, on every index.
    [Fact]
    public void HomesLineAboveItsCardsSaysEachIdeaComesWithHowItSells()
    {
        foreach (var reading in Universes.Offered)
        {
            var idea = new HomeIdea(SetupFamilies.Pullback, "BMY", null, "A dip.", 49.20m, 47.50m, 58m, false, null);
            var home = Read(new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Reading = reading, Ideas = [idea], IdeasCount = 1 }, string.Empty));

            Assert.Contains("Each idea comes with what to pay, where to give up if it goes wrong, and how it sells.", home, StringComparison.Ordinal);
            Assert.DoesNotContain("three prices decided tonight", home, StringComparison.Ordinal);
        }
    }

    // A pick listed tonight under a plan buying at a price reads on the chart page's brief as that plan buys and sells it, and a
    // card's management follows the plan's sale: a staged sale's shares, none of the rule's own exit or a rising stop.
    [Fact]
    public void ThePlansSaleManagesTheCardAndNoRisingStopOrRuleExitIsSaidOfIt()
    {
        var row = new DecisionCardRow("SML", EntryNight, BreakoutRule.Name, "S2", 1, 41.30m, 37m, null, "the rule", "{}", "[]", null, Trail: 3m, Cap: 60) { Plan = Ordered(PlanPoints.TypicalStop(2), PlanPoints.StagedSales, staged: [42, 44]) };
        var view = CardScreen.View(row, CardContext.None);

        Assert.Equal(
            [
                "Sell at the next trading day's open after a day that closes below 38.00.",
                "Sell a third at the next trading day's open after a day that closes at or above 42.00.",
                "Sell a third at the next trading day's open after a day that closes at or above 44.00.",
                "Keep the last third until a day that closes below the stop or the time limit.",
                "Otherwise sell at the close 60 trading days after the buy.",
            ],
            view.Management);

        var drawn = Read(Regex.Match(new MarkRenderer().DecisionCard(view), "<section class=\"card-plan\".*?</section>", RegexOptions.Singleline).Value);

        Assert.Contains("Sell if it closes below38.00", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("raised after each higher close", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("rising", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("can no longer lose", drawn, StringComparison.Ordinal);
    }
}
