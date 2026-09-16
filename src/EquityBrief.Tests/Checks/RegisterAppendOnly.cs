using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// register-append-only.
//
// The candidate register refuses updates and deletes, the correction divisor
// matches the rows registered before the window opened, and a registered
// candidate whose evaluator's source has moved without its version fails rather
// than being evaluated under a rule the register does not name.
//
// The whole guardrail rests on one thing being true: that what was registered
// cannot be changed once results are in. Every other part of the loop is arithmetic
// over rows, and arithmetic over rows somebody could edit afterwards is a
// pre-registration in name only. So the refusal is asserted at the store, where
// it holds against anything that opens the file, and not only against the one
// component this repository gives an insert to.
public class RegisterAppendOnly
{
    internal static CheckReach Reach => new(
        "register-append-only",
        ["docs/ARCHITECTURE.html", "docs/SCHEMA.md"],
        [
            CheckReach.Key(Scope.StoresTable, "Candidate register"),
            CheckReach.Key(Scope.FailureTable, "Something tries to edit or delete a register row"),
            CheckReach.Key(Scope.FailureTable, "The candidate register and the correction disagree"),
            CheckReach.Key(Scope.LimitsTable, "Family size and correction"),
        ]);

    const string Table = "candidate_register";

    static readonly DateTimeOffset Opened = new(2026, 9, 16, 21, 0, 0, TimeSpan.Zero);

    // ---- the refusal at the store ----

    [Fact]
    public async Task AnUpdateAndADeleteAreBothRefusedByTheTableItself()
    {
        using var store = new TemporaryStore().Migrated();

        var registrar = new CandidateRegistrar(Clock(Opened), store.DatabaseFile);

        var written = await registrar.RegisterAsync(
            "momentum index at thirty",
            "the relative strength index at or below thirty",
            "the share of setups that beat their own break-even",
            MomentumIndexReading.EvaluatorName,
            new Dictionary<string, double>(StringComparer.Ordinal) { [MomentumIndexReading.Level] = 30 },
            "register-test-1");

        Assert.Equal(CandidateRegistrar.Registered, written.Outcome);

        var before = await RowsAsync(store);

        Assert.Single(before);

        // Both statements, against the store rather than through the registrar,
        // because the registrar offers neither and the property is about what the
        // file refuses rather than about what this repository happens to ask it.
        var edit = Assert.Throws<SqliteException>(() => store.Execute(
            $"UPDATE {Table} SET parameters = '{{\"level\": 70}}' WHERE id = 1;"));

        var removal = Assert.Throws<SqliteException>(() => store.Execute(
            $"DELETE FROM {Table} WHERE id = 1;"));

        Assert.Contains("append only", edit.Message, StringComparison.Ordinal);
        Assert.Contains("append only", removal.Message, StringComparison.Ordinal);

        // The other half of section 18's row, and the one an abort that rolled
        // back only part of a statement would fail: nothing changed, and the
        // register still reads as it did.
        var after = await RowsAsync(store);

        Assert.Equal(before, after);
    }

    [Fact]
    public async Task ARetirementIsANewRowNamingWhatItRetiresAndTheOriginalStands()
    {
        using var store = new TemporaryStore().Migrated();

        var registrar = new CandidateRegistrar(Clock(Opened), store.DatabaseFile);

        await registrar.RegisterAsync(
            "momentum index at thirty",
            "the relative strength index at or below thirty",
            "the share of setups that beat their own break-even",
            MomentumIndexReading.EvaluatorName,
            new Dictionary<string, double>(StringComparer.Ordinal) { [MomentumIndexReading.Level] = 30 },
            "register-test-2");

        var registered = Assert.Single(await RowsAsync(store));

        var withdrawn = await registrar.RetireAsync(
            "momentum index at thirty",
            "14 resolved setups of a minimum of 250, and the reading fired on 4 of 2,018 name-nights",
            "register-test-3");

        Assert.Equal(CandidateRegistrar.Retired, withdrawn.Outcome);

        var rows = await RowsAsync(store);

        Assert.Equal(2, rows.Count);

        // The registration is still there, byte for byte, which is what append
        // only means and what a retirement implemented as an edit would break.
        Assert.Equal(registered, rows[0]);

        Assert.Equal(CandidateFamily.Retired, rows[1].Event);
        Assert.Equal("momentum index at thirty", rows[1].Retires);
        Assert.Contains("250", rows[1].Evidence!, StringComparison.Ordinal);

        // And the retiring row carries what was withdrawn rather than blanks, so
        // a person reading the register alone can see what stopped being tested.
        Assert.Equal(registered.Evaluator, rows[1].Evaluator);
        Assert.Equal(registered.EvaluatorVersion, rows[1].EvaluatorVersion);
        Assert.Equal(registered.Parameters, rows[1].Parameters);

        // A retirement of something nothing registered is refused, and recorded,
        // rather than writing a row the divisor would subtract against nothing.
        var nothing = await registrar.RetireAsync("a candidate nobody registered", "none", "register-test-4");

        Assert.Equal(CandidateRegistrar.Refused, nothing.Outcome);
        Assert.Equal(2, (await RowsAsync(store)).Count);
    }

