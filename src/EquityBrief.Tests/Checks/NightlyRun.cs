using System.Diagnostics;
using System.Text.RegularExpressions;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Membership;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// nightly-run. The night runs the steps that exist, in the order section 14
// states, and a failure names the step and exits non-zero.
//
// Section 14's list is a claim about the run rather than about a component, and
// it is the ordering that carries it: membership before the backfill, because a
// backfill asks which names are members; the backfill before the fetch, because
// a name with no year is a name the fetch would leave with one session.
//
// Only four of the nine steps exist and the rest are absent rather than
// stubbed, so this asserts the order of what runs rather than the length of the
// list. A step that printed "skipped" would be a step a reader counts as run.
public class NightlyRun
{
    internal static CheckReach Reach => new(
        "nightly-run",
        ["docs/ARCHITECTURE.html", "fixtures/membership-2026-09-05"],
        [
            // 5.5, the forward returns and the news pulse.
            CheckReach.Key(NightlyRunSteps.Heading, "Fill forward returns for past listings that matured today, and recompute the universe base rate."),
            CheckReach.Key(NightlyRunSteps.Heading, "Count today's articles per name from one dated news query, paged until the day is covered and every page counted, fanned out to names in code rather than asked for per name. The page count follows the day's news volume and not the size of the universe (see: News is one dated query, paged to cover the day, and attributed to names locally)."),
            CheckReach.Key(NightlyRunSteps.Heading, "Close the arithmetic and record its counts: names computed, names on the list, reasons fired, stale names, duration."),

            // 5.4, tonight's list.
            CheckReach.Key(NightlyRunSteps.Heading, "Evaluate the list reasons for every name."),

            // 5.3, the facts file.
            CheckReach.Key(NightlyRunSteps.Heading, "Write the facts file for every name."),

            // 5.2, the move annotator.
            CheckReach.Key(NightlyRunSteps.Heading, "Annotate the largest moves for every name."),

            CheckReach.Key(NightlyRunSteps.Heading, "Load index membership and record any joins and leaves."),
            CheckReach.Key(NightlyRunSteps.Heading, "Backfill one year for any member with no stored history, which on the first run is every name and afterwards is only a new joiner."),
            CheckReach.Key(NightlyRunSteps.Heading, "Fetch the day's bulk bar file, one request, and store the bars for every name that has not left the index by the session, a name announced to join included, first fetching in bulk, one request each, any session the store is missing since the last night that ran (see: A session the night finds missing is fetched in bulk before tonight's) (see: An announced index change takes effect on its effective date, and a joining name is stored from the announcement)."),
            CheckReach.Key(NightlyRunSteps.Heading, "Fetch the index's dated events for the horizon, one request, and store what the provider files (see: A calendar event is fetched once for the whole index, and the calendar holds provider events only)."),
            CheckReach.Key(NightlyRunSteps.Heading, "Compute the indicators for every name."),
            CheckReach.Key(NightlyRunSteps.Heading, "Mark the swings for every name."),
            CheckReach.Key(NightlyRunSteps.Heading, "Build the volume profile for every name."),
            CheckReach.Key(NightlyRunSteps.Heading, "Build the levels for every name."),
            CheckReach.Key(NightlyRunSteps.Heading, "Classify the trend state and build the ladder for every name, writing a row whether or not it carries a tranche (see: A ladder row is written for every index member every night) (see: The trend classifier returns its label to the ladder builder)."),
            CheckReach.Key(Scope.LimitsTable, "Per-request timeout and the night's deadline"),

            // 5.7. The row states a figure the night is bounded by and the
            // deadline follows it by three, which is a relationship between two
            // stated numbers and is assertable here. What the figure should be
            // is a property of the running system and is carried as an
            // operating obligation, read on the operational header.
            CheckReach.Key(Scope.LimitsTable, "Nightly wall clock, at index size"),
            CheckReach.Key(Scope.FailureTable, "Bulk price feed unavailable, run log"),
            CheckReach.Key(Scope.FailureTable, "A feed answers with a session other than the one asked for"),
            CheckReach.Key(Scope.FailureTable, "A feed answers with none of the index in it"),
            CheckReach.Key(Scope.FailureTable, "A feed answers with a row the reader cannot read"),
            CheckReach.Key(Scope.FailureTable, "A session the night finds missing"),
            CheckReach.Key(Scope.FailureTable, "A night on a day the exchange did not trade"),
            CheckReach.Key(Scope.FailureTable, "An index change announced before it takes effect"),

            // The stale-and-failed region's stopped stage, which only a night
            // can put there, decomposed from the region at the phase 5 sign-off.
            CheckReach.Key("15.10 Run", "Stale and failed, the stage a night stopped on"),
        ]);

    const string Fixture = "membership-2026-09-05";

    static readonly DateTimeOffset Night = new(2026, 9, 8, 21, 10, 0, TimeSpan.Zero);

    static string FixtureFolder() => Path.Combine(Repository.Root, "fixtures", Fixture);

