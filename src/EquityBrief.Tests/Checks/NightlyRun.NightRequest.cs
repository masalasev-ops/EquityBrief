using EquityBrief.Worker.News;
using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Data.Migrations;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;
using EquityBrief.Worker.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// nightly-run, 11.4, 12.6 and 13.1: after the overnight queue the night asks for a report on the first
// six names its page draws, in the page's order on a night the families drew it and, on a night before
// them, improving businesses first where the night stored its readings and the swing filter's own
// order within a state, one request a name marked as asked by the night, and starts the drain it was
// handed as a press does.
public partial class NightlyRun
{
    // What the night starts, held by the test so a night is run without a drain reaching a model.
    internal sealed class NightLauncherForTheSuite : IDrainLauncher
    {
        public const string Line = "The worker was asked to start by the suite.";

        public const string LabellerLine = "The labeller was asked to start by the suite.";

        public int Started { get; private set; }

        public int LabellerStarts { get; private set; }

        public DrainStart Start()
        {
            Started++;

            return new DrainStart(true, Line);
        }

        public DrainStart StartTheLabeller()
        {
            LabellerStarts++;

            return new DrainStart(true, LabellerLine);
        }

        public const string BackupLine = "The store's copy was asked to start by the suite.";

        // Each copy the night asked for, and whether it was to wait for the labeller.
        public List<bool> BackupStarts { get; } = [];

        public DrainStart StartTheBackup(bool afterTheLabeller)
        {
            BackupStarts.Add(afterTheLabeller);

            return new DrainStart(true, BackupLine);
        }
    }

