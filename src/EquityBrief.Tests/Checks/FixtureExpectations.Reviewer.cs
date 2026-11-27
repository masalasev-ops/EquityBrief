using System.Globalization;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Loop;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 24.36a: the reviewer matches a pick among the past cases like it under the same plan that closed
// before its night, at most 250, drawing no figure under 30 and leaving the pick's own stock out; its line is worked by hand
// and names no stock or date; the night's random draw repeats from its seed; and over a constructed store each listed pick and
// each drawn member is written once with what history expected, a case closing on or after the night never counted.
// see: The reviewer matches each pick among the past cases like it that closed before its night, and a seeded draw of the night's members the same way
public partial class FixtureExpectations
{
    internal static string[] ReviewerClaims =>
    [
        CheckReach.Key(Scope.LimitsTable, "The reviewer"),
    ];

    internal static string[] ReviewerRows => [.. ComponentAccess.ReviewerDeskRows, .. SchemaColumns.ReviewStoreRows, .. ReviewerClaims];

    static readonly int ReviewRsi = LedgerReadings.IndexOf("rsi");

    static IReadOnlyList<double?> ReviewReadings(double rsi)
    {
        var readings = new double?[LedgerReadings.Count];

        readings[ReviewRsi] = rsi;

        return readings;
    }

    [Fact]
    public void TheReviewerMatchesAPickAmongThePastCasesThatClosedBeforeItsNightAndItsLineIsWorkedByHand()
    {
        var night = new DateOnly(2026, 11, 20);
        var cases = new List<ReviewCase>();

        // Forty cases closed before the night, each reaching its take-profit and making $1 per $1 risked; ten more like it that
        // closed on or after the night, each losing $9; and the pick's own stock's case, losing $9 too.
        for (var at = 0; at < 40; at++)
        {
            cases.Add(new ReviewCase($"C{at}", new DateOnly(2018, 1, 2).AddDays(at * 30), new DateOnly(2018, 1, 9).AddDays(at * 30), ReviewReadings(50 + (at % 5)), 1.0, true));
        }

        for (var at = 0; at < 10; at++)
        {
            cases.Add(new ReviewCase($"L{at}", new DateOnly(2026, 11, 10).AddDays(at), night.AddDays(at), ReviewReadings(51), -9.0, false));
        }

        cases.Add(new ReviewCase("PICK", new DateOnly(2020, 3, 2), new DateOnly(2020, 3, 9), ReviewReadings(51), -9.0, false));

        var expected = Reviewer.Expect(cases, night, "PICK", ReviewReadings(51), [ReviewRsi], ["rsi"], ChanceForm.Target);

        Assert.Equal(new Expectation(40, 40, 1.0), expected);
        Assert.Equal("Past cases most like this one: of the 40 most like it under this plan, 40 (100%) reached the take-profit before the stop; on average $1.00 made or lost per $1 risked.", Reviewer.Line(expected));

        // Fewer than 30 closed before the night draw no figure and read the line saying so; a pick holding none of the
        // readings matched on is matched among none.
        Assert.Null(Reviewer.Expect(cases.Take(29).ToArray(), night, "PICK", ReviewReadings(51), [ReviewRsi], ["rsi"], ChanceForm.Target));
        Assert.Equal(Reviewer.TooFew, Reviewer.Line(null));
        Assert.Null(Reviewer.Expect(cases, night, "PICK", new double?[LedgerReadings.Count], [ReviewRsi], ["rsi"], ChanceForm.Target));

        // A losing half: twenty of the forty losing $1 instead, the mean nothing and twenty of forty reaching the take-profit.
        var halved = cases.Take(40).Select((one, at) => at % 2 == 0 ? one with { Result = -1.0, Won = false } : one).ToArray();

        Assert.Equal("Past cases most like this one: of the 40 most like it under this plan, 20 (50%) reached the take-profit before the stop; on average $0.00 made or lost per $1 risked.", Reviewer.Line(Reviewer.Expect(halved, night, "PICK", ReviewReadings(51), [ReviewRsi], ["rsi"], ChanceForm.Target)));

        // The draw: the same members on the same night and plan drawn the same way, a different night drawn its own way, and
        // never more than asked; the random mean is the drawn members' figures' mean, those holding none left out.
        string[] members = [.. Enumerable.Range(0, 300).Select(at => $"M{at}")];
        var drawn = Reviewer.Draw(members, "SML", night, "pullback");

        Assert.Equal(Reviewer.RandomMembers, drawn.Count);
        Assert.Equal(drawn, Reviewer.Draw(members.Reverse(), "SML", night, "pullback"));
        Assert.NotEqual(drawn, Reviewer.Draw(members, "SML", night.AddDays(1), "pullback"));
        Assert.Equal(2.0, Reviewer.RandomMean([new Expectation(30, 10, 1.0), null, new Expectation(40, 30, 3.0)]));
        Assert.Null(Reviewer.RandomMean([null, null]));
    }

