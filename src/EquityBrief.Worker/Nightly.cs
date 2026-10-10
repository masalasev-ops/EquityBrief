using EquityBrief.Core.Bars;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Data.Migrations;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Calendar;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indicators;
using EquityBrief.Worker.Facts;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Ladders;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Moves;
using EquityBrief.Worker.Levels;
using EquityBrief.Worker.News;
using EquityBrief.Worker.Nights;
using EquityBrief.Worker.Quarters;
using EquityBrief.Worker.Returns;
using EquityBrief.Worker.Rules;
using EquityBrief.Worker.Shortlist;
using EquityBrief.Worker.Filter;
using EquityBrief.Worker.Swings;
using EquityBrief.Worker.Volume;
using EquityBrief.Worker.Membership;
using EquityBrief.Worker.Research;

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
    // step's own name. The facts step differs, since it runs the assembler
    // and the detector and each writes its own row, and so does the swing filter's,
    // which runs the filter and then the family lister; a stop inside either is
    // recorded under the first of the two the run does not hold yet.
    public sealed record Step(string Name, Func<Task<string>> Run, IReadOnlyList<string>? Stages = null);

    // Two figures the whole nightly path rests on, carried out of the run so
    // the script can print them and nightly-cost can read them back.
    public sealed record NightOutcome(string RunId, int ModelCalls, int NetworkRequests, IReadOnlyList<string> Steps);

    // How many more times a night that stops before its close tries again, how long it waits first, and
    // what it waits with, which the suite replaces so a test does not sleep. A night the scheduler starts
    // tries three more times fifteen minutes apart; a run of the rest of a night, and a night the suite
    // runs unless it asks, tries once.
    // see: A night that stops before its close is tried again from the step that stopped, three more times fifteen minutes apart, each try under a deadline of its own
    public sealed record TryPlan(int Retries, TimeSpan Wait, Func<TimeSpan, Task>? Delay = null)
    {
        public static TryPlan Standard { get; } = new(3, TimeSpan.FromMinutes(15));

        public static TryPlan Once { get; } = new(0, TimeSpan.Zero);
    }

    // The commit the script built this night from and the line it states it with, which the night records
    // on the run log under its own stage once the store can hold the row, so the run page shows the commit
    // each night was built from. A night the suite or a person runs from a build of their own carries none
    // and records none.
    // see: Each night is built from a clean copy of the main checkout's own commit and never from its working tree, and refuses only a checkout off main or ahead of the remote's main
    public sealed record Build(string Commit, string Note)
    {
        public string Detail => Note.StartsWith("built from ", StringComparison.Ordinal) ? Note : $"built from {Commit}, {Note}";
    }

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
        TimeSpan? deadline = null,
        IDrainLauncher? launcher = null,
        bool askForTheFirstName = true,
        TryPlan? tries = null,
        Build? build = null,
        CardSettings? cards = null,
        FormingSettings? forming = null,
        string? adopt = null)
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
            deadline,
            launcher,
            askForTheFirstName,
            tries,
            build: build,
            cards: cards,
            forming: forming,
            adopt: adopt);
    }

    // `runId` is the id of the night's first try, and `tryNumber` the try this run starts as: one for a
    // night, and the next for a run of the rest of one, which `resume` starts from the first step the
    // night's tries have not finished.
    public static async Task<int> RunAsync(
        StoreLocation store,
        NightFeeds feeds,
        string indexCode,
        IClock clock,
        TextWriter output,
        TextWriter error,
        string? runId = null,
        TimeSpan? deadline = null,
        IDrainLauncher? launcher = null,
        bool askForTheFirstName = true,
        TryPlan? tries = null,
        int tryNumber = 1,
        bool resume = false,
        Build? build = null,
        CardSettings? cards = null,
        FormingSettings? forming = null,
        string? adopt = null)
    {
        // The night's deadline, and the thing that can cancel it.
        //
        // Every feed interface has accepted a cancellation token since 1.1 and
        // nothing supplied one, so a night that hung on a socket hung until
        // somebody looked. This is the source, and it is the night's rather than
        // a request's: a per-request timeout bounds one attempt, and three
        // attempts on nine steps is a bound nobody would recognise as an
        // evening.
        // see: A feed is tried three times with a doubling backoff and the night's news query waits ninety seconds a try, and the night has a two-hour deadline it cannot move
        var limit = deadline ?? RetryPolicy.Standard.Deadline;

        // Each try's deadline is its own, so a try again is not started with the minutes an earlier try
        // spent already gone.
        var night = new CancellationTokenSource(limit);

        // The instant and not only the date. A night that fails halfway and is
        // re-run is a second run, and SCHEMA's grain is one row per run per
        // stage: keyed on the date alone the re-run collides on the primary key
        // and the night fails on its first step, which is exactly what a
        // re-run is for. The caller may name its own, which is how a replay
        // records under an id it chose.
        var nightStartedAt = clock.UtcNow;

        var firstTry = runId ?? FormattableString.Invariant($"night-{nightStartedAt:yyyyMMddTHHmmssZ}");
        var plan = tries ?? TryPlan.Once;
        var number = tryNumber;

        runId = NightClose.TryId(firstTry, number);

        // One night at a time: a second, a run of the rest of one among them, is refused before it writes
        // anything, and writes no row of its own, since a row would read as the night the page names.
        using var held = NightLock.Take(store.DataRoot, firstTry);

        if (held is null)
        {
            error.WriteLine(
                $"nightly: {NightLock.Holder(store.DataRoot) ?? "another night"} holds the night's lock under the data root, " +
                "so this run was refused before its first step and wrote nothing. Run it again once that night has ended.");
            night.Dispose();

            return 1;
        }

        var (membership, historical, bulkFeed, corporate, calendar, _, companies, market) = feeds;

        // The indices read beside the night's own, the S&P 400 and 600 from their funds' files: every stage that stores
        // or computes what each member needs reads their members too, and every stage that ranks, lists or records a
        // family reads the night's own index alone.
        // see: The universe is the S&P 1500's three indices with each member tagged by its index, and membership is fetched
        var wider = MembershipLoader.WiderOf(feeds.Funds, indexCode);

        // The order is section 14's, for the steps that exist. Migrate is not
        // one of its steps: it is what makes the store able to hold the night,
        // and a night against a store a version behind is a night that fails on
        // a missing column halfway through.
        Step[] steps =
        [
            new(FirstStep, async () =>
            {
                var outcome = MigrationRunner.Standard().Apply(store.DatabaseFile);

                // The commit this run was built from, recorded once the store can hold the row, under this
                // try's id, so a run of the rest of the night records the build it ran too.
                if (build is not null)
                {
                    var at = clock.UtcNow;

                    await NightClose.RecordStopAsync(store.DatabaseFile, store.DataRoot, runId, [NightBuild.Stage], at, at, NightClose.Ok, build.Detail);
                }

                return outcome.Applied.Count == 0
                    ? $"schema version {outcome.To}, nothing pending" + (build is null ? string.Empty : $", {build.Detail}")
                    : $"applied {outcome.Applied.Count}, {outcome.From} to {outcome.To}" + (build is null ? string.Empty : $", {build.Detail}");
            }),
            new("membership", async () =>
            {
                var rows = await new MembershipLoader(membership, clock, store.DatabaseFile, feeds.Funds)
                    .LoadAsync(indexCode, runId, night.Token);

                return $"{rows} rows written";
            }),
            new("backfill", async () =>
            {
                var outcome = await new Backfill(historical, clock, store.DatabaseFile)
                    .RunAsync(indexCode, runId, night.Token, wider);

                return $"{outcome.RowsWritten} rows written over {outcome.Requests} request(s), " +
                    $"{(outcome.Unserved ?? []).Count} member(s) holding no year after it";
            }),
            // The day's bars, and after them the index's and the VIX's series, one request a series, which only
            // the registered family rules' market switches read, so a series not stored stops nothing.
            // see: The night asks for the market series' daily closes once a series, and keeps them apart from the members' bars
            new("fetch", async () =>
            {
                var outcome = await new BarFetcher(bulkFeed, clock, store.DatabaseFile)
                    .RunAsync(indexCode, runId, night.Token, wider);
                var series = await new MarketSeriesFetcher(market, clock, store.DatabaseFile)
                    .RunAsync(runId, night.Token);

                return $"{outcome.RowsWritten} rows written for {outcome.MembersStored} member(s), " +
                    FormattableString.Invariant($"{outcome.RowsDropped} dropped below {outcome.Oldest:yyyy-MM-dd}, ") +
                    $"{outcome.NotTraded} row(s) listed and not traded, " +
                    $"{outcome.Unreadable} row(s) outside the index the reader refused, " +
                    $"{outcome.Unaccounted.Count} member(s) the file carried nothing for, " +
                    $"{(outcome.CaughtUp ?? []).Count} missed session(s) caught up, " +
                    $"{(outcome.CaughtUpShort ?? new Dictionary<DateOnly, IReadOnlyList<string>>()).Values.Sum(names => names.Count)} " +
                    "member-session(s) a caught-up file carried nothing for, " +
                    $"{outcome.Requests} request(s); market series {series.Detail}";
            }, [BarFetcher.Stage, MarketSeriesFetcher.Stage]),
            // And the dividends the answer carried for the names the night stores, kept for the name page's dividend
            // history with no request of their own.
            // see: Each dividend a member paid is kept from the night's bulk answer and from one history run, and read as the provider restated it on the day it was read
            new("actions", async () =>
            {
                var outcome = await new CorporateActionChecker(corporate, historical, clock, store.DatabaseFile)
                    .RunAsync(indexCode, runId, night.Token, wider);

                var kept = await new Dividends.DividendKeeper(clock, store.DatabaseFile)
                    .KeepTheNightAsync(outcome.Paid ?? [], runId, night.Token);

                return $"{outcome.Actions} action(s), {outcome.Refetched} refetched, " +
                    $"{outcome.Suspect.Count} suspect, {(outcome.Spent ?? []).Count} left suspect with retries spent, " +
                    $"{outcome.Requests} request(s); {kept.Handed} dividend(s) for the names stored, {kept.Kept} kept new";
            }, [CorporateActionChecker.Stage, Dividends.DividendKeeper.Stage]),
            // The calendar, one request for the whole index's dated events over
            // the window. The earnings date is needed nightly by the ladder
            // builder and the shortlist builder, so it is here rather than on
            // the on-demand path, and it is one request whatever the universe
            // size.
            new("calendar", async () =>
            {
                // And the dividend calendar, one request for each of the 21 sessions after the night.
                // see: The night asks the dividend calendar for each of the next 21 sessions, one request a session
                var outcome = await new CalendarFetcher(calendar, clock, store.DatabaseFile, feeds.Dividends)
                    .RunAsync(indexCode, clock.SessionDateAt(clock.UtcNow), runId, night.Token, wider);

                return FormattableString.Invariant($"{outcome.EventsReturned} event(s) over {outcome.From:yyyy-MM-dd} to ") +
                    FormattableString.Invariant($"{outcome.To:yyyy-MM-dd}, {outcome.RowsWritten} stored, ") +
                    $"{outcome.NotMembers} for names the index does not hold, " +
                    $"{outcome.NoLongerFiled} no longer filed, {outcome.RowsDropped} dropped, {outcome.Requests} request(s)" +
                    (outcome.DividendsFault is { } fault
                        ? $"; the dividend calendar not read: {fault}"
                        : feeds.Dividends.Requests > 0 ? $"; {outcome.ExDividends} ex-dividend date(s)" : string.Empty);
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
            // The chart's averages over the sessions before the store's year, from the pulled history, which only the
            // chart draws.
            // see: The chart's averages are read over the sessions before the store's year from the pulled history at the store's scale, by a step only the chart reads
            new("chart-averages", async () =>
            {
                var outcome = await new ChartAverager(clock, store.DatabaseFile)
                    .RunAsync(runId, night.Token);

                return FormattableString.Invariant($"{outcome.NamesWarmed} name(s) read from a pull, {outcome.NamesWithout} with none, {outcome.RowsWritten} row(s)");
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
            // Section 14's step 12, and the first stage after the ladder. It
            // reads the same bars every stage before it read and writes the
            // rows the how-it-got-here table is built from.
            new("moves", async () =>
            {
                var outcome = await new MoveAnnotator(clock, store.DatabaseFile)
                    .RunAsync(runId, night.Token);

                return $"{outcome.RowsWritten} move(s) written for {outcome.NamesExamined} name(s), " +
                    $"{outcome.RowsDropped} dropped";
            }),
            // Section 14's step 13. The readings the swing filter's gates are measured
            // against, for every member, and the night's breadth. It reads the bars and
            // the indicators every stage before it wrote and makes no request.
            new("swing-readings", async () =>
            {
                var outcome = await new SwingReader(clock, store.DatabaseFile)
                    .RunAsync(indexCode, runId, night.Token);

                return $"{outcome.RowsWritten} row(s) for {outcome.Members} member(s), {outcome.Read} read, " +
                    $"{outcome.Stale} with no bar for the session, {outcome.Gapped} gapped, {outcome.NoBars} holding no bar, " +
                    $"{outcome.RowsDropped} dropped";
            }),
            // Section 14's step 14. The four readings of every member's reported quarters and the state
            // they give it, from the quarters fetched on the nights before this one, before the listings
            // and the facts file that read them. It makes no request: the quarters it reads were asked
            // for after an earlier night's close. From 15.2 every member's readings follow, under a row of their own,
            // since the state the quarters give a member is one of them.
            // see: Four readings of a member's reported quarters are worked out every night by rules the measured split settled, and its state is read from sales and operating margin alone
            new("fundamental-readings", async () =>
            {
                var outcome = await new FundamentalReader(clock, store.DatabaseFile)
                    .RunAsync(indexCode, runId, night.Token, wider);

                await new Members.MemberReader(clock, store.DatabaseFile).RunAsync(indexCode, runId, night.Token, wider);

                return $"{outcome.RowsWritten} row(s) for {outcome.Members} member(s), " +
                    string.Join(", ", outcome.States.Select(state => $"{state.Value} {state.Key}"));
            }),
            // Section 14's step 15. It runs before the facts file, which is the
            // order section 14 states, and the facts assembler reads the
            // listings from 5.4 onward for the same reason.
            new("listings", async () =>
            {
                var outcome = await new ShortlistBuilder(clock, store.DatabaseFile)
                    .RunAsync(indexCode, runId, nightStartedAt, night.Token);

                return $"{outcome.RowsWritten} row(s) for {outcome.MembersConsidered} member(s), " +
                    $"{outcome.Fired} fired, {outcome.ReasonsFired} reason(s) fired";
            }),
            // Section 14's step 16. The swing filter, after the listings, because the trade
            // gate reads the ladder's first tranche as tonight's listing kept it. It changes
            // nothing the listings wrote and makes no request. The names it passes are the
            // pullback family's, the rule is recorded for its session once its rows are stored, the
            // family evaluator then stores every other family's answers under the filter's market
            // check, and the family lister draws the page's list from what each family passed.
            // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
            // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
            new("swing-filter", async () =>
            {
                // The swing family standing when the night started, evaluated in the filter's shadow, every other
                // family's registered rules, evaluated in the family evaluator's, and the sector heavyweights' registered
                // rules, each kept in a book of its own. The analysts' estimates a swing rule reading them needs are
                // asked for through the fetcher, and a night run again for an earlier session asks for none, the sixth
                // carve-out of the nightly rule.
                // see: The night asks for the estimates of each member a rule reading them passes on everything else, once a member a night
                var register = await new CandidateRegistrar(clock, store.DatabaseFile).RowsAsync(night.Token);
                var family = FamilyShadow.For(register, nightStartedAt);
                var rules = FamilyRuleShadow.For(register, nightStartedAt);
                var estimates = new EstimatesFetcher(askForTheFirstName ? companies : null, () => feeds.WeightedCalls, clock, store.DatabaseFile);
                var outcome = await new SwingFilter(clock, store.DatabaseFile)
                    .RunAsync(indexCode, runId, family, night.Token, estimates);
                await estimates.RecordAsync(runId, night.Token);
                var recorded = await NightClose.RecordRuleAsync(store.DatabaseFile, night.Token);
                var evaluated = await new FamilyEvaluator(clock, store.DatabaseFile).RunAsync(runId, rules, night.Token);
                var listed = await new FamilyLister(clock, store.DatabaseFile).RunAsync(runId, night.Token);
                // The catalogue's readings of the night's members, read only where a standing rule's hooks read them.
                // see: Every engine's settings hooks land together and all default off, so the families' pins move once
                var hookReadings = rules.Standing.Any(rule => EquityBrief.Core.Loop.RuleHooks.Of(EquityBrief.Core.Candidates.CandidateEvaluator.Read(rule.Parameters)).ReadsReadings)
                    ? await new EquityBrief.Worker.Ledger.SetupLedger(clock, store.DatabaseFile).ReadingsTonightAsync(indexCode, night.Token)
                    : null;
                var kept = await new FamilyRecorder(clock, store.DatabaseFile).RunAsync(indexCode, runId, rules.Standing, night.Token, hookReadings);
                var held = await new HeavyweightBook(clock, store.DatabaseFile).RunAsync(indexCode, runId, night.Token, HeavyweightBook.Standing(register, nightStartedAt));

                // Each change the operator approved on the Loop page, and with the adopt setting reading automatic each
                // proposal that passed and holds no decision, applied before the S&P 400's and 600's families read the
                // night, so the family reads it from tonight on that index alone. A failure is named on the step's row,
                // and the families read the settings as they stood.
                // see: An approved change is applied before the next night from the night's own build, on the index it was approved on alone
                string applied;

                try
                {
                    var applying = await new EquityBrief.Worker.Loop.LoopApply(clock, store.DatabaseFile, adopt).RunAsync(runId, night.Token);

                    applied = applying.Applications.Count == 0 ? "no approved change waiting" : $"{applying.Applied} approved change(s) applied and {applying.Refused} refused";
                }
                catch (Exception failed) when (failed is Microsoft.Data.Sqlite.SqliteException or ArgumentException or InvalidOperationException)
                {
                    applied = "the approved changes not applied tonight, " + failed.Message;
                }

                // The S&P 400's and 600's provisional rules, read after the S&P 500's list is drawn so each index's list
                // holds back a stock whose S&P 500 trade is still open. A failure in their part is caught and named on
                // their own row, and the step goes on.
                // see: The 400's and 600's provisional picks are computed on the night by the sweep's own code into tables of their own
                // see: A failure in the S&P 400's or 600's part of the night is caught and named, and the S&P 500's night is built regardless
                var indices = await new IndexFamilies(clock, store.DatabaseFile).RunAsync(
                    runId,
                    night.Token,
                    register,
                    nightStartedAt,
                    (index, token) => new EquityBrief.Worker.Ledger.SetupLedger(clock, store.DatabaseFile).ReadingsTonightAsync(index, token));

                // A card for each stock every index's families listed and each its books bought, read from the rows the
                // stages above stored. A failure in an index's cards is named on their own row, and the step goes on.
                // see: A pick's card advises on the trade and removes no pick, and code computes every figure on it
                var decisionCards = await new DecisionCards(clock, store.DatabaseFile, cards).RunAsync(runId, night.Token);

                // Every standing rule's night on every index, live or variant: its listed count, its funnel, its empty
                // stretch against its mark, the picks a variant keeps where no other table does, and the members forming a
                // breakout under each breakout rule. A failure in an index's part is named on the stage's row, and the
                // step goes on.
                // see: A variant's picks are shown on its card when chosen and its results only under its tests
                // see: The forming list advises and never lists a stock
                var ruleCards = await new RuleCards(clock, store.DatabaseFile, forming).RunAsync(runId, register, nightStartedAt, night.Token);

                // Each live rule's periods read against its reference once they have closed with every unit in them
                // settled, a rule two counted periods running under its low flagged. A failure is named on the step's
                // row, and the step goes on.
                // see: The live alarm flags a rule whose edge stood under its reference's fifth percentile two periods running
                string alarmed;

                try
                {
                    var alarm = await new EquityBrief.Worker.Loop.LiveAlarmReader(clock, store.DatabaseFile).RunAsync(runId, night.Token);

                    alarmed = $"{alarm.Written} alarm period(s) read, {alarm.Flagged} live rule(s) flagged";
                }
                catch (Exception failed) when (failed is Microsoft.Data.Sqlite.SqliteException or ArgumentException or InvalidOperationException)
                {
                    alarmed = "the live alarm not read tonight, " + failed.Message;
                }

                // Each trade the operator took followed to the night under its rule's own management, and their record. A
                // failure is named on its own row, and the step goes on.
                // see: A taken trade's fill is the next session's open once its bar is stored, and the plan's buy marked provisional until then
                var takenFollowed = await new TakenFollower(clock, store.DatabaseFile).RunAsync(runId, night.Token);

                return $"{outcome.RowsWritten} row(s) for {outcome.Members} member(s), {outcome.Passing} passing, " +
                    $"{outcome.Excluded} excluded, version {outcome.Version}" +
                    (recorded ? ", listed by the swing filter" : ", no session stored for the list's rule") +
                    $"; {evaluated.Families.Sum(family => family.Passed)} passed by the other setup families" +
                    $"; {listed.Listed} on the page's list" +
                    $"; {kept.Kept} kept by the registered family rules" +
                    $"; {held.Held} held by the sector heavyweights" +
                    $"; {(held.Rules ?? []).Count(rule => rule.Fault is null)} heavyweights rule(s) kept in books of their own" +
                    $"; {applied}" +
                    $"; {string.Join(", ", indices.Nights.Select(one => one.Fault is null ? $"{one.Listed} on the {one.Index} list" : $"the {one.Index} list not computed tonight"))}" +
                    $"; {decisionCards.Cards} decision card(s)" +
                    (decisionCards.Indices.Any(one => one.Fault is not null) ? ", " + string.Join(", ", decisionCards.Indices.Where(one => one.Fault is not null).Select(one => $"the {one.Index} cards not computed tonight")) : string.Empty) +
                    $"; {ruleCards.Rules} rule row(s), {ruleCards.Picks} variant pick(s) and {ruleCards.Forming} forming" +
                    (ruleCards.Indices.Any(one => one.Fault is not null) ? ", " + string.Join(", ", ruleCards.Indices.Where(one => one.Fault is not null).Select(one => $"the {one.Index} rule rows not computed tonight")) : string.Empty) +
                    $"; {alarmed}" +
                    (takenFollowed.Fault is null ? $"; {takenFollowed.Followed} taken trade(s) followed" : "; the taken trades not followed tonight");
            }, [SwingFilter.Stage, EstimatesFetcher.Stage, FamilyEvaluator.Stage, FamilyLister.Stage, FamilyRecorder.Stage, HeavyweightBook.Stage, EquityBrief.Worker.Loop.LoopApply.Stage, IndexFamilies.Stage, DecisionCards.Stage, RuleCards.Stage, EquityBrief.Worker.Loop.LiveAlarmReader.Stage, TakenFollower.Stage]),
            // Section 14's step 17. The setup ledger, after the families have drawn every index's list, since it
            // stores the live rule's own pass and the night's pick beside each setup. It appends tonight's setups
            // on each index and closes the windows of the setups stored before that ended on tonight's close. A
            // failure in an index's part is named on its row, and the step goes on.
            // see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
            new("ledger", async () =>
            {
                var outcome = await new EquityBrief.Worker.Ledger.SetupLedger(clock, store.DatabaseFile).NightAsync(runId, night.Token);

                return EquityBrief.Worker.Ledger.SetupLedger.Detail(outcome.Indices);
            }, [EquityBrief.Worker.Ledger.SetupLedger.Stage]),
            // Section 14's step 18. The shape proposer, after the swing filter, since it counts the
            // gate results the filter has just stored. It writes a proposal once the open version's
            // ordinary nights reach the trigger, and never a version: an acceptance is the operator's.
            new("shape-proposal", async () =>
            {
                var outcome = await new ShapeProposer(clock, store.DatabaseFile)
                    .RunAsync(runId, night.Token);

                return outcome.Proposal is { } written
                    ? $"proposal {written} written for filter version {outcome.Version} over {outcome.Ordinary} ordinary night(s)"
                    : $"{outcome.Ordinary} ordinary night(s) under filter version {outcome.Version}, {(outcome.Crossed ? "a proposal already stands" : "nothing proposed")}";
            }),
            // Section 14's step 19. The facts file and the change list are one
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
            // Section 14's step 20. Once per night rather than per name,
            // because the base rate is a figure over the whole population and a
            // per-name pass would compute it once per name from the same rows.
            new("forward-returns", async () =>
            {
                var outcome = await new ForwardReturnFiller(clock, store.DatabaseFile)
                    .RunAsync(runId, night.Token);

                return $"{outcome.RowsWritten} row(s) written over {outcome.ListingsExamined} listing(s) and {outcome.PlansExamined} swing plan(s), {outcome.Kept} kept as decided, " +
                    $"{outcome.Matured} newly matured, {outcome.Immature} not yet matured, {outcome.PlansNotScorable} swing plan(s) not scorable from the night's close";
            }),
            // Section 14's step 21. One dated query, paged until the day is
            // covered, fanned out to names in code.
            new("news-pulse", async () =>
            {
                var outcome = await new NewsPulseCounter(feeds.News, clock, store.DatabaseFile)
                    .RunAsync(indexCode, clock.SessionDateAt(clock.UtcNow), runId, night.Token, wider);

                return $"{outcome.Articles} article(s) over {outcome.Requests} page(s), " +
                    $"{outcome.RowsWritten} row(s), {outcome.NamesCounted} name(s) with news, " +
                    $"{outcome.RowsDropped} dropped";
            }),
            // Section 14's step 22, from 8.6. Counterfactual and free: the bars
            // are already stored, so scoring a version costs the ladder stage run
            // again per version and the level stage as well for a version of the
            // merge distance, and never a request. It runs after the arithmetic
            // it replays and before the close, so the close's counts are of the
            // night the live rules produced.
            //
            // It throws where a live rule moved inside an open window, which
            // stops the night at this step by the same path every other failure
            // takes. That is the point rather than a harshness: a measurement
            // whose subject moved says nothing about either version.
            new("rule-versions", async () =>
            {
                var outcome = await new RuleVersionScorer(clock, store.DatabaseFile)
                    .RunAsync(clock.SessionDateAt(clock.UtcNow), runId, night.Token);

                return $"{outcome.Versions} open version(s), {outcome.Replayed} replayed with {outcome.ReplayedMergeDistance} of the merge distance, " +
                    $"{outcome.RowsWritten} score(s) over {outcome.NamesScored} name(s), {outcome.NamesNotComputed} left out, {outcome.RowsDropped} dropped";
            }),
            // Section 14's step 23, which closes the arithmetic and records its
            // counts. It computes nothing: every figure is counted off the
            // store the night has just written, which is what makes it a record
            // of what happened rather than of what each stage intended.
            new("close", async () =>
            {
                var outcome = await new NightClose(clock, store.DatabaseFile)
                    .RunAsync(indexCode, runId, night.Token, wider);

                return $"{outcome.NamesComputed} name(s) computed, {outcome.NamesOnTheList} on the list, " +
                    $"{outcome.ReasonsFired} reason(s) fired, {outcome.NamesStale} stale, " +
                    $"{outcome.Duration}";
            }),
            // Section 14's step 24, after the arithmetic has closed and before the night's requests. The
            // reported quarters of the members due, one request for a member's fundamentals and one for
            // its closes where the answer is stored, the fourth carve-out the nightly rule names. It is
            // handed no token from the night's deadline, which bounds the arithmetic: it is bounded by its
            // own limit and by the day's allowance, read off the night's feeds before every ask. A night
            // run again for an earlier session asks for nothing, since the quarters it would store are
            // today's and not that night's.
            // see: A member's reported quarters are fetched on the night after it reports, and asked for again on the five nights after and weekly after that until the quarter is posted
            new("quarters", async () =>
            {
                if (!askForTheFirstName)
                {
                    return "no quarters were asked for, since this night was run again for an earlier session";
                }

                var outcome = await new QuarterFetcher(companies, historical, () => feeds.WeightedCalls, clock, store.DatabaseFile)
                    .RunAsync(indexCode, runId, wider: wider);

                return QuarterFetcher.Detail(outcome);
            }),
            // Section 14's step 25, after the quarters fetch and before the night's requests. The archive's daily index
            // for each weekday since the refresh last read one, and the facts of the members whose filer filed a
            // report, an amendment or a results announcement, the seventh carve-out the nightly rule names: free and
            // from the SEC rather than the provider, its documents on its own row. It is handed no token from the
            // night's deadline and is bounded by its own limit, so a slow or refused archive leaves the facts as they
            // were and never touches the arithmetic. The ledger then reads the business readings of tonight's setups
            // again for the members refreshed, so they read every filing the archive posted before the session. A
            // night run again for an earlier session refreshes nothing, since the days it would read are today's.
            // see: The night refreshes the facts of the members that filed since its last read of the archive's daily index, after the close under its own limit
            new("filings", async () =>
            {
                if (!askForTheFirstName)
                {
                    return "no filings were refreshed, since this night was run again for an earlier session";
                }

                if (feeds.Filings is not { } archive)
                {
                    return "no filings were refreshed, since the night was given no archive feed: the archive's contact is not configured";
                }

                var outcome = await new FilingsRefresher(archive, clock, store.DatabaseFile)
                    .NightAsync(indexCode, runId, wider);

                var readAgain = await new EquityBrief.Worker.Ledger.SetupLedger(clock, store.DatabaseFile)
                    .BusinessAgainAsync(clock.SessionDateAt(clock.UtcNow), outcome.Refreshed);

                return FilingsRefresher.Detail(outcome) + FormattableString.Invariant($"; {readAgain} of tonight's setup(s) read again");
            }),
            // Section 14's step after the filings refresh. The Treasury's 10-year par yield for the session's year, one free
            // request to the Treasury and none to the provider, every session to the night's kept that the store does not
            // hold. It is handed no token from the night's deadline, and a refused or unreadable answer keeps nothing and
            // stops no step. A night run again for an earlier session reads it too, since the table holds that session.
            // see: The Treasury's 10-year par yield is read once a night after the close and kept a session a row
            new("treasury", async () =>
            {
                var read = await new Treasury.TreasuryReader(clock, store.DatabaseFile)
                    .RunAsync(feeds.Treasury, clock.SessionDateAt(clock.UtcNow), runId);

                return Treasury.TreasuryReader.Detail(read, clock.SessionDateAt(clock.UtcNow));
            }),
            // Section 14's step 26, after the filings refresh. The night asks for six reports taken
            // in turn across the S&P 500's, 400's and 600's pages and starts the drain as a press
            // does: it writes a row a name and starts one process, and each pass is the drain's own
            // run, its calls and requests on its own rows, at the off-peak rate. It is handed no token
            // from the night's deadline, which bounds the arithmetic. A night run again for an earlier
            // session asks for nothing, since its list is not tonight's.
            // see: The six reports a night are taken in turn across the three indices, one at a time in the page's order
            new("report", async () =>
            {
                var started = clock.UtcNow;
                var asked = 0;
                string said;

                if (!askForTheFirstName)
                {
                    said = "no report was asked for, since this night was run again for an earlier session";
                }
                else
                {
                    var ask = await RequestDrain.AskForTheNightAsync(store.DatabaseFile, clock.SessionDateAt(clock.UtcNow), clock);

                    asked = ask.Asked.Count;
                    said = asked == 0
                        ? ask.Line
                        : ask.Line + ". " + (launcher?.Start().Line ?? "No drain was started, since this night was handed nothing to start one with.");
                }

                await RequestDrain.RecordTheNightAsync(store.DatabaseFile, runId, started, clock.UtcNow, asked, said);

                return said;
            }),
            // Section 14's step after the report. The night starts the news labeller as it starts the
            // drain: one process of its own, whose calls and spend sit on the labeller's own run and never
            // the night's, the fifth carve-out of the nightly rule. It is handed no token from the night's
            // deadline. A night run again for an earlier session starts none, since its list is not tonight's,
            // and a night handed nothing to start one with says so and starts none, which is how the suite's
            // nights make no model call.
            // see: The news labeller is a process of its own the night starts after the close, and its calls and its spend are its own
            new("label-news", async () =>
            {
                var started = clock.UtcNow;
                var said = !askForTheFirstName
                    ? "no labeller was started, since this night was run again for an earlier session"
                    : launcher?.StartTheLabeller().Line ?? "No labeller was started, since this night was handed nothing to start one with.";

                await NewsLabeller.RecordTheNightAsync(store.DatabaseFile, runId, started, clock.UtcNow, said);

                return said;
            }),
            // Section 14's last step, after the labeller's start. The night starts the store's copy as it starts
            // the labeller: one process of its own, which waits until the night, its drain and its labeller have
            // finished, copies the store and writes one row of its own. A night run again for an earlier session
            // starts one that waits for no labeller, since that night starts none, and a night handed nothing to
            // start one with says so and starts none.
            // see: The store is copied once the night and every process it started have finished and the newest three copies are kept after each is opened and read, and the copy writes a row as it starts and one as it ends
            new("backup", async () =>
            {
                var started = clock.UtcNow;
                var said = launcher?.StartTheBackup(afterTheLabeller: askForTheFirstName).Line
                    ?? "No copy of the store was started, since this night was handed nothing to start one with.";

                await Backup.StoreBackup.RecordTheNightAsync(store.DatabaseFile, runId, started, clock.UtcNow, said);

                return said;
            }),
        ];

        output.WriteLine($"nightly: {runId}, store {store.DatabaseFile}");

        // The session this night is for, which every stage below reads off the
        // same clock.
        var session = clock.SessionDateAt(clock.UtcNow);

        // A run of the rest of a night starts from the first step its tries have not finished, having
        // migrated the store first, since the checkout it runs from may be newer than the night's.
        var start = 0;

        if (resume)
        {
            var done = (await NightResume.NewestAsync(store.DatabaseFile, clock))?.Done ?? new HashSet<string>(StringComparer.Ordinal);

            // The migration writes no row of its own and is run first whatever the tries finished.
            start = Array.FindIndex(steps, 1, step => !(step.Stages ?? [step.Name]).All(done.Contains));

            if (start < 0)
            {
                output.WriteLine($"nightly: every step of {firstTry} has finished, so there is nothing to run.");
                night.Dispose();

                return 0;
            }
        }

        // The close's place in the night: a step after it is not tried again, since the list is drawn.
        var closeAt = Array.FindIndex(steps, step => step.Name == NightClose.Stage);

        while (true)
        {
            var (code, stoppedAt, again) = await RunStepsAsync(resume && number == tryNumber && start > 0 ? [0, .. Enumerable.Range(start, steps.Length - start)] : Enumerable.Range(start, steps.Length - start));

            if (stoppedAt is not { } at || !again || at > closeAt || number - tryNumber >= plan.Retries)
            {
                night.Dispose();

                if (stoppedAt is null)
                {
                    break;
                }

                return code;
            }

            // The try that stopped says when the next starts, beside its stop, which is what the pages read
            // a night waiting to try again off.
            var next = clock.UtcNow + plan.Wait;
            var waiting =
                $"try {number + 1} of {tryNumber + plan.Retries} starts from step '{steps[at].Name}' at " +
                next.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture);

            output.WriteLine("nightly: " + waiting);

            try
            {
                await NightClose.RecordStopAsync(store.DatabaseFile, store.DataRoot, runId, [NightClose.TryAgainStage], clock.UtcNow, clock.UtcNow, NightClose.Waiting, waiting);
            }
            catch (Exception failure)
            {
                error.WriteLine($"nightly: the next try could not be recorded on the run log: {failure.Message}");
            }

            await (plan.Delay ?? (wait => Task.Delay(wait)))(plan.Wait);

            number++;
            runId = NightClose.TryId(firstTry, number);
            night.Dispose();
            night = new CancellationTokenSource(limit);
            start = at;

            output.WriteLine($"nightly: {runId}, from step '{steps[at].Name}'");
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

        // The steps named, in order: the exit code, the step a stop stopped at, and whether a try again may
        // be made from it, which it may after a failure or the deadline and never after the day's allowance
        // or a day the exchange calendar cannot place. A day the exchange did not trade stops the night at
        // its second step with an exit code of nought.
        async Task<(int Code, int? StoppedAt, bool Again)> RunStepsAsync(IEnumerable<int> indices)
        {
        foreach (var index in indices)
        {
            var step = steps[index];

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

                    return (1, index, false);
                }

                if (!traded)
                {
                    var closed = NightClose.NotASession(session);

                    output.WriteLine("nightly: " + closed);
                    await RecordNoSessionAsync(store, runId, clock.UtcNow, closed, error);

                    return (0, index, false);
                }
            }

            // The local stop, before the provider's own. Exceeding the daily
            // allowance arrives from the provider as a rejected rate, which is
            // an unavailable feed and loses the reason; stopping here says what
            // actually happened.
            // see: The night's cost is counted in weighted calls against the stated daily allowance
            var stepStarted = clock.UtcNow;

            // What the feeds had been asked before this step, so a step that
            // stops records the requests it made rather than none.
            var requestsBefore = feeds.Requests;

            if (feeds.WeightedCalls >= ProviderWeights.DailyAllowance)
            {
                var stopped =
                    $"stopped before step '{step.Name}'. The night has spent " +
                    $"{feeds.WeightedCalls} weighted call(s) against an allowance of " +
                    $"{ProviderWeights.DailyAllowance}. Last night's bars are kept and every name " +
                    "is stale.";

                error.WriteLine("nightly: " + stopped);
                await RecordStopAsync(store, runId, step, stepStarted, clock.UtcNow, NightClose.Stopped, stopped, error);

                return (1, index, false);
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
                await RecordStopAsync(store, runId, step, stepStarted, clock.UtcNow, NightClose.Stopped, stopped, error, feeds.Requests - requestsBefore);

                return (1, index, true);
            }
            catch (Exception failure)
            {
                // The step is named, and the exit code is non-zero. Both halves
                // matter: a scheduler reads the code and a person reads the name.
                // And the run log carries it, because the person reads the run
                // page and a scheduled task's stderr goes nowhere.
                var failed = $"step '{step.Name}' failed: {failure.Message}";

                error.WriteLine("nightly: " + failed);
                await RecordStopAsync(store, runId, step, stepStarted, clock.UtcNow, NightClose.Failed, failed, error, feeds.Requests - requestsBefore);

                return (1, index, true);
            }
        }

        return (0, null, false);
        }
    }

    // A night refused before its first step, being a source that cannot be
    // resolved or a key that is missing, on stderr and on the run log.
    //
    // Until the phase 5 sign-off the command line wrote such a refusal to stderr
    // alone, and a scheduled task's stderr goes nowhere: a night whose secrets
    // file had gone missing exited 1 and left no row, so the run page showed the
    // night before as the newest with nothing wrong. The row is written under
    // the first step, being the step the night stopped before, and only into a
    // store that already exists, because a refusal that created an empty store
    // would leave a second fault behind the first. A store whose log cannot take
    // the row says so on stderr and the night still exits 1: recording the
    // refusal must never turn it into a different failure.
    public const string FirstStep = "migrate";

    public static async Task<int> RefusedAsync(
        StoreLocation store,
        string runId,
        IClock clock,
        string message,
        TextWriter error)
    {
        error.WriteLine($"nightly: {message}");

        if (!File.Exists(store.DatabaseFile))
        {
            return 1;
        }

        try
        {
            var at = clock.UtcNow;

            await NightClose.RecordStopAsync(
                store.DatabaseFile,
                store.DataRoot,
                runId,
                [FirstStep],
                at,
                at,
                NightClose.Refused,
                "refused before the first step: " + message);
        }
        catch (Exception failure)
        {
            error.WriteLine($"nightly: the refusal could not be recorded on the run log: {failure.Message}");
        }

        return 1;
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
        TextWriter error,
        int networkRequests = 0)
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
                detail,
                networkRequests);

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
