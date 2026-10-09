using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indices;

namespace EquityBrief.Tests.Checks;

// nightly-run, 17.2: the cards' rule rows written over a constructed store: each index family's rule drawing its list
// with the parts its members failed as its funnel, a family listing nothing carrying a stretch of one night and no mark,
// an index the night did not read written nothing, a swing filter variant's own picks kept in the list's order with a
// stock its open pick holds off, that pick walked to its end on the night's close, the history the record command
// replays read under the night's own row, and a night run again replacing its own rows.
// see: A variant's picks are shown on its card when chosen and its results only under its tests
// see: A card's stretch line counts its mark over past empty nights and draws none under thirty completed stretches
public partial class NightlyRun
{
    const string RuleNight = "2026-10-07";
    const string NightBefore = "2026-10-06";
    const string LiveFilter = "the live swing filter, version 5";
    const string FilterVariant = "the swing filter with the market gate off, from version 5";

    static string Shadow(bool live, bool variant, string plan = "clear") =>
        "{\"candidates\":[" +
        $"{{\"candidate\":\"{LiveFilter}\",\"fired\":{(live ? "true" : "false")},\"values\":{{\"market\":\"passed\",\"trend and strength\":\"passed\",\"setup\":\"{(live ? "passed" : "failed")}\",\"trigger\":\"{(live ? "passed" : "failed")}\",\"trade\":\"{(live ? "passed" : "failed")}\",\"plan\":\"clear\"}}}}," +
        $"{{\"candidate\":\"{FilterVariant}\",\"fired\":{(variant ? "true" : "false")},\"values\":{{\"market\":\"passed\",\"trend and strength\":\"passed\",\"setup\":\"passed\",\"trigger\":\"{(variant ? "passed" : "failed")}\",\"trade\":\"{(variant ? "passed" : "failed")}\",\"plan\":\"{plan}\"}}}}" +
        "],\"skipped\":[]}";

    static void FilterRow(TemporaryStore store, string ticker, string night, bool live, bool variant, double strength, int band, string entry, string stop, string target, double reward) =>
        store.Execute(
            "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, trigger_pass, trade, swing_entry, swing_stop, swing_target, swing_reward_to_risk, exclusions, passed, gates, shadow, clear_stop, clear_target, clear_reward_to_risk, strength, band_strength) " +
            $"VALUES ('{ticker}', '{night}', '5', 'code', 1, 1, 1, {(live ? 1 : 0)}, {(live ? 1 : 0)}, '{entry}', '{stop}', '{target}', {reward.ToString(System.Globalization.CultureInfo.InvariantCulture)}, '[]', {(live ? 1 : 0)}, " +
            $"'{{\"gates\":[{{\"gate\":\"setup\",\"passed\":true,\"reason\":\"constructed\",\"values\":{{\"depth\":\"2.5\"}}}}],\"notes\":[]}}', '{Shadow(live, variant)}', '{stop}', '{target}', {reward.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {strength.ToString(System.Globalization.CultureInfo.InvariantCulture)}, {band});");

