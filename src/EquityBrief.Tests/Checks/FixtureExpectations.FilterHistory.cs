using System.Globalization;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Filter;
using EquityBrief.Worker.Returns;

namespace EquityBrief.Tests.Checks;

// The swing filter's results replayed for the sessions before its first stored night, over the fixture's
// two nights, 2026-09-03 and 2026-09-04, whose own results the night stored. The two sessions before the
// first, 2026-09-01 and 2026-09-02, hold none, and are the ones the command may replay.
// see: The swing filter's results are replayed for the sessions before its first stored night for the trigger's arrival alone, and removed once no night can read them
public partial class FixtureExpectations
{
    static readonly string[] BeforeTheNights = ["2026-09-01", "2026-09-02"];

    static void OpenVersion(TemporaryStore store) =>
        store.Execute(
            "INSERT INTO filter_version (version, settings, opened_at, closed_at, evidence) " +
            $"VALUES ('2', '{FilterSettings.Proposed.Write()}', '2026-09-02T00:00:00Z', NULL, 'the open version the sessions are replayed under');");

    static async Task<(int Exit, string Output, string Error)> History(TemporaryStore store, params string[] args)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);

        var exit = await FilterHistory.RunAsync(
            ["filter-history", .. args],
            FixedClock.At(new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates),
            store.DatabaseFile,
            output,
            error);

        return (exit, output.ToString(), error.ToString());
    }

    // Run as a person runs it: each session's results are stored under the replayed version with the
    // night's code, no rank and no shadow, one row for every member the replay read, each result the one
    // the counts give for that session under the open version's settings, and the nights' own rows are
    // untouched. A session a night drew, the night's own session, a range holding no session, no version
    // open and a range not named are each refused, and a refusal writes nothing.
    [Fact]
    public async Task TheFilterHistoryStoresEachReplayedSessionsResultsAndRefusesWhatANightDrew()
    {
        using var store = await WithTwoNights();

        string Rows() => string.Join(";", Query(store, "SELECT session_date || '|' || version || '|' || COUNT(*) FROM gate_result GROUP BY session_date, version ORDER BY 1;"));
        string Runs() => string.Join(";", Query(store, "SELECT COUNT(*) FROM run_log;"));

        var nightsOnly = Rows();
        var runs = Runs();

        Assert.Equal(2, Query(store, "SELECT DISTINCT session_date FROM gate_result;").Count);

        var none = await History(store, "--from", BeforeTheNights[0], "--through", BeforeTheNights[1]);

        Assert.Equal(1, none.Exit);
        Assert.Contains("no filter version is open", none.Error, StringComparison.Ordinal);

        OpenVersion(store);

        foreach (var (args, said) in new[]
        {
            (new[] { "--from", "2026-09-02", "--through", "2026-09-03" }, "2026-09-03 already hold the filter's results"),
            (new[] { "--from", "2026-09-04", "--through", "2026-09-04" }, "is the night's to draw"),
            (new[] { "--from", "2026-08-29", "--through", "2026-08-30" }, "no exchange session falls between 2026-08-29 and 2026-08-30"),
        })
        {
            var refused = await History(store, args);

            Assert.Equal(1, refused.Exit);
            Assert.Contains(said, refused.Error, StringComparison.Ordinal);
        }

        Assert.Equal(2, (await History(store, "--from", "2026-09-02")).Exit);
        Assert.Equal((nightsOnly, runs), (Rows(), Runs()));

        var replayed = await History(store, "--from", BeforeTheNights[0], "--through", BeforeTheNights[1]);

        Assert.Equal(0, replayed.Exit);
        Assert.Contains($"2 session(s) replayed under version 2's settings", replayed.Output, StringComparison.Ordinal);

        // What the counts give for the two sessions under the same settings, read by their own run.
        var expected = new List<string>();

        await new FilterCounts(store.DatabaseFile).CountAsync(
            Index,
            true,
            evaluated: (session, _, results) => expected.AddRange(results.Select(result =>
                FormattableString.Invariant($"{session:yyyy-MM-dd}|{result.Ticker}|{(result.TriggerEvent is { } fired ? (fired ? 1 : 0) : "none")}|{(result.Passed ? 1 : 0)}"))),
            under: new Dictionary<string, FilterSettings>(StringComparer.Ordinal) { ["2"] = FilterSettings.Proposed },
            only: [.. BeforeTheNights.Select(day => DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture))]);

        Assert.NotEmpty(expected);
        Assert.Equal(
            [.. expected.Order(StringComparer.Ordinal)],
            Query(store, $"SELECT session_date || '|' || ticker || '|' || IFNULL(trigger_event, 'none') || '|' || passed FROM gate_result WHERE version = '{ReplayedResults.Version}' ORDER BY 1;"));
        Assert.Equal(
            [.. BeforeTheNights.Select(day => $"{day}|{SwingFilter.CodeVersion}|0|0")],
            Query(store, $"SELECT session_date || '|' || MIN(code) || '|' || COUNT(rank) || '|' || COUNT(shadow) FROM gate_result WHERE version = '{ReplayedResults.Version}' GROUP BY session_date ORDER BY 1;"));
        Assert.Equal(nightsOnly, string.Join(";", Query(store, $"SELECT session_date || '|' || version || '|' || COUNT(*) FROM gate_result WHERE version <> '{ReplayedResults.Version}' GROUP BY session_date, version ORDER BY 1;")));

        // Its run log row, by hand and never a night.
        Assert.Equal(
            [$"{FilterHistory.Stage}|ok|{expected.Count}"],
            Query(store, $"SELECT stage || '|' || outcome || '|' || rows_written FROM run_log WHERE run_id GLOB '{FilterHistory.RunPrefix}*';"));
        Assert.Contains(FilterHistory.RunPrefix, RunScreen.RunsByHand);

        // Replayed once, a session is not replayed again.
        var again = await History(store, "--from", BeforeTheNights[0], "--through", BeforeTheNights[1]);

        Assert.Equal(1, again.Exit);
        Assert.Contains("2026-09-01, 2026-09-02 already hold the filter's results", again.Error, StringComparison.Ordinal);
    }

    // Removed as a person removes them: the replayed results of the sessions named are taken out and nothing
    // else, a night's own row on one of those sessions staying, and one run log row by hand names each session
    // with its count and the names that passed. The night after the fixture's second reads the three sessions
    // before it, and the night of 2026-09-04 run again reads 2026-09-01 to 2026-09-03, so a range reaching
    // 2026-09-01 is refused naming the sessions kept; a range holding no replayed result and a range not named
    // are refused too, and a refusal writes nothing.
    [Fact]
    public async Task TheFilterHistoryRemovesReplayedResultsAloneAndRefusesASessionANightCanStillRead()
    {
        using var store = await WithTwoNights();

        OpenVersion(store);

        Assert.Equal(0, (await History(store, "--from", "2026-08-27", "--through", BeforeTheNights[1])).Exit);

        string Replayed() => string.Join(";", Query(store, $"SELECT session_date || '|' || COUNT(*) FROM gate_result WHERE version = '{ReplayedResults.Version}' GROUP BY session_date ORDER BY 1;"));
        string Nights() => string.Join(";", Query(store, $"SELECT session_date || '|' || version || '|' || COUNT(*) FROM gate_result WHERE version <> '{ReplayedResults.Version}' GROUP BY session_date, version ORDER BY 1;"));
        string Runs() => string.Join(";", Query(store, "SELECT COUNT(*) FROM run_log;"));

        Assert.Equal(
            ["2026-08-27", "2026-08-28", "2026-08-31", "2026-09-01", "2026-09-02"],
            Query(store, $"SELECT DISTINCT session_date FROM gate_result WHERE version = '{ReplayedResults.Version}' ORDER BY 1;"));

        // One replayed row taken as a night's own, which a removal leaves where it is.
        var own = Query(store, "SELECT MIN(ticker) FROM gate_result WHERE session_date = '2026-08-28';").Single();

        store.Execute($"UPDATE gate_result SET version = '2' WHERE ticker = '{own}' AND session_date = '2026-08-28';");

        // And one replayed row taken as passing, so the names a removal says are read and not only a count;
        // the fixture's replayed sessions before its nights pass nobody.
        var passer = Query(store, $"SELECT MAX(ticker) FROM gate_result WHERE version = '{ReplayedResults.Version}' AND session_date = '2026-08-27';").Single();

        store.Execute($"UPDATE gate_result SET passed = 1 WHERE ticker = '{passer}' AND session_date = '2026-08-27';");

        var (replayed, nights, runs) = (Replayed(), Nights(), Runs());

        foreach (var (args, exit, said) in new[]
        {
            (new[] { "--from", "2026-08-31", "--through", BeforeTheNights[1] }, 1, "2026-09-01, 2026-09-02 may still be read by a night: the trigger's arrival reads 3 session(s) before a night"),
            (new[] { "--from", "2026-08-24", "--through", "2026-08-26" }, 1, "no replayed result is stored between 2026-08-24 and 2026-08-26"),
            (new[] { "--from", "2026-08-27" }, 2, "name the sessions to remove the replayed results of"),
        })
        {
            var refused = await History(store, ["--remove", .. args]);

            Assert.Equal(exit, refused.Exit);
            Assert.Contains(said, refused.Error, StringComparison.Ordinal);
        }

        Assert.Equal((replayed, nights, runs), (Replayed(), Nights(), Runs()));

        // What the three sessions held, each named with its count and the names passing.
        var held = string.Join("; ", ((string[])["2026-08-27", "2026-08-28", "2026-08-31"]).Select(session =>
        {
            var passing = Query(store, $"SELECT ticker FROM gate_result WHERE version = '{ReplayedResults.Version}' AND session_date = '{session}' AND passed = 1 ORDER BY ticker;");
            var count = Query(store, $"SELECT COUNT(*) FROM gate_result WHERE version = '{ReplayedResults.Version}' AND session_date = '{session}';").Single();

            return $"{session}, {count} result(s), " + (passing.Count == 0 ? "none passing" : $"{passing.Count} passing ({string.Join(", ", passing)})");
        }));
        Assert.Contains($"2026-08-27, ", held, StringComparison.Ordinal);
        Assert.Contains($"passing ({passer}", held, StringComparison.Ordinal);

        var total = Query(store, $"SELECT COUNT(*) FROM gate_result WHERE version = '{ReplayedResults.Version}' AND session_date BETWEEN '2026-08-27' AND '2026-08-31';").Single();
        var kept = string.Join(";", replayed.Split(';').Where(session => string.CompareOrdinal(session, "2026-09-01") >= 0));

        Assert.Equal(2, kept.Split(';').Length);

        var removed = await History(store, "--remove", "--from", "2026-08-27", "--through", "2026-08-31");

        Assert.Equal(0, removed.Exit);
        Assert.Equal($"{total} replayed result(s) of 3 session(s) removed: {held}", removed.Output.Trim());

        Assert.Equal(kept, Replayed());
        Assert.Equal(nights, Nights());
        Assert.Equal(["2"], Query(store, $"SELECT version FROM gate_result WHERE ticker = '{own}' AND session_date = '2026-08-28';"));

        // Its run log row, by hand and never a night, saying what it removed.
        Assert.Equal(
            [$"{FilterHistory.RemovalStage}|ok|0|{removed.Output.Trim()}"],
            Query(store, $"SELECT stage || '|' || outcome || '|' || rows_written || '|' || detail FROM run_log WHERE run_id GLOB '{FilterHistory.RunPrefix}*' AND stage = '{FilterHistory.RemovalStage}';"));

        // Removed once, a session has nothing left to remove.
        var again = await History(store, "--remove", "--from", "2026-08-27", "--through", "2026-08-31");

        Assert.Equal(1, again.Exit);
        Assert.Contains("no replayed result is stored between 2026-08-27 and 2026-08-31", again.Error, StringComparison.Ordinal);
    }

    // The trigger's arrival reads a replayed session, and nothing else does. Before the replay, the night
    // run again under an open version finds 2026-09-02, the third session of its window, holding no
    // results for some members and fails their trigger saying so; after it, none. The pages read no
    // replayed result for a session, a name or a name's newest, and the filler scores no replayed plan.
    [Fact]
    public async Task NoReaderButTheTriggersArrivalReadsAReplayedResult()
    {
        using var store = await WithTwoNights();

        OpenVersion(store);

        var night = FixedClock.At(FixtureEvening, SessionZones.UnitedStates);
        const string Unread = "%no gate result is stored for 2026-09-02%";

        int UnreadOnTheNight() => int.Parse(
            Query(store, $"SELECT COUNT(*) FROM gate_result WHERE session_date = '2026-09-04' AND json_extract(gates, '$.gates[3].reason') LIKE '{Unread}';").Single(),
            CultureInfo.InvariantCulture);

        await new SwingFilter(night, store.DatabaseFile).RunAsync(Index, "before-the-history");

        Assert.True(UnreadOnTheNight() > 0, "No member's trigger turned on 2026-09-02 before the replay, so the replay has nothing to answer.");

        Assert.Equal(0, (await History(store, "--from", BeforeTheNights[0], "--through", BeforeTheNights[1])).Exit);

        await new SwingFilter(night, store.DatabaseFile).RunAsync(Index, "after-the-history");

        Assert.Equal(0, UnreadOnTheNight());

        // The pages.
        var api = new ReadApi(store.DatabaseFile, night);
        var ticker = Query(store, $"SELECT MIN(ticker) FROM gate_result WHERE version = '{ReplayedResults.Version}' AND session_date = '2026-09-02';").Single();

        Assert.Empty(await api.GateResultsAsync(new DateOnly(2026, 9, 2)));
        Assert.Null(await api.GateResultAsync(ticker, new DateOnly(2026, 9, 2)));
        Assert.NotNull(await api.GateResultAsync(ticker));

        store.Execute($"DELETE FROM gate_result WHERE ticker = '{ticker}' AND version <> '{ReplayedResults.Version}';");

        Assert.Null(await api.GateResultAsync(ticker));

        // The filler: replayed rows carry plans and none is scored, where the nights' own plans are.
        Assert.NotEmpty(Query(store, $"SELECT ticker FROM gate_result WHERE version = '{ReplayedResults.Version}' AND swing_stop IS NOT NULL AND swing_target IS NOT NULL;"));

        await new ForwardReturnFiller(FixedClock.At(new DateTimeOffset(2026, 9, 5, 22, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile).RunAsync("history-fill");

        Assert.Empty(Query(store, "SELECT ticker FROM forward_return WHERE horizon IN ('swing', 'swing-20') AND session_date IN ('2026-09-01', '2026-09-02');"));
        Assert.NotEmpty(Query(store, "SELECT ticker FROM forward_return WHERE horizon IN ('swing', 'swing-20');"));
    }
}
