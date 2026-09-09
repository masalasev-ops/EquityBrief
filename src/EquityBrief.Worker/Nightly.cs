using EquityBrief.Core.Configuration;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data.Migrations;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Membership;

namespace EquityBrief.Worker;

// The night, as an ordered list of named steps.
//
// It runs only the steps that exist. Section 14's run order has nine and four
// are built, so the rest are absent rather than stubbed: a step that printed
// "skipped" would be a step a reader counts as run.
//
// Every failure names the step. A night that fails silently in the middle is
// one the operator finds by noticing the page is stale in the morning, and the
// run log is what they read instead.
public static class Nightly
{
    public sealed record Step(string Name, Func<Task<string>> Run);

    // Two figures the whole nightly path rests on, carried out of the run so
    // the script can print them and nightly-cost can read them back.
    public sealed record NightOutcome(string RunId, int ModelCalls, int NetworkRequests, IReadOnlyList<string> Steps);

    // What the command line calls: resolve the feeds from a fixture folder,
    // optionally replace one of them with a live feed, then run.
    //
    // `bulk` is how 2.1 puts one live feed on the night while the other three
    // stay recorded. It is a parameter rather than a setting because the setting
    // is 2.6's work, and a half-built configuration path is the thing that lets
    // a mistyped fixture folder resolve to the network.
    public static async Task<int> RunAsync(
        StoreLocation store,
        string fixtureFolder,
        string indexCode,
        IClock clock,
        TextWriter output,
        TextWriter error,
        string? runId = null,
        IBulkPriceFeed? bulk = null,
        TimeSpan? deadline = null)
    {
        if (!Directory.Exists(fixtureFolder))
        {
            error.WriteLine(
                $"nightly: no fixture at '{fixtureFolder}'. Only the bulk price feed reaches the " +
                "provider so far, so the rest of a night runs over a captured one and needs to be " +
                "told which.");

            return 1;
        }

        var feeds = NightFeeds.FromFixture(fixtureFolder);

        return await RunAsync(
            store,
            bulk is null ? feeds : feeds with { Bulk = bulk },
            indexCode,
            clock,
            output,
            error,
            runId,
            deadline);
    }

    public static async Task<int> RunAsync(
        StoreLocation store,
        NightFeeds feeds,
        string indexCode,
        IClock clock,
        TextWriter output,
        TextWriter error,
        string? runId = null,
        TimeSpan? deadline = null)
    {
        // The night's deadline, and the thing that can cancel it.
        //
        // Every feed interface has accepted a cancellation token since 1.1 and
        // nothing supplied one, so a night that hung on a socket hung until
        // somebody looked. This is the source, and it is the night's rather than
        // a request's: a per-request timeout bounds one attempt, and three
        // attempts on nine steps is a bound nobody would recognise as an
        // evening.
        // see: A feed is tried three times with a doubling backoff, and the night has a deadline it cannot move
        var limit = deadline ?? RetryPolicy.Standard.Deadline;

        using var night = new CancellationTokenSource(limit);

        // The instant and not only the date. A night that fails halfway and is
        // re-run is a second run, and SCHEMA's grain is one row per run per
        // stage: keyed on the date alone the re-run collides on the primary key
        // and the night fails on its first step, which is exactly what a
        // re-run is for. The caller may name its own, which is how a replay
        // records under an id it chose.
        runId ??= $"night-{clock.UtcNow:yyyyMMddTHHmmssZ}";

        var (membership, historical, bulkFeed, corporate, _) = feeds;

        // The order is section 14's, for the steps that exist. Migrate is not
        // one of its steps: it is what makes the store able to hold the night,
        // and a night against a store a version behind is a night that fails on
        // a missing column halfway through.
        Step[] steps =
        [
            new("migrate", () =>
            {
                var outcome = MigrationRunner.Standard().Apply(store.DatabaseFile);

                return Task.FromResult(outcome.Applied.Count == 0
                    ? $"schema version {outcome.To}, nothing pending"
                    : $"applied {outcome.Applied.Count}, {outcome.From} to {outcome.To}");
            }),
            new("membership", async () =>
            {
                var rows = await new MembershipLoader(membership, clock, store.DatabaseFile)
                    .LoadAsync(indexCode, runId, night.Token);

                return $"{rows} rows written";
            }),
            new("backfill", async () =>
            {
                var outcome = await new Backfill(historical, clock, store.DatabaseFile)
                    .RunAsync(indexCode, runId, night.Token);

                return $"{outcome.RowsWritten} rows written over {outcome.Requests} request(s)";
            }),
            new("fetch", async () =>
            {
                var outcome = await new BarFetcher(bulkFeed, clock, store.DatabaseFile)
                    .RunAsync(indexCode, runId, night.Token);

                return $"{outcome.RowsWritten} rows written for {outcome.MembersStored} member(s), " +
                    $"{outcome.RowsDropped} dropped below {outcome.Oldest:yyyy-MM-dd}, " +
                    $"{bulkFeed.NotSessions.Count} row(s) listed and not traded, " +
                    $"{outcome.Unaccounted.Count} member(s) the file carried nothing for, " +
                    $"{outcome.Requests} request(s)";
            }),
            new("actions", async () =>
            {
                var outcome = await new CorporateActionChecker(corporate, historical, clock, store.DatabaseFile)
                    .RunAsync(indexCode, runId, night.Token);

                return $"{outcome.Actions} action(s), {outcome.Refetched} refetched, " +
                    $"{outcome.Suspect.Count} suspect, {outcome.Requests} request(s)";
            }),
        ];

        output.WriteLine($"nightly: {runId}, store {store.DatabaseFile}");

        foreach (var step in steps)
        {
            // The local stop, before the provider's own. Exceeding the daily
            // allowance arrives from the provider as a rejected rate, which is
            // an unavailable feed and loses the reason; stopping here says what
            // actually happened.
            // see: The night's cost is counted in weighted calls against the stated daily allowance
            if (feeds.WeightedCalls >= ProviderWeights.DailyAllowance)
            {
                error.WriteLine(
                    $"nightly: stopped before step '{step.Name}'. The night has spent " +
                    $"{feeds.WeightedCalls} weighted call(s) against an allowance of " +
                    $"{ProviderWeights.DailyAllowance}. Last night's bars are kept and every name " +
                    "is stale.");

                return 1;
            }

            try
            {
                output.WriteLine($"  {step.Name}: {await step.Run()}");
            }
            catch (OperationCanceledException) when (night.IsCancellationRequested)
            {
                // The deadline, named as itself rather than as a step that
                // failed. A night that ran out of time and a night whose
                // provider refused are different mornings, and a cancellation
                // reported as a failure reads as the second.
                error.WriteLine(
                    $"nightly: step '{step.Name}' passed the night's deadline of " +
                    $"{limit.TotalMinutes:0.###} minute(s) and was stopped. Last night's bars are " +
                    "kept and every name is stale.");

                return 1;
            }
            catch (Exception failure)
            {
                // The step is named, and the exit code is non-zero. Both halves
                // matter: a scheduler reads the code and a person reads the name.
                error.WriteLine($"nightly: step '{step.Name}' failed: {failure.Message}");

                return 1;
            }
        }

        // The nightly claim, printed where the operator reads it rather than
        // only stored. Zero model calls because nothing on this path calls one,
        // and the request count is what the four feeds counted, live or
        // recorded alike, because the count is on the interface.
        var requests = feeds.Requests;

        output.WriteLine(
            $"nightly: green, 0 model calls, {requests} network request(s), " +
            $"{feeds.WeightedCalls} weighted call(s) of {ProviderWeights.DailyAllowance}");

        return 0;
    }
}
