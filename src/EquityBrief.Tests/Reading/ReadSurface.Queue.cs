using EquityBrief.Api.Reading;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, the 6.10 correction of 2026-10-10: the overnight queue is retired, so the run page draws
// no region for it and no night's drafts, and a queue row an earlier night wrote is still read as that
// night ran: its time is no part of the night's duration and a queue at its limit is no stage that failed.
// see: The key under each figure is retired with the overnight queue that wrote it, and its stored rows are drawn nowhere
public partial class ReadSurface
{
    [Fact]
    public async Task TheRunPageDrawsNoQueueRegionAndNoNightsDrafts()
    {
        var night = await FixtureReplay.NightAsync();

        using var store = night.Store;

        Assert.True(night.Code == 0, night.Error);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var body = await client.GetStringAsync("/screens/run/2026-09-08");

        // The page drew, and drew the regions either side of where the queue's stood.
        Assert.Contains("class=\"stale-and-failed\"", body, StringComparison.Ordinal);
        Assert.Contains("class=\"harness\"", body, StringComparison.Ordinal);

        Assert.DoesNotContain("overnight-queue", body, StringComparison.Ordinal);
        Assert.DoesNotContain("the overnight queue", body, StringComparison.Ordinal);
        Assert.DoesNotContain("data-figure=\"overnight drafts\"", body, StringComparison.Ordinal);
        Assert.DoesNotContain("drafts written overnight", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APageOverAStoreHoldingAnAcceptedKeyDrawsNoKeyAndNoLineThatItWasLeftOut()
    {
        // KEYS holding the queue's accepted key for the night, then a later version of it left out, and MSFT the
        // accepted key alone: the name pages draw neither, by its prose or its name, the stale and failed region
        // names no key that fell back, and a name holding the key alone is no researched name.
        using var store = await FixtureReplay.ReplayedAsync();

        const string Prose = "Keysight closed at 333.42, a key the retired queue wrote for the night.";
        const string Reason = "a reason only a stored key carries";

        store.Execute(
            "INSERT INTO research_section (ticker, section, version, as_of, model, status, prose, source_ids, reject_reason) VALUES " +
            $"('KEYS', '{EquityBrief.Core.Research.ClaimRules.RetiredKey}', 1, '2026-09-08', 'a writer', 'accepted', '{Prose}', '[]', NULL), " +
            $"('KEYS', '{EquityBrief.Core.Research.ClaimRules.RetiredKey}', 2, '2026-09-08', 'a writer', 'fallback', '', '[]', '{Reason}'), " +
            $"('MSFT', '{EquityBrief.Core.Research.ClaimRules.RetiredKey}', 1, '2026-09-08', 'a writer', 'accepted', '{Prose}', '[]', NULL);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        foreach (var ticker in new[] { "KEYS", "MSFT" })
        {
            var page = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{ticker}"));

            Assert.Contains($"data-ticker=\"{ticker}\"", page, StringComparison.Ordinal);
            Assert.DoesNotContain(EquityBrief.Core.Research.ClaimRules.RetiredKey, page, StringComparison.Ordinal);
            Assert.DoesNotContain(Prose, page, StringComparison.Ordinal);
            Assert.DoesNotContain(Reason, page, StringComparison.Ordinal);
        }

        var run = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-09-08"));

        Assert.Contains("class=\"stale-and-failed\"", run, StringComparison.Ordinal);
        Assert.DoesNotContain(Reason, run, StringComparison.Ordinal);

        Assert.DoesNotContain(await Api(store).ResearchedAsync(), row => row.Ticker is "KEYS" or "MSFT");
    }

    [Fact]
    public async Task AnEarlierNightsQueueRowIsNoPartOfItsDurationAndAQueueAtItsLimitIsNoStageThatFailed()
    {
        // A night whose arithmetic took three minutes and whose queue then ran for an hour, at
        // its limit, with the quarters step before the queue and the report asked for after it,
        // as the night of 2026-09-28 ran. The header's duration is the three minutes the wall
        // clock row bounds, and the queue that stopped at its limit did what its limit was for.
        using var store = new TemporaryStore().Migrated();

        void Logged(string stage, string started, string ended, string outcome) =>
            store.Execute(
                "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) " +
                $"VALUES ('night-x', '{stage}', '{started}', '{ended}', '{outcome}', 0, 0, 0, '0', '');");

        Logged("fetch", "2026-09-09T01:10:00Z", "2026-09-09T01:11:00Z", "ok");
        Logged("listings", "2026-09-09T01:11:00Z", "2026-09-09T01:12:00Z", "ok");
        Logged("close", "2026-09-09T01:12:00Z", "2026-09-09T01:13:00Z", "ok");
        Logged("quarters", "2026-09-09T01:13:00Z", "2026-09-09T01:13:30Z", "ok");
        Logged(RunScreen.QueueStage, "2026-09-09T01:13:30Z", "2026-09-09T02:13:30Z", RunScreen.QueueAtItsLimit);
        Logged("report", "2026-09-09T02:13:30Z", "2026-09-09T02:13:31Z", "ok");

        var api = Api(store);

        Assert.Equal("00:03:00", await api.NightDurationAsync(new DateOnly(2026, 9, 8)));

        var stages = RunScreen.Stages(await api.RunLogAsync(new DateOnly(2026, 9, 8)));

        Assert.Equal(6, stages.Count);
        Assert.Empty(RunScreen.Failed(stages));

        // A queue the local model stopped is one.
        Assert.Single(RunScreen.Failed([.. stages.Select(stage => stage.Stage == RunScreen.QueueStage ? stage with { Outcome = "unavailable" } : stage)]));
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