    [Fact]
    public async Task AChangeToACandidateThatStandsRegisteredIsRefusedAndTheAttemptIsOnTheRunLog()
    {
        // Section 18's row from the other end. The store refuses an edit made
        // against the file; this is the edit made at the front door, which is the
        // one that can be told why, and the run log is the surface it is told on.
        using var store = new TemporaryStore().Migrated();

        var registrar = new CandidateRegistrar(Clock(Opened), store.DatabaseFile);

        var parameters = new Dictionary<string, double>(StringComparer.Ordinal) { [MomentumIndexReading.Level] = 30 };

        await registrar.RegisterAsync(
            "momentum index at thirty",
            "the relative strength index at or below thirty",
            "the share of setups that beat their own break-even",
            MomentumIndexReading.EvaluatorName,
            parameters,
            "register-test-5");

        var again = await registrar.RegisterAsync(
            "momentum index at thirty",
            "the relative strength index at or below forty",
            "the share of setups that beat their own break-even",
            MomentumIndexReading.EvaluatorName,
            new Dictionary<string, double>(StringComparer.Ordinal) { [MomentumIndexReading.Level] = 40 },
            "register-test-6");

        Assert.Equal(CandidateRegistrar.Refused, again.Outcome);
        Assert.Single(await RowsAsync(store));

        var log = Refusals(store);

        Assert.Single(log);
        Assert.Contains("already stands registered", log[0], StringComparison.Ordinal);

        // A retirement and then a fresh registration is the way through, and it
        // leaves three rows rather than one edited one.
        await registrar.RetireAsync("momentum index at thirty", "superseded by the same reading at forty", "register-test-7");

        var replacement = await registrar.RegisterAsync(
            "momentum index at forty",
            "the relative strength index at or below forty",
            "the share of setups that beat their own break-even",
            MomentumIndexReading.EvaluatorName,
            new Dictionary<string, double>(StringComparer.Ordinal) { [MomentumIndexReading.Level] = 40 },
            "register-test-8");

        Assert.Equal(CandidateRegistrar.Registered, replacement.Outcome);
        Assert.Equal(3, (await RowsAsync(store)).Count);
    }

