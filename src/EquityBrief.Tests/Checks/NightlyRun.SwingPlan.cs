using EquityBrief.Core.Candidates;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Fills;

namespace EquityBrief.Tests.Checks;

// nightly-run, 23.10's second part: the live swing filter stating a plan keeps the page list's pullbacks as its own trades
// under it, each with the order its plan places at the night's close, while a variant stating none keeps its own picks as
// before; an order whose window passed unfilled is walked to its window's last session with no result; the fill step prices
// each from the rule picks; and the pages read the live rule's trades from them, the one waiting and the other not filled.
// see: The S&P 500's swing filter reads the hooks its night keeps a pick under, and its heavyweight book a book's
public partial class NightlyRun
{
    static readonly string[] PlanSessions = ["2026-09-28", "2026-09-29", "2026-09-30", "2026-10-01", "2026-10-02", "2026-10-05", "2026-10-06", RuleNight];

    [Fact]
    public async Task TheLiveSwingFilterStatingAPlanKeepsThePageListsPullbacksUnderItAndEveryPageReadsThem()
    {
        using var store = new TemporaryStore().Migrated();
        var combination = PlanPrices.Named("0.84 typical days under the close within 5 sessions, 4 typical days under the buy, the rule's sale")!;
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 7, 23, 50, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        // Eight flat sessions for AAA and BBB, no typical move among them, so an order holds no level. Tonight the live filter
        // fires on BBB, which the page lists first, and the variant on AAA alone.
        foreach (var ticker in new[] { "AAA", "BBB" })
        {
            foreach (var session in PlanSessions)
            {
                Bar(store, ticker, session, "100");
            }
        }

        FilterRow(store, "BBB", RuleNight, live: true, variant: false, 0.5, 5, "100", "96", "108", 2.0);
        FilterRow(store, "AAA", RuleNight, live: false, variant: true, 0.9, 9, "100", "95", "110", 2.0);
        store.Execute($"INSERT INTO family_night (session_date, families) VALUES ('{RuleNight}', '[\"pullback\",\"breakout\",\"drift\"]');");
        store.Execute($"INSERT INTO family_pick (session_date, ticker, family, state, place, also) VALUES ('{RuleNight}', 'BBB', 'pullback', 'listed', 1, '[]');");

        // The live filter's trade on AAA from 2026-09-28 under the plan, an order holding no level, its window of 5 sessions
        // passed unfilled by tonight, and held at most a session, so tonight, the seventh session after it, its window and its
        // cap have passed and it is benchmarked: AAA's typical move that night 2, so its stop 2.5 moves under, over the two
        // members the index held, neither of whose flat bars holds a typical move, so none is read.
        var unplaced = EquityBrief.Worker.Loop.PlanTrades.Unplaced(combination, 95, 1);

        store.Execute(
            "INSERT INTO rule_pick (index_code, rule, ticker, session_date, family, place, entry, stop, target, reward_to_risk, cap, why, plan) " +
            $"VALUES ('GSPC', '{LiveFilter}', 'AAA', '2026-09-28', 'pullback', 1, '100', '95', '110', 2.0, 1, '{{}}', '{unplaced.Json().Replace("'", "''", StringComparison.Ordinal)}');");
        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', 'AAA', '2020-01-02', NULL, '2026-09-28T21:00:00Z'), ('GSPC', 'BBB', '2020-01-02', NULL, '2026-09-28T21:00:00Z');");
        store.Execute("INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ('AAA', '2026-09-28', 'atr14', 2.0, 14);");

        var planned = new Dictionary<string, double>(SwingFilterRule.ParametersOf(Core.Filter.FilterSettings.Proposed), StringComparer.Ordinal);

        foreach (var (name, value) in PlanHook.ParametersOf(combination))
        {
            planned[name] = value;
        }

        RegisterRow[] register =
        [
            new(1, LiveFilter, "rule", "test", SwingFilterRule.EvaluatorName, CandidateEvaluator.Write(planned), "v", CandidateFamily.Registered, null, new DateTimeOffset(2026, 10, 4, 16, 0, 0, TimeSpan.Zero), null),
            Registered(2, FilterVariant, SwingFilterRule.EvaluatorName, "2026-10-04T16:00:00Z"),
        ];

        var outcome = await new RuleCards(clock, store.DatabaseFile).RunAsync("night-rule-cards", register, new DateTimeOffset(2026, 10, 7, 21, 0, 0, TimeSpan.Zero));

        Assert.Equal(("GSPC", 2, 2), outcome.Indices.Select(index => (index.Index, index.Rules, index.Picks)).First());

        // The live filter keeps BBB at the page's place, at the plan clear of the noise its verdict names, with the order its
        // plan placed; the variant keeps AAA as before, stating no plan.
        Assert.Equal(
            [$"{LiveFilter}|BBB|1|100|96|108|1", $"{FilterVariant}|AAA|1|100|95|110|0"],
            Texts(store, $"SELECT rule || '|' || ticker || '|' || place || '|' || entry || '|' || stop || '|' || target || '|' || (plan IS NOT NULL) FROM rule_pick WHERE session_date = '{RuleNight}' ORDER BY rule;"));
        Assert.Equal(combination.Words, PlanOrder.FromJson(Texts(store, $"SELECT plan FROM rule_pick WHERE session_date = '{RuleNight}' AND rule = '{LiveFilter}';").Single())!.Words);

        // The order whose window passed unfilled ends on its window's last session, the fifth after its night, with no result;
        // it is benchmarked over the two members, none read, and tonight's, its window and cap still ahead, is not.
        Assert.Equal(["2026-10-05||0"], Texts(store, "SELECT ended_on || '|' || COALESCE(result, '') || '|' || COALESCE(benchmark, '') || members FROM rule_pick WHERE session_date = '2026-09-28';"));
        Assert.Equal(["1|1"], Texts(store, $"SELECT (benchmark IS NULL) || '|' || (members IS NULL) FROM rule_pick WHERE session_date = '{RuleNight}' AND rule = '{LiveFilter}';"));

        // The fill step prices each from the rule picks: the one not filled, and none for the one still waiting; and the
        // pages read both as the live rule's trades under its plan.
        await new TradeFiller(clock, store.DatabaseFile).RunAsync("night-fills");

        var trades = await new EquityBrief.Api.Reading.ReadApi(store.DatabaseFile, clock).PlannedTradesAsync("GSPC", new DateOnly(2026, 10, 7));

        Assert.Equal(
            [("AAA", new DateOnly(2026, 9, 28), true, true, (DateOnly?)null), ("BBB", new DateOnly(2026, 10, 7), true, false, (DateOnly?)null)],
            trades.Select(row => (row.Ticker, row.Listed, row.Live, row.NotFilled, row.FilledOn)));
        Assert.All(trades, row => Assert.Equal((LiveFilter, "pullback"), (row.Owner, row.Family)));
        Assert.Empty(await new EquityBrief.Api.Reading.ReadApi(store.DatabaseFile, clock).PlannedTradesAsync("MID", new DateOnly(2026, 10, 7)));
    }
}