    static JsonElement Expected(string stage) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureFolder(), "expectations", stage + ".json"))).RootElement;

    static IReadOnlyList<string[]> StoreRows(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<string[]>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(at => reader.IsDBNull(at) ? "null" : Convert.ToString(reader.GetValue(at), CultureInfo.InvariantCulture)!)]);
        }

        return rows;
    }

    // The first name the night's list draws, worked by the test's own arithmetic off the gate rows the
    // night stored for the names the swing filter passed: the reward to risk of the trade the gate read,
    // higher first, then relative strength, then band strength, then the ticker. The stored rank is not
    // read, so a drain following something other than the filter's order is caught.
    static string? FirstPassed(TemporaryStore store, string night)
    {
        var rows = StoreRows(store, $"SELECT ticker, ladder_reward_to_risk, swing_reward_to_risk, strength, band_strength, gates FROM gate_result WHERE session_date = '{night}' AND passed = 1;")
            .Select(row =>
            {
                using var gates = JsonDocument.Parse(row[5]);
                var input = gates.RootElement.GetProperty("gates").EnumerateArray()
                    .Single(gate => gate.GetProperty("gate").GetString() == "trade")
                    .GetProperty("values").GetProperty("input").GetString();

                double? Figure(string value) => value == "null" ? null : double.Parse(value, CultureInfo.InvariantCulture);

                return (Ticker: row[0], Ratio: Figure(input == "swing" ? row[2] : row[1]) ?? double.MinValue, Strength: Figure(row[3]) ?? double.MinValue, Band: Figure(row[4]) ?? double.MinValue);
            })
            .ToList();

        rows.Sort((a, b) =>
            a.Ratio != b.Ratio ? b.Ratio.CompareTo(a.Ratio)
            : a.Strength != b.Strength ? b.Strength.CompareTo(a.Strength)
            : a.Band != b.Band ? b.Band.CompareTo(a.Band)
            : string.CompareOrdinal(a.Ticker, b.Ticker));

        return rows.Count == 0 ? null : rows[0].Ticker;
    }

    // Two of the fixture night's members passed, in an order neither their tickers nor their reasons give:
    // MSFT's trade at 3.2 before AAPL's at 2.4, and KEYS, which fired three reasons, failing a gate. The
    // ranks are the ones the filter's order gives those figures.
    static void Passing(TemporaryStore store, string night)
    {
        store.Execute($"UPDATE gate_result SET passed = 1, rank = 1, ladder_reward_to_risk = 3.2, swing_reward_to_risk = 3.2 WHERE ticker = 'MSFT' AND session_date = '{night}';");
        store.Execute($"UPDATE gate_result SET passed = 1, rank = 2, ladder_reward_to_risk = 2.4, swing_reward_to_risk = 2.4 WHERE ticker = 'AAPL' AND session_date = '{night}';");
        store.Execute($"UPDATE listing SET fired_count = 3 WHERE ticker = 'KEYS' AND session_date = '{night}';");
    }

    [Fact]
    public async Task AfterTheQueueTheNightAsksForNoReportOnANightNoNamePassedAndItsRowSaysWhy()
    {
        var expected = Expected("night-request");
        var launcher = new NightLauncherForTheSuite();

        using var store = new TemporaryStore();

        var (code, _, error) = await NightAsync(store, runId: "night-with-request", launcher: launcher);

        Assert.True(code == 0, error);

        var night = expected.GetProperty("night").GetString()!;

        // The fixture's night is the first its store filters, so no member's trigger can read an arrival
        // and none passes, which the test reads off the rows rather than off the drain.
        Assert.Null(FirstPassed(store, night));
        Assert.Equal(expected.GetProperty("listed").GetArrayLength(), StoreRows(store, $"SELECT ticker FROM gate_result WHERE session_date = '{night}' AND passed = 1;").Count);
        Assert.Equal(
            [[night, expected.GetProperty("rule").GetString()!]],
            StoreRows(store, "SELECT session_date, rule FROM list_rule;"));

        // No request and no drain started, and the step still runs after the close, the quarters step and
        // the queue, with the labeller's start after it, its own row saying why and recording no model call
        // and no request.
        Assert.Empty(StoreRows(store, "SELECT ticker FROM research_request;"));
        Assert.Equal(expected.GetProperty("count").GetInt32(), StoreRows(store, "SELECT ticker FROM research_request;").Count);
        Assert.Equal(0, launcher.Started);

        var stages = RunLog(store, "night-with-request");

        Assert.Equal(
            [EquityBrief.Worker.Nights.NightClose.Stage, EquityBrief.Worker.Quarters.QuarterFetcher.Stage, OvernightQueue.Stage, "report", NewsLabeller.NightStage, EquityBrief.Worker.Backup.StoreBackup.NightStage],
            stages.Select(row => row.Stage).TakeLast(6));
        Assert.Equal("ok", stages[^3].Outcome);
        Assert.Equal(expected.GetProperty("line").GetString(), stages[^3].Detail);
        Assert.Equal(
            [["0", "0"]],
            StoreRows(store, "SELECT model_calls, network_requests FROM run_log WHERE run_id = 'night-with-request' AND stage = 'report';"));
    }

    [Fact]
    public async Task TheNightAsksForTheFirstSixNamesDownItsPageInItsOrderAndNoOther()
    {
        // A night the families did not draw, which a replayed night is: the names the swing filter passed,
        // in its order, MSFT's trade at 3.2 before AAPL's at 2.4, both inside the six.
        using var store = await FixtureReplay.ReplayedAsync();

        var night = Expected("night-request").GetProperty("replayNight").GetString()!;

        Passing(store, night);

        var first = FirstPassed(store, night);

        Assert.Equal("MSFT", first);
        Assert.Empty(StoreRows(store, $"SELECT session_date FROM family_night WHERE session_date = '{night}';"));

        var at = FixedClock.At(new DateTimeOffset(2026, 9, 8, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var ask = await RequestDrain.AskForTheNightAsync(store.DatabaseFile, DateOnly.ParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture), at);

        Assert.Equal(["MSFT", "AAPL"], ask.Asked);
        Assert.Equal("MSFT is first on the list, and a report on it was asked for; AAPL is number 2 on the list, and a report on it was asked for", ask.Line);
        Assert.Equal(
            [["AAPL", "night", "paid", "outstanding"], ["MSFT", "night", "paid", "outstanding"]],
            StoreRows(store, "SELECT ticker, asked_from, lane, state FROM research_request ORDER BY ticker;"));

        // A night the families drew, constructed: seven stocks listed down the page across two cards, the
        // places given out of ticker order, one more held back by an open trade and one past its family's
        // five. The six asked for are the first six places, in that order; the seventh, the held and the
        // one past five are not. Section 17 states six, and the constant is the families' own.
        Assert.Equal(6, RequestDrain.NightAsksFor);
        Assert.Equal(EquityBrief.Core.Families.SetupFamilies.ReportsANight, RequestDrain.NightAsksFor);

        using var drawn = new TemporaryStore().Migrated();

        drawn.Execute("INSERT INTO family_night (session_date, families) VALUES ('2026-10-01', '[\"pullback\",\"breakout\"]');");

        foreach (var (ticker, family, state, place) in new[]
        {
            ("ZED", "pullback", "listed", "1"), ("YAK", "pullback", "listed", "2"), ("XYL", "pullback", "listed", "3"),
            ("WAB", "pullback", "listed", "4"), ("VFC", "pullback", "listed", "5"), ("AAA", "breakout", "listed", "6"),
            ("BBB", "breakout", "listed", "7"), ("CCC", "pullback", "open trade", "NULL"), ("DDD", "pullback", "past five", "NULL"),
        })
        {
            drawn.Execute(
                "INSERT INTO family_pick (session_date, ticker, family, state, place, also) " +
                $"VALUES ('2026-10-01', '{ticker}', '{family}', '{state}', {place}, '[]');");
        }

        var six = await RequestDrain.AskForTheNightAsync(drawn.DatabaseFile, new DateOnly(2026, 10, 1), at);

        Assert.Equal(["ZED", "YAK", "XYL", "WAB", "VFC", "AAA"], six.Asked);
        Assert.StartsWith("ZED is first on the list, and a report on it was asked for; YAK is number 2 on the list, and a report on it was asked for", six.Line, StringComparison.Ordinal);
        Assert.EndsWith("AAA is number 6 on the list, and a report on it was asked for", six.Line, StringComparison.Ordinal);
        Assert.Equal(6, StoreRows(drawn, "SELECT ticker FROM research_request WHERE asked_from = 'night' AND state = 'outstanding';").Count);
        Assert.Empty(StoreRows(drawn, "SELECT ticker FROM research_request WHERE ticker IN ('BBB', 'CCC', 'DDD');"));

        // And a night the families drew with every stock held back asks for none and says the page lists none.
        using var held = new TemporaryStore().Migrated();

        held.Execute("INSERT INTO family_night (session_date, families) VALUES ('2026-10-01', '[\"pullback\"]');");
        held.Execute("INSERT INTO family_pick (session_date, ticker, family, state, place, also, held_family, held_night) VALUES ('2026-10-01', 'CCC', 'pullback', 'open trade', NULL, '[]', 'pullback', '2026-09-29');");

        var none = await RequestDrain.AskForTheNightAsync(held.DatabaseFile, new DateOnly(2026, 10, 1), at);

        Assert.Empty(none.Asked);
        Assert.Equal("no stock is on the page's list for 2026-10-01, so no report was asked for", none.Line);
    }

    [Fact]
    public async Task OnANightTheMarketGateClosedTheNightAsksForNoReportAndSaysWhy()
    {
        using var store = await FixtureReplay.ReplayedAsync();

        var night = Expected("night-request").GetProperty("replayNight").GetString()!;

        // The two names made to pass, and then the market gate closed for every member, as a night whose
        // breadth fell below its floor stores it: no member passes, and the night asks for none.
        Passing(store, night);
        store.Execute($"UPDATE gate_result SET market = 0, passed = 0, rank = NULL WHERE session_date = '{night}';");

        Assert.Null(FirstPassed(store, night));

        var ask = await RequestDrain.AskForTheNightAsync(
            store.DatabaseFile,
            DateOnly.ParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            FixedClock.At(new DateTimeOffset(2026, 9, 8, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates));

        Assert.Empty(ask.Asked);
        Assert.Equal($"no name passed the swing filter on {night}, so no report was asked for", ask.Line);
        Assert.Empty(StoreRows(store, "SELECT ticker FROM research_request;"));
    }

    [Fact]
    public async Task ANameWithARequestWaitingGetsNoneAndTheNightsRowSaysSo()
    {
        foreach (var state in new[] { "outstanding", "writing" })
        {
            using var store = await FixtureReplay.ReplayedAsync();

            var night = Expected("night-request").GetProperty("replayNight").GetString()!;

            Passing(store, night);

            var first = FirstPassed(store, night);

            store.Execute(
                "INSERT INTO research_request (ticker, asked_at, asked_from, lane, state) VALUES "
                + $"('{first}', '2026-09-08T12:00:00Z', 'list', 'paid', '{state}');");

            var ask = await RequestDrain.AskForTheNightAsync(
                store.DatabaseFile,
                DateOnly.ParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                FixedClock.At(new DateTimeOffset(2026, 9, 8, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates));

            // The first is passed over and the second, AAPL, is asked for, since the night asks down its page.
            Assert.Equal(["AAPL"], ask.Asked);
            Assert.Equal($"{first} is first on the list and has a request {state} already, so none was added; AAPL is number 2 on the list, and a report on it was asked for", ask.Line);
            Assert.Equal(
                [["AAPL", "night", "outstanding"], [first!, "list", state]],
                StoreRows(store, "SELECT ticker, asked_from, state FROM research_request ORDER BY ticker;"));
        }

        // And a night the store holds no gate row for asks for nothing and says so.
        using var quiet = await FixtureReplay.ReplayedAsync();

        var none = await RequestDrain.AskForTheNightAsync(
            quiet.DatabaseFile,
            new DateOnly(2026, 8, 3),
            FixedClock.At(new DateTimeOffset(2026, 8, 3, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates));

        Assert.Empty(none.Asked);
        Assert.Equal("no name passed the swing filter on 2026-08-03, so no report was asked for", none.Line);
        Assert.Empty(StoreRows(quiet, "SELECT ticker FROM research_request;"));
    }

    [Fact]
    public async Task ANightRunAgainForAnEarlierSessionAsksForNoReport()
    {
        var launcher = new NightLauncherForTheSuite();

        using var store = new TemporaryStore();

        var output = new StringWriter();
        var error = new StringWriter();

        var code = await Nightly.RunAsync(
            new StoreLocation(Path.GetDirectoryName(store.DatabaseFile)!),
            FixtureFolder(),
            "GSPC",
            FixedClock.At(Night, SessionZones.UnitedStates),
            output,
            error,
            "night-again",
            launcher: launcher,
            askForTheFirstName: false);

        Assert.True(code == 0, error.ToString());
        Assert.Empty(StoreRows(store, "SELECT ticker FROM research_request;"));
        Assert.Equal(0, launcher.Started);
        Assert.Equal("no report was asked for, since this night was run again for an earlier session", RunLog(store, "night-again")[^3].Detail);

        // And it asks for no member's quarters: what it would store is today's answer and not that
        // night's, so the step says so and writes no ask and no quarter.
        Assert.Contains("  quarters: no quarters were asked for, since this night was run again for an earlier session", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(StoreRows(store, "SELECT ticker FROM quarter_ask;"));
        Assert.Empty(StoreRows(store, "SELECT ticker FROM reported_quarter;"));
    }

    [Fact]
    public void ANightForASessionNamedOnTheCommandLineAsksForNoReport()
    {
        // Through the worker's own entry point, as a rehearsal runs it. The session named on the
        // command line is what tells the night it is run again, so this reads the argument's wiring
        // rather than a flag the test hands the night. Both models point at a closed port, so a night
        // that did ask could start no paid pass. It starts from the worker's own build output, since
        // the suite's output carries the packages the suite resolves rather than the ones the worker
        // names.
        using var store = new TemporaryStore().Migrated();

        var dotnet = Shell.Locate("dotnet");
        var worker = Repository.BuildOutput("EquityBrief.Worker", "EquityBrief.Worker.dll");

        Assert.NotNull(dotnet);
        Assert.True(File.Exists(worker), $"No worker assembly at {worker}.");

        var environment = store.WorkerEnvironment();

        environment["EquityBrief__Models__Local__BaseAddress"] = "http://127.0.0.1:9/v1/";
        environment["EquityBrief__Models__Research__BaseAddress"] = "http://127.0.0.1:9/";

        var night = Shell.Run(dotnet!, [worker, "nightly", "--session", "2026-09-08", "--fixture", FixtureFolder()], store.Root, environment);

        Assert.True(night.ExitCode == 0, $"Exit {night.ExitCode}: {night.StandardError}");
        Assert.Contains("report: no report was asked for, since this night was run again for an earlier session", night.StandardOutput, StringComparison.Ordinal);
        Assert.Empty(StoreRows(store, "SELECT ticker FROM research_request;"));
    }
}
