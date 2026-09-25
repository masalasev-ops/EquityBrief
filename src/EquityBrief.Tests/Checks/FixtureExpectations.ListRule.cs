using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Facts;
using EquityBrief.Worker.Nights;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 12.6: the rule each evening's list was drawn by, recorded by the night for the
// session its swing filter drew, and the facts retention reading each past evening by its own rule.
public partial class FixtureExpectations
{
    [Fact]
    public async Task TheNightRecordsTheFiltersSessionOnceItsRowsAreStoredAndAnEarlierSessionKeepsItsOwn()
    {
        using var store = await FixtureReplay.ReplayedAsync();

        // The replay the checks read records no rule, so the fixture's night reads as the reasons listed
        // it until the night's step records it. An earlier session holds its own rule beside it.
        Assert.Empty(Query(store, "SELECT session_date FROM list_rule;"));

        Insert(store, "INSERT INTO list_rule (session_date, rule) VALUES ('2026-09-04', 'reasons');");

        Assert.True(await NightClose.RecordRuleAsync(store.DatabaseFile));
        Assert.Equal(["2026-09-04|reasons", "2026-09-08|filter"], Query(store, "SELECT session_date, rule FROM list_rule ORDER BY session_date;"));

        // The session is the newest any name holds, which is the filter's night.
        Assert.Equal(Query(store, "SELECT MAX(session_date) FROM bar;"), Query(store, "SELECT MAX(session_date) FROM list_rule;"));

        // A session drawn again is recorded by the night that drew it last.
        Insert(store, "UPDATE list_rule SET rule = 'reasons' WHERE session_date = '2026-09-08';");

        Assert.True(await NightClose.RecordRuleAsync(store.DatabaseFile));
        Assert.Equal(["2026-09-04|reasons", "2026-09-08|filter"], Query(store, "SELECT session_date, rule FROM list_rule ORDER BY session_date;"));

        // A night whose filter stored no rows for its session records nothing, so the session reads as the
        // reasons listed it rather than as a filter's list of nobody.
        using var empty = await FixtureReplay.ReplayedAsync();

        Insert(empty, "DELETE FROM gate_result;");

        Assert.False(await NightClose.RecordRuleAsync(empty.DatabaseFile));
        Assert.Empty(Query(empty, "SELECT session_date FROM list_rule;"));
    }

    [Fact]
    public async Task TheFactsRetentionKeepsAPastEveningsPayloadWhereThatEveningsRuleListedTheName()
    {
        using var store = await WithFacts();
        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        var (fired, passed) = (FixtureExpectation.Names[0], FixtureExpectation.Names[1]);
        var payload = Query(store, $"SELECT payload FROM facts WHERE ticker = '{fired}';").Single().Replace("'", "''", StringComparison.Ordinal);

        // Two past evenings: on 2000-01-03 the reasons listed the store's evening, the first name firing one
        // and the second firing none while its gate row passed; on 2000-01-04 the filter listed it, the first
        // name firing two and not passing and the second passing. Worked by hand: kept whole, the first
        // name's file on the evening the reasons listed it and the second's on the evening the filter did;
        // emptied, the other two.
        foreach (var (session, filter, firedCount, passes) in new[] { ("2000-01-03", false, (1, 0), (0, 1)), ("2000-01-04", true, (2, 0), (0, 1)) })
        {
            foreach (var (ticker, count, pass) in new[] { (fired, firedCount.Item1, passes.Item1), (passed, firedCount.Item2, passes.Item2) })
            {
                Insert(
                    store,
                    "INSERT INTO facts (ticker, session_date, payload, payload_hash) VALUES " +
                    $"('{ticker}', '{session}', '{payload}', '{ticker}-{session}');");
                Insert(
                    store,
                    "INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons, band_strength) " +
                    $"VALUES ('{ticker}', '{session}', '[]', {count}, '{{}}', '{{\"candidates\":[],\"skipped\":[]}}', 0);");
                Insert(
                    store,
                    "INSERT INTO gate_result (ticker, session_date, version, code, market, trend, setup, trigger_pass, trade, exclusions, passed, rank, gates) " +
                    $"VALUES ('{ticker}', '{session}', '1', 'code', 1, 1, 1, 1, 1, '[]', {pass}, {(pass == 1 ? "1" : "NULL")}, '{{\"gates\":[],\"notes\":[]}}');");
            }

            if (filter)
            {
                Insert(store, $"INSERT INTO list_rule (session_date, rule) VALUES ('{session}', 'filter');");
            }
        }

        var outcome = await new ChangeDetector(clock, store.DatabaseFile).RunAsync("changes-by-rule");

        Assert.Equal(
            [$"{fired}|2000-01-03|whole", $"{fired}|2000-01-04|emptied", $"{passed}|2000-01-03|emptied", $"{passed}|2000-01-04|whole"],
            Query(store, "SELECT ticker, session_date, CASE WHEN payload = '' THEN 'emptied' ELSE 'whole' END FROM facts WHERE session_date < '2001-01-01' ORDER BY ticker, session_date;"));
        Assert.Equal(2, outcome.PayloadsEmptied);
    }
}