    [Fact]
    public async Task AnEvaluatorNobodyCarriesAndAFamilyAtItsMaximumAreBothRefusedAtTheWrite()
    {
        using var store = new TemporaryStore().Migrated();

        var registrar = new CandidateRegistrar(Clock(Opened), store.DatabaseFile);

        var unknown = await registrar.RegisterAsync(
            "something plausible",
            "a rule in words",
            "a test in words",
            "an-evaluator-nothing-implements",
            new Dictionary<string, double>(StringComparer.Ordinal),
            "register-test-9");

        Assert.Equal(CandidateRegistrar.Refused, unknown.Outcome);
        Assert.Contains("no evaluator named", unknown.Detail, StringComparison.Ordinal);
        Assert.Empty(await RowsAsync(store));

        // A parameter set the evaluator does not read, which is the same fault
        // one column along: the row would say what runs and be wrong about it.
        var mismatched = await registrar.RegisterAsync(
            "momentum index at thirty",
            "a rule in words",
            "a test in words",
            MomentumIndexReading.EvaluatorName,
            new Dictionary<string, double>(StringComparer.Ordinal) { ["threshold"] = 30 },
            "register-test-10");

        Assert.Equal(CandidateRegistrar.Refused, mismatched.Outcome);
        Assert.Contains("does not read is a row", mismatched.Detail, StringComparison.Ordinal);

        // The maximum, over constructed rows rather than by registering eight,
        // because what is being asserted is the bound and not the loop.
        var full = Enumerable.Range(1, CandidateFamily.Maximum)
            .Select(index => Row(index, FormattableString.Invariant($"candidate {index}"), CandidateFamily.Registered, null, Opened.AddDays(-1)))
            .ToArray();

        var refusal = CandidateRegistrar.Refusal(
            full,
            "one more",
            MomentumIndexReading.EvaluatorName,
            new Dictionary<string, double>(StringComparer.Ordinal) { [MomentumIndexReading.Level] = 30 },
            Opened);

        Assert.NotNull(refusal);
        Assert.Contains("maximum family of 8", refusal, StringComparison.Ordinal);

        // And one below it is accepted, so the bound is the boundary rather than
        // a refusal that fires early and reads the same from outside.
        Assert.Null(CandidateRegistrar.Refusal(
            full[..(CandidateFamily.Maximum - 1)],
            "one more",
            MomentumIndexReading.EvaluatorName,
            new Dictionary<string, double>(StringComparer.Ordinal) { [MomentumIndexReading.Level] = 30 },
            Opened));
    }

    // ---- the divisor ----

    [Fact]
    public void TheDivisorCountsTheRowsRegisteredBeforeTheWindowOpenedAndNoOthers()
    {
        // Hand-worked, over constructed rows. Four cases and they are not
        // symmetric: the two that matter are a candidate registered after the
        // window, which would make the test harder than it was, and one retired
        // after it, which would make the test easier, and only the second turns
        // noise into a discovery.
        var before = Opened.AddDays(-7);
        var after = Opened.AddDays(7);

        RegisterRow[] rows =
        [
            Row(1, "registered before", CandidateFamily.Registered, null, before),
            Row(2, "registered before and retired before", CandidateFamily.Registered, null, before),
            Row(3, "registered before and retired before", CandidateFamily.Retired, "registered before and retired before", before),
            Row(4, "registered before and retired after", CandidateFamily.Registered, null, before),
            Row(5, "registered before and retired after", CandidateFamily.Retired, "registered before and retired after", after),
            Row(6, "registered after", CandidateFamily.Registered, null, after),
        ];

        // Two: "registered before", and "registered before and retired after",
        // which was among the things being tried for the whole of the window.
        Assert.Equal(2, CandidateFamily.Divisor(rows, Opened));

        // The window as it stands later sees the retirement and the late
        // registration, which is what restarting the clock means.
        Assert.Equal(2, CandidateFamily.Divisor(rows, after.AddDays(1)));

        // A window that opened before anything was registered divides by nothing,
        // which is the case a corrected threshold must not silently read as one.
        Assert.Equal(0, CandidateFamily.Divisor(rows, before.AddDays(-1)));

        // And what stands registered, asked at three instants, because the shadow
        // column asks this of every row on every night.
        Assert.True(CandidateFamily.StandsAt(rows, "registered before", Opened));
        Assert.False(CandidateFamily.StandsAt(rows, "registered before and retired before", Opened));
        Assert.True(CandidateFamily.StandsAt(rows, "registered before and retired after", Opened));
        Assert.False(CandidateFamily.StandsAt(rows, "registered before and retired after", after));
        Assert.False(CandidateFamily.StandsAt(rows, "registered after", Opened));
        Assert.True(CandidateFamily.StandsAt(rows, "registered after", after));
    }

