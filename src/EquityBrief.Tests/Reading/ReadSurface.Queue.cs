using System.Text.Json;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Bars;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Worker;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Reading;

// read-surface, 6.10: the run page's overnight queue region, drawn off the queue's row and
// the exchange's calendar, and the two places the queue's row is kept out of what it is not:
// the night's duration and the stages that failed.
public partial class ReadSurface
{
    static QueueRow Row(DateOnly night, string outcome = OvernightQueue.Ran) =>
        new(night, new DateTimeOffset(night.ToDateTime(new TimeOnly(21, 30)), TimeSpan.Zero), outcome,
            JsonSerializer.Serialize(new { night = night.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), queued = new[] { "AAPL", "KEYS" }, completed = new[] { new { ticker = "AAPL" } }, left = new[] { "KEYS" }, limitHours = 1, awake = "held awake" }));

    [Fact]
    public async Task TheRunPageSaysTheQueueRanAndHowManyQueuedPassesCompletedAndWereLeft()
    {
        var night = await FixtureReplay.NightAsync();

        using var store = night.Store;

        Assert.True(night.Code == 0, night.Error);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var body = await client.GetStringAsync("/screens/run/2026-09-08");

        // The counts read off the queue's own row rather than written here.
        using var detail = JsonDocument.Parse(Text(store, $"SELECT detail FROM run_log WHERE stage = '{OvernightQueue.Stage}';"));

        var queued = detail.RootElement.GetProperty("queued").GetArrayLength();
        var completed = detail.RootElement.GetProperty("completed").GetArrayLength();
        var left = detail.RootElement.GetProperty("left").GetArrayLength();

        Assert.True(queued > 0, "The fixture night queued nothing, so the region's counts say nothing.");

        Assert.Contains($"class=\"overnight-queue\" data-night=\"2026-09-08\" data-queue=\"ran\"", body, StringComparison.Ordinal);
        Assert.Contains($"data-queued=\"{queued}\" data-completed=\"{completed}\" data-left=\"{left}\" data-not-run=\"0\"", body, StringComparison.Ordinal);
        Assert.Contains($"the overnight queue ran on 2026-09-08: {completed} of {queued} queued pass(es) completed, {left} left for the next night", body, StringComparison.Ordinal);
        Assert.Contains("the machine was held awake", body, StringComparison.Ordinal);

        // And it sits where section 15.10 puts it, after stale and failed and before the harness.
        Assert.True(
            body.IndexOf("class=\"stale-and-failed\"", StringComparison.Ordinal) < body.IndexOf("class=\"overnight-queue\"", StringComparison.Ordinal)
            && body.IndexOf("class=\"overnight-queue\"", StringComparison.Ordinal) < body.IndexOf("class=\"harness\"", StringComparison.Ordinal),
            "The overnight queue's region is not between stale and failed and the harness.");
    }

    [Fact]
    public async Task ANightTheQueueDidNotRunIsNamedOnThePageAsANightItDidNotRun()
    {
        // Over the store a night wrote: the queue ran on 2026-09-08, and the page for
        // 2026-09-10, two traded sessions later, names both nights it did not run, the
        // 9th and the 10th, each on a line of its own, rather than drawing an empty region.
        var night = await FixtureReplay.NightAsync();

        using var store = night.Store;
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var body = await client.GetStringAsync("/screens/run/2026-09-10");

        Assert.Contains("data-queue=\"not run\"", body, StringComparison.Ordinal);
        Assert.Contains("data-not-run=\"2\"", body, StringComparison.Ordinal);
        Assert.Contains("<p class=\"not-run\" data-night=\"2026-09-09\">the overnight queue did not run on 2026-09-09</p>", body, StringComparison.Ordinal);
        Assert.Contains("<p class=\"not-run\" data-night=\"2026-09-10\">the overnight queue did not run on 2026-09-10</p>", body, StringComparison.Ordinal);

        // The projection over constructed rows, against the exchange's own calendar. From a
        // queue that ran on Thursday 2026-09-03 to a page for Wednesday 2026-09-09: Friday the
        // 4th traded, the weekend and Labor Day on the 7th did not, Tuesday the 8th traded, and
        // the page's own night has no row.
        var gap = RunScreen.Queue([Row(new DateOnly(2026, 9, 3))], new DateOnly(2026, 9, 9), ExchangeClosures.IsSession);

        Assert.Null(gap.Outcome);
        Assert.False(gap.NeverRan);
        Assert.Equal([new DateOnly(2026, 9, 4), new DateOnly(2026, 9, 8), new DateOnly(2026, 9, 9)], gap.NotRun);

        // With a row on the page's night, that night ran and the two before it did not.
        var ranTonight = RunScreen.Queue([Row(new DateOnly(2026, 9, 3)), Row(new DateOnly(2026, 9, 9), OvernightQueue.StoppedAtItsLimit)], new DateOnly(2026, 9, 9), ExchangeClosures.IsSession);

        Assert.Equal(OvernightQueue.StoppedAtItsLimit, ranTonight.Outcome);
        Assert.Equal([new DateOnly(2026, 9, 4), new DateOnly(2026, 9, 8)], ranTonight.NotRun);
        Assert.Equal((2, 1, 1), (ranTonight.Queued, ranTonight.Completed, ranTonight.Left));

        var drawn = new MarkRenderer().OvernightQueue(ranTonight);

        Assert.Contains("it started no pass once its limit of 1 hour(s) had passed", drawn, StringComparison.Ordinal);
        Assert.Contains("the overnight queue did not run on 2026-09-04", drawn, StringComparison.Ordinal);

        // A store whose queue never ran names no night, and says so, rather than naming every
        // night it holds from before the queue existed.
        var never = RunScreen.Queue([], new DateOnly(2026, 9, 9), ExchangeClosures.IsSession);

        Assert.True(never.NeverRan);
        Assert.Empty(never.NotRun);
        Assert.Contains("the overnight queue has not run on any night this store holds, up to 2026-09-09", new MarkRenderer().OvernightQueue(never), StringComparison.Ordinal);

        // A row under a later night says nothing about an earlier page.
        Assert.True(RunScreen.Queue([Row(new DateOnly(2026, 9, 10))], new DateOnly(2026, 9, 9), ExchangeClosures.IsSession).NeverRan);
    }

    [Fact]
    public async Task AQueueTheLocalModelStoppedSaysOnThePageThatItCouldNotRun()
    {
        var night = await FixtureReplay.NightAsync(NightQueue.FromFixture(Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05"), new RecordingAwake()) with { LocalModel = new FixtureExpectations.NothingAnsweringLocal() });

        using var store = night.Store;
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var body = await client.GetStringAsync("/screens/run/2026-09-08");

        Assert.Contains("data-outcome=\"unavailable\"", body, StringComparison.Ordinal);
        Assert.Contains("it could not run: " + ProseWriter.Unavailable, body, StringComparison.Ordinal);

        // A queue the local model stopped is a stage a person has to look at, and it is on the
        // stale and failed region's list.
        Assert.Contains($"data-stage=\"{OvernightQueue.Stage}\" data-outcome=\"{OvernightQueue.Unavailable}\"", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheNightsDurationIsTheArithmeticsAndAQueueAtItsLimitIsNotAStageThatFailed()
    {
        // A night whose arithmetic took three minutes and whose queue then ran for an hour, at
        // its limit. The header's duration is the three minutes the wall clock row bounds, and
        // the queue that stopped at its limit did what its limit is for.
        using var store = new TemporaryStore().Migrated();

        void Logged(string stage, string started, string ended, string outcome) =>
            store.Execute(
                "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) " +
                $"VALUES ('night-x', '{stage}', '{started}', '{ended}', '{outcome}', 0, 0, 0, '0', '');");

        Logged("fetch", "2026-09-09T01:10:00Z", "2026-09-09T01:11:00Z", "ok");
        Logged("listings", "2026-09-09T01:11:00Z", "2026-09-09T01:12:00Z", "ok");
        Logged("close", "2026-09-09T01:12:00Z", "2026-09-09T01:13:00Z", "ok");
        Logged(OvernightQueue.Stage, "2026-09-09T01:13:00Z", "2026-09-09T02:13:00Z", OvernightQueue.StoppedAtItsLimit);

        var api = Api(store);

        Assert.Equal("00:03:00", await api.NightDurationAsync(new DateOnly(2026, 9, 8)));

        var stages = RunScreen.Stages(await api.RunLogAsync(new DateOnly(2026, 9, 8)));

        Assert.Equal(4, stages.Count);
        Assert.Empty(RunScreen.Failed(stages));

        // A queue the local model stopped is one.
        Assert.Single(RunScreen.Failed([.. stages.Select(stage => stage.Stage == OvernightQueue.Stage ? stage with { Outcome = OvernightQueue.Unavailable } : stage)]));
    }

    static string Text(TemporaryStore store, string sql)
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToString(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture)!;
    }
}
