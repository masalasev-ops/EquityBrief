using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// A watched pick on Home draws the plan its kind's test recommends, priced on the stock's own levels on the night and said
// as not traded: the pricing worked by hand over constructed levels, the night pricing a constructed card from its bars,
// and the block in each of its forms.
// see: A watched pick on Home draws the plan the long test recommends for its kind of pick, priced on its own levels and not traded
public partial class ReadSurface
{
    static readonly PlanLevels HandLevels = new([(100, 95), (90, 84)], [105, 112, 120], [103, 108]);

    [Fact]
    public void ARecommendedPlanIsPricedOnAPicksOwnLevelsWorkedByHand()
    {
        var sale = new PlanSale(125, null, []);

        // The second zone within 20 sessions at its own stop, sold at the first band above it: 90, 84 and 103.
        var zone = PlanPrices.Of(new PlanCombination(PlanPoints.SecondZone, 20, PlanPoints.ZoneStop, PlanPoints.FirstBand), 110, HandLevels, 98, 2, 104, sale, 63)!;

        Assert.Equal((90.0, 20, 84.0, 103.0, 0, 63), (zone.Buy!.Value, zone.Window, zone.Stop, zone.Target!.Value, zone.Staged.Count, zone.Cap));

        // The 20-session average within 10, 1.5 typical days of 2 under it, sold in stages above it: 98, 95 and 105, 112, 120.
        var average = PlanPrices.Of(new PlanCombination(PlanPoints.Average, 10, PlanPoints.TypicalStop(1.5), PlanPoints.StagedSales), 110, HandLevels, 98, 2, 104, sale, 63)!;

        Assert.Equal((98.0, 95.0, (double?)null), (average.Buy!.Value, average.Stop, average.Target));
        Assert.Equal([105.0, 112.0, 120.0], average.Staged);

        // The next open at the rule's own stop and sale: no level, the rule's 104 and 125.
        var open = PlanPrices.Of(new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.RuleStop, PlanPoints.RuleSale), 110, HandLevels, 98, 2, 104, sale, 63)!;