    // Over a constructed store: the S&P 600 pullback's newest learned score weighs rsi alone; forty of its setups closed before
    // the night of 2026-11-20, from both ledgers, each making $1 and reaching its take-profit, and five closing on the night,
    // each losing $9; two pullbacks listed that night and 120 members holding the night's readings. Without the word nothing is
    // written; with it each pick and each of the 100 drawn members is written once, every figure the $1 the closed cases made,
    // and a second review of the night writes nothing.
    [Fact]
    public async Task TheReviewerWritesEachPickAndDrawnMemberOnceWithWhatTheCasesClosedBeforeItsNightMade()
    {
        using var store = new TemporaryStore().Migrated();
        var night = new DateOnly(2026, 11, 20);
        var model = new RidgeModel([ReviewRsi], [1.0], [50.0], [5.0], [0.0, 0.0], 0, 400, -1, 1, new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal));

        store.Execute("INSERT INTO loop_run (run_id, month, index_code, through, started_at, ended_at, folds) VALUES ('loop-test-SML-1', '2026-11', 'SML', '2026-11-06', '2026-11-07T12:00:00Z', '2026-11-07T12:01:00Z', 5);");
        store.Execute(
            "INSERT INTO loop_model (run_id, index_code, family, kind, year, learned_from, learned_before, setups, readings, parameters, hash, pin, words) " +
            $"VALUES ('loop-test-SML-1', 'SML', 'pullback', 'ridge', 2026, '2005-01-03', '2026-01-02', 400, 'rsi', '{model.Canonical()}', '{model.Hash}', '{RidgeScore.Version}', 'the score weighs most rsi higher');");