    static void Bar(TemporaryStore store, string ticker, string session, string close) =>
        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
            $"VALUES ('{ticker}', '{session}', '{close}', '{close}', '{close}', '{close}', 1000000, 'constructed', '{session}T21:00:00Z', '{close}');");

    static RegisterRow Registered(long id, string candidate, string evaluator, string at) =>
        new(id, candidate, "rule", "test", evaluator, "{}", "v", CandidateFamily.Registered, null, DateTimeOffset.Parse(at, System.Globalization.CultureInfo.InvariantCulture), null);

    [Fact]
    public async Task TheStageWritesEachRulesNightTheVariantsOwnPicksAndTheIndexFunnelsAndARunAgainReplacesItsRows()
    {
        using var store = new TemporaryStore().Migrated();

        // The S&P 500's two nights for AAA, BBB and CCC: on the night before the variant fired on AAA and kept it at
        // 100 with the stop at 95 and the target at 110; tonight AAA closes at 94, through its stop, BBB fires for the
        // variant at a reward to risk of 3 and AAA fires again, while CCC fires for neither, and the live filter lists
        // BBB alone.
        foreach (var ticker in new[] { "AAA", "BBB", "CCC" })
        {
            Bar(store, ticker, NightBefore, "100");
            Bar(store, ticker, RuleNight, ticker == "AAA" ? "94" : "101");
        }

        FilterRow(store, "AAA", RuleNight, live: false, variant: true, 0.9, 9, "94", "90", "102", 2.0);
        FilterRow(store, "BBB", RuleNight, live: true, variant: true, 0.5, 5, "101", "99", "107", 3.0);
        FilterRow(store, "CCC", RuleNight, live: false, variant: false, 0.7, 7, "101", "99", "107", 3.0);
        store.Execute($"INSERT INTO family_night (session_date, families) VALUES ('{RuleNight}', '[\"pullback\",\"breakout\",\"drift\"]');");
        store.Execute($"INSERT INTO family_pick (session_date, ticker, family, state, place, also) VALUES ('{RuleNight}', 'BBB', 'pullback', 'listed', 1, '[]');");
        store.Execute(
            "INSERT INTO rule_pick (index_code, rule, ticker, session_date, family, place, entry, stop, target, reward_to_risk, cap, why) " +
            $"VALUES ('GSPC', '{FilterVariant}', 'AAA', '{NightBefore}', 'pullback', 1, '100', '95', '110', 2.0, 20, '{{}}');");

        // The S&P 400's night, its lists open: the breakout passed M1 and listed it, M2 had no setup and M3 sat under the
        // floors; the pullback and the drift passed none. The S&P 600 was not read.
        store.Execute(
            "INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced) VALUES " +
            $"('MID', '{RuleNight}', 3, 0.6, 1, '{IndexFamilies.Settings("MID")}', 0);");
        store.Execute(
            "INSERT INTO index_family_result (index_code, session_date, ticker, family, passed, place, entry, stop, target, trail, cap, order_by, reason) VALUES " +
            $"('MID', '{RuleNight}', 'M1', 'breakout', 1, 1, '50', '48', NULL, '2', 63, 2.0, NULL), " +
            $"('MID', '{RuleNight}', 'M2', 'breakout', 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, '{IndexNightRead.NoSetup}'), " +
            $"('MID', '{RuleNight}', 'M3', 'breakout', 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, '{IndexNightRead.UnderTheFloors}'), " +
            $"('MID', '{RuleNight}', 'M1', 'pullback', 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, '{IndexNightRead.NoSetup}'), " +
            $"('MID', '{RuleNight}', 'M2', 'pullback', 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, '{IndexNightRead.NoSetup}'), " +
            $"('MID', '{RuleNight}', 'M3', 'pullback', 0, NULL, NULL, NULL, NULL, NULL, NULL, NULL, '{IndexNightRead.NoSetup}');");
        store.Execute(
            "INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also) VALUES " +
            $"('MID', '{RuleNight}', 'M1', 'breakout', 'listed', 1, '[]');");

        // The S&P 400 breakout's history as the record command replays it: 600 sessions before the night, listing on the
        // first of every twelve, so 50 completed stretches of eleven empty nights and a mark of 11.
        var cards = new RuleCards(FixedClock.At(new DateTimeOffset(2026, 10, 7, 23, 50, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile);
        var history = Enumerable.Range(0, 600).Select(at => (new DateOnly(2024, 1, 1).AddDays(at), at % 12 == 0 ? 1 : 0)).ToArray();

        Assert.Equal(600, await cards.WriteHistoryAsync("MID", "breakout", RuleRows.ProvisionalRule, history));
        Assert.Equal(["history"], Texts(store, "SELECT DISTINCT source FROM rule_night;"));

        RegisterRow[] register = [Registered(1, LiveFilter, "swing-filter", "2026-10-04T16:00:00Z"), Registered(2, FilterVariant, "swing-filter", "2026-10-04T16:00:00Z")];
        var outcome = await cards.RunAsync("night-rule-cards", register, new DateTimeOffset(2026, 10, 7, 21, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 10, 7), outcome.Session);
        Assert.Equal([("GSPC", 2, 1, 0, false), ("MID", 5, 0, 0, false), ("SML", 0, 0, 0, false)], outcome.Indices.Select(index => (index.Index, index.Rules, index.Picks, index.Forming, index.Fault is not null)));

        // The live filter's row counts the page's list and its funnel the shadow's gates; the variant's its own pick; and
        // from 17.8 the S&P 400's fundamentals-first family, which listed nothing.
        Assert.Equal(
            [$"GSPC|pullback|{LiveFilter}|1|1|0", $"GSPC|pullback|{FilterVariant}|1|1|0", "MID|breakout|provisional|1|1|0", "MID|drift|provisional|1|0|1", "MID|fundamentals|provisional|1|0|1", "MID|heavyweight|provisional|1|0|", "MID|pullback|provisional|1|0|1"],
            Texts(store, $"SELECT index_code || '|' || family || '|' || rule || '|' || evaluated || '|' || listed || '|' || COALESCE(stretch, '') FROM rule_night WHERE session_date = '{RuleNight}' ORDER BY index_code, family, rule;"));
        Assert.Equal(
            RuleRows.GatesJson([("market", 3), ("trend and strength", 3), ("setup", 1), ("trigger", 1), ("trade", 1)]),
            Texts(store, $"SELECT gates FROM rule_night WHERE session_date = '{RuleNight}' AND rule = '{LiveFilter}';").Single());
        Assert.Equal(
            RuleRows.GatesJson([("market", 3), ("trend and strength", 3), ("setup", 3), ("trigger", 2), ("trade", 2)]),
            Texts(store, $"SELECT gates FROM rule_night WHERE session_date = '{RuleNight}' AND rule = '{FilterVariant}';").Single());
        Assert.Equal(
            RuleRows.GatesJson([(IndexNightRead.MarketClosed, 3), (IndexNightRead.NoSetup, 2), (IndexNightRead.UnderTheFloors, 1), (IndexNightRead.NoProfit, 1), (IndexNightRead.NoCover, 1)]),
            Texts(store, $"SELECT gates FROM rule_night WHERE session_date = '{RuleNight}' AND index_code = 'MID' AND family = 'breakout';").Single());

        // The S&P 400 breakout's night reads its stretch over the replayed history and tonight: listed tonight, so no
        // stretch, 50 completed stretches over 601 sessions and the mark of 11 its past empty nights set.
        Assert.Equal(["0|11|0|50|601"], Texts(store, $"SELECT stretch || '|' || mark || '|' || flagged || '|' || completed || '|' || sessions FROM rule_night WHERE session_date = '{RuleNight}' AND index_code = 'MID' AND family = 'breakout';"));

        // The variant's pick: BBB alone, AAA held by its pick from the night before, which ended at tonight's close
        // through its stop at 1.2 risks lost, and CCC not fired on.
        Assert.Equal([$"BBB|1|101|99|107|3.0"], Texts(store, $"SELECT ticker || '|' || place || '|' || entry || '|' || stop || '|' || target || '|' || reward_to_risk FROM rule_pick WHERE session_date = '{RuleNight}';"));
        Assert.Equal([$"AAA|{RuleNight}|-1.2"], Texts(store, $"SELECT ticker || '|' || ended_on || '|' || result FROM rule_pick WHERE session_date = '{NightBefore}';"));
        Assert.Equal(["{\"depth\":\"2.5\"}"], Texts(store, $"SELECT why FROM rule_pick WHERE session_date = '{RuleNight}';"));

        // A night run again replaces its own rows and leaves the history and the earlier pick as they were.
        var again = await cards.RunAsync("night-rule-cards-again", register, new DateTimeOffset(2026, 10, 7, 21, 0, 0, TimeSpan.Zero));

        Assert.Equal(outcome.Indices, again.Indices);
        Assert.Equal(["607"], Texts(store, "SELECT COUNT(*) FROM rule_night;"));
        Assert.Equal(["2"], Texts(store, "SELECT COUNT(*) FROM rule_pick;"));
        Assert.Equal([RuleCards.Ok, RuleCards.Ok], Texts(store, $"SELECT outcome FROM run_log WHERE stage = '{RuleCards.Stage}' ORDER BY rowid;"));
        Assert.StartsWith("2 rule(s), 1 pick(s) of their own and 0 forming on the S&P 500; 5 rule(s), 0 pick(s) of their own and 0 forming on the S&P 400; 0 rule(s)", Texts(store, $"SELECT detail FROM run_log WHERE stage = '{RuleCards.Stage}' ORDER BY rowid;").First(), StringComparison.Ordinal);
    }
}
