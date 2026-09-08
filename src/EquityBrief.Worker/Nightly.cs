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

    public static async Task<int> RunAsync(
        StoreLocation store,
        string fixtureFolder,
        string indexCode,
        IClock clock,
        TextWriter output,
        TextWriter error,
        string? runId = null)
    {
        if (!Directory.Exists(fixtureFolder))
        {
            error.WriteLine(
                $"nightly: no fixture at '{fixtureFolder}'. The live feeds are not built yet, so a " +
                "night runs over a captured one and needs to be told which.");

            return 1;
        }

        // The instant and not only the date. A night that fails halfway and is
        // re-run is a second run, and SCHEMA's grain is one row per run per
        // stage: keyed on the date alone the re-run collides on the primary key
        // and the night fails on its first step, which is exactly what a
        // re-run is for. The caller may name its own, which is how a replay
        // records under an id it chose.
        runId ??= $"night-{clock.UtcNow:yyyyMMddTHHmmssZ}";

        var membership = RecordedIndexMembershipFeed.FromFile(
            Path.Combine(fixtureFolder, "index-constituents.json"));
        var historical = RecordedHistoricalBarFeed.FromFolder(fixtureFolder);
        var bulk = RecordedBulkPriceFeed.FromFolder(fixtureFolder);
        var corporate = RecordedCorporateActionFeed.FromFolder(fixtureFolder);

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
                    .LoadAsync(indexCode, runId);

                return $"{rows} rows written";
            }),
            new("backfill", async () =>
            {
                var outcome = await new Backfill(historical, clock, store.DatabaseFile)
                    .RunAsync(indexCode, runId);

                return $"{outcome.RowsWritten} rows written over {outcome.Requests} request(s)";
            }),
            new("fetch", async () =>
            {
                var outcome = await new BarFetcher(bulk, clock, store.DatabaseFile)
                    .RunAsync(indexCode, runId);

                return $"{outcome.RowsWritten} rows written for {outcome.MembersStored} member(s), " +
                    $"{outcome.RowsDropped} dropped below {outcome.Oldest:yyyy-MM-dd}, " +
                    $"{outcome.Requests} request(s)";
            }),
            new("actions", async () =>
            {
                var outcome = await new CorporateActionChecker(corporate, historical, clock, store.DatabaseFile)
                    .RunAsync(indexCode, runId);

                return $"{outcome.Actions} action(s), {outcome.Refetched} refetched, " +
                    $"{outcome.Suspect.Count} suspect, {outcome.Requests} request(s)";
            }),
        ];

        output.WriteLine($"nightly: {runId}, store {store.DatabaseFile}");

        foreach (var step in steps)
        {
            try
            {
                output.WriteLine($"  {step.Name}: {await step.Run()}");
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
        // and the request count is what the two feeds counted.
        var requests = historical.Requests + bulk.Requests + corporate.Requests;

        output.WriteLine($"nightly: green, 0 model calls, {requests} network request(s)");

        return 0;
    }
}