        Assert.Equal(((double?)null, 104.0, 125.0), (open.Buy, open.Stop, open.Target!.Value));
        Assert.True(PlanPrices.Traded(new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.RuleStop, PlanPoints.RuleSale)));

        // A third zone the levels do not draw, and a stop at or over the buy, price nothing.
        Assert.Null(PlanPrices.Of(new PlanCombination(PlanPoints.ThirdZone, 5, PlanPoints.ZoneStop, PlanPoints.RuleSale), 110, HandLevels, 98, 2, 104, sale, 63));
        Assert.Null(PlanPrices.Of(new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.RuleStop, PlanPoints.TimeLimit), 110, HandLevels, 98, 2, 111, sale, 63));

        // The words a run stores name the combination back.
        Assert.Equal(zone.Combination, PlanPrices.Named(zone.Combination)!.Words);
        Assert.Null(PlanPrices.Named("no such combination"));
    }

    [Fact]
    public async Task TheNightPricesEachPriceCardAtItsFamilysRecommendationFromTheStocksOwnBars()
    {
        using var store = new TemporaryStore().Migrated();

        // 260 sessions rising 5 cents a day with a range of a dollar, so the typical daily move is a dollar and the last close
        // is 62.95.
        var sessions = new List<DateOnly>();

        for (var day = new DateOnly(2025, 10, 1); sessions.Count < 260; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                sessions.Add(day);
            }
        }

        var night = sessions[^1].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        store.Execute("INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) VALUES " + string.Join(", ", sessions.Select((session, at) =>
        {
            var close = 50m + (0.05m * at);

            return FormattableString.Invariant($"('TST', '{session:yyyy-MM-dd}', '{close}', '{close + 0.5m}', '{close - 0.5m}', '{close}', 1000000, 'test', '2026-11-10T00:00:00Z')");
        })) + ";");
        store.Execute($@"
            INSERT INTO decision_card (index_code, session_date, family, ticker, place, entry, stop, target, rule, settings, lines, cap) VALUES
                ('GSPC', '{night}', 'drift', 'TST', 1, '62.95', '61', '70', 'r', '{{}}', '[]', 60),
                ('GSPC', '{night}', 'pullback', 'TST', 2, '62.95', '61', '70', 'r', '{{}}', '[]', 63),
                ('GSPC', '{night}', 'improving', 'TST', 3, '62.95', NULL, NULL, 'r', '{{}}', '[]', NULL);
            INSERT INTO plan_point (run_id, index_code, family, through, setups, combination, later_fills, later_edge_per_pick, traded_later_edge_per_pick) VALUES
                ('p1', 'GSPC', 'drift', '{night}', 10, 'the next open, 1 typical days under the buy, the time limit alone', 500, 0.05, -0.1),
                ('p1', 'GSPC', 'pullback', '{night}', 10, 'the next open, the rule''s stop, the rule''s sale', 500, 0.2, 0.1);");

        var (read, priced) = await new EquityBrief.Worker.Cards.DecisionCards(SystemClock.ForUnitedStatesSessions(), store.DatabaseFile).PriceAsync(null);

        Assert.Equal((sessions[^1], 1), (read!.Value, priced));

        string? Alternative(string family)
        {
            using var connection = store.Open();
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT alternative FROM decision_card WHERE family = '{family}';";

            return command.ExecuteScalar() as string;
        }

        // The drift's: bought at the next open, a typical daily move of a dollar under the close, sold at its time limit.
        using var drift = JsonDocument.Parse(Alternative("drift")!);
        var root = drift.RootElement;

        Assert.Equal("the next open, 1 typical days under the buy, the time limit alone", root.GetProperty("combination").GetString());
        Assert.Equal(62.95m, decimal.Parse(root.GetProperty("close").GetString()!, CultureInfo.InvariantCulture));
        Assert.Equal(61.95, double.Parse(root.GetProperty("stop").GetString()!, CultureInfo.InvariantCulture), 3);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("buy").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("target").ValueKind);
        Assert.Equal((60, 500, 0.05, -0.1), (root.GetProperty("cap").GetInt32(), root.GetProperty("fills").GetInt32(), root.GetProperty("edge").GetDouble(), root.GetProperty("traded").GetDouble()));

        // The pullback's recommendation is the traded plan itself, and the Fundamentals card no price family's: neither is priced.
        Assert.Null(Alternative("pullback"));
        Assert.Null(Alternative("improving"));
    }

    static string AlternativeJson(string combination, string? buy, string stop, string? target = null, string staged = "[]", string? trail = null, bool bounce = false, double edge = 0.037, double traded = -0.124, string? unplaced = null) =>
        JsonSerializer.Serialize(new { combination, unplaced, close = "62.14", buy, window = 20, stop = unplaced is null ? stop : null, target, staged = JsonSerializer.Deserialize<string[]>(staged), trail, bounce, cap = 63, from = 2019, through = 2026, fills = 2934, edge, traded, run = "r" });

    [Fact]
    public void AWatchedCardDrawsItsKindsRecommendedPlanInEachOfItsForms()
    {
        string Block(string json) => Read(SinglePageApp.AlternativeBlock(AlternativePlan.Parse(json)!, Universes.Large));

        // A zone plan that did better: its wait, its three prices, its latest sale, what the test found and that it is not traded.
        var better = Block(AlternativeJson("the second zone within 20 sessions, 2 typical days under the buy, the first band above", "60", "56.4174", "64.6207"));

        Assert.Equal(
            "A plan that did better: Wait for a lower price: buy only if it dips to the price below within 20 trading days; if it never does, there is no trade. It is sold after 63 trading days at the latest."
            + "Buy if it dips to about$60.00Sell if it falls to$56.42Aim to sell at$64.62"
            + "We tested this plan on S&P 500 stocks from 2019 to 2026 the same way: picks like this did about 4 cents better for every $1 put at risk than simply buying other S&P 500 stocks on the same days, against about 12 cents worse with the plan the rules use. The rules do not trade it yet.",
            better);

        // One that lost less, sold in parts.
        var less = Block(AlternativeJson("the second zone within 20 sessions, 2 typical days under the buy, the staged sales", "175.91", "162.31", staged: "[\"190.5\",\"204.77\"]", edge: -0.036, traded: -0.192));

        Assert.StartsWith("A plan that lost less:", less, StringComparison.Ordinal);
        Assert.Contains("Sell in parts at$190.50 and $204.77", less, StringComparison.Ordinal);
        Assert.Contains("it also did worse than simply buying other S&P 500 stocks on the same days, about 4 cents worse for every $1 put at risk, against about 19 cents worse with the plan the rules use. The rules do not trade it.", less, StringComparison.Ordinal);

        // A next-open plan rising behind its highest close, and a dip's bounce.
        Assert.Contains("Buy at the next open, as the rules do, and sell it differently. The give-up price rises behind its highest close by $1.20, and it is sold after 63 trading days at the latest.", Block(AlternativeJson("the next open, 1 typical days under the buy, the rule's sale", null, "61", trail: "1.2")), StringComparison.Ordinal);
        Assert.Contains("Aim to sellWhen it bounces back", Block(AlternativeJson("the next open, 1 typical days under the buy, the rule's sale", null, "61", bounce: true)), StringComparison.Ordinal);
        Assert.Contains("Sell after63 trading days", Block(AlternativeJson("the next open, 1 typical days under the buy, the time limit alone", null, "61")), StringComparison.Ordinal);

        // One the stock's levels place none of tonight says why; one that did no better than the traded plan says so.
        Assert.StartsWith("A plan that did better: " + SinglePageApp.UnplacedZone, Block(AlternativeJson("the second zone within 10 sessions, 2 typical days under the buy, the time limit alone", null, "0", unplaced: "zone", edge: 0.022, traded: -0.038)), StringComparison.Ordinal);
        Assert.Equal(SinglePageApp.NoBetterPlan, Block(AlternativeJson("the next open, 1 typical days under the buy, the time limit alone", null, "61", edge: -0.2, traded: -0.1)));

        // The cents both ways.
        Assert.Equal(["about 4 cents better", "less than 1 cent worse"], new[] { (0.037, "better"), (-0.003, "worse") }.Select(one => SinglePageApp.Cents(one.Item1, one.Item2)));
    }

    [Fact]
    public void HomeDrawsTheRecommendedPlanOnAWatchedCardAndNotOnAnIdeas()
    {
        var plan = AlternativePlan.Parse(AlternativeJson("the second zone within 20 sessions, 2 typical days under the buy, the first band above", "60", "56.4174", "64.6207"))!;
        var idea = new HomeIdea(DipRule.Name, "NUE", null, "Dipped.", 150m, 140m, 160m, false, null, LongRecord: new SetupLongRecord("GSPC", DipRule.Name, 0.06, 512, 2019, 2026), Alternative: plan);
        var region = WebUtility.HtmlDecode(new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Ideas = [idea], IdeasCount = 1, Watching = [Watched(SetupFamilies.Pullback, "BMY") with { Alternative = plan }] }, string.Empty));

        // The watched card draws it after why it is not bought; the idea's card does not.
        Assert.Single(Regex.Matches(region, "<div class=\"home-alt\""));
        Assert.Matches("data-ticker=\"BMY\".*?<p class=\"home-why-not\">.*?</p><div class=\"home-alt\" data-alt=\"better\"", region.Replace("\n", " ", StringComparison.Ordinal));
        Assert.Empty(SinglePageApp.HomeLinesStartingMidSentence(region));
    }
}
