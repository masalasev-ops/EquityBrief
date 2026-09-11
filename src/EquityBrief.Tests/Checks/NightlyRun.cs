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
            CheckReach.Key(NightlyRunSteps.Heading, "Fetch the day's bulk bar file, one request, and store the bars for current members."),
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
