using System.Globalization;
using System.Net;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Reading;

// read-surface, a drain that stops on an error, the 9.2 correction of 2026-10-03: the guard the drain verb runs its
// put-back and its queue in writes one row of its own where an error escapes either and none where the work finishes,
// the queue page states the newest such row until a pass starts after it, and the run page's checklist names it on
// the night its row fell on.
// see: A drain that stops on an error writes a row of its own, and the queue page states it until a pass starts after it
public partial class ReadSurface
{
    // The rows a drain's stop adds: the queue page's region as the four parts its row states, the run page
    // checklist's item and section 18's row.
    internal static readonly string[] DrainStopRows =
    [
        CheckReach.Key("15.15 Queue", "A drain that stopped, the newest drain that stopped on an error outside a pass"),
        CheckReach.Key("15.15 Queue", "A drain that stopped, the time it started in New York's time and UTC and the error it stopped on"),
        CheckReach.Key("15.15 Queue", "A drain that stopped, stated above the requests until a pass starts after it"),
        CheckReach.Key("15.15 Queue", "A drain that stopped, nothing where no drain stopped"),
        CheckReach.Key("15.10 Run", "Anything to worry about, no drain stopped on an error, and where one did the time it started named with its error"),
        CheckReach.Key(Scope.FailureTable, "The drain stops on an error outside a pass"),
    ];

    [Fact]
    public async Task TheGuardWritesOneRowOfItsOwnWhereAnErrorEscapesADrainsWorkAndNoneWhereItFinishes()
    {
        using var store = new TemporaryStore().Migrated();

        var clock = FixedClock.At(UtcAt("2026-10-03T00:26:14Z"), SessionZones.UnitedStates);

        // Work that finishes writes nothing and says nothing.
        Assert.Null(await RequestDrain.GuardAsync(store.DatabaseFile, store.Root, clock, () => Task.CompletedTask));
        Assert.Empty(Rows(store, "SELECT run_id FROM run_log;"));

        // Work an error escapes, its words naming the store by its path, writes one row under a run named for the
        // instant the drain started, and the line the verb prints says it stopped and why.
        var said = await RequestDrain.GuardAsync(store.DatabaseFile, store.Root, clock, async () =>
        {
            await Task.Yield();

            throw new InvalidOperationException($"database is locked while '{store.DatabaseFile}' was read");
        });

        Assert.NotNull(said);
        Assert.StartsWith("stopped on an error: InvalidOperationException: database is locked while '", said, StringComparison.Ordinal);

        var row = Assert.Single(Rows(store, "SELECT run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail FROM run_log;"));

        Assert.Equal(
            ["drain-20261003T002614Z", DrainStops.Stage, "2026-10-03T00:26:14Z", "2026-10-03T00:26:14Z", DrainStops.Failed, "0", "0", "0", "0"],
            row.Take(9));
        Assert.StartsWith("InvalidOperationException: database is locked while '", row[9], StringComparison.Ordinal);
        Assert.False(AbsolutePaths.LooksAbsolute(row[9]), row[9]);

        // The drain verb runs its put-back and its queue inside the guard, once it holds the lock, and exits failing
        // where the guard says it stopped, read off its own source.
        var program = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Program.cs"));
        var verb = program[program.IndexOf("static async Task<int> Drain()", StringComparison.Ordinal)..];
        var end = verb.IndexOf("\n}", StringComparison.Ordinal);

        verb = verb[..end];

        var locked = verb.IndexOf("DrainLock.AcquireAsync", StringComparison.Ordinal);
        var guarded = verb.IndexOf("RequestDrain.GuardAsync", StringComparison.Ordinal);
        var putBack = verb.IndexOf("RequestDrain.PutBackAsync", StringComparison.Ordinal);
        var drained = verb.IndexOf("RequestDrain.DrainAsync", StringComparison.Ordinal);
        var failing = verb.IndexOf("if (stopped is not null)", StringComparison.Ordinal);

        Assert.True(locked >= 0 && guarded > locked && putBack > guarded && drained > putBack && failing > drained, "The drain verb does not run its put-back and its queue inside the guard once it holds the lock.");
        Assert.Contains("return 1;", verb[failing..], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheQueuePageStatesTheNewestDrainThatStoppedUntilAPassStartsAfterIt()
    {
        using var store = new TemporaryStore().Migrated();

        // A pass that ran before both drains, an older stop and the newest, which stopped two seconds after it started
        // at 00:26:14 UTC on 2026-10-03, 20:26 the evening before in New York, a failed row of another stage newer
        // than both, and the request the newest left outstanding.
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, detail) VALUES "
            + "('research-20261002T110000Z-MDT', 'research', '2026-10-02T11:00:00Z', '2026-10-02T11:05:00Z', 'ok', NULL),"
            + "('drain-20261002T120000Z', 'drain', '2026-10-02T12:00:00Z', '2026-10-02T12:00:03Z', 'failed', 'IOException: the disk was busy'),"
            + "('drain-20261003T002614Z', 'drain', '2026-10-03T00:26:14Z', '2026-10-03T00:26:16Z', 'failed', 'SqliteException: SQLite Error 5: ''database is locked''.'),"
            + "('night-20261002T233013Z', 'report', '2026-10-03T00:30:00Z', '2026-10-03T00:30:02Z', 'failed', 'a row of another stage');"
            + "INSERT INTO research_request (ticker, asked_at, asked_from, lane, state) VALUES "
            + "('QCOM', '2026-10-03T00:26:12Z', 'night', 'paid', 'outstanding');");

        async Task<string> QueueAt(string instant)
        {
            using var host = new PassHost(store.Root) { Clock = FixedClock.At(UtcAt(instant), SessionZones.UnitedStates) };
            using var client = host.CreateClient();

            return WebUtility.HtmlDecode(await client.GetStringAsync("/screens/queue"));
        }

        var stated = await QueueAt("2026-10-03T01:00:00Z");

        Assert.Contains(
            "<p class=\"drain-stopped\" data-started-at=\"2026-10-03T00:26:14Z\">The drain that started at 2026-10-02 20:26 New York (UTC-04:00), 00:26 UTC stopped on an error: SqliteException: SQLite Error 5: 'database is locked'.; what is queued waits for the next drain, which a press or the next night starts.</p>",
            stated,
            StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(stated, "class=\"drain-stopped\""));

        // Stated above the requests, the first of whose regions follows it.
        var stop = stated.IndexOf("class=\"drain-stopped\"", StringComparison.Ordinal);
        var requests = stated.IndexOf("data-region=\"outstanding\"", StringComparison.Ordinal);

        Assert.True(stop >= 0 && requests > stop, "The drain's stop is not stated above the requests.");

        // A pass of a later drain started after both, and neither stop is what the queue waits on.
        store.Execute("INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome) VALUES ('research-20261003T010500Z-QCOM', 'fundamentals', '2026-10-03T01:05:00Z', '2026-10-03T01:06:00Z', 'ok');");

        Assert.DoesNotContain("drain-stopped", await QueueAt("2026-10-03T01:10:00Z"), StringComparison.Ordinal);

        // And a store whose drains never stopped states nothing, though a pass of one ended on an error.
        using var quiet = new TemporaryStore().Migrated();

        quiet.Execute("INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, detail) VALUES ('research-20261003T000000Z-QCOM', 'research', '2026-10-03T00:00:00Z', '2026-10-03T00:04:00Z', 'failed', 'the pass ended on an error');");

        using var quietHost = new PassHost(quiet.Root) { Clock = FixedClock.At(UtcAt("2026-10-03T01:00:00Z"), SessionZones.UnitedStates) };
        using var quietClient = quietHost.CreateClient();

        Assert.DoesNotContain("drain-stopped", WebUtility.HtmlDecode(await quietClient.GetStringAsync("/screens/queue")), StringComparison.Ordinal);
    }

