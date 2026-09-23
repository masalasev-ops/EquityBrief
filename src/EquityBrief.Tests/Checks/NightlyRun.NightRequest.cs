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

// nightly-run, 11.4: after the overnight queue the night asks for a report on the first name
// drawn on its list, one request marked as asked by the night, and starts the drain it was
// handed as a press does.
public partial class NightlyRun
{
    // What the night starts, held by the test so a night is run without a drain reaching a model.
    sealed class NightLauncher : IDrainLauncher
    {
        public const string Line = "The worker was asked to start by the suite.";

        public int Started { get; private set; }

        public DrainStart Start()
        {
            Started++;

            return new DrainStart(true, Line);
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

    // The first name the night's list draws, worked by the test's own arithmetic off the listing
    // rows the night wrote: most reasons fired first, then the plan's reward to risk from its first
    // tranche, the zone's midpoint against its stop and its first traded exit, with a plan holding
    // none after every one holding one, then the ticker.
    static string FirstDrawn(TemporaryStore store, string night)
    {
        var rows = StoreRows(store, $"SELECT ticker, fired_count, plan_at_listing FROM listing WHERE session_date = '{night}' AND fired_count > 0;")
            .Select(row =>
            {
                using var plan = JsonDocument.Parse(row[2]);
                var root = plan.RootElement;

                decimal? Price(string name) =>
                    root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                        ? decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture)
                        : null;

                decimal? ratio = null;

                if (Price("entryLow") is { } low && Price("entryHigh") is { } high && Price("stop") is { } stop && Price("firstTradedTarget") is { } target)
                {
                    var middle = (low + high) / 2;

                    ratio = middle > stop ? Math.Round((target - middle) / (middle - stop), 4, MidpointRounding.AwayFromZero) : null;
                }

                return (Ticker: row[0], Fired: int.Parse(row[1], CultureInfo.InvariantCulture), Ratio: ratio);
            })
            .ToList();

        Assert.NotEmpty(rows);

        rows.Sort((a, b) =>
            b.Fired != a.Fired ? b.Fired.CompareTo(a.Fired)
            : (a.Ratio is null) != (b.Ratio is null) ? (a.Ratio is null ? 1 : -1)
            : a.Ratio != b.Ratio ? b.Ratio!.Value.CompareTo(a.Ratio!.Value)
            : string.CompareOrdinal(a.Ticker, b.Ticker));

        return rows[0].Ticker;
    }

    [Fact]
    public async Task AfterTheQueueTheNightAsksForAReportOnTheFirstNameItsListDrawsAndStartsTheDrainItWasHanded()
    {
        var expected = Expected("night-request");
        var launcher = new NightLauncher();

        using var store = new TemporaryStore();

        var (code, _, error) = await NightAsync(store, runId: "night-with-request", launcher: launcher);

        Assert.True(code == 0, error);

        var night = expected.GetProperty("night").GetString()!;
        var first = FirstDrawn(store, night);

        // One request, for the first name the list draws and no other, marked as the expectation
        // says a night's request is marked, and one drain asked to start.
        Assert.Equal(
            [[first, expected.GetProperty("askedFrom").GetString()!, expected.GetProperty("lane").GetString()!, expected.GetProperty("state").GetString()!]],
            StoreRows(store, "SELECT ticker, asked_from, lane, state FROM research_request;"));
        Assert.Equal(RequestDrain.NightAsksFor, expected.GetProperty("count").GetInt32());
        Assert.Equal(RequestDrain.NightAsksFor, launcher.Started);

        // The step runs last, after the close and the queue, and its own row says what it asked
        // for and what the drain came to, and records no model call and no request.
        var stages = RunLog(store, "night-with-request");

        Assert.Equal([EquityBrief.Worker.Nights.NightClose.Stage, OvernightQueue.Stage, "report"], stages.Select(row => row.Stage).TakeLast(3));
        Assert.Equal("ok", stages[^1].Outcome);
        Assert.Contains($"{first} is first on the list, and a report on it was asked for. {NightLauncher.Line}", stages[^1].Detail, StringComparison.Ordinal);
        Assert.Equal(
            [["0", "0"]],
            StoreRows(store, "SELECT model_calls, network_requests FROM run_log WHERE run_id = 'night-with-request' AND stage = 'report';"));
    }

    [Fact]
    public async Task ANameWithARequestWaitingGetsNoneAndTheNightsRowSaysSo()
    {
        foreach (var state in new[] { "outstanding", "writing" })
        {
            using var store = await FixtureReplay.ReplayedAsync();

            var night = Expected("night-request").GetProperty("replayNight").GetString()!;
            var first = FirstDrawn(store, night);

            store.Execute(
                "INSERT INTO research_request (ticker, asked_at, asked_from, lane, state) VALUES "
                + $"('{first}', '2026-09-08T12:00:00Z', 'list', 'paid', '{state}');");

            var ask = await RequestDrain.AskForTheNightAsync(
                store.DatabaseFile,
                DateOnly.ParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                FixedClock.At(new DateTimeOffset(2026, 9, 8, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates));

            Assert.Empty(ask.Asked);
            Assert.Equal($"{first} is first on the list and has a request {state} already, so none was added", ask.Line);
            Assert.Single(StoreRows(store, "SELECT ticker FROM research_request;"));
        }

        // And a night on which nothing fired asks for nothing and says so.
        using var quiet = await FixtureReplay.ReplayedAsync();

        var none = await RequestDrain.AskForTheNightAsync(
            quiet.DatabaseFile,
            new DateOnly(2026, 8, 3),
            FixedClock.At(new DateTimeOffset(2026, 8, 3, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates));

        Assert.Empty(none.Asked);
        Assert.Equal("no name fired on 2026-08-03, so no report was asked for", none.Line);
        Assert.Empty(StoreRows(quiet, "SELECT ticker FROM research_request;"));
    }

    [Fact]
    public async Task ANightRunAgainForAnEarlierSessionAsksForNoReport()
    {
        var launcher = new NightLauncher();

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
        Assert.Equal("no report was asked for, since this night was run again for an earlier session", RunLog(store, "night-again")[^1].Detail);
    }
}
