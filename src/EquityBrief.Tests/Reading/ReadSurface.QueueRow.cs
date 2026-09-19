using EquityBrief.Api.Reading;
using EquityBrief.Core.Bars;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Reading;

// read-surface, the 6.10 correction: a queue row a failed step wrote carries the step's message
// rather than the queue's record, and a night run again for its session writes a later row with an
// earlier instant, so the run page reads the night's row written last, and reads a message as one.
public partial class ReadSurface
{
    const string LockedQueue = "step 'queue' failed: SQLite Error 5: 'database is locked'.";

    [Fact]
    public async Task ANightRunAgainAfterItsQueueFailedIsDrawnFromTheRowWrittenLastAndTheFailedRowBreaksNothing()
    {
        var night = await FixtureReplay.NightAsync();

        using var store = night.Store;

        Assert.True(night.Code == 0, night.Error);

        // The store as a night run again for its session leaves it: the failed night's row written
        // first, carrying the step's message and the later instant, and the queue's own row written
        // after it with the earlier instant the session's evening stamps.
        store.Execute(
            $"CREATE TEMP TABLE kept AS SELECT * FROM run_log WHERE stage = '{OvernightQueue.Stage}'; " +
            $"DELETE FROM run_log WHERE stage = '{OvernightQueue.Stage}'; " +
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES " +
            $"('night-failed', '{OvernightQueue.Stage}', '2026-09-09T02:50:34Z', '2026-09-09T03:10:40Z', 'failed', 0, 0, 0, '0', '{LockedQueue.Replace("'", "''")}'); " +
            "INSERT INTO run_log SELECT * FROM kept;");

        Assert.Equal("failed", Text(store, $"SELECT outcome FROM run_log WHERE stage = '{OvernightQueue.Stage}' ORDER BY started_at DESC LIMIT 1;"));

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var response = await client.GetAsync("/screens/run/2026-09-08");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"The run page answered {(int)response.StatusCode}.");

        // Drawn from the row written last, the night run again, and not from the failed one.
        Assert.Contains("class=\"overnight-queue\" data-night=\"2026-09-08\" data-queue=\"ran\"", body, StringComparison.Ordinal);
        Assert.Contains("the overnight queue ran on 2026-09-08: ", body, StringComparison.Ordinal);
        Assert.DoesNotContain("the overnight queue failed on", body, StringComparison.Ordinal);

        // A night whose row written last is a failed step's says the queue failed, with the step's
        // own message, and states no count it does not hold.
        var on = new DateOnly(2026, 9, 8);
        var failedRow = new QueueRow(on, new DateTimeOffset(2026, 9, 9, 2, 50, 34, TimeSpan.Zero), "failed", LockedQueue);

        var failed = RunScreen.Queue([Row(on), failedRow], on, ExchangeClosures.IsSession);

        Assert.Equal("failed", failed.Outcome);
        Assert.Equal(LockedQueue, failed.Reason);
        Assert.Equal(0, failed.Queued);

        var drawn = new MarkRenderer().OvernightQueue(failed);

        Assert.Contains($"<p data-outcome=\"failed\">the overnight queue failed on 2026-09-08: {LockedQueue}</p>", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("queued pass(es) completed", drawn, StringComparison.Ordinal);

        // And the order is the order written, whatever the instants: the queue's own row written
        // after the failed one is the night's, though its instant is the earlier.
        var rewritten = RunScreen.Queue([failedRow, Row(on)], on, ExchangeClosures.IsSession);

        Assert.Equal(OvernightQueue.Ran, rewritten.Outcome);
        Assert.Equal(2, rewritten.Queued);
    }
}
