using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Ledger;

namespace EquityBrief.Tests.Reading;

// read-surface, 17.7: the learned score's part of a pick's card, matched by the night from the ledger over a constructed
// store and read back off the rendered card on Tonight and on the stock's page, and off the Loop page. With the newest
// run's score fitted and none of its proposals passed, the card draws the setups like the pick and the words that the
// score is not yet validated on this index and no rank, its forty setups' mean standing at the rule's own so the part
// says it is not distinguishable from the rule's record; with a newer run whose score passed, the rank is drawn on all
// three pages, worked by hand from the pick's own reading, and the setups the night matches, the nearest 250 of 300
// all winning a risk against the rule's two thirds, are told from the record and say nothing of it.
// see: A pick's card draws the setups like it under its rule beneath the rule's record, and the score's rank only once the score passed on its index
public partial class ReadSurface
{
    // The parts of the card's section and the Loop page's this check reaches.
    internal static readonly string[] ScorePageClaims =
    [
        CheckReach.Key(Scope.CardPage, Scope.CardScorePart),
        CheckReach.Key(Scope.LoopPage, Scope.LoopScore),
    ];

    static readonly DateOnly ScoreStart = new(2026, 1, 5);

    // Weekdays from the start, so the sessions are a plausible exchange calendar.
    static DateOnly ScoreDay(int at)
    {
        var day = ScoreStart;

        for (var step = 0; step < at; step++)
        {
            day = day.AddDays(1);

            while (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                day = day.AddDays(1);
            }
        }

        return day;
    }

