using EquityBrief.Core.Bars;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data.Migrations;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Calendar;
using EquityBrief.Worker.Indicators;
using EquityBrief.Worker.Facts;
using EquityBrief.Worker.Ladders;
using EquityBrief.Worker.Moves;
using EquityBrief.Worker.Levels;
using EquityBrief.Worker.News;
using EquityBrief.Worker.Nights;
using EquityBrief.Worker.Returns;
using EquityBrief.Worker.Shortlist;
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
    // `Stages` are the run log stages the step writes, where they are not the
    // step's own name. Only the facts step differs, since it runs the assembler
    // and the detector and each writes its own row; a stop inside it is recorded
    // under the first of the two the run does not hold yet.
    public sealed record Step(string Name, Func<Task<string>> Run, IReadOnlyList<string>? Stages = null);

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
        runId ??= FormattableString.Invariant($"night-{clock.UtcNow:yyyyMMddTHHmmssZ}");

        var (membership, historical, bulkFeed, corporate, calendar, _) = feeds;

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
                    FormattableString.Invariant($"{outcome.RowsDropped} dropped below {outcome.Oldest:yyyy-MM-dd}, ") +
                    $"{bulkFeed.NotSessions.Count} row(s) listed and not traded, " +
                    $"{bulkFeed.Unreadable.Count} row(s) outside the index the reader refused, " +
                    $"{outcome.Unaccounted.Count} member(s) the file carried nothing for, " +
                    $"{(outcome.CaughtUp ?? []).Count} missed session(s) caught up, " +
                    $"{outcome.Requests} request(s)";
            }),
            new("actions", async () =>
            {
                var outcome = await new CorporateActionChecker(corporate, historical, clock, store.DatabaseFile)
                    .RunAsync(indexCode, runId, night.Token);

                return $"{outcome.Actions} action(s), {outcome.Refetched} refetched, " +
                    $"{outcome.Suspect.Count} suspect, {outcome.Requests} request(s)";
            }),
            // The calendar, one request for the whole index's dated events over
            // the window. The earnings date is needed nightly by the ladder
            // builder and the shortlist builder, so it is here rather than on
            // the on-demand path, and it is one request whatever the universe
            // size.
            new("calendar", async () =>
            {
                var outcome = await new CalendarFetcher(calendar, clock, store.DatabaseFile)
                    .RunAsync(indexCode, clock.SessionDateAt(clock.UtcNow), runId, night.Token);

                return FormattableString.Invariant($"{outcome.EventsReturned} event(s) over {outcome.From:yyyy-MM-dd} to ") +
                    FormattableString.Invariant($"{outcome.To:yyyy-MM-dd}, {outcome.RowsWritten} stored, ") +
                    $"{outcome.NotMembers} for names the index does not hold, " +
                    $"{outcome.RowsDropped} dropped, {outcome.Requests} request(s)";
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
            // Section 14's step 11, and the first stage after the ladder. It
            // reads the same bars every stage before it read and writes the
            // rows the how-it-got-here table is built from.
            new("moves", async () =>
            {
                var outcome = await new MoveAnnotator(clock, store.DatabaseFile)
                    .RunAsync(runId, night.Token);

                return $"{outcome.RowsWritten} move(s) written for {outcome.NamesExamined} name(s), " +
                    $"{outcome.RowsDropped} dropped";
            }),
            // Section 14's step 12. It runs before the facts file, which is the
            // order section 14 states, and the facts assembler reads the
            // listings from 5.4 onward for the same reason.
            new("listings", async () =>
            {
                var outcome = await new ShortlistBuilder(clock, store.DatabaseFile)
                    .RunAsync(indexCode, runId, night.Token);

                return $"{outcome.RowsWritten} row(s) for {outcome.MembersConsidered} member(s), " +
                    $"{outcome.Fired} fired, {outcome.ReasonsFired} reason(s) fired";
            }),
            // Section 14's step 13. The facts file and the change list are one
            // stage rather than two, because the detector compares tonight's
            // payload against the last stored one and there is nothing for it
            // to read until the assembler has written tonight's.
            new("facts", async () =>
            {
                var assembled = await new FactsAssembler(clock, store.DatabaseFile)
                    .RunAsync(runId, night.Token);

                var changes = await new ChangeDetector(clock, store.DatabaseFile)
                    .RunAsync(runId, night.Token);

                return $"{assembled.RowsWritten} file(s) written for {assembled.NamesExamined} name(s), " +
                    $"{assembled.Replaced} replacing a stored file that differed, {assembled.Unchanged} unchanged, " +
                    $"{assembled.FactsWritten} fact(s), {changes.ChangesRecorded} material change(s), " +
                    $"{(changes.NotCompared ?? []).Count} not compared, {changes.PayloadsEmptied} payload(s) emptied";
            }, [FactsAssembler.Stage, ChangeDetector.Stage]),
            // Section 14's step 14. Once per night rather than per name,
            // because the base rate is a figure over the whole population and a
            // per-name pass would compute it once per name from the same rows.
            new("forward-returns", async () =>
            {
                var outcome = await new ForwardReturnFiller(clock, store.DatabaseFile)
                    .RunAsync(runId, night.Token);

                return $"{outcome.RowsWritten} row(s) over {outcome.ListingsExamined} listing(s), " +
                    $"{outcome.Matured} matured, {outcome.Immature} not yet matured";
            }),
            // Section 14's step 15. One dated query, paged until the day is
            // covered, fanned out to names in code.
            new("news-pulse", async () =>
            {
                var outcome = await new NewsPulseCounter(feeds.News, clock, store.DatabaseFile)
                    .RunAsync(indexCode, clock.SessionDateAt(clock.UtcNow), runId, night.Token);

                return $"{outcome.Articles} article(s) over {outcome.Requests} page(s), " +
                    $"{outcome.RowsWritten} row(s), {outcome.NamesCounted} name(s) with news, " +
                    $"{outcome.RowsDropped} dropped";
            }),
            // Section 14's step 16, which closes the arithmetic and records its
            // counts. It computes nothing: every figure is counted off the
            // store the night has just written, which is what makes it a record
            // of what happened rather than of what each stage intended.
            new("close", async () =>
            {
                var outcome = await new NightClose(clock, store.DatabaseFile)
                    .RunAsync(indexCode, runId, night.Token);

                return $"{outcome.NamesComputed} name(s) computed, {outcome.NamesOnTheList} on the list, " +
                    $"{outcome.ReasonsFired} reason(s) fired, {outcome.NamesStale} stale, " +
                    $"{outcome.Duration}";
            }),
        ];

        output.WriteLine($"nightly: {runId}, store {store.DatabaseFile}");

        // The session this night is for, which every stage below reads off the
        // same clock.
        var session = clock.SessionDateAt(clock.UtcNow);

        foreach (var step in steps)
        {
            // A day the exchange did not trade, asked once the store can hold
            // the row that says so and before anything is fetched.
            //
            // Until the phase 5 sign-off a night on a Saturday or a holiday
            // asked the provider for a session that does not exist, was refused
            // at the fetch for a file dated the day before or holding none of
            // the index, and exited 1: the scheduler recorded a failure every
            // weekend, and a Friday caught up on a Saturday was rolled back with
            // the refusal. Nothing is owed on such a day, so the night records
            // that it ran, names the day, and exits 0. The weekday a closure
            // table cannot place is still refused, since guessing is what the
            // table exists to stop.
            // see: A night on a day the exchange did not trade fetches nothing and exits clean
            if (ReferenceEquals(step, steps[1]))
            {
                bool traded;

                try
                {
                    traded = ExchangeClosures.IsSession(session);
                }
                catch (InvalidOperationException failure)
                {
                    var failed = $"stopped before step '{step.Name}': {failure.Message}";

                    error.WriteLine("nightly: " + failed);
                    await RecordStopAsync(store, runId, step, clock.UtcNow, clock.UtcNow, NightClose.Failed, failed, error);

                    return 1;
                }

                if (!traded)
                {
                    var closed = NightClose.NotASession(session);

                    output.WriteLine("nightly: " + closed);
                    await RecordNoSessionAsync(store, runId, clock.UtcNow, closed, error);

                    return 0;
                }
            }

            // The local stop, before the provider's own. Exceeding the daily
            // allowance arrives from the provider as a rejected rate, which is
            // an unavailable feed and loses the reason; stopping here says what
            // actually happened.
            // see: The night's cost is counted in weighted calls against the stated daily allowance
            var stepStarted = clock.UtcNow;

            if (feeds.WeightedCalls >= ProviderWeights.DailyAllowance)
            {
                var stopped =
                    $"stopped before step '{step.Name}'. The night has spent " +
                    $"{feeds.WeightedCalls} weighted call(s) against an allowance of " +
                    $"{ProviderWeights.DailyAllowance}. Last night's bars are kept and every name " +
                    "is stale.";

                error.WriteLine("nightly: " + stopped);
                await RecordStopAsync(store, runId, step, stepStarted, clock.UtcNow, NightClose.Stopped, stopped, error);

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
                var stopped =
                    $"step '{step.Name}' passed the night's deadline of " +
                    $"{limit.TotalMinutes.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)} " +
                    "minute(s) and was stopped. Last night's bars are kept and every name is stale.";

                error.WriteLine("nightly: " + stopped);
                await RecordStopAsync(store, runId, step, stepStarted, clock.UtcNow, NightClose.Stopped, stopped, error);

                return 1;
            }
            catch (Exception failure)
            {
                // The step is named, and the exit code is non-zero. Both halves
                // matter: a scheduler reads the code and a person reads the name.
                // And the run log carries it, because the person reads the run
                // page and a scheduled task's stderr goes nowhere.
                var failed = $"step '{step.Name}' failed: {failure.Message}";

                error.WriteLine("nightly: " + failed);
                await RecordStopAsync(store, runId, step, stepStarted, clock.UtcNow, NightClose.Failed, failed, error);

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

    // A night with no session, written where the run log is read, with the same
    // rule as a stop: failing to record it is said on stderr and never turns a
    // clean night into a failed one.
    static async Task RecordNoSessionAsync(
        StoreLocation store,
        string runId,
        DateTimeOffset at,
        string detail,
        TextWriter error)
    {
        try
        {
            await NightClose.RecordStopAsync(
                store.DatabaseFile,
                store.DataRoot,
                runId,
                [NightClose.Stage],
                at,
                at,
                NightClose.NoSession,
                detail);
        }
        catch (Exception failure)
        {
            error.WriteLine($"nightly: the night with no session could not be recorded on the run log: {failure.Message}");
        }
    }

    // The stop, written where the run page reads it. A night that cannot write
    // it, being one whose store will not open, still exits non-zero and says so
    // on stderr: recording the failure must never be what turns it into a
    // different one.
    static async Task RecordStopAsync(
        StoreLocation store,
        string runId,
        Step step,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        string outcome,
        string detail,
        TextWriter error)
    {
        try
        {
            var recorded = await NightClose.RecordStopAsync(
                store.DatabaseFile,
                store.DataRoot,
                runId,
                step.Stages ?? [step.Name],
                startedAt,
                endedAt,
                outcome,
                detail);

            if (recorded is null)
            {
                error.WriteLine($"nightly: the run log already holds every stage step '{step.Name}' writes, so the stop is not recorded there.");
            }
        }
        catch (Exception failure)
        {
            error.WriteLine($"nightly: the stop could not be recorded on the run log: {failure.Message}");
        }
    }
}