    [Fact]
    public async Task TheDivisorOverTheStoreMatchesTheDivisorOverTheRowsItHolds()
    {
        // Section 18's other row: the register and the correction disagreeing is
        // a failure of the harness rather than a verdict shown anyway. The two
        // are computed from the same rows by two routes, one through the store
        // and one over what was written, and they are asserted equal.
        using var store = new TemporaryStore().Migrated();

        var clock = Clock(Opened.AddDays(-3));
        var registrar = new CandidateRegistrar(clock, store.DatabaseFile);

        foreach (var (candidate, level, run) in new[]
        {
            ("momentum index at thirty", 30d, "register-test-9a"),
            ("momentum index at twenty", 20d, "register-test-9b"),
        })
        {
            await registrar.RegisterAsync(
                candidate,
                "the relative strength index at or below the level",
                "the share of setups that beat their own break-even",
                MomentumIndexReading.EvaluatorName,
                new Dictionary<string, double>(StringComparer.Ordinal) { [MomentumIndexReading.Level] = level },
                run);
        }

        var rows = await registrar.RowsAsync();

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, CandidateFamily.Divisor(rows, Opened));

        // Counted straight out of the store by a query rather than through the
        // reader, so a reader that dropped a row would not agree with itself.
        Assert.Equal(CandidateFamily.Divisor(rows, Opened), StandingAt(store, Opened));
    }

    // ---- the evaluator pin ----

    [Fact]
    public void EveryEvaluatorsVersionIsTheHashOfItsOwnSource()
    {
        var evaluators = CandidateEvaluators.All;

        Assert.True(evaluators.Count >= 2, $"The code carries {evaluators.Count} evaluator(s), expected at least 2.");

        var faults = new List<string>();

        foreach (var evaluator in evaluators)
        {
            var source = SourceOf(evaluator);
            var pin = CandidateEvaluator.Pin(source);

            if (pin != evaluator.Version)
            {
                faults.Add(
                    $"{evaluator.Name} carries version {evaluator.Version} and its source hashes to {pin}. " +
                    "A changed evaluator is a new registration retiring the old one, so raise the version " +
                    "to the hash and register the candidate again rather than editing what the register names.");
            }
        }

        Assert.Empty(faults);

        // Two versions of one file differ, which is the property the equality
        // above rests on and which a hash of nothing would pass without.
        var first = SourceOf(evaluators[0]);

        Assert.NotEqual(CandidateEvaluator.Pin(first), CandidateEvaluator.Pin(first + "\n// a line that changes what this does\n"));

        // And each evaluator's name resolves, both ways, so the catalogue cannot
        // carry a name nothing implements or an evaluator nothing may register.
        Assert.All(evaluators, evaluator => Assert.Same(evaluator, CandidateEvaluators.Find(evaluator.Name)));
        Assert.Null(CandidateEvaluators.Find("an-evaluator-nothing-implements"));
    }

    [Fact]
    public void ThePinIsTakenOverTheSameBytesOnEitherPlatformAndOverEitherLineEnding()
    {
        // The two ways a checkout can differ from the bytes that were committed
        // with nothing in the file having changed. Without this the version of an
        // untouched evaluator would depend on which machine cloned the
        // repository, and this check would be reporting on the checkout.
        const string source =
            "namespace EquityBrief.Core.Candidates;\n" +
            "public sealed class Sample : CandidateEvaluator\n" +
            "{\n" +
            "    public override string Version => \"000000000000\";\n" +
            "}\n";

        var lf = CandidateEvaluator.Pin(source);

        Assert.Equal(lf, CandidateEvaluator.Pin(source.Replace("\n", "\r\n", StringComparison.Ordinal)));
        Assert.Equal(lf, CandidateEvaluator.Pin("﻿" + source));
        Assert.Equal(lf, CandidateEvaluator.Pin("﻿" + source.Replace("\n", "\r\n", StringComparison.Ordinal)));

        // The version line is the one line the pin is taken over the absence of,
        // because a hash of a file including its own hash never settles. Changing
        // it moves nothing; changing anything else moves the pin.
        Assert.Equal(lf, CandidateEvaluator.Pin(source.Replace("000000000000", "ffffffffffff", StringComparison.Ordinal)));
        Assert.NotEqual(lf, CandidateEvaluator.Pin(source.Replace("Sample", "Other", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ARegisteredCandidateWhoseEvaluatorHasMovedOnFails()
    {
        // The roster row's own words, over a store holding a row that names a
        // version the code no longer carries. It is written straight into the
        // table rather than through the registrar, because the registrar writes
        // the version the code has and the state being asserted is the one that
        // arrives when the code moves afterwards.
        using var store = new TemporaryStore().Migrated();

        Insert(store, 1, "momentum index at thirty", MomentumIndexReading.EvaluatorName, "000000000000", CandidateFamily.Registered, null, Opened);

        var registrar = new CandidateRegistrar(Clock(Opened), store.DatabaseFile);
        var drifted = Drifted(await registrar.RowsAsync());

        var found = Assert.Single(drifted);

        Assert.Contains("momentum index at thirty", found, StringComparison.Ordinal);
        Assert.Contains("000000000000", found, StringComparison.Ordinal);

        // A row naming the version the code carries is not drifted, and a
        // retired one is not asked about at all: the rule is about candidates
        // still being evaluated.
        Insert(store, 2, "momentum index at twenty", MomentumIndexReading.EvaluatorName, new MomentumIndexReading().Version, CandidateFamily.Registered, null, Opened);
        Insert(store, 3, "momentum index at thirty", MomentumIndexReading.EvaluatorName, "000000000000", CandidateFamily.Retired, "momentum index at thirty", Opened);

        Assert.Empty(Drifted(await registrar.RowsAsync()));

        // And the corpus's own register, which is empty here because nothing in
        // this repository registers a candidate as part of a build. The
        // population is stated so a run over none cannot read as a pass.
        Assert.Empty(Drifted([]));
    }

    // Which registered, unretired rows name a version their evaluator no longer
    // carries. Apart from the fact so the proof above runs this reader rather
    // than a copy of it, and so the shadow column at 8.4 asks the same question.
    internal static IReadOnlyList<string> Drifted(IReadOnlyList<RegisterRow> rows)
    {
        var faults = new List<string>();

        foreach (var row in rows.Where(row => row.Event == CandidateFamily.Registered))
        {
            if (!CandidateFamily.StandsAt(rows, row.Candidate, DateTimeOffset.MaxValue))
            {
                continue;
            }

            var evaluator = CandidateEvaluators.Find(row.Evaluator);

            if (evaluator is null)
            {
                faults.Add($"'{row.Candidate}' names evaluator '{row.Evaluator}', which the code does not carry.");
            }
            else if (evaluator.Version != row.EvaluatorVersion)
            {
                faults.Add(
                    $"'{row.Candidate}' was registered under {row.Evaluator} at {row.EvaluatorVersion} and the " +
                    $"code carries {evaluator.Version}. A changed evaluator is a new registration, so the row " +
                    "names a rule the code no longer runs.");
            }
        }

        return faults;
    }

    // ---- the fixture ----

    // Named the way every other check names an expectation, so the sweep that
    // asks whether a file is read at all can find this one.
    static System.Text.Json.JsonElement Expected(string stage) =>
        System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Repository.Root, "fixtures", "membership-2026-09-05", "expectations", stage + ".json"))).RootElement;

    static DateTimeOffset At(System.Text.Json.JsonElement one, string name) =>
        DateTimeOffset.ParseExact(
            one.GetProperty(name).GetString()!,
            "yyyy-MM-ddTHH:mm:ssZ",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    // The registrations the whole-pipeline replay writes, read from the
    // expectation rather than written here, so the file is the statement of what
    // the fixture holds and this is only what performs it.
    internal static async Task ReplayRegistrationsAsync(TemporaryStore store)
    {
        var expectation = Expected("candidate-register");

        foreach (var one in expectation.GetProperty("registrations").EnumerateArray())
        {
            var registrar = new CandidateRegistrar(Clock(At(one, "registeredAt")), store.DatabaseFile);

            var parameters = one.GetProperty("parameters").EnumerateObject()
                .ToDictionary(pair => pair.Name, pair => pair.Value.GetDouble(), StringComparer.Ordinal);

            var outcome = await registrar.RegisterAsync(
                one.GetProperty("candidate").GetString()!,
                one.GetProperty("rule").GetString()!,
                one.GetProperty("test").GetString()!,
                one.GetProperty("evaluator").GetString()!,
                parameters,
                "replay-register-" + one.GetProperty("candidate").GetString()!.Replace(' ', '-'));

            Assert.Equal(CandidateRegistrar.Registered, outcome.Outcome);
        }

        foreach (var one in expectation.GetProperty("retirements").EnumerateArray())
        {
            var registrar = new CandidateRegistrar(Clock(At(one, "registeredAt")), store.DatabaseFile);

            var outcome = await registrar.RetireAsync(
                one.GetProperty("retires").GetString()!,
                one.GetProperty("evidence").GetString()!,
                "replay-retire-" + one.GetProperty("retires").GetString()!.Replace(' ', '-'));

            Assert.Equal(CandidateRegistrar.Retired, outcome.Outcome);
        }
    }

    [Fact]
    public async Task TheDivisorOverTheFixtureMatchesTheOneWorkedByHand()
    {
        // The derived expectation. Every divisor in the file is worked in the
        // file itself, over rows the file also states, so what is compared is a
        // derivation against the code rather than a run against a copy of itself.
        using var store = new TemporaryStore().Migrated();

        await ReplayRegistrationsAsync(store);

        var expectation = Expected("candidate-register");
        var rows = await new CandidateRegistrar(Clock(Opened), store.DatabaseFile).RowsAsync();

        Assert.Equal(expectation.GetProperty("rowsWritten").GetInt32(), rows.Count);
        Assert.Equal(CandidateFamily.Maximum, expectation.GetProperty("maximumFamily").GetInt32());

        var divisors = expectation.GetProperty("divisors").EnumerateArray().ToArray();

        Assert.True(divisors.Length >= 4, $"The expectation works {divisors.Length} divisor(s), expected at least 4.");

        foreach (var one in divisors)
        {
            var opened = one.GetProperty("windowOpenedAt").GetString();

            Assert.Equal(
                (opened, one.GetProperty("divisor").GetInt32()),
                (opened, CandidateFamily.Divisor(rows, At(one, "windowOpenedAt"))));
        }

        // The divisors are not all the same number, so a Divisor that returned a
        // constant could not pass the loop above.
        Assert.True(
            divisors.Select(one => one.GetProperty("divisor").GetInt32()).Distinct().Count() >= 3,
            "The worked divisors take fewer than three values, so a constant would satisfy them.");

        foreach (var one in expectation.GetProperty("standing").EnumerateArray())
        {
            var candidate = one.GetProperty("candidate").GetString()!;
            var at = one.GetProperty("at").GetString();

            Assert.Equal(
                (at, candidate, one.GetProperty("stands").GetBoolean()),
                (at, candidate, CandidateFamily.StandsAt(rows, candidate, At(one, "at"))));
        }

        // And the rows themselves carry the evaluator and the version the code
        // holds, which is what makes the register a statement of what will run.
        foreach (var row in rows)
        {
            var evaluator = CandidateEvaluators.Find(row.Evaluator);

            Assert.NotNull(evaluator);
            Assert.Equal(evaluator.Version, row.EvaluatorVersion);
        }
    }

    // ---- the source half, and the document ----

    [Fact]
    public void NothingInTheShippedSourceUpdatesOrDeletesARegisterRow()
    {
        var sources = Repository.SourceFiles()
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "EquityBrief.Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToArray();

        Assert.True(sources.Length >= 5, $"Scanned {sources.Length} shipped source files, expected at least 5.");

        var offences = sources
            .SelectMany(file => SourceStatements.In(File.ReadAllText(file))
                .Where(write => write.Table.Equals(Table, StringComparison.OrdinalIgnoreCase))
                .Where(write => write.Operation is not SourceStatements.Insert)
                .Select(write => $"{Path.GetFileName(file)}: {write.Operation} on {write.Table}"))
            .ToArray();

        Assert.Empty(offences);

        // The population carrying the property is the inserts, and a run finding
        // none has asserted nothing about a table nothing writes to.
        var inserts = sources
            .SelectMany(file => SourceStatements.In(File.ReadAllText(file))
                .Where(write => write.Table.Equals(Table, StringComparison.OrdinalIgnoreCase) && write.Operation == SourceStatements.Insert))
            .ToArray();

        Assert.True(inserts.Length >= 1, $"Found {inserts.Length} insert(s) against {Table}, expected at least 1.");

        // And the reader can find one, so the emptiness above is a property
        // rather than a reader that looks at the wrong table.
        Assert.Contains(
            SourceStatements.In($"UPDATE {Table} SET rule = 'x';"),
            write => write.Operation == SourceStatements.Update && write.Table == Table);

        Assert.Contains(
            SourceStatements.In($"DELETE FROM {Table} WHERE id = 1;"),
            write => write.Operation == SourceStatements.Delete && write.Table == Table);
    }

    [Fact]
    public void TheMaximumTheLimitsRowStatesIsTheOneTheCodeCarries()
    {
        // Section 17's row, pinned. The figure is a convention rather than a
        // measurement, which is exactly why it needs pinning: nothing else would
        // notice the document and the bound drifting apart.
        var architecture = Corpus.Read("docs/ARCHITECTURE.html");

        var stated = System.Text.RegularExpressions.Regex.Matches(
                architecture,
                @"maximum family size of (\d+)")
            .Select(match => int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();

        Assert.True(stated.Length >= 1, $"Found {stated.Length} statement(s) of the maximum family size, expected at least 1.");
        Assert.All(stated, size => Assert.Equal(CandidateFamily.Maximum, size));
    }

    // ---- helpers ----

    // The session zone is the one every other clock in this repository resolves,
    // so a test written against it exercises the derivation a night uses rather
    // than a second one written for the test.
    static FixedClock Clock(DateTimeOffset at) => FixedClock.At(at, SessionZones.UnitedStates);

    static string SourceOf(CandidateEvaluator evaluator) =>
        File.ReadAllText(Path.Combine(
            Repository.Root,
            "src",
            "EquityBrief.Core",
            "Candidates",
            evaluator.GetType().Name + ".cs"));

    static RegisterRow Row(long id, string candidate, string @event, string? retires, DateTimeOffset at) =>
        new(id, candidate, "a rule", "a test", MomentumIndexReading.EvaluatorName, "{}", "000000000000", @event, retires, at, null);

    static async Task<IReadOnlyList<RegisterRow>> RowsAsync(TemporaryStore store) =>
        await new CandidateRegistrar(Clock(Opened), store.DatabaseFile).RowsAsync();

    static IReadOnlyList<string> Refusals(TemporaryStore store)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = $"SELECT detail FROM run_log WHERE stage = '{CandidateRegistrar.Stage}' AND outcome = '{CandidateRegistrar.Refused}' ORDER BY started_at;";

        var details = new List<string>();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            details.Add(reader.GetString(0));
        }

        return details;
    }

    static int StandingAt(TemporaryStore store, DateTimeOffset at)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = $@"
            SELECT COUNT(*) FROM (
                SELECT candidate FROM {Table}
                WHERE event = '{CandidateFamily.Registered}' AND registered_at < $at
                EXCEPT
                SELECT retires FROM {Table}
                WHERE event = '{CandidateFamily.Retired}' AND registered_at < $at
            );";

        command.Parameters.AddWithValue("$at", at.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    static void Insert(
        TemporaryStore store,
        long id,
        string candidate,
        string evaluator,
        string version,
        string @event,
        string? retires,
        DateTimeOffset at)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = $@"
            INSERT INTO {Table} (
                id, candidate, rule, test, evaluator, parameters, evaluator_version,
                event, retires, registered_at, evidence)
            VALUES ($id, $candidate, 'a rule', 'a test', $evaluator, '{{}}', $version, $event, $retires, $at, NULL);";

        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$candidate", candidate);
        command.Parameters.AddWithValue("$evaluator", evaluator);
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$event", @event);
        command.Parameters.AddWithValue("$retires", (object?)retires ?? DBNull.Value);
        command.Parameters.AddWithValue("$at", at.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

        command.ExecuteNonQuery();
    }
}
