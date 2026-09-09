using EquityBrief.Core.Configuration;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data.Migrations;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Indicators;
using EquityBrief.Worker.Ladders;
using EquityBrief.Worker.Levels;
using EquityBrief.Worker.Swings;
using EquityBrief.Worker.Volume;
using EquityBrief.Worker.Membership;

namespace EquityBrief.Worker;

// The night, as an ordered list of named steps.
//
// It runs only the steps that exist, so the rest are absent rather than stubbed:
// a step that printed "skipped" would be a step a reader counts as run. Section
// 14's per-name work was one step in that document and one step here until 4.0,
// which is how three components shipped in phase 3 and were run by no night at
// all.
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
            // Section 14's per-name computations, one step each and in its
            // order. They were one step in the document until 4.0 and one step
            // here because the others did not exist. What that hid is that
            // three of them had existed since phase 3 and no night ran any of
            // them: the swing finder, the volume profile builder and the level
            // builder were called only from the suite, so a store this code
            // wrote held no swing, no profile and no band while every fixture
            // test passed over stores the tests built themselves.
            //
            // The order is load bearing rather than tidy. Levels read swings and
            // the profile, and the ladder reads levels, so a step out of place
            // computes over last night's rows or over none.
            //
            // None of them makes a request or calls a model.
            new("indicators", async () =>
            {
                var outcome = await new IndicatorEngine(clock, store.DatabaseFile)
                    .RunAsync(runId, night.Token);

                return $"{outcome.RowsWritten} rows written for {outcome.NamesComputed} name(s), " +
                    $"{outcome.NotAvailable} not available";
            }),
            new("swings", async () =>
            {
                var outcome = await new SwingFinder(clock, store.DatabaseFile)
                    .RunAsync(runId, night.Token);

                return $"{outcome.RowsWritten} rows written for {outcome.NamesExamined} name(s), " +
                    $"{outcome.Highs} high(s) and {outcome.Lows} low(s)";
            }),
            new("volume-profile", async () =>
            {
                var outcome = await new VolumeProfileBuilder(clock, store.DatabaseFile)
                    .RunAsync(runId, night.Token);

                return $"{outcome.RowsWritten} rows written for {outcome.NamesProfiled} name(s), " +
                    $"{outcome.NamesTooShort} too short for a window";
            }),
            new("levels", async () =>
            {
                var outcome = await new LevelBuilder(clock, store.DatabaseFile)
                    .RunAsync(runId, night.Token);

                return $"{outcome.BandsWritten} band(s) written for {outcome.NamesBanded} name(s), " +
                    $"{outcome.NamesSkipped} skipped";
            }),
            // The trend state and the ladder are one stage rather than two.
            // The classifier writes nothing and hands its label to the builder
            // that writes the row it sits on, which is what the catalogue says
            // of it in words.
            new("ladders", async () =>
            {
                var outcome = await new LadderBuilder(clock, store.DatabaseFile)
                    .RunAsync(indexCode, runId, night.Token);

                return $"{outcome.RowsWritten} row(s) written for {outcome.MembersConsidered} member(s), " +
                    $"{outcome.Uptrend} uptrend, {outcome.Downtrend} downtrend, {outcome.Range} range, " +
                    $"{outcome.NotClassified} not classified";
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
        // and the request count is what the five feeds counted, live or recorded
        // alike, because the count is on the interface.
        //
        // Which source it ran against is on the same line, and it is read off
        // the feeds rather than off the setting that chose them. Before 2.6 this
        // said "network request(s)" on a night that touched no network, because
        // a recorded feed counts the calls a live one would have made: that is
        // deliberate, since it is how a replay measures the cost shape, and it
        // is exactly why the line has to say which kind of night this was.
        var live = feeds.ReachesTheNetwork;

        output.WriteLine(
            $"nightly: green over {(live ? "the provider" : "a capture")}, 0 model calls, " +
            $"{feeds.Requests} {(live ? "network request(s)" : "request(s), none of them to a network")}, " +
            $"{feeds.WeightedCalls} weighted call(s) of {ProviderWeights.DailyAllowance}");

        return 0;
    }
}