    static async Task<(int Code, string Output, string Error)> NightAsync(
        TemporaryStore store,
        string? fixture = null,
        string? runId = null,
        IBulkPriceFeed? bulk = null,
        TimeSpan? deadline = null,
        IClock? clock = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var code = await Nightly.RunAsync(
            new StoreLocation(Path.GetDirectoryName(store.DatabaseFile)!),
            fixture ?? FixtureFolder(),
            "GSPC",
            clock ?? FixedClock.At(Night, SessionZones.UnitedStates),
            output,
            error,
            runId,
            bulk,
            deadline);

        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void TheLimitsRowStatesTheFiguresTheCodeUses()
    {
        // The first clause of the Per-request timeout and the night's deadline
        // note. It stood in ProviderRequestTests, where it ran, passed and
        // backed no verdict, because a check's tests are the ones its carrier
        // declares. Moved rather than copied: a limit stated in a document and
        // again in code is two places holding one fact, and the same assertion
        // in two test classes is a third.
        //
        // The row is read rather than repeated, so a figure changed in either
        // place fails here.
        var row = Corpus.Read("docs/ARCHITECTURE.html");
        var at = row.IndexOf("Per-request timeout and the night's deadline", StringComparison.Ordinal);

        Assert.True(at >= 0, "Section 17 no longer carries the timeout and deadline row.");

        var cell = row[at..row.IndexOf("</tr>", at, StringComparison.Ordinal)];
        var policy = RetryPolicy.Standard;

        Assert.Contains($"at most {policy.Attempts} attempts", cell, StringComparison.Ordinal);
        Assert.Contains($"{policy.FirstWait.TotalSeconds:0} seconds and then {policy.FirstWait.TotalSeconds * 2:0}", cell, StringComparison.Ordinal);
        Assert.Contains($"bounded by {policy.Timeout.TotalSeconds:0} seconds", cell, StringComparison.Ordinal);
        Assert.Contains($"bounded by {policy.Deadline.TotalMinutes:0} minutes", cell, StringComparison.Ordinal);

        // The wall clock the deadline is three times, read off its own row, so
        // the limit cannot move in one place alone. Before 5.7 the two numbers
        // sat side by side with a comment saying one was three times the other,
        // and the comment was the only thing that would have noticed.
        var wallClockAt = row.IndexOf("Nightly wall clock, at index size", StringComparison.Ordinal);

        Assert.True(wallClockAt >= 0, "Section 17 no longer carries the wall clock row.");

        // The description cell, not the whole row.
        //
        // Slicing to `</tr>` spans all four cells, and this row states two of
        // the phrases below in its Asserted-by cell as well as in its
        // description: "operational header" and "three times" each appear
        // twice inside it. So the assertions held over the second occurrence
        // and deleting the claim from the cell that carries it left them green,
        // which the phase 5 sign-off found. The claim is what the description
        // cell says, so that is what is read.
        var rowEnd = row.IndexOf("</tr>", wallClockAt, StringComparison.Ordinal);
        var subjectEnd = row.IndexOf("</td>", wallClockAt, StringComparison.Ordinal);
        var descriptionStart = row.IndexOf("<td>", subjectEnd, StringComparison.Ordinal);
        var descriptionEnd = row.IndexOf("</td>", descriptionStart, StringComparison.Ordinal);

        Assert.True(
            descriptionStart >= 0 && descriptionEnd > descriptionStart && descriptionEnd < rowEnd,
            "Section 17's wall clock row has no description cell to read, so the assertions below would " +
            "run over the whole row and pass on a phrase repeated in the Asserted-by cell.");

        var wallClock = row[descriptionStart..descriptionEnd];

        Assert.Contains(
            $"bounded by {RetryPolicy.WallClock.TotalMinutes:0} minutes",
            wallClock,
            StringComparison.Ordinal);

        // The relationship, with the multiple written as the number the row's
        // own prose states rather than as the constant the code derives from.
        // Asserting the derivation against the constant it is derived from is a
        // thing asserted against itself, which cannot fail: `Deadline` is
        // defined as `WallClock * DeadlineMultiple`, so changing the multiple
        // moves both sides together and the document is what notices.
        Assert.Contains("three times", wallClock, StringComparison.Ordinal);
        Assert.Equal(RetryPolicy.WallClock * 3, policy.Deadline);

        // The row says the figure is proposed and names what settles it, which
        // is what keeps a proposed limit from being read as a measured one.
        Assert.Contains("proposed until", wallClock, StringComparison.Ordinal);
        Assert.Contains("operational header", wallClock, StringComparison.Ordinal);

        // And the population is the index the fetch returned rather than the
        // literal 500, which is the other half of the row's own claim.
        Assert.Contains("rather than as the literal 500", wallClock, StringComparison.Ordinal);
    }

    // A payload that arrives and is wrong, in the two shapes section 18 now
    // carries. Both are induced against the fixture rather than described.
    //
    // A feed that did not answer and a feed that answered with something else
    // are different behaviours, and only the second can be mistaken for a night
    // that ran. That is the whole reason "unavailable" needed a definition
    // before these rows could be written.
    // see: A feed is unavailable when it does not answer, and wrong when it answers with something else

    [Fact]
    public async Task APayloadForAnotherSessionIsRefusedAndTheStoredBarsAreUnchanged()
    {
        using var store = new TemporaryStore();

        // A clean night first, so there is a stored series for the refusal to
        // leave alone. A test that induced the failure against an empty store
        // would prove that nothing was written where nothing could have been.
        var (clean, _, _) = await NightAsync(store, runId: "night-one");

        Assert.Equal(0, clean);

        var before = Count(store);

        Assert.True(before > 700, $"The store held {before} bars before the refusal, expected a year.");

        // The fixture's file is for 2026-09-08. This night asks for the session
        // after it, which is what a night run a day later against a stale file
        // looks like from here.
        var (code, _, error) = await NightAsync(
            store,
            runId: "night-stale",
            clock: FixedClock.At(new DateTimeOffset(2026, 9, 9, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates));

        Assert.Equal(1, code);
        Assert.Contains("step 'fetch'", error, StringComparison.Ordinal);
        Assert.Contains("2026-09-08", error, StringComparison.Ordinal);
        Assert.Contains("2026-09-09 was asked for", error, StringComparison.Ordinal);

        // The stored series is exactly as it was. Not shortened, not added to.
        Assert.Equal(before, Count(store));
    }

    [Fact]
    public async Task APayloadHoldingNoneOfTheIndexIsRefusedBeforeAnythingIsStored()
    {
        using var store = new TemporaryStore();

        var (clean, _, _) = await NightAsync(store, runId: "night-one");

        Assert.Equal(0, clean);

        var before = Count(store);

        // A file with rows in it and none of them ours, which is what a night
        // run before the close produced against the live provider: the payload
        // was full of symbols this index does not hold and carried nothing for
        // any of its five hundred members.
        var none = new ShortBulkFeed(RecordedBulkPriceFeed.FromFolder(FixtureFolder()), "AAPL", "MSFT", "KEYS", "NFLX");

        var (code, _, error) = await NightAsync(store, runId: "night-none", bulk: none);

        Assert.Equal(1, code);
        Assert.Contains("step 'fetch'", error, StringComparison.Ordinal);
        Assert.Contains("carries nothing for any of the 4 current member(s)", error, StringComparison.Ordinal);
        Assert.Equal(before, Count(store));
    }

    [Fact]
    public async Task ARowTheReaderCannotReadStopsTheNightOnlyWhenItIsAMembers()
    {
        // The row section 18 gained at the phase 5 sign-off, over a whole night
        // rather than over the fetcher alone. The fractional volume is the
        // provider's own, copied from its file for 2026-09-08, where six of
        // 50,249 rows carried one and each refused the night for every name.
        var fixture = File.ReadAllText(Directory.GetFiles(FixtureFolder(), "bulk-*.json").Single());
        const string Fund =
            """{"code":"EWG","exchange_short_name":"US","date":"2026-09-08","open":43.79,"high":43.8401,"low":43.545,"close":43.57,"adjusted_close":43.57,"volume":530131.7}""";

        using (var store = new TemporaryStore())
        {
            var outside = new RecordedBulkPriceFeed(fixture.TrimEnd().TrimEnd(']') + "," + Fund + "]");

            var (code, output, error) = await NightAsync(store, runId: "night-fund", bulk: outside);

            Assert.True(code == 0, error);
            Assert.Contains("1 row(s) outside the index the reader refused", output, StringComparison.Ordinal);
            Assert.Equal(FixtureExpectation.CurrentMembers.Length, Tickers(store, "2026-09-08").Count);
        }

        using (var store = new TemporaryStore())
        {
            var (clean, _, _) = await NightAsync(store, runId: "night-one");

            Assert.Equal(0, clean);

            var before = Count(store);
            var onMember = System.Text.RegularExpressions.Regex.Replace(
                fixture, @"(""code"":\s*""MSFT""[^}]*?""volume"":\s*)(\d+)", "${1}${2}.5");

            Assert.NotEqual(fixture, onMember);

            var (code, _, error) = await NightAsync(store, runId: "night-member", bulk: new RecordedBulkPriceFeed(onMember));

            Assert.Equal(1, code);
            Assert.Contains("step 'fetch'", error, StringComparison.Ordinal);
            Assert.Contains("MSFT carries a volume of", error, StringComparison.Ordinal);
            Assert.Contains("which is not a whole number", error, StringComparison.Ordinal);
            Assert.Equal(before, Count(store));

            // And on the run log, where the operator reads it.
            var stopped = Assert.Single(RunLog(store, "night-member"), row => row.Outcome != "ok");

            Assert.Equal("fetch", stopped.Stage);
            Assert.Contains("MSFT", stopped.Detail, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task APayloadShortOfSomeMembersIsStoredForTheRestAndSaysHowMany()
    {
        // The counter-test, and it is the one the live night wrote. Two of 503
        // current members are absent from an ordinary day's file, so a rule that
        // refused on any absence would refuse every night. The name simply has
        // no bar tonight, which is its own shorter history.
        //
        // What must not happen is silence. The count leaves the stage so a rise
        // from two to two hundred is visible without refusing anything.
        using var store = new TemporaryStore();

        var short1 = new ShortBulkFeed(RecordedBulkPriceFeed.FromFolder(FixtureFolder()), "MSFT", "NFLX");

        var (code, output, error) = await NightAsync(store, runId: "night-short", bulk: short1);

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error);
        Assert.Contains("2 member(s) the file carried nothing for", output, StringComparison.Ordinal);

        // The two it did carry are stored, and the two it did not are absent
        // rather than invented.
        var stored = Tickers(store, "2026-09-08");

        Assert.Equal(["AAPL", "KEYS"], stored);
    }

    static IReadOnlyList<string> Tickers(TemporaryStore store, string session)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT ticker FROM bar WHERE session_date = '{session}' ORDER BY ticker;";

        var rows = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    [Fact]
    public async Task AFeedThatDoesNotAnswerKeepsLastNightsBarsAndSaysSoOnTheRunLog()
    {
        // The behaviour half of the decomposed unavailable row. Its banner and
        // tonight's list are phase 5 surfaces and are owed at 5.4; what is owed
        // here is that the store is left as it was and the night says why.
        using var store = new TemporaryStore();

        var (clean, _, _) = await NightAsync(store, runId: "night-one");

        Assert.Equal(0, clean);

        var before = Count(store);

        var (code, _, error) = await NightAsync(store, runId: "night-down", bulk: new FailingBulkFeed());

        Assert.Equal(1, code);
        Assert.Contains("step 'fetch'", error, StringComparison.Ordinal);
        Assert.Contains("did not answer", error, StringComparison.Ordinal);
        Assert.Equal(before, Count(store));

        // And on the run log, which is what the row's own name says and what
        // this test did not assert until the phase 5 sign-off. The step and the
        // reason reached stderr and nowhere else, so a scheduled night, whose
        // stderr goes nowhere, recorded a failure only as the scheduler's exit
        // code: the first one showed eleven clean stages on the run page.
        var logged = RunLog(store, "night-down");

        var stopped = Assert.Single(logged, row => row.Outcome != "ok");

        Assert.Equal("fetch", stopped.Stage);
        Assert.Equal("failed", stopped.Outcome);
        Assert.Contains("did not answer", stopped.Detail, StringComparison.Ordinal);

        // Read back through the surface a person reads, rather than off the
        // column: the run page's stale-and-failed region draws the step.
        var api = new EquityBrief.Api.Reading.ReadApi(store.DatabaseFile, FixedClock.At(Night, SessionZones.UnitedStates));
        var stages = EquityBrief.Api.Reading.RunScreen.Stages(
            [.. (await api.RunLogAsync(new DateOnly(2026, 9, 8))).Where(row => row.RunId == "night-down")]);
        var failed = EquityBrief.Api.Reading.RunScreen.Failed(stages);

        Assert.Equal(["fetch"], [.. failed.Select(stage => stage.Stage)]);

        var region = new EquityBrief.Web.Marks.MarkRenderer().StaleAndFailed([], failed);

        Assert.Contains("data-stage=\"fetch\"", region, StringComparison.Ordinal);
        Assert.Contains("did not answer", region, StringComparison.Ordinal);
        Assert.DoesNotContain("no stage of this night failed", region, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRunPageOpensOnANightThatStoppedBeforeItsList()
    {
        // The page a person opens, and not a read with a date in it. Until the
        // phase 5 sign-off the run page opened on the newest night the listings
        // held and kept only that night's rows, so the reviewer stopped a night
        // at the fetch on 2026-09-09 and the default page showed 2026-09-08
        // saying no stage of this night failed. The test above reads a stop
        // through `RunLogAsync` with the date supplied, which is how the claim
        // passed while the page missed it.
        using var store = new TemporaryStore();

        var (clean, _, _) = await NightAsync(store, runId: "night-one");

        Assert.Equal(0, clean);

        var wednesday = FixedClock.At(new DateTimeOffset(2026, 9, 9, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        var (code, _, _) = await NightAsync(store, runId: "night-stopped", bulk: new FailingBulkFeed(), clock: wednesday);

        Assert.Equal(1, code);

        var api = new EquityBrief.Api.Reading.ReadApi(store.DatabaseFile, wednesday);

        // The listings still end on the night before, which is where the page
        // opened; the run page now opens on the night that ran.
        Assert.Equal(new DateOnly(2026, 9, 8), await api.NewestNightAsync());
        Assert.Equal(new DateOnly(2026, 9, 9), await api.RunNightAsync());

        var stages = EquityBrief.Api.Reading.RunScreen.Stages(await api.RunLogAsync((await api.RunNightAsync())!.Value));
        var region = new EquityBrief.Web.Marks.MarkRenderer().StaleAndFailed([], EquityBrief.Api.Reading.RunScreen.Failed(stages));

        Assert.Contains("data-stage=\"fetch\"", region, StringComparison.Ordinal);
        Assert.DoesNotContain("no stage of this night failed", region, StringComparison.Ordinal);

        // The read surface's own row and a night with no session, both later,
        // do not move it: neither is a night that ran.
        using (var connection = new SqliteConnection($"Data Source={store.DatabaseFile}"))
        {
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES " +
                "('read-later', 'read-api', '2026-09-12T15:00:00Z', '2026-09-12T15:00:00Z', 'started', 0, 0, 0, '0', NULL), " +
                "('night-saturday', 'close', '2026-09-12T23:30:00Z', '2026-09-12T23:30:00Z', 'no session', 0, 0, 0, '0', 'a Saturday');";
            command.ExecuteNonQuery();
        }

        Assert.Equal(new DateOnly(2026, 9, 9), await api.RunNightAsync());

        // And the stop row carries the request the step made before it failed,
        // which it recorded as none until the same correction.
        Assert.Equal(1, Scalar(store, "SELECT network_requests FROM run_log WHERE run_id = 'night-stopped' AND stage = 'fetch';"));
    }

    [Fact]
    public async Task ADeadlinePassedInsideTheActionsStepIsRecordedThereAndMarksNoNameSuspect()
    {
        // The corporate action check caught every exception per name, the
        // night's cancellation included, so a deadline passed during a refetch
        // marked a name suspect, the stage finished, and the stop was recorded
        // under a later step. Found by the phase 5 sign-off reviewer.
        using var store = new TemporaryStore();

        var started = Stopwatch.StartNew();
        var (warm, _, _) = await NightAsync(store, runId: "night-one");

        started.Stop();

        Assert.Equal(0, warm);

        // Measured from the warm night, for the reason the deadline test above
        // gives: an absolute bound is a claim about the machine.
        var deadline = (started.Elapsed * 2) + TimeSpan.FromSeconds(2);

        // The fixture's own day carries no action on a member, so one is added
        // for the first member, which is what makes the step refetch.
        var feeds = NightFeeds.FromFixture(FixtureFolder());
        var slow = new SlowHistoricalFeed();
        var member = FixtureExpectation.CurrentMembers.Order(StringComparer.Ordinal).First();
        var night = feeds with
        {
            Historical = slow,
            Corporate = new OneActionFeed(feeds.Corporate, member),
        };

        var output = new StringWriter();
        var error = new StringWriter();

        var code = await Nightly.RunAsync(
            new StoreLocation(Path.GetDirectoryName(store.DatabaseFile)!),
            night,
            "GSPC",
            FixedClock.At(Night, SessionZones.UnitedStates),
            output,
            error,
            "night-slow-actions",
            deadline);

        // The backfill asked nothing, since every name holds its year, so the
        // one request the slow feed saw is the action step's refetch.
        Assert.True(slow.Requests >= 1, $"The action step never reached the refetch: {output}");

        Assert.Equal(1, code);
        Assert.Contains("step 'actions' passed the night's deadline", error.ToString(), StringComparison.Ordinal);

        var stopped = Assert.Single(RunLog(store, "night-slow-actions"), row => row.Outcome != "ok");

        Assert.Equal("actions", stopped.Stage);
        Assert.Equal("stopped", stopped.Outcome);

        // No name was marked suspect for the night running out of time.
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM series_state WHERE state = 'suspect';"));
    }

    [Fact]
    public async Task ANightRefusedBeforeItsFirstStepSaysSoOnTheRunLog()
    {
        // A missing key or an unresolvable source refused the night on stderr
        // alone, which a scheduled task discards.
        using var store = new TemporaryStore();

        EquityBrief.Data.Migrations.MigrationRunner.Standard().Apply(store.DatabaseFile);

        var error = new StringWriter();
        var location = new StoreLocation(Path.GetDirectoryName(store.DatabaseFile)!);

        var code = await Nightly.RefusedAsync(
            location,
            "night-refused",
            FixedClock.At(Night, SessionZones.UnitedStates),
            "no EODHD key is configured, so a live night cannot reach the provider",
            error);

        Assert.Equal(1, code);
        Assert.Contains("no EODHD key", error.ToString(), StringComparison.Ordinal);

        var row = Assert.Single(RunLog(store, "night-refused"));

        Assert.Equal(Nightly.FirstStep, row.Stage);
        Assert.Equal("failed", row.Outcome);
        Assert.Contains("refused before the first step: no EODHD key", row.Detail, StringComparison.Ordinal);

        // And a store that does not exist yet is not created by the refusal.
        var elsewhere = Path.Combine(Path.GetDirectoryName(store.DatabaseFile)!, "not-a-store-yet");

        Directory.CreateDirectory(elsewhere);

        var absent = new StoreLocation(elsewhere);

        Assert.Equal(1, await Nightly.RefusedAsync(absent, "night-refused", FixedClock.At(Night, SessionZones.UnitedStates), "no key", new StringWriter()));
        Assert.False(File.Exists(absent.DatabaseFile));
    }

    [Fact]
    public async Task ACaughtUpFileShortOfAMemberSaysWhichOnTheFetchRow()
    {
        // A caught-up file carrying nothing for some members was stored for the
        // members it carried and the shortfall was discarded, where tonight's
        // file reports the same thing by name. Found by the phase 5 sign-off
        // reviewer.
        using var store = new TemporaryStore();

        var (first, _, firstError) = await NightAsync(store, runId: "night-one");

        Assert.True(first == 0, firstError);

        var members = FixtureExpectation.CurrentMembers.Order(StringComparer.Ordinal).ToArray();
        var missing = members[0];
        var thursday = FixedClock.At(new DateTimeOffset(2026, 9, 10, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        var bulk = new HoledBulkFeed(
            new NextSessionBulkFeed(RecordedBulkPriceFeed.FromFolder(FixtureFolder()), new DateOnly(2026, 9, 8)),
            new DateOnly(2026, 9, 9),
            [missing]);

        var (code, output, error) = await NightAsync(store, runId: "night-short-catch-up", bulk: bulk, clock: thursday);

        Assert.True(code == 0, error);
        Assert.Contains("1 member-session(s) a caught-up file carried nothing for", output, StringComparison.Ordinal);

        var detail = FetchDetail(store, "night-short-catch-up");

        Assert.Contains($"\"caughtUpShort\":{{\"2026-09-09\":{{\"count\":1,\"first\":[\"{missing}\"]}}}}", detail, StringComparison.Ordinal);

        // Tonight's file carried every member, which the row says in the same
        // shape rather than by omission; and the counts that went to the night's
        // stdout alone are on the row beside it.
        Assert.Contains("\"unaccounted\":{\"count\":0,\"first\":[]}", detail, StringComparison.Ordinal);
        Assert.Contains("\"notTraded\":", detail, StringComparison.Ordinal);
        Assert.Contains("\"unreadable\":0", detail, StringComparison.Ordinal);

        // The rest were stored for the missed session.
        Assert.Equal(members.Length - 1, Scalar(store, "SELECT COUNT(*) FROM bar WHERE session_date = '2026-09-09';"));
    }

    [Fact]
    public async Task AStopInsideTheFactsStepIsRecordedUnderTheStageThatStoppedAndNotOverTheOneThatRan()
    {
        // The one composite step. The assembler writes its row and the detector
        // writes another, so a failure in the detector must land under
        // "changes" rather than being dropped on the conflict with "facts".
        using var store = new TemporaryStore();

        var (clean, _, _) = await NightAsync(store, runId: "night-one");

        Assert.Equal(0, clean);

        var started = new DateTimeOffset(2026, 9, 8, 21, 20, 0, TimeSpan.Zero);

        await using (var connection = new SqliteConnection($"Data Source={store.DatabaseFile}"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) " +
                "VALUES ('night-half', 'facts', '2026-09-08T21:20:00Z', '2026-09-08T21:20:01Z', 'ok', 4, 0, 0, '0', 'written');";
            await command.ExecuteNonQueryAsync();
        }

        var recorded = await EquityBrief.Worker.Nights.NightClose.RecordStopAsync(
            store.DatabaseFile,
            Path.GetDirectoryName(store.DatabaseFile)!,
            "night-half",
            ["facts", "changes"],
            started,
            started.AddSeconds(2),
            EquityBrief.Worker.Nights.NightClose.Failed,
            "step 'facts' failed: the detector refused");

        Assert.Equal("changes", recorded);

        var rows = RunLog(store, "night-half");

        Assert.Contains(rows, row => row is { Stage: "facts", Outcome: "ok" });
        Assert.Contains(rows, row => row is { Stage: "changes", Outcome: "failed" });
    }

    [Fact]
    public async Task AStopRecordedOnTheRunLogCarriesNoAbsolutePath()
    {
        // The recorder applies the scrub, asserted through the row it writes
        // rather than through the helper alone. The helper's own test below
        // stayed green with the scrub removed from the recorder, which is the
        // mutation that found this gap.
        using var store = new TemporaryStore().Migrated();
        var root = Path.GetDirectoryName(store.DatabaseFile)!;
        var at = new DateTimeOffset(2026, 9, 8, 21, 20, 0, TimeSpan.Zero);

        var detail = $"step 'migrate' failed: unable to open '{store.DatabaseFile}'";

        Assert.True(AbsolutePaths.LooksAbsolute(detail), detail);

        await EquityBrief.Worker.Nights.NightClose.RecordStopAsync(
            store.DatabaseFile, root, "night-path", ["migrate"], at, at, EquityBrief.Worker.Nights.NightClose.Failed, detail);

        var stored = Assert.Single(RunLog(store, "night-path")).Detail;

        Assert.False(AbsolutePaths.LooksAbsolute(stored), stored);
        Assert.Contains("unable to open", stored, StringComparison.Ordinal);
    }

    [Fact]
    public void AStopsDetailCarriesNoAbsolutePath()
    {
        // Exception text carries absolute paths mid-string, and a run log row
        // is copied between machines. The data root is named and any other
        // rooted token is replaced, and a date or a ratio inside a token is
        // left alone.
        var root = Path.Combine(Path.GetTempPath(), "equitybrief-probe");
        var message =
            $"could not open '{Path.Combine(root, "equitybrief.db")}', also /var/tmp/x and C:\\Users\\someone\\file.json, " +
            "on 2026/09/08 at a ratio of 3/4";

        var portable = EquityBrief.Worker.Nights.NightClose.Portable(message, root);

        Assert.False(AbsolutePaths.LooksAbsolute(portable), portable);
        Assert.Contains("<data root>", portable, StringComparison.Ordinal);
        Assert.Contains("2026/09/08", portable, StringComparison.Ordinal);
        Assert.Contains("3/4", portable, StringComparison.Ordinal);

        // The matcher's own proof: the input was absolute before.
        Assert.True(AbsolutePaths.LooksAbsolute(message));
    }

    sealed record LoggedStage(string Stage, string Outcome, string Detail);

    static IReadOnlyList<LoggedStage> RunLog(TemporaryStore store, string runId)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT stage, outcome, IFNULL(detail, '') FROM run_log WHERE run_id = $run ORDER BY rowid;";
        command.Parameters.AddWithValue("$run", runId);

        var rows = new List<LoggedStage>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(new LoggedStage(reader.GetString(0), reader.GetString(1), reader.GetString(2)));
        }

        return rows;
    }

    [Fact]
    public async Task NoRunLogValueCarriesAnUnsubstitutedPlaceholder()
    {
        // The general form of a defect four components carried until 5.7. Each
        // wrote its outcome as "ok, {dropped} dropped" with the interpolation
        // prefix missing, so the brace reached the column literally, the run
        // page drew it, and the outcome stopped matching "ok" which is what
        // decides whether a stage is reported as failed.
        //
        // Asserted over the whole replayed chain rather than at the four sites,
        // because the next one will be written somewhere else. A stored value
        // holding a brace around a bare identifier is a format string that was
        // never formatted, and nothing else in this store has a reason to hold
        // one.
        using var store = await FixtureExpectations.WithReturns();

        var placeholder = new Regex(@"\{[A-Za-z_][A-Za-z0-9_]*\}");
        var rows = 0;
        var unsubstituted = new List<string>();

        await using (var connection = new SqliteConnection($"Data Source={store.DatabaseFile}"))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT stage, outcome, IFNULL(detail, '') FROM run_log ORDER BY rowid;";

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                rows++;

                foreach (var value in new[] { reader.GetString(1), reader.GetString(2) })
                {
                    if (placeholder.IsMatch(value))
                    {
                        unsubstituted.Add($"{reader.GetString(0)}: {value}");
                    }
                }
            }
        }

        // The scope carrying the property is the rows read, and it is floored
        // well under what the chain writes so ordinary growth never moves it. A
        // replay that logged nothing would satisfy the assertion below by having
        // nothing to check.
        Assert.True(rows >= 10, $"Read {rows} run log rows, expected at least 10.");

        Assert.DoesNotContain(unsubstituted, _ => true);

        // And the permanent proof that the reading can fail, over constructed
        // input rather than by breaking a component.
        Assert.Matches(placeholder, "ok, {dropped} dropped");
        Assert.DoesNotMatch(placeholder, "ok");
        Assert.DoesNotMatch(placeholder, "503 name(s), 4024 move(s), 0 dropped");
    }

    [Fact]
    public async Task EveryStageThatSucceededSaysOkAndNothingElse()
    {
        // The other half, and the one the run page reads. `RunScreen.Failed`
        // decides what to draw in the stale-and-failed region by comparing the
        // outcome against "ok", so an outcome that carries a count is a stage
        // reported as failed on every night it runs. The vocabulary is closed
        // and this is where that is asserted.
        using var store = await FixtureExpectations.WithReturns();

        var outcomes = new List<(string Stage, string Outcome)>();

        await using (var connection = new SqliteConnection($"Data Source={store.DatabaseFile}"))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT stage, outcome FROM run_log ORDER BY rowid;";

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                outcomes.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        Assert.True(outcomes.Count >= 10, $"Read {outcomes.Count} run log rows, expected at least 10.");

        // Every stage of a clean replay succeeded, so every outcome is the one
        // word. A stage that fails writes its own outcome and is not reached
        // here, which is why this is asserted over a chain that ran clean.
        Assert.DoesNotContain(outcomes, row => row.Outcome != "ok");
    }

    [Fact]
    public async Task ANightThatPassesItsDeadlineStopsAndSaysSo()
    {
        // The bound the night has never had. Every feed interface has accepted a
        // cancellation token since 1.1 and nothing supplied one, so a night that
        // hung on a socket hung until somebody looked.
        //
        // Named as a deadline rather than as a step that failed, because a night
        // that ran out of time and a night whose provider refused are different
        // mornings and the operator reads one line to tell them apart.
        using var store = new TemporaryStore();

        // A clean night first, so the deadline below has only the fetch left to
        // spend itself on. Without it the migrate, membership and backfill steps
        // are doing real work and a slow runner reaches the deadline before the
        // fetch.
        //
        // The deadline is measured from that night rather than stated as a
        // figure, and that is the repair this test has now had twice. 2.7 set it
        // at a quarter of a second and a cold runner beat it; the correction
        // raised it to three seconds and called it generous, and a runner that
        // took seven and a half minutes over a suite the other ran in four beat
        // that too. Both were the same shape: a bound written as an absolute
        // number is a bound that assumes a machine speed, and this corpus's own
        // rule is that a test whose answer depends on how fast the machine is
        // has no answer.
        //
        // Measured, it scales. The warm night is every step doing its real work
        // once, so twice that plus a margin is comfortably more than the steps
        // before the fetch will take on the same machine, and it is far less
        // than the five minutes the feed below hangs for. A runner ten times
        // slower produces a warm night ten times longer and a deadline ten
        // times larger, and the arrangement holds.
        var started = Stopwatch.StartNew();
        var (warm, _, _) = await NightAsync(store, runId: "night-one");

        started.Stop();

        Assert.Equal(0, warm);

        var deadline = (started.Elapsed * 2) + TimeSpan.FromSeconds(2);

        var slow = new SlowBulkFeed();

        var (code, _, error) = await NightAsync(
            store,
            runId: "night-slow",
            bulk: slow,
            deadline: deadline);

        // The arrangement, asserted rather than assumed. If the deadline fired
        // before the fetch the feed was never entered, and the assertions below
        // would be about a step this test did not mean to name.
        Assert.True(
            slow.Requests == 1,
            $"the night reached the fetch {slow.Requests} time(s) with a deadline of {deadline}, " +
            $"measured from a warm night of {started.Elapsed}. The deadline fired before the step " +
            "this test is about.");

        Assert.Equal(1, code);
        Assert.Contains("passed the night's deadline", error, StringComparison.Ordinal);
        Assert.Contains("step 'fetch'", error, StringComparison.Ordinal);

        // And the promise section 18 makes about a feed that did not answer.
        Assert.Contains("Last night's bars are kept", error, StringComparison.Ordinal);

        // On the run log as well, as a stop rather than a failure, against the
        // step the deadline caught.
        var stopped = Assert.Single(RunLog(store, "night-slow"), row => row.Outcome != "ok");

        Assert.Equal("fetch", stopped.Stage);
        Assert.Equal("stopped", stopped.Outcome);
        Assert.Contains("passed the night's deadline", stopped.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANightInsideItsDeadlineIsUnaffectedByHavingOne()
    {
        // The counter-test. A deadline that stopped a night that was doing fine
        // would be a worse fault than no deadline at all.
        using var store = new TemporaryStore();

        var (code, output, error) = await NightAsync(store, deadline: TimeSpan.FromMinutes(5));

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error);
        Assert.Contains("nightly: green", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANightRunsEndToEndOverTheFixture()
    {
        using var store = new TemporaryStore();

        var (code, output, error) = await NightAsync(store);

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error);
        Assert.Contains("nightly: green over a capture, 0 model calls", output, StringComparison.Ordinal);

        // And the request count says what it is. A recorded feed counts the
        // calls a live one would have made, which is how a replay measures
        // the cost shape, so the line has to say which kind of night this
        // was rather than print "network request(s)" over a capture.
        Assert.Contains("none of them to a network", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheStepsRunInTheOrderSectionFourteenStatesThem()
    {
        // The order is read off the run's own output rather than off the source,
        // because the claim is that the night runs them in order and a list
        // written in the right order can still be iterated in another.
        using var store = new TemporaryStore();

        var (_, output, _) = await NightAsync(store);

        var membership = output.IndexOf("  membership:", StringComparison.Ordinal);
        var backfill = output.IndexOf("  backfill:", StringComparison.Ordinal);
        var fetch = output.IndexOf("  fetch:", StringComparison.Ordinal);
        var actions = output.IndexOf("  actions:", StringComparison.Ordinal);

        Assert.True(membership >= 0 && backfill >= 0 && fetch >= 0, $"A step did not run: {output}");
        Assert.True(membership < backfill, "The backfill ran before membership, so it asked an empty index which names are members.");
        Assert.True(backfill < fetch, "The fetch ran before the backfill, so a new name would hold one session rather than a year.");
        Assert.True(fetch < actions, "The action check ran before the fetch, so it would refetch a year the night was about to add a session to.");

        // And the order the document states is the order asserted, read from
        // section 14 rather than repeated here.
        var steps = NightlyRunSteps.In(File.ReadAllText(Repository.Architecture));

        Assert.True(steps.Count >= 8, $"Read {steps.Count} steps from section 14, expected at least 8.");
        Assert.StartsWith("Load index membership", steps[0], StringComparison.Ordinal);
        Assert.StartsWith("Backfill one year", steps[1], StringComparison.Ordinal);
        Assert.StartsWith("Fetch the day", steps[2], StringComparison.Ordinal);
        Assert.StartsWith("Check splits and dividends", steps[3], StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachStepDoesWhatSectionFourteenSaysItDoes()
    {
        // The order alone would pass over three steps that ran and did nothing.
        using var store = new TemporaryStore();

        var (_, first, _) = await NightAsync(store, runId: "night-one");

        // Derived from the captured files and the night's own window rather
        // than written down. The window is the session the night runs on less a
        // year, so a figure written here would be a figure about the day this
        // test was written and would move with the fixture.
        var populations = Fixtures.Populations(FixtureFolder());
        var current = populations.Constituents - populations.Departed.Count;
        var opens = ((IClock)FixedClock.At(Night, SessionZones.UnitedStates)).SessionDateAt(Night).AddYears(-1);

        var backfilled = Directory
            .GetFiles(FixtureFolder(), "bars-*.json")
            .Sum(file => SessionsFrom(file, opens));

        Assert.Contains($"membership: {populations.Constituents} rows written", first, StringComparison.Ordinal);
        Assert.Contains($"backfill: {backfilled} rows written over {current} request(s)", first, StringComparison.Ordinal);
        Assert.Contains($"fetch: {current} rows written for {current} member(s)", first, StringComparison.Ordinal);
        Assert.Contains("1 request(s)", first, StringComparison.Ordinal);

        // Stated, because the two derivations above would agree at zero.
        Assert.Equal(FixtureExpectation.Constituents, populations.Constituents);
        Assert.Equal(FixtureExpectation.CurrentMembers.Length, current);
        Assert.True(backfilled > 700, $"The backfill wrote {backfilled} rows, expected more than 700.");

        // A second night over the same store backfills nothing, which is what
        // the step says of itself: afterwards it is only a new joiner.
        var (code, second, _) = await NightAsync(store, runId: "night-two");

        Assert.Equal(0, code);
        Assert.Contains("backfill: 0 rows written over 0 request(s)", second, StringComparison.Ordinal);
        Assert.Contains("fetch: 0 rows written", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASecondConsecutiveSessionCompletesEveryStepAndListsEveryMember()
    {
        // The night after the first one, which no test ran until the phase 5
        // sign-off and which the first scheduled night showed cannot finish.
        //
        // Every night the suite ran was either a store's first night or a
        // re-run of the same session, and both hide the one thing a second
        // evening has: last night's facts file sitting beside tonight's bars.
        // The shortlist builder compared the two and refused, because section
        // 14 writes the listings before the facts, so on 2026-09-10 the live
        // night stopped at step 12 having written eleven clean stages and
        // nothing at all about the twelfth.
        using var store = new TemporaryStore();

        var (first, _, firstError) = await NightAsync(store, runId: "night-one");

        Assert.True(first == 0, "The first night did not run clean: " + firstError);

        // The fixture's own day served again as the next session's, so the
        // second night has a file for the session it asks for. The prices are
        // the same and that does not matter here: what this asserts is that the
        // night reaches its last step and lists every member, not what fired.
        var nextSession = FixedClock.At(new DateTimeOffset(2026, 9, 9, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var bulk = new NextSessionBulkFeed(RecordedBulkPriceFeed.FromFolder(FixtureFolder()), new DateOnly(2026, 9, 8));

        var (second, output, error) = await NightAsync(store, runId: "night-two", bulk: bulk, clock: nextSession);

        Assert.True(second == 0, $"The second consecutive night exited {second}. Error: {error}");
        Assert.Equal(string.Empty, error);
        Assert.Contains("  close:", output, StringComparison.Ordinal);

        // The hard rule, over both nights and not only the one a replay holds:
        // a listings row for every member every night, whether or not a reason
        // fired.
        var current = FixtureExpectation.CurrentMembers.Length;

        Assert.Equal(current, Scalar(store, "SELECT COUNT(*) FROM listing WHERE session_date = '2026-09-08';"));
        Assert.Equal(current, Scalar(store, "SELECT COUNT(*) FROM listing WHERE session_date = '2026-09-09';"));

        // And the stages after the listings ran for the new session rather than
        // being skipped, which is what a night that stopped at step 12 leaves
        // missing.
        Assert.Equal(current, Scalar(store, "SELECT COUNT(*) FROM facts WHERE session_date = '2026-09-09';"));
        Assert.Equal(1, Scalar(store, "SELECT COUNT(*) FROM run_log WHERE run_id = 'night-two' AND stage = 'close';"));
    }

    [Fact]
    public async Task ANightAfterOneThatDidNotRunFetchesTheMissedSessionFirst()
    {
        // The hole every name shares, which the observed calendar cannot see.
        // The store ends on 2026-09-08; this night is 2026-09-10, so 2026-09-09
        // is a session the exchange traded and no night stored.
        using var store = new TemporaryStore();

        var (first, _, firstError) = await NightAsync(store, runId: "night-one");

        Assert.True(first == 0, firstError);

        var thursday = FixedClock.At(new DateTimeOffset(2026, 9, 10, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var bulk = new NextSessionBulkFeed(RecordedBulkPriceFeed.FromFolder(FixtureFolder()), new DateOnly(2026, 9, 8));

        var (code, output, error) = await NightAsync(store, runId: "night-after-a-miss", bulk: bulk, clock: thursday);

        Assert.True(code == 0, error);
        Assert.Contains("1 missed session(s) caught up", output, StringComparison.Ordinal);

        // Every member holds both sessions, the missed one and tonight's.
        var current = FixtureExpectation.CurrentMembers.Length;

        Assert.Equal(current, Scalar(store, "SELECT COUNT(*) FROM bar WHERE session_date = '2026-09-09';"));
        Assert.Equal(current, Scalar(store, "SELECT COUNT(*) FROM bar WHERE session_date = '2026-09-10';"));

        // One request more than an ordinary night, and the run log says which
        // session it was for.
        Assert.Equal(2, Scalar(store, "SELECT network_requests FROM run_log WHERE run_id = 'night-after-a-miss' AND stage = 'fetch';"));
        Assert.Contains("\"caughtUp\":[\"2026-09-09\"]", FetchDetail(store, "night-after-a-miss"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AClosureBetweenTwoNightsCostsNoRequest()
    {
        // The fixture's own night: the store ends on Friday 2026-09-04 and the
        // night is Tuesday 2026-09-08, with Labor Day between them. A table
        // missing the closure would ask the provider for it and refuse.
        using var store = new TemporaryStore();

        var (code, output, error) = await NightAsync(store, runId: "night-over-a-closure");

        Assert.True(code == 0, error);
        Assert.Contains("0 missed session(s) caught up", output, StringComparison.Ordinal);
        Assert.Equal(1, Scalar(store, "SELECT network_requests FROM run_log WHERE run_id = 'night-over-a-closure' AND stage = 'fetch';"));
        Assert.Contains("\"caughtUp\":[]", FetchDetail(store, "night-over-a-closure"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMissedSessionThatCannotBeFetchedStopsTheNightByName()
    {
        using var store = new TemporaryStore();

        var (first, _, firstError) = await NightAsync(store, runId: "night-one");

        Assert.True(first == 0, firstError);

        var before = Count(store);
        var thursday = FixedClock.At(new DateTimeOffset(2026, 9, 10, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        // The missed day's file holds none of the index, which is what a closure
        // the table does not know about would look like.
        var bulk = new HoledBulkFeed(
            new NextSessionBulkFeed(RecordedBulkPriceFeed.FromFolder(FixtureFolder()), new DateOnly(2026, 9, 8)),
            new DateOnly(2026, 9, 9),
            FixtureExpectation.CurrentMembers);

        var (code, _, error) = await NightAsync(store, runId: "night-unfillable", bulk: bulk, clock: thursday);

        Assert.Equal(1, code);
        Assert.Contains("step 'fetch'", error, StringComparison.Ordinal);
        Assert.Contains("missing session 2026-09-09", error, StringComparison.Ordinal);
        Assert.Contains("the closure table is missing it", error, StringComparison.Ordinal);

        // Nothing stored: not the missed day, and not tonight's either, since a
        // night storing past a hole would move it rather than fill it.
        Assert.Equal(before, Count(store));

        var stopped = Assert.Single(RunLog(store, "night-unfillable"), row => row.Outcome != "ok");

        Assert.Equal("fetch", stopped.Stage);
        Assert.Contains("2026-09-09", stopped.Detail, StringComparison.Ordinal);
    }

    // A night over feeds the caller assembled, so a test can read what every
    // feed was asked for afterwards rather than trusting the stage's own line.
    static async Task<(int Code, string Output, string Error)> NightAsync(
        TemporaryStore store,
        NightFeeds feeds,
        string runId,
        IClock clock)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var code = await Nightly.RunAsync(
            new StoreLocation(Path.GetDirectoryName(store.DatabaseFile)!),
            feeds,
            "GSPC",
            clock,
            output,
            error,
            runId);

        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task AnAnnouncedChangeTakesEffectOnItsDateAndAJoinerIsStoredFromTheAnnouncement()
    {
        // The rebalance of 2026-09-21, in the fixture's own week. The membership
        // feed carries it on 2026-09-08, effective 2026-09-09: one member
        // leaving and one joining. Until the phase 5 sign-off every stage read
        // the leaver as gone and the joiner as in from the night the feed
        // carried the change, so the leaver lost its bars and its row for the
        // days it was still in the index and the joiner was listed early.
        using var store = new TemporaryStore();

        var members = FixtureExpectation.CurrentMembers.Order(StringComparer.Ordinal).ToArray();
        var leaver = members[0];
        var joiner = members[1];
        var effective = new DateOnly(2026, 9, 9);

        NightFeeds Feeds(IBulkPriceFeed? bulk = null)
        {
            var feeds = NightFeeds.FromFixture(FixtureFolder());

            return feeds with
            {
                Membership = new RebalancedMembershipFeed(feeds.Membership, leaver, joiner, effective),
                Bulk = bulk ?? feeds.Bulk,
            };
        }

        var (first, _, firstError) = await NightAsync(store, Feeds(), "night-announced", FixedClock.At(Night, SessionZones.UnitedStates));

        Assert.True(first == 0, firstError);

        // Before the effective date. The leaver is still in: backfilled,
        // stored, laddered and listed. The joiner is stored, from its backfill
        // and from tonight's file, so it joins with its year whole, and is
        // neither laddered nor listed.
        Assert.True(Scalar(store, $"SELECT COUNT(*) FROM bar WHERE ticker = '{leaver}';") > 200, $"{leaver} was not backfilled.");
        Assert.True(Scalar(store, $"SELECT COUNT(*) FROM bar WHERE ticker = '{joiner}';") > 200, $"{joiner} was not backfilled.");
        Assert.Equal(1, Scalar(store, $"SELECT COUNT(*) FROM listing WHERE ticker = '{leaver}' AND session_date = '2026-09-08';"));
        Assert.Equal(1, Scalar(store, $"SELECT COUNT(*) FROM ladder WHERE ticker = '{leaver}';"));
        Assert.Equal(0, Scalar(store, $"SELECT COUNT(*) FROM listing WHERE ticker = '{joiner}';"));
        Assert.Equal(0, Scalar(store, $"SELECT COUNT(*) FROM ladder WHERE ticker = '{joiner}';"));

        // The list is the index on the session and not the membership table:
        // every member but the one not yet joined.
        Assert.Equal(members.Length - 1, Scalar(store, "SELECT COUNT(*) FROM listing WHERE session_date = '2026-09-08';"));

        // On the effective date each goes the other way.
        var bulk = new NextSessionBulkFeed(RecordedBulkPriceFeed.FromFolder(FixtureFolder()), new DateOnly(2026, 9, 8));
        var nextSession = FixedClock.At(new DateTimeOffset(2026, 9, 9, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        var (second, _, secondError) = await NightAsync(store, Feeds(bulk), "night-effective", nextSession);

        Assert.True(second == 0, secondError);
        Assert.Equal(0, Scalar(store, $"SELECT COUNT(*) FROM bar WHERE ticker = '{leaver}' AND session_date = '2026-09-09';"));
        Assert.Equal(1, Scalar(store, $"SELECT COUNT(*) FROM bar WHERE ticker = '{joiner}' AND session_date = '2026-09-09';"));
        Assert.Equal(0, Scalar(store, $"SELECT COUNT(*) FROM listing WHERE ticker = '{leaver}' AND session_date = '2026-09-09';"));
        Assert.Equal(1, Scalar(store, $"SELECT COUNT(*) FROM listing WHERE ticker = '{joiner}' AND session_date = '2026-09-09';"));
        Assert.Equal(members.Length - 1, Scalar(store, "SELECT COUNT(*) FROM listing WHERE session_date = '2026-09-09';"));
    }

    [Fact]
    public async Task AJoinerBackfilledThroughTonightDoesNotHideAMissedSession()
    {
        // The store ends on 2026-09-08 and the night is 2026-09-10, so
        // 2026-09-09 is missed. A name joins tonight and its backfill is served
        // through tonight, as the live endpoint serves it. Read over every bar,
        // the newest stored session before tonight was the joiner's 2026-09-09,
        // so the fetch saw nothing missing and every other name was left short
        // the session, which a reviewer reproduced at the phase 5 sign-off.
        using var store = new TemporaryStore();

        var members = FixtureExpectation.CurrentMembers.Order(StringComparer.Ordinal).ToArray();
        var joiner = members[^1];

        var firstFeeds = NightFeeds.FromFixture(FixtureFolder());
        var withoutJoiner = firstFeeds with { Membership = new RebalancedMembershipFeed(firstFeeds.Membership, joiner, null, null, drop: true) };

        var (first, _, firstError) = await NightAsync(store, withoutJoiner, "night-before-the-join", FixedClock.At(Night, SessionZones.UnitedStates));

        Assert.True(first == 0, firstError);
        Assert.Equal(0, Scalar(store, $"SELECT COUNT(*) FROM bar WHERE ticker = '{joiner}';"));

        var feeds = NightFeeds.FromFixture(FixtureFolder());
        var tonight = feeds with
        {
            Historical = new ThroughTonightHistoricalFeed(feeds.Historical),
            Bulk = new NextSessionBulkFeed(RecordedBulkPriceFeed.FromFolder(FixtureFolder()), new DateOnly(2026, 9, 8)),
        };

        var (code, output, error) = await NightAsync(
            store,
            tonight,
            "night-of-the-join",
            FixedClock.At(new DateTimeOffset(2026, 9, 10, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates));

        Assert.True(code == 0, error);

        // The joiner's backfill did reach tonight, so the case is the one it
        // names rather than a backfill that happened to stop early.
        Assert.Equal(1, Scalar(store, $"SELECT COUNT(*) FROM bar WHERE ticker = '{joiner}' AND session_date = '2026-09-09' AND source = 'historical';"));

        // And every member holds the missed session, fetched in bulk.
        Assert.Contains("1 missed session(s) caught up", output, StringComparison.Ordinal);
        Assert.Equal(members.Length, Scalar(store, "SELECT COUNT(*) FROM bar WHERE session_date = '2026-09-09';"));
        Assert.Equal(members.Length, Scalar(store, "SELECT COUNT(*) FROM bar WHERE session_date = '2026-09-10';"));
    }

    [Fact]
    public void EveryMembershipReadIsOneOfTheTwoFormsOverTheSession()
    {
        // The behaviour above is asserted through the fetch, the backfill, the
        // list and the ladder. Seven sites read the index and the calendar, the
        // news pulse, the close and the read surface are not in that night's
        // assertions, so this holds every one of them to one of the two forms,
        // with the set that stores stated rather than counted: `left IS NULL` on
        // its own is the form that listed a name before it joined, and it
        // passes nothing a reader would notice.
        //
        // Comments are stripped first, because the sites that were corrected
        // say what they read before, and a sentence naming a pattern is not a
        // use of it.
        var leftIsNull = new Regex(@"(?:\w\.)?""*left""*\s+IS\s+NULL", RegexOptions.IgnoreCase);
        var notLeft = new Regex(@"^\s+OR\s+(?:\w\.)?""*left""*\s*>\s*\$(session|on)\b", RegexOptions.IgnoreCase);
        var joinedBy = new Regex(@"\(\s*(?:\w\.)?joined\s+IS\s+NULL\s+OR\s+(?:\w\.)?joined\s*<=\s*\$session\s*\)", RegexOptions.IgnoreCase);

        var stored = new List<string>();
        var member = new List<string>();
        var bare = new List<string>();

        foreach (var file in Directory.GetFiles(Path.Combine(Repository.Root, "src"), "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(Repository.Root, file).Replace('\\', '/');

            if (relative.StartsWith("src/EquityBrief.Tests/", StringComparison.Ordinal)
                || relative.Contains("/Migrations/", StringComparison.Ordinal)
                || relative.Contains("/obj/", StringComparison.Ordinal)
                || relative.Contains("/bin/", StringComparison.Ordinal))
            {
                continue;
            }

            var code = string.Join('\n', File.ReadAllLines(file)
                .Select(line => line.TrimStart().StartsWith("//", StringComparison.Ordinal) ? string.Empty : line));

            foreach (Match site in leftIsNull.Matches(code))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var after = code[(site.Index + site.Length)..];

                if (!notLeft.IsMatch(after))
                {
                    bare.Add(name);

                    continue;
                }

                // The statement the site sits in, read back to its opening
                // quote, is what says whether the join date is asked too.
                var opening = code.LastIndexOf("@\"", site.Index, StringComparison.Ordinal);
                var statement = code[opening..(site.Index + site.Length)];

                (joinedBy.IsMatch(statement) ? member : stored).Add(name);
            }
        }

        Assert.True(bare.Count == 0, "A membership read takes `left IS NULL` alone, which reads an announced change as effective: " + string.Join(", ", bare));

        // The stages that store bars and their adjustment, and the loader's own
        // past-date query, which asks its join date with `joined <= $on`
        // rather than admitting an unknown one and is the one form here that
        // answers about a date other than tonight.
        // The action check reads the form twice: for the names it checks and,
        // from the phase 5 sign-off, for the suspect names it retries.
        Assert.Equal(
            ["Backfill", "BarFetcher", "CorporateActionChecker", "CorporateActionChecker", "MembershipLoader"],
            stored.Order(StringComparer.Ordinal));

        // Every other read asks the join date as well. Stated as a set, and a
        // site added under either form moves one of the two.
        Assert.Equal(
            ["CalendarFetcher", "LadderBuilder", "NewsPulseCounter", "NightClose", "ReadApi", "ReadApi", "ShortlistBuilder"],
            member.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(2026, 9, 12, "Saturday")]
    [InlineData(2026, 9, 7, "Monday")]
    public async Task ANightOnADayTheExchangeDidNotTradeFetchesNothingAndExitsClean(int year, int month, int day, string weekday)
    {
        // A Saturday, and Labor Day. Until the phase 5 sign-off both asked the
        // provider for a session that does not exist and exited 1 at the fetch.
        using var store = new TemporaryStore();

        var feeds = NightFeeds.FromFixture(FixtureFolder());
        var closed = FixedClock.At(new DateTimeOffset(year, month, day, 23, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        var (code, output, error) = await NightAsync(store, feeds, "night-closed", closed);

        Assert.True(code == 0, $"The night exited {code}: {error}");
        Assert.Equal(string.Empty, error);
        Assert.Contains($"is a {weekday} the exchange did not trade", output, StringComparison.Ordinal);

        // Nothing asked of any feed and nothing stored, and the one row that
        // says the night ran.
        Assert.Equal(0, feeds.Requests);
        Assert.Equal(0, Count(store));
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM membership;"));

        var row = Assert.Single(RunLog(store, "night-closed"));

        Assert.Equal(EquityBrief.Worker.Nights.NightClose.Stage, row.Stage);
        Assert.Equal(EquityBrief.Worker.Nights.NightClose.NoSession, row.Outcome);

        // The run page's failed region does not count it. The read surface
        // states the word itself, since it holds no reference to the worker,
        // so the two are asserted to agree here.
        Assert.Equal(EquityBrief.Worker.Nights.NightClose.NoSession, EquityBrief.Api.Reading.RunScreen.NoSession);
        Assert.Empty(EquityBrief.Api.Reading.RunScreen.Failed(
            [new EquityBrief.Web.Marks.StageRow(row.Stage, DateTimeOffset.UnixEpoch, 0, 0, 0, 0, "0", row.Outcome, row.Detail)]));
    }

    [Fact]
    public void TheClosureTableAgreesWithTheCapturedCalendarAndRefusesPastItsRange()
    {
        // Derived from the provider's own captured bars rather than from the
        // table: every weekday inside the window the bar files span is a session
        // exactly where some captured name holds it. A table missing a closure,
        // or holding a day the exchange traded, disagrees here.
        var sessions = Directory.GetFiles(FixtureFolder(), "bars-*.json")
            .SelectMany(file => RecordedHistoricalBarFeed
                .Parse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file))
                .Select(bar => bar.SessionDate))
            .ToHashSet();

        var first = sessions.Min();
        var last = sessions.Max();
        var weekdays = 0;

        for (var day = first; day <= last; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            weekdays++;

            Assert.True(
                EquityBrief.Core.Bars.ExchangeClosures.IsSession(day) == sessions.Contains(day),
                $"{day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)} is " +
                (sessions.Contains(day) ? "a captured session the table calls a closure." : "a closure the table calls a session."));
        }

        // The window carries the property, stated in advance: a year of weekdays.
        Assert.True(weekdays >= 250, $"Compared {weekdays} weekdays, expected a year's worth.");

        // Between a Friday and the Tuesday after a Monday closure, nothing.
        Assert.Empty(EquityBrief.Core.Bars.ExchangeClosures.SessionsBetween(new DateOnly(2026, 9, 4), new DateOnly(2026, 9, 8)));
        Assert.Equal([new DateOnly(2026, 9, 9)], EquityBrief.Core.Bars.ExchangeClosures.SessionsBetween(new DateOnly(2026, 9, 8), new DateOnly(2026, 9, 10)));

        // Past its range it refuses a weekday rather than guessing, and a span
        // holding only a weekend asks it nothing.
        var refused = Assert.Throws<InvalidOperationException>(() => EquityBrief.Core.Bars.ExchangeClosures.IsSession(new DateOnly(2028, 1, 3)));

        Assert.Contains("closure table covers", refused.Message, StringComparison.Ordinal);
        Assert.Empty(EquityBrief.Core.Bars.ExchangeClosures.SessionsBetween(new DateOnly(2028, 1, 7), new DateOnly(2028, 1, 10)));
    }

    static string FetchDetail(TemporaryStore store, string runId)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT detail FROM run_log WHERE run_id = $run AND stage = 'fetch';";
        command.Parameters.AddWithValue("$run", runId);

        return (string)command.ExecuteScalar()!;
    }

    [Fact]
    public async Task AFailingStepIsNamedAndTheNightExitsNonZero()
    {
        // The done condition, and the reason it is a done condition: a night
        // that fails silently in the middle is one the operator finds by
        // noticing the page is stale in the morning.
        using var store = new TemporaryStore();

        var (code, _, error) = await NightAsync(store, Path.Combine(Repository.Root, "fixtures", "no-such-fixture"));

        Assert.Equal(1, code);
        Assert.Contains("nightly: no fixture at", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFeedThatFailsLeavesTheStoredBarsAsTheyWere()
    {
        // The half of the failure row that lands here. The banner and tonight's
        // list are phase 5 surfaces, so the row's claim is owed at 5.4; what is
        // owed now is that a failed fetch keeps last night's bars rather than
        // leaving a half-written night.
        using var store = new TemporaryStore();

        await NightAsync(store);

        var before = Count(store);

        var failing = new FailingBulkFeed();
        var fetcher = new BarFetcher(failing, FixedClock.At(Night, SessionZones.UnitedStates), store.DatabaseFile);

        await Assert.ThrowsAsync<HttpRequestFailure>(() => fetcher.RunAsync("GSPC", "run-broken"));

        Assert.Equal(before, Count(store));
        Assert.True(before > 700, $"The store held {before} bars, so this compared two small numbers.");
    }

    // Sessions in one captured file at or after a date, read through the
    // shipped parser rather than by reading the JSON here, so the count is the
    // one the pipeline sees.
    static int SessionsFrom(string file, DateOnly opens) =>
        EquityBrief.Core.Providers.RecordedHistoricalBarFeed
            .Parse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file))
            .Count(bar => bar.SessionDate >= opens);

    static int Scalar(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt32(command.ExecuteScalar());
    }

    static int Count(TemporaryStore store)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM bar;";

        return Convert.ToInt32(command.ExecuteScalar());
    }
}

// A feed that fails the way the provider does. Named for what it stands in for,
// so the test reads as the failure row rather than as an exception.
public sealed class HttpRequestFailure(string message) : Exception(message);

// A feed that answers with the file minus one name, which is what a truncated
// bulk payload looks like: the name is absent altogether rather than present and
// not traded.
sealed class ShortBulkFeed(EquityBrief.Core.Providers.IBulkPriceFeed inner, params string[] drop)
    : EquityBrief.Core.Providers.IBulkPriceFeed
{
    public int Requests => inner.Requests;

    public IReadOnlyList<string> NotSessions => inner.NotSessions;

    public async Task<IReadOnlyList<EquityBrief.Core.Providers.BulkBar>> RowsAsync(
        string exchange,
        DateOnly session,
        CancellationToken cancellation = default) =>
        [.. (await inner.RowsAsync(exchange, session, cancellation))
            .Where(row => !drop.Contains(row.Ticker, StringComparer.Ordinal))];
}

// A feed that answers for the session asked for with another session's rows,
// redated. It stands in for the next evening's file, which the fixture does not
// hold, so a second consecutive night has something to store.
sealed class NextSessionBulkFeed(EquityBrief.Core.Providers.IBulkPriceFeed inner, DateOnly captured)
    : EquityBrief.Core.Providers.IBulkPriceFeed
{
    public int Requests => inner.Requests;

    public IReadOnlyList<string> NotSessions => inner.NotSessions;

    public async Task<IReadOnlyList<EquityBrief.Core.Providers.BulkBar>> RowsAsync(
        string exchange,
        DateOnly session,
        CancellationToken cancellation = default) =>
        [.. (await inner.RowsAsync(exchange, captured, cancellation))
            .Select(row => row with { Bar = row.Bar with { SessionDate = session } })];
}

// A feed whose file for one session holds none of the index, which is what a
// closure the table does not know about looks like from the fetcher.
sealed class HoledBulkFeed(EquityBrief.Core.Providers.IBulkPriceFeed inner, DateOnly holed, IReadOnlyCollection<string> members)
    : EquityBrief.Core.Providers.IBulkPriceFeed
{
    public int Requests => inner.Requests;

    public IReadOnlyList<string> NotSessions => inner.NotSessions;

    public async Task<IReadOnlyList<EquityBrief.Core.Providers.BulkBar>> RowsAsync(
        string exchange,
        DateOnly session,
        CancellationToken cancellation = default)
    {
        var rows = await inner.RowsAsync(exchange, session, cancellation);

        return session == holed
            ? [.. rows.Where(row => !members.Contains(row.Ticker, StringComparer.Ordinal))]
            : rows;
    }
}

// A membership feed carrying a rebalance the recorded one does not: one name
// leaving and one joining on an effective date after the night, or one name
// dropped altogether, which is a name the index has not announced yet.
sealed class RebalancedMembershipFeed(
    IIndexMembershipFeed inner,
    string leaverOrDropped,
    string? joiner,
    DateOnly? effective,
    bool drop = false) : IIndexMembershipFeed
{
    public int Requests => inner.Requests;

    public async Task<IReadOnlyList<IndexConstituent>> ConstituentsAsync(
        string indexCode,
        CancellationToken cancellationToken = default) =>
        [.. (await inner.ConstituentsAsync(indexCode, cancellationToken))
            .Where(row => !(drop && row.Ticker == leaverOrDropped))
            .Select(row =>
                !drop && row.Ticker == leaverOrDropped ? row with { Left = effective }
                : row.Ticker == joiner ? row with { Joined = effective }
                : row)];
}

// A historical feed that serves a name through the night it is asked on, as the
// live endpoint does, by carrying the last captured session forward to every
// weekday up to the end of the range. The recorded one stops at the day it was
// captured, which is what hid a joiner's backfill reaching past a missed night.
sealed class ThroughTonightHistoricalFeed(IHistoricalBarFeed inner) : IHistoricalBarFeed
{
    public int Requests => inner.Requests;

    public async Task<IReadOnlyList<ProviderBar>> BarsAsync(
        string ticker,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        var bars = (await inner.BarsAsync(ticker, from, to, cancellationToken)).ToList();

        if (bars.Count == 0)
        {
            return bars;
        }

        var last = bars[^1];

        foreach (var day in EquityBrief.Core.Bars.ExchangeClosures.SessionsBetween(last.SessionDate, to.AddDays(1)))
        {
            bars.Add(last with { SessionDate = day });
        }

        return bars;
    }
}

// The recorded action feed with one split added on a named member, so the action
// step has a name to refetch on a day the capture carries none for the index.
sealed class OneActionFeed(ICorporateActionFeed inner, string ticker) : ICorporateActionFeed
{
    public int Requests => inner.Requests;

    public async Task<IReadOnlyList<CorporateAction>> ActionsAsync(
        string exchange,
        DateOnly session,
        CancellationToken cancellation = default) =>
        [.. await inner.ActionsAsync(exchange, session, cancellation), new CorporateAction(ticker, session, ActionKind.Split, "2/1")];
}

// A historical feed that never answers, so a refetch hangs until the night's
// deadline cancels it.
sealed class SlowHistoricalFeed : IHistoricalBarFeed
{
    public int Requests { get; private set; }

    public async Task<IReadOnlyList<ProviderBar>> BarsAsync(
        string ticker,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        Requests++;

        await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);

        return [];
    }
}

// A feed that never answers, which is what a hung socket looks like from here.
sealed class SlowBulkFeed : EquityBrief.Core.Providers.IBulkPriceFeed
{
    public int Requests { get; private set; }

    public IReadOnlyList<string> NotSessions => [];

    public async Task<IReadOnlyList<EquityBrief.Core.Providers.BulkBar>> RowsAsync(
        string exchange,
        DateOnly session,
        CancellationToken cancellation = default)
    {
        Requests++;

        await Task.Delay(TimeSpan.FromMinutes(5), cancellation);

        return [];
    }
}

sealed class FailingBulkFeed : EquityBrief.Core.Providers.IBulkPriceFeed
{
    public int Requests { get; private set; }

    // A feed that did not answer skipped nothing. The two are different states
    // and this is the one where the file never arrived.
    public IReadOnlyList<string> NotSessions => [];

    public Task<IReadOnlyList<EquityBrief.Core.Providers.BulkBar>> RowsAsync(
        string exchange,
        DateOnly session,
        CancellationToken cancellation = default)
    {
        Requests++;

        throw new HttpRequestFailure("the bulk price feed did not answer");
    }
}