    [Fact]
    public void TheRunPagesChecklistNamesADrainThatStoppedOnTheNightItsRowFellOn()
    {
        var night = RunScreen.Night(
            [.. FinishedNight.Where(row => row.Stage != RunScreen.QueueStage)],
            TenthOfSeptember,
            DateTimeOffset.Parse("2026-09-11T01:00:00Z", CultureInfo.InvariantCulture),
            FifteenMinutes);

        IReadOnlyList<WorryItem> Read(params RunStageRow[] rows) => RunScreen.Worries([], night, [], [], rows);

        var stop = LogRow("drain-20260911T002614Z", DrainStops.Stage, "2026-09-11T00:26:14Z", "2026-09-11T00:26:16Z", DrainStops.Failed, detail: "SqliteException: database is locked");
        var named = Read(stop)[7];

        Assert.Equal("No drain stopped on an error", named.Item);
        Assert.Equal((WorryItem.Failed, "the drain that started at 00:26 UTC stopped on an error: SqliteException: database is locked"), (named.State, named.Why));

        // A failed row of another stage, and a night holding no stop, hold it.
        var other = LogRow("research-20260911T001000Z-MDT", "research", "2026-09-11T00:10:00Z", "2026-09-11T00:20:00Z", "failed");

        Assert.Equal((WorryItem.Held, (string?)null), (Read(other)[7].State, Read(other)[7].Why));
        Assert.Equal(WorryItem.Held, Read()[7].State);
    }
}