        using (var connection = store.Open())
        using (var transaction = connection.BeginTransaction())
        {
            void Case(string table, string ticker, DateOnly session, DateOnly ended, double result, string end, double rsi)
            {
                using var insert = connection.CreateCommand();

                insert.Transaction = transaction;
                insert.CommandText = table == "long_setup"
                    ? "INSERT INTO long_setup (index_code, family, ticker, session_date, rule, live_pass, picked, entry, stop, target, cap, result, cost, edge, end, ended_on, settled, source, pin, last_end, last_settled, run_id, rsi) VALUES ('SML', 'pullback', $ticker, $session, 'r', 1, 1, '100', '95', '110', 63, $result, 0, $result, $end, $ended, 1, 'long', 'p', $end, 1, 'r', $rsi);"
                    : "INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, picked, entry, stop, target, trail, cap, result, cost, edge, end, ended_on, settled, source, pin, rsi) VALUES ('SML', 'pullback', $ticker, $session, 'r', 1, 1, '100', '95', '110', NULL, 63, $result, 0, $result, $end, $ended, 1, 'night', 'p', $rsi);";
                insert.Parameters.AddWithValue("$ticker", ticker);
                insert.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                insert.Parameters.AddWithValue("$ended", ended.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                insert.Parameters.AddWithValue("$result", result);
                insert.Parameters.AddWithValue("$end", end);
                insert.Parameters.AddWithValue("$rsi", rsi);
                insert.ExecuteNonQuery();
            }

            for (var at = 0; at < 20; at++)
            {
                Case("long_setup", $"A{at}", new DateOnly(2010, 1, 4).AddDays(at * 60), new DateOnly(2010, 1, 11).AddDays(at * 60), 1.0, "target", 50 + (at % 5));
                Case("setup", $"B{at}", new DateOnly(2021, 1, 4).AddDays(at * 60), new DateOnly(2021, 1, 11).AddDays(at * 60), 1.0, "target", 50 + (at % 5));
            }

            for (var at = 0; at < 5; at++)
            {
                Case("setup", $"N{at}", night.AddDays(-7), night, -9.0, "stop", 51);
            }

            using var pick = connection.CreateCommand();

            pick.Transaction = transaction;
            pick.CommandText = "INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also, held_index, held_family, held_night) VALUES " +
                "('SML', '2026-11-20', 'P1', 'pullback', 'listed', 1, '[]', NULL, NULL, NULL), ('SML', '2026-11-20', 'P2', 'pullback', 'listed', 2, '[]', NULL, NULL, NULL), ('SML', '2026-11-20', 'P3', 'pullback', 'under another', NULL, '[]', NULL, NULL, NULL);";
            pick.ExecuteNonQuery();

            using var reading = connection.CreateCommand();

            reading.Transaction = transaction;
            reading.CommandText = "INSERT INTO session_reading (index_code, session_date, ticker, readings, pin, source) VALUES ('SML', '2026-11-20', $ticker, $readings, $pin, 'night');";
            reading.Parameters.AddWithValue("$ticker", string.Empty);
            reading.Parameters.AddWithValue("$readings", string.Empty);
            reading.Parameters.AddWithValue("$pin", LedgerReadings.Version);

            foreach (var ticker in Enumerable.Range(0, 118).Select(at => $"M{at}").Concat(["P1", "P2"]))
            {
                reading.Parameters["$ticker"].Value = ticker;
                reading.Parameters["$readings"].Value = Worker.Ledger.SetupLedger.ReadingsText(ReviewReadings(51));
                reading.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 21, 13, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var output = new StringWriter();
        var desk = new ReviewerDesk(clock, store.DatabaseFile, store.Root, output);

        long Count(string sql)
        {
            using var connection = store.Open();
            using var command = connection.CreateCommand();

            command.CommandText = sql;

            return (long)command.ExecuteScalar()!;
        }

        Assert.Equal(0, await desk.RunAsync("SML", night, word: false));
        Assert.Contains("2 pick(s) across 1 family(ies), 120 member(s)", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, Count("SELECT COUNT(*) FROM review;"));

        Assert.Equal(0, await desk.RunAsync("SML", night, word: true));
        Assert.Equal(2, Count("SELECT COUNT(*) FROM review WHERE kind = 'pick' AND cases = 40 AND reached = 40 AND mean = 1.0 AND matched_on = 'rsi';"));
        Assert.Equal(Reviewer.RandomMembers, Count("SELECT COUNT(*) FROM review WHERE kind = 'random' AND cases = 40 AND mean = 1.0;"));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM review WHERE ticker = 'P3';"));

        Assert.Equal(0, await desk.RunAsync("SML", night, word: true));
        Assert.Contains("is reviewed already", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(2 + Reviewer.RandomMembers, Count("SELECT COUNT(*) FROM review;"));
        Assert.Throws<SqliteException>(() => store.Execute("UPDATE review SET mean = 0;"));
        Assert.Throws<SqliteException>(() => store.Execute("DELETE FROM review;"));
    }
}