    static string ScoreStamp(int at) => ScoreDay(at).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // A score weighing the close over its 20-session average alone, 0.2 a spread of 0.1, so 2 on its own scale; on the S&P
    // 500 its learning setups' scores rise by 0.02 a hundredth from 1.
    static RidgeModel ScoreOfTheCloseOverTwenty() => new(
        [LedgerReadings.IndexOf("close_over_twenty")],
        [1.0],
        [0.1],
        [0.2],
        [0.0, 0.0],
        0,
        400,
        -1,
        1,
        new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal) { ["GSPC"] = [.. Enumerable.Range(0, 101).Select(hundredth => 1 + (0.02 * hundredth))] });

    // Three S&P 500 members over seventy sessions at 100, AAA closing at 110 on the seventy-first, which the breakout lists.
    static TemporaryStore ScoreStore()
    {
        var store = new TemporaryStore().Migrated();
        var night = ScoreStamp(70);

        foreach (var ticker in new[] { "AAA", "BBB", "CCC" })
        {
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', '2025-01-02', NULL, '2026-01-05T21:00:00Z');");

            for (var at = 0; at <= 70; at++)
            {
                var close = ticker == "AAA" && at == 70 ? 110m : 100m;

                store.Execute(
                    "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
                    $"VALUES ('{ticker}', '{ScoreStamp(at)}', '{close}', '{close + 1m}', '{close - 1m}', '{close}', 1000000, 'constructed', '{ScoreStamp(at)}T21:00:00Z', '{close}');");
            }
        }

        foreach (var ticker in new[] { "AAA", "BBB", "CCC" })
        {
            store.Execute(
                "INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) " +
                $"VALUES ('{ticker}', '{night}', '[]', 0, '{{}}', '{{\"candidates\":[],\"skipped\":[]}}');");
        }

        store.Execute($"INSERT INTO family_night (session_date, families) VALUES ('{night}', '[\"breakout\"]');");
        store.Execute(
            "INSERT INTO family_result (session_date, ticker, family, passed, missed, place, entry, stop, target, order_by, exclusions, gates) " +
            $"VALUES ('{night}', 'AAA', 'breakout', 1, 0, 1, '110', '106', NULL, 2.0, '[]', '{{\"gates\":[]}}');");
        store.Execute(
            "INSERT INTO family_pick (session_date, ticker, family, state, place, also, held_family, held_night) " +
            $"VALUES ('{night}', 'AAA', 'breakout', 'listed', 1, '[]', NULL, NULL);");

        return store;
    }

    // A tester run on the S&P 500 storing the score fitted on all finished data for the breakout and its three proposals,
    // the first passed where asked.
    static string ScoreDate(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static void ScoreRun(TemporaryStore store, string run, bool passed)
    {
        var model = ScoreOfTheCloseOverTwenty();
        var night = ScoreDay(70);

        store.Execute($"INSERT INTO loop_run (run_id, month, index_code, through, started_at, ended_at, folds) VALUES ('{run}', '2026-04', 'GSPC', '{ScoreDate(night)}', '2026-04-14T12:00:00Z', '2026-04-14T12:01:00Z', 5);");
        store.Execute(
            "INSERT INTO loop_model (run_id, index_code, family, year, learned_from, learned_before, setups, readings, parameters, hash, pin, words) " +
            $"VALUES ('{run}', 'GSPC', 'breakout', 2027, '2019-01-02', '{ScoreDate(night.AddDays(1))}', 400, 'close_over_twenty', '{model.Canonical()}', '{model.Hash}', '{RidgeScore.Version}', 'the score weighs most close_over_twenty higher');");

        for (var rank = 1; rank <= 3; rank++)
        {
            store.Execute(
                "INSERT INTO loop_proposal (run_id, index_code, family, proposal, words, current_words, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, detectable, stable_folds, passed, finding) " +
                $"VALUES ('{run}', 'GSPC', 'breakout', '{EquityBrief.Worker.Loop.ScoreProcedures.ProposalName(rank)}', 'orders the list by the learned score {model.Hash}', 'the breakout rule today', 'risks', 400, 19, 0.001, 1, 1, 4, 4, 2.0, 1, 0.09, 5, {(passed && rank == 1 ? 1 : 0)}, 'the score weighs most close_over_twenty higher');");
        }
    }

    // A finished S&P 500 breakout setup the live rule passed, reading its close over its 20-session average.
    static void ScoreSetup(TemporaryStore store, string ticker, int at, double closeOverTwenty, double edge) =>
        store.Execute(
            "INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, picked, entry, stop, cap, risk_moves, close_over_twenty, result, benchmark, edge, sessions, end, ended_on, settled, source, pin) " +
            $"VALUES ('GSPC', 'breakout', '{ticker}', '{ScoreDate(new DateOnly(2025, 1, 2).AddDays(at))}', 'the breakout', 1, NULL, '50', '48', 63, 1.0, {closeOverTwenty.ToString("R", CultureInfo.InvariantCulture)}, {edge.ToString("R", CultureInfo.InvariantCulture)}, 0, {edge.ToString("R", CultureInfo.InvariantCulture)}, 5, 'target', '{ScoreDate(new DateOnly(2025, 1, 2).AddDays(at + 7))}', 1, 'history', '{LedgerReadings.Version}');");

    static async Task<(string Named, string Loop)> ScorePagesAsync(TemporaryStore store)
    {
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        return (
            WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/AAA/{ScoreStamp(70)}")),
            WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=500")));
    }

    static string ScorePart(string page)
    {
        var part = Regex.Match(page, "<section class=\"card-score\".*?</section>", RegexOptions.Singleline);

        Assert.True(part.Success, "The page draws no score part on the pick's card.");

        return part.Value;
    }

    [Fact]
    public async Task TheScoresRankIsDrawnOnlyOnceTheScorePassedOnItsIndexAndTheSetupsLikeThePickOnBothSidesOfTheirSentence()
    {
        using var store = ScoreStore();
        var clock = new WaitedClock(new DateTimeOffset(2026, 4, 13, 23, 50, 0, TimeSpan.Zero));

        // AAA's close over its 20-session average, the last of the twenty closes at 110 and the rest at 100: 110 over
        // 100.5, read through the ledger's own catalogue as the night reads it for the card.
        var readings = (await new SetupLedger(clock, store.DatabaseFile).ReadingsTonightAsync("GSPC"))("AAA")!;

        Assert.Equal(110 / 100.5, readings[LedgerReadings.IndexOf("close_over_twenty")]!.Value, 9);

        // The newest run fitted the score and none of its proposals passed; forty finished setups of forty stocks reading
        // 1.00 to 1.39, winning and losing a risk in turn.
        ScoreRun(store, "loop-test-GSPC-1", passed: false);

        for (var at = 0; at < 40; at++)
        {
            ScoreSetup(store, FormattableString.Invariant($"S{at:00}"), at, 1 + (0.01 * at), at % 2 == 0 ? 1 : -1);
        }

        await new DecisionCards(clock, store.DatabaseFile).RunAsync("cards-1");

        var stored = CardSimilar.Read(Strings(store, "SELECT similar FROM decision_card WHERE ticker = 'AAA';").Single());

        Assert.Equal([string.Empty], Strings(store, "SELECT COALESCE(score_rank, '') FROM decision_card WHERE ticker = 'AAA';"));
        Assert.Equal((40, 40, 0.0, 0.0), (stored.Count, stored.RuleSetups, stored.Mean, stored.RuleMean));
        Assert.False(stored.Distinguishable);

        var (named, loop) = await ScorePagesAsync(store);
        var part = ScorePart(named);

        Assert.Contains("<p class=\"card-caution\">Setups like this one under this rule on the S&P 500, not this stock's chance.</p>", part, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"card-rank degraded\" data-rank=\"none\">{CardSimilar.NotValidated}.</p>", part, StringComparison.Ordinal);
        Assert.Contains("<dd data-similar=\"40\"", part, StringComparison.Ordinal);
        Assert.Contains("40 of the rule's 40 finished setups on this index, the nearest on close_over_twenty", part, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"card-verdict\" data-distinguishable=\"false\">{CardSimilar.NotDistinguishable}</p>", part, StringComparison.Ordinal);
        Assert.DoesNotContain("places it at hundredth", part, StringComparison.Ordinal);

        Assert.Contains($"<p class=\"loop-rank degraded\" data-rank=\"none\">{CardSimilar.NotValidated}.</p>", loop, StringComparison.Ordinal);
        Assert.Contains($"data-validated=\"0\" data-hash=\"{ScoreOfTheCloseOverTwenty().Hash}\"", loop, StringComparison.Ordinal);

        // A newer run whose first proposal of the score passed; three hundred setups reading 0.5 up by 0.002, the first
        // fifty losing a risk and the rest winning one. AAA's score is twice 110 / 100.5, 2.189, at or over the hundredths
        // up to the 59th, 1 + 0.02 x 59 = 2.18; 298 of the three hundred read under it, and the nearest 250 are the 51st
        // to the 300th, every one winning, against the rule's two thirds.
        ScoreRun(store, "loop-test-GSPC-2", passed: true);
        store.Execute("DELETE FROM setup;");

        for (var at = 0; at < 300; at++)
        {
            ScoreSetup(store, FormattableString.Invariant($"T{at:000}"), at, 0.5 + (0.002 * at), at >= 50 ? 1 : -1);
        }

        await new DecisionCards(clock, store.DatabaseFile).RunAsync("cards-2");

        var matched = CardSimilar.Read(Strings(store, "SELECT similar FROM decision_card WHERE ticker = 'AAA';").Single());

        Assert.Equal(["59"], Strings(store, "SELECT score_rank FROM decision_card WHERE ticker = 'AAA';"));
        Assert.Equal((250, 300, 1.0, 1.0, 1.0), (matched.Count, matched.RuleSetups, matched.Mean, matched.Low, matched.High));
        Assert.Equal(200.0 / 300.0, matched.RuleMean!.Value, 12);
        Assert.True(matched.Distinguishable);

        (named, loop) = await ScorePagesAsync(store);
        part = ScorePart(named);

        Assert.Contains("<p class=\"card-rank\" data-rank=\"59\">The learned score, which passed the walk-forward tester on this index, places it at hundredth 59 of the setups it learned on here.</p>", part, StringComparison.Ordinal);
        Assert.DoesNotContain(CardSimilar.NotValidated, part, StringComparison.Ordinal);
        Assert.DoesNotContain(CardSimilar.NotDistinguishable, part, StringComparison.Ordinal);
        Assert.Contains("<dd data-similar=\"250\"", part, StringComparison.Ordinal);

        Assert.Contains("data-validated=\"1\"", loop, StringComparison.Ordinal);
        Assert.Contains("<span data-ticker=\"AAA\" data-rank=\"59\">AAA, hundredth 59</span>", loop, StringComparison.Ordinal);
        Assert.DoesNotContain(CardSimilar.NotValidated, loop, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACardWhoseScoreCannotBeReadNamesWhyAndIsWrittenRegardless()
    {
        using var store = ScoreStore();
        var clock = new WaitedClock(new DateTimeOffset(2026, 4, 13, 23, 50, 0, TimeSpan.Zero));

        // No run has fitted a score: the part says so. A run whose stored text cannot be read: the part names the failure
        // and the card is written with every other line.
        await new DecisionCards(clock, store.DatabaseFile).RunAsync("cards-1");

        Assert.Equal(
            "no score has been fitted for this rule on this index yet; the walk-forward tester fits it",
            CardSimilar.Read(Strings(store, "SELECT similar FROM decision_card WHERE ticker = 'AAA';").Single()).Unmatched);

        ScoreRun(store, "loop-test-GSPC-1", passed: true);
        store.Execute("UPDATE loop_model SET parameters = 'readings=no_such_reading';");

        await new DecisionCards(clock, store.DatabaseFile).RunAsync("cards-2");

        Assert.StartsWith(
            "the learned score could not be read tonight: ",
            CardSimilar.Read(Strings(store, "SELECT similar FROM decision_card WHERE ticker = 'AAA';").Single()).Unmatched,
            StringComparison.Ordinal);
        Assert.Equal([string.Empty], Strings(store, "SELECT COALESCE(score_rank, '') FROM decision_card WHERE ticker = 'AAA';"));
        Assert.Equal(["5"], Strings(store, "SELECT json_array_length(lines) FROM decision_card WHERE ticker = 'AAA';"));

        var (named, _) = await ScorePagesAsync(store);

        Assert.Contains("<p class=\"degraded\" data-similar=\"none\">No setups matched: the learned score could not be read tonight: ", ScorePart(named), StringComparison.Ordinal);
    }

    // The S&P 500's families night the card checks read: the breakout lists K1 and K2, which a learned score reaches, and
    // the pullback F1 to F5, the swing filter's, which none reaches. Before any run has fitted a score each breakout card
    // says so with the words that the score is not yet validated; K1's card stored with a rank and a part draws both on
    // Tonight, and a pullback card draws no part at all.
    [Fact]
    public async Task TonightDrawsEachPicksScorePartAsTheNightStoredItAndNoneWhereNoScoreReaches()
    {
        using var store = await FamilyPagesStore();

        await new DecisionCards(new WaitedClock(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero)), store.DatabaseFile).RunAsync("cards");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var rows = await CardsReadBack(client, StoredCards(store), TheSwitch);

        foreach (var ticker in new[] { "K1", "K2" })
        {
            var unscored = ScorePart(rows[ticker]);

            Assert.Contains($"<p class=\"card-rank degraded\" data-rank=\"none\">{CardSimilar.NotValidated}.</p>", unscored, StringComparison.Ordinal);
            Assert.Contains("<p class=\"degraded\" data-similar=\"none\">No setups matched: no score has been fitted for this rule on this index yet; the walk-forward tester fits it.</p>", unscored, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("card-score", rows["F1"], StringComparison.Ordinal);

        var stored = new CardSimilar(["close_over_twenty", "liquidity"], 250, 0.125, 0.2, 0.1, 0.3, 0.05, 3120);

        store.Execute($"UPDATE decision_card SET score_rank = 83, similar = '{stored.Json()}' WHERE ticker = 'K1';");

        var part = ScorePart((await CardsReadBack(client, StoredCards(store), TheSwitch))["K1"]);

        Assert.Contains("<p class=\"card-rank\" data-rank=\"83\">The learned score, which passed the walk-forward tester on this index, places it at hundredth 83 of the setups it learned on here.</p>", part, StringComparison.Ordinal);
        Assert.Contains("<dd data-similar=\"250\" data-distance=\"0.125\">250 of the rule's 3,120 finished setups on this index, the nearest on close_over_twenty, liquidity by their places among them, a median distance of 0.125</dd>", part, StringComparison.Ordinal);
        Assert.Contains("<dd data-mean=\"0.2\" data-low=\"0.1\" data-high=\"0.3\">a mean +0.20 of the risk against the same plan on every member that session, before costs, ninety per cent between +0.10 and +0.30</dd>", part, StringComparison.Ordinal);
        Assert.Contains("<dd data-rule-mean=\"0.05\">a mean +0.05 over every one of them</dd>", part, StringComparison.Ordinal);
        Assert.DoesNotContain(CardSimilar.NotValidated, part, StringComparison.Ordinal);
        Assert.DoesNotContain(CardSimilar.NotDistinguishable, part, StringComparison.Ordinal);
    }
}
