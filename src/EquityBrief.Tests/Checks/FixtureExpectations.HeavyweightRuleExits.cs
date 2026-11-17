using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 23.10: on the operator's answer of 2026-11-17 a holding of the S&P 400's or 600's live heavyweights
// rule's own book bought before the rule was registered at the rules restart, with no stop and no take-profit, takes the
// book's exit from its own buy on the next night, stored with that night, and is sold that night where its close stands under
// the stop; a background version's holding keeps the exits it was bought with; the night run again writes the same.
// see: Each rule takes profit in the shape its test chose on the early years, never at the nearest price it turned down at
public partial class FixtureExpectations
{
    [Fact]
    public async Task ALiveHeavyweightsRulesHoldingBoughtBeforeItsRegistrationTakesTheBooksExitFromItsOwnBuy()
    {
        var (store, closes) = HeavyweightRuleStore();

        using (store)
        {
            var heavyweights = HeavyweightIndexEvaluator("heavyweight-400");
            var freezeAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

            // The live rule at the index's own book's settings, registered 1, and the cover as its one variant, registered 2.
            Assert.Equal(0, (await RegisterVerbAt(
                store,
                freezeAt,
                RegisterVerb.IndexFamily, "heavyweight", "--index", "MID", "--parameters", Typed(IndexRules.Provisional(heavyweights)),
                "--variants", "quality=2")).Code);

            var register = await new CandidateRegistrar(FixedClock.At(freezeAt, SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();

            async Task Night(DateOnly session, string run) =>
                await new IndexFamilies(FixedClock.At(new DateTimeOffset(session.ToDateTime(new TimeOnly(23, 40)), TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile)
                    .RunAsync(run, default, register, freezeAt.AddHours(1));

            IReadOnlyList<string> L1() => TextRows(
                store,
                "SELECT r.id || '|' || h.entered_on || '|' || COALESCE(h.stop, 'none') || '|' || COALESCE(h.take_profit_from, 'none') || '|' || COALESCE(h.exit_added_on, 'none') || '|' || COALESCE(h.ended_on, 'open') || '|' || COALESCE(h.reason, 'held') " +
                "FROM index_heavyweight_rule_holding h JOIN candidate_register r ON r.candidate = h.candidate WHERE h.ticker = 'L1' ORDER BY r.id;");

            // The first night: both rules buy L1. Each holding is then dated the session before the registration and its exit
            // cleared, as a holding bought before the rules restart stood, with L1's typical daily move on that session $2.
            await Night(new DateOnly(2026, 10, 1), "first");
            store.Execute("UPDATE index_heavyweight_rule_holding SET entered_on = '2026-09-30', stop = NULL, take_profit = NULL, take_profit_from = NULL WHERE ticker = 'L1';");
            store.Execute($"INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ('L1', '2026-09-30', '{IndicatorSeries.Atr14}', 2.0, 252);");

            Assert.Equal(["1|2026-09-30|none|none|none|open|held", "2|2026-09-30|none|none|none|open|held"], L1());

            // The next night L1 closes at half the night before's. The live rule's holding takes the S&P 400 book's exit from its
            // own buy, the stop 4 typical moves of $2 under it, $8, and is sold at that close under the stop; the variant's keeps
            // none and holds it.
            var buy = closes["L1"][260];
            var exit = BookExits.Of(Core.Families.HeavyweightRule.Name, "MID");
            var stop = Data.Money.ToStorage(Statistic.ToPrice(Statistic.FromPrice(buy) - (exit.Moves * 2.0)));
            var crash = Math.Round(closes["L1"][260] / 2, 4);

            Assert.Equal(4.0, exit.Moves);
            StoreYear(store, "L1", [new FamilyBar(new DateOnly(2026, 10, 2), crash, crash, crash, 1_000_000)]);
            StoreYear(store, "L2", [new FamilyBar(new DateOnly(2026, 10, 2), closes["L2"][261], closes["L2"][261], closes["L2"][261], 100_000)]);
            StoreYear(store, "L3", [new FamilyBar(new DateOnly(2026, 10, 2), closes["L3"][261], closes["L3"][261], closes["L3"][261], 1_000_000)]);

            await Night(new DateOnly(2026, 10, 2), "second");

            var live = L1()[0].Split('|');

            Assert.Equal(["1", "2026-09-30", stop], live[..3]);
            Assert.NotEqual("none", live[3]);
            Assert.Equal(["2026-10-02", "2026-10-02", TakeProfit.UnderItsStop], live[4..]);
            Assert.Equal("2|2026-09-30|none|none|none|open|held", L1()[1]);

            // The night run again opens what it sold, clears the exit it added and writes both again.
            var once = L1();

            await Night(new DateOnly(2026, 10, 2), "second-again");

            Assert.Equal(once, L1());
        }
    }
}
