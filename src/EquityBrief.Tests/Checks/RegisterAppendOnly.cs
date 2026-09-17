using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Prices;
using EquityBrief.Data;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Indicators;
using EquityBrief.Worker.Shortlist;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Shortlist;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// register-append-only.
//
// The candidate register refuses an update, a delete and a replace, the correction divisor
// counts the candidates standing before the window opened, and a registered candidate whose
// evaluation's sources have moved without its version fails rather than being evaluated under a
// rule the register does not name.
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

        var logged = RunLogRows(store);

        // A replace, in each form SQLite accepts one, with recursive triggers off and on.
        foreach (var recursive in new[] { 0, 1 })
        {
            foreach (var rewrite in Rewrites)
            {
                using var connection = store.Open();
                using var pragma = connection.CreateCommand();

                pragma.CommandText = FormattableString.Invariant($"PRAGMA recursive_triggers = {recursive};");
                pragma.ExecuteNonQuery();

                using var command = connection.CreateCommand();

                command.CommandText = rewrite;

                var refused = Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());

                Assert.Contains("append only", refused.Message, StringComparison.Ordinal);

                pragma.CommandText = "PRAGMA recursive_triggers = 0;";
                pragma.ExecuteNonQuery();
            }
        }

        // The refusal rolls back the statement it refuses, so nothing reaches the run log either; that half is the registrar's.
        Assert.Equal(logged, RunLogRows(store));

        // The id is the table's only key, being its row id, so a replace can conflict with nothing the trigger does not read.
        Assert.Empty(IndexesOn(store));

        // The other half of section 18's row, and the one an abort that rolled
        // back only part of a statement would fail: nothing changed, and the
        // register still reads as it did.
        var after = await RowsAsync(store);

        Assert.Equal(before, after);

        // And a row the table does not hold is still an append.
        store.Execute(Rewrites[0].Replace("VALUES (1,", "VALUES (2,", StringComparison.Ordinal).Replace("INSERT OR REPLACE", "INSERT", StringComparison.Ordinal));

        Assert.Equal(2, (await RowsAsync(store)).Count);
    }

    const string Columns = "id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence";

    static readonly string[] Rewrites =
    [
        $"INSERT OR REPLACE INTO {Table} ({Columns}) VALUES (1, 'momentum index at thirty', 'a rule', 'a test', '{MomentumIndexReading.EvaluatorName}', '{{\"level\": 70}}', '000000000000', 'registered', NULL, '2026-09-10T21:00:00Z', NULL);",
        $"REPLACE INTO {Table} ({Columns}) VALUES (1, 'momentum index at thirty', 'a rule', 'a test', '{MomentumIndexReading.EvaluatorName}', '{{\"level\": 90}}', '000000000000', 'registered', NULL, '2026-09-01T21:00:00Z', NULL);",
        $"REPLACE INTO main.{Table} ({Columns}) VALUES (1, 'momentum index at thirty', 'a rule', 'a test', '{MomentumIndexReading.EvaluatorName}', '{{\"level\": 11}}', '000000000000', 'registered', NULL, '2026-09-01T21:00:00Z', NULL);",
        $"INSERT INTO {Table} ({Columns}) VALUES (1, 'momentum index at thirty', 'a rule', 'a test', '{MomentumIndexReading.EvaluatorName}', '{{\"level\": 50}}', '000000000000', 'registered', NULL, '2026-09-01T21:00:00Z', NULL) ON CONFLICT (id) DO UPDATE SET parameters = excluded.parameters;",
        $"UPDATE OR REPLACE {Table} SET parameters = '{{}}' WHERE id = 1;",
    ];

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

    [Fact]
    public async Task AParameterValueThatIsNotAFiniteNumberIsRefusedAtTheWrite()
    {
        using var store = new TemporaryStore().Migrated();

        var registrar = new CandidateRegistrar(Clock(Opened), store.DatabaseFile);

        // At NaN the evaluator's at-or-below comparison is false on every name-night, and at infinity true on every one.
        foreach (var (value, run) in new[] { (double.NaN, "nan"), (double.PositiveInfinity, "infinity"), (double.NegativeInfinity, "negative-infinity") })
        {
            var refused = await registrar.RegisterAsync(
                "momentum index at nothing",
                "the relative strength index at or below a level",
                "the share of setups that beat their own break-even",
                MomentumIndexReading.EvaluatorName,
                new Dictionary<string, double>(StringComparer.Ordinal) { [MomentumIndexReading.Level] = value },
                "register-test-finite-" + run);

            Assert.Equal(CandidateRegistrar.Refused, refused.Outcome);
            Assert.Contains("not a finite number", refused.Detail, StringComparison.Ordinal);
        }

        Assert.Empty(await RowsAsync(store));
        Assert.Equal(3, Refusals(store).Count);

        // The largest finite values and zero are numbers a comparison can be made against, and are admitted.
        foreach (var value in new[] { double.MaxValue, 0d, -double.MaxValue })
        {
            Assert.Null(CandidateRegistrar.Refusal(
                [],
                "momentum index at a finite level",
                MomentumIndexReading.EvaluatorName,
                new Dictionary<string, double>(StringComparer.Ordinal) { [MomentumIndexReading.Level] = value },
                Opened));
        }
    }

    [Fact]
    public async Task ACandidateRetiredAndRegisteredAgainStandsOnceInTheDivisorTheMaximumAndTheRunPage()
    {
        using var store = new TemporaryStore().Migrated();

        for (var index = 1; index <= 7; index++)
        {
            Assert.Equal(
                CandidateRegistrar.Registered,
                (await RegisterAtAsync(store, FormattableString.Invariant($"candidate {index}"), 30, Opened.AddDays(index - 10))).Outcome);
        }

        await RegisterAtAsync(store, "momentum index at thirty", 30, Opened.AddDays(-3).AddHours(1));
        await RetireAtAsync(store, "momentum index at thirty", Opened.AddDays(-2));

        // The same name registered again after its retirement, as a candidate whose evaluator moved is registered under the code that now runs.
        var again = await RegisterAtAsync(store, "momentum index at thirty", 30, Opened.AddDays(-1));

        Assert.Equal(CandidateRegistrar.Registered, again.Outcome);
        Assert.Contains("family of 8 of 8", again.Detail, StringComparison.Ordinal);

        // Seven others and the name once is eight, the maximum, so a ninth is refused.
        var ninth = await RegisterAtAsync(store, "one more", 30, Opened.AddDays(-1).AddHours(1));

        Assert.Equal(CandidateRegistrar.Refused, ninth.Outcome);
        Assert.Contains("8 candidates already stand registered", ninth.Detail, StringComparison.Ordinal);

        var rows = await RowsAsync(store);

        Assert.Equal(10, rows.Count);
        Assert.Equal(8, CandidateFamily.Divisor(rows, Opened));
        Assert.Equal(7, CandidateFamily.Divisor(rows, Opened.AddDays(-2).AddHours(1)));

        var standing = ShadowColumn.StandingAt(rows, Opened);

        Assert.Equal(8, standing.Count);
        Assert.Equal(10, Assert.Single(standing, row => row.Candidate == "momentum index at thirty").Id);

        // The run page reads the same rule off the same rows.
        var page = new MarkRenderer().ShadowCandidates(
            RunScreen.Shadow(await new ReadApi(store.DatabaseFile, Clock(Opened)).RegisteredCandidatesAsync(), Opened));

        Assert.Contains("data-shadow=\"8\"", page, StringComparison.Ordinal);
        Assert.Contains("data-divisor=\"8\"", page, StringComparison.Ordinal);

        // Over these rows a count of names registered less names retired reads 7, so they tell that rule from this one.
        Assert.Equal(7, rows.Where(row => row.Event == CandidateFamily.Registered).Select(row => row.Candidate)
            .Except(rows.Where(row => row.Event == CandidateFamily.Retired).Select(row => row.Retires!), StringComparer.Ordinal)
            .Count());
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
            Row(7, "retired and registered again before", CandidateFamily.Registered, null, before),
            Row(8, "retired and registered again before", CandidateFamily.Retired, "retired and registered again before", before.AddHours(1)),
            Row(9, "retired and registered again before", CandidateFamily.Registered, null, before.AddHours(2)),
            Row(10, "registered in the second the window opened", CandidateFamily.Registered, null, Opened),
        ];

        // Three: "registered before", "registered before and retired after", which was among the things
        // being tried for the whole of the window, and the name retired and registered again, once.
        Assert.Equal(3, CandidateFamily.Divisor(rows, Opened));

        // A registration in the second the window opened is read as after it, and counts from the next second.
        Assert.Equal(3, CandidateFamily.Divisor(rows, Opened.AddMilliseconds(400)));
        Assert.Equal(4, CandidateFamily.Divisor(rows, Opened.AddSeconds(1)));
        Assert.True(CandidateFamily.StandsAt(rows, "registered in the second the window opened", Opened.AddMilliseconds(400)));

        // The window as it stands later sees the retirement and the late
        // registration, which is what restarting the clock means.
        Assert.Equal(4, CandidateFamily.Divisor(rows, after.AddDays(1)));

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
        Assert.False(CandidateFamily.StandsAt(rows, "retired and registered again before", before.AddMinutes(90)));
        Assert.True(CandidateFamily.StandsAt(rows, "retired and registered again before", Opened));

        // The shadow column reads the divisor's rule, so a night and a window starting at one instant see the same candidates.
        foreach (var at in new[] { before.AddDays(-1), Opened, Opened.AddMilliseconds(400), Opened.AddSeconds(1), after.AddDays(1) })
        {
            Assert.Equal(
                (at, CandidateFamily.Divisor(rows, at)),
                (at, ShadowColumn.StandingAt(rows, at).Select(row => row.Candidate).Distinct(StringComparer.Ordinal).Count()));
        }

        Assert.DoesNotContain(ShadowColumn.StandingAt(rows, Opened.AddMilliseconds(400)), row => row.Candidate == "registered in the second the window opened");
    }

    [Fact]
    public async Task TheDivisorOverTheStoreMatchesTheDivisorOverTheRowsItHolds()
    {
        // Section 18's other row: the register and the correction disagreeing is
        // a failure of the harness rather than a verdict shown anyway. The two
        // are computed from the same rows by two routes, one through the store
        // and one over what was written, and they are asserted equal.
        using var store = new TemporaryStore().Migrated();

        await RegisterAtAsync(store, "momentum index at thirty", 30, Opened.AddDays(-5));
        await RegisterAtAsync(store, "momentum index at twenty", 20, Opened.AddDays(-4));
        await RetireAtAsync(store, "momentum index at thirty", Opened.AddDays(-3));
        await RegisterAtAsync(store, "momentum index at thirty", 30, Opened.AddDays(-2));
        await RegisterAtAsync(store, "momentum index at forty", 40, Opened.AddDays(1));
        await RetireAtAsync(store, "momentum index at twenty", Opened.AddDays(2));

        var rows = await new CandidateRegistrar(Clock(Opened), store.DatabaseFile).RowsAsync();

        Assert.Equal(6, rows.Count);

        DateTimeOffset[] instants =
        [
            Opened.AddDays(-6),
            Opened.AddDays(-4).AddHours(1),
            Opened.AddDays(-3).AddHours(1),
            Opened.AddDays(-2).AddMilliseconds(500),
            Opened,
            Opened.AddDays(3),
        ];

        // Counted straight out of the store by a query rather than through the
        // reader, so a reader that dropped a row would not agree with itself.
        foreach (var at in instants)
        {
            Assert.Equal((at, CandidateFamily.Divisor(rows, at)), (at, StandingAt(store, at)));
        }

        Assert.True(
            instants.Select(at => StandingAt(store, at)).Distinct().Count() >= 3,
            "The divisors over the store take fewer than three values, so a constant would satisfy them.");

        // Over this store a set of names registered less names retired reads one fewer, so the two routes do not share that rule.
        Assert.Equal(2, StandingAt(store, Opened));
        Assert.Equal(1, NamesRegisteredLessNamesRetired(store, Opened));
    }

    // ---- the evaluator pin ----

    [Fact]
    public void EveryEvaluatorsVersionIsThePinOfTheSourcesItsEvaluationRunsThrough()
    {
        var evaluators = CandidateEvaluators.All;

        Assert.True(evaluators.Count >= 2, $"The code carries {evaluators.Count} evaluator(s), expected at least 2.");
        Assert.Equal(8, CandidateEvaluator.EvaluationSources.Count);

        var shared = CandidateEvaluator.EvaluationSources
            .Select(path => File.ReadAllText(Path.Combine(Repository.Root, path)))
            .ToArray();

        Assert.All(shared, source => Assert.True(source.Length > 500, "A source the pin is taken over read as nearly empty."));

        var faults = new List<string>();

        foreach (var evaluator in evaluators)
        {
            var pin = CandidateEvaluator.Pin([SourceOf(evaluator), .. shared]);

            if (pin != evaluator.Version)
            {
                faults.Add(
                    $"{evaluator.Name} carries version {evaluator.Version} and its sources pin to {pin}. " +
                    "A changed evaluation is a new registration retiring the old one, so raise the version " +
                    "to the pin and register the candidate again rather than editing what the register names.");
            }
        }

        Assert.Empty(faults);

        foreach (var evaluator in evaluators)
        {
            string[] sources = [SourceOf(evaluator), .. shared];

            // Every source moves the pin, the evaluator's own and each it runs through.
            for (var at = 0; at < sources.Length; at++)
            {
                var moved = sources.ToArray();
                moved[at] += "\n// a line that changes what this does\n";

                Assert.NotEqual(evaluator.Version, CandidateEvaluator.Pin(moved));
            }

            // And exactly one line is left out of it, the evaluator's own version.
            Assert.Equal(
                1,
                sources.SelectMany(source => source.Split('\n'))
                    .Count(line => line.TrimStart().StartsWith(CandidateEvaluator.VersionDeclaration, StringComparison.Ordinal)));
        }

        // Every shipped file that computes a reading, hands one to an evaluation or runs one is among the sources.
        var onThePath = Repository.SourceFiles()
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "EquityBrief.Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .Where(file => EvaluationCall.IsMatch(SourceStatements.WithoutComments(File.ReadAllText(file))))
            .Select(file => Path.GetRelativePath(Repository.Root, file).Replace(Path.DirectorySeparatorChar, '/'))
            .ToArray();

        Assert.True(onThePath.Length >= 3, $"Found {onThePath.Length} file(s) on the evaluation path, expected at least 3.");
        Assert.All(onThePath, path => Assert.Contains(path, CandidateEvaluator.EvaluationSources));

        // And each source is the file of a type the evaluation calls, so none is a file nothing on the path lives in.
        Type[] called =
        [
            typeof(Money),
            typeof(Statistic),
            typeof(IndicatorSeries),
            typeof(IndicatorEngine),
            typeof(ShortlistBuilder),
            typeof(ShadowColumn),
            typeof(CandidateEvaluators),
            typeof(CandidateEvaluator),
        ];

        Assert.Equal(CandidateEvaluator.EvaluationSources, called.Select(FileOf));

        // And each evaluator's name resolves, both ways, so the catalogue cannot
        // carry a name nothing implements or an evaluator nothing may register.
        Assert.All(evaluators, evaluator => Assert.Same(evaluator, CandidateEvaluators.Find(evaluator.Name)));
        Assert.Null(CandidateEvaluators.Find("an-evaluator-nothing-implements"));
    }

    static readonly Regex EvaluationCall = new(
        @"\bIndicatorSeries\.For\(|\bnew\s+CandidateNight\(|\bShadowColumn\.Evaluate\(|\bCandidateEvaluator\.Read\(|\binsert\s+into\s+indicator\b",
        RegexOptions.IgnoreCase);

    static string FileOf(Type type) =>
        "src/" + type.Assembly.GetName().Name + "/" +
        string.Concat(type.Namespace![type.Assembly.GetName().Name!.Length..].TrimStart('.').Split('.', StringSplitOptions.RemoveEmptyEntries).Select(part => part + "/")) +
        type.Name + ".cs";

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

        var lf = CandidateEvaluator.Pin([source]);

        Assert.Equal(lf, CandidateEvaluator.Pin([source.Replace("\n", "\r\n", StringComparison.Ordinal)]));
        Assert.Equal(lf, CandidateEvaluator.Pin(["﻿" + source]));
        Assert.Equal(lf, CandidateEvaluator.Pin(["﻿" + source.Replace("\n", "\r\n", StringComparison.Ordinal)]));

        // The version line is the one line the pin is taken over the absence of,
        // because a hash of a file including its own hash never settles. Changing
        // it moves nothing; changing anything else moves the pin.
        Assert.Equal(lf, CandidateEvaluator.Pin([source.Replace("000000000000", "ffffffffffff", StringComparison.Ordinal)]));
        Assert.NotEqual(lf, CandidateEvaluator.Pin([source.Replace("Sample", "Other", StringComparison.Ordinal)]));
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

        // A name registered again is asked about by the registration it stands by, and never by one it retired.
        Insert(store, 4, "momentum index at forty", MomentumIndexReading.EvaluatorName, "000000000000", CandidateFamily.Registered, null, Opened.AddHours(1));
        Insert(store, 5, "momentum index at forty", MomentumIndexReading.EvaluatorName, "000000000000", CandidateFamily.Retired, "momentum index at forty", Opened.AddHours(2));
        Insert(store, 6, "momentum index at forty", MomentumIndexReading.EvaluatorName, new MomentumIndexReading().Version, CandidateFamily.Registered, null, Opened.AddHours(3));
        Insert(store, 7, "momentum index at fifty", MomentumIndexReading.EvaluatorName, new MomentumIndexReading().Version, CandidateFamily.Registered, null, Opened.AddHours(1));
        Insert(store, 8, "momentum index at fifty", MomentumIndexReading.EvaluatorName, new MomentumIndexReading().Version, CandidateFamily.Retired, "momentum index at fifty", Opened.AddHours(2));
        Insert(store, 9, "momentum index at fifty", MomentumIndexReading.EvaluatorName, "000000000000", CandidateFamily.Registered, null, Opened.AddHours(3));

        Assert.Contains("momentum index at fifty", Assert.Single(Drifted(await registrar.RowsAsync())), StringComparison.Ordinal);

        // And the register the fixture replay writes, through the registrar at the versions the code carries.
        using var replayed = new TemporaryStore().Migrated();

        await ReplayRegistrationsAsync(replayed);

        var written = await new CandidateRegistrar(Clock(Opened), replayed.DatabaseFile).RowsAsync();

        Assert.True(
            CandidateFamily.Standing(written, DateTimeOffset.MaxValue).Count >= 2,
            "The fixture's register holds fewer than two standing candidates, so an empty answer over it asserts little.");
        Assert.Empty(Drifted(written));
    }

    // Which registered, unretired rows name a version their evaluator no longer
    // carries. Apart from the fact so the proof above runs this reader rather
    // than a copy of it, and so the shadow column at 8.4 asks the same question.
    internal static IReadOnlyList<string> Drifted(IReadOnlyList<RegisterRow> rows)
    {
        var faults = new List<string>();

        foreach (var row in CandidateFamily.Standing(rows, DateTimeOffset.MaxValue))
        {
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
            ["yyyy-MM-ddTHH:mm:ssZ", "yyyy-MM-ddTHH:mm:ss.fffZ"],
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
            await ReplayRegistrationAsync(store, one, "replay-register-");
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

        foreach (var one in expectation.GetProperty("registeredAgain").EnumerateArray())
        {
            await ReplayRegistrationAsync(store, one, "replay-register-again-");
        }
    }

    static async Task ReplayRegistrationAsync(TemporaryStore store, System.Text.Json.JsonElement one, string run)
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
            run + one.GetProperty("candidate").GetString()!.Replace(' ', '-'));

        Assert.Equal(CandidateRegistrar.Registered, outcome.Outcome);
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

        Assert.True(divisors.Length >= 6, $"The expectation works {divisors.Length} divisor(s), expected at least 6.");

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

        // And in each other form SQLite accepts a rewrite or a removal in, a replace being a removal of the row it conflicts with.
        foreach (var statement in new[]
        {
            $"INSERT OR REPLACE INTO {Table} (id) VALUES (1);",
            $"REPLACE INTO {Table} (id) VALUES (1);",
            $"REPLACE INTO main.{Table} (id) VALUES (1);",
            $"INSERT INTO {Table} (id) VALUES (1) ON CONFLICT (id) DO UPDATE SET rule = 'x';",
            $"UPDATE OR REPLACE {Table} SET rule = 'x';",
            $"UPDATE {Table} AS row SET rule = 'x';",
            $"DELETE FROM main.{Table} WHERE id = 1;",
        })
        {
            Assert.Contains(
                SourceStatements.In(statement),
                write => write.Operation is not SourceStatements.Insert && write.Table == Table);
        }

        // An append reads as an insert and nothing else, so the reader does not call every write a removal.
        Assert.All(
            SourceStatements.In($"INSERT OR IGNORE INTO {Table} (id) VALUES (2);").Concat(SourceStatements.In($"INSERT INTO {Table} (id) VALUES (2);")),
            write => Assert.Equal((SourceStatements.Insert, Table), (write.Operation, write.Table)));
    }

    [Fact]
    public async Task ALiveReasonIsNeitherRegisteredNorRetiredThroughTheRegister()
    {
        // 13.3's minimum guardrail has a second clause: a higher minimum before a
        // live condition may be retired. Nothing at runtime retires one, and this
        // is the door that could: a retirement of a live reason's name refused,
        // and a candidate registered under one refused, since a later retirement
        // of that name would then have two meanings.
        using var store = new TemporaryStore().Migrated();

        var registrar = new CandidateRegistrar(Clock(Opened), store.DatabaseFile);

        Assert.Equal(6, ShortlistSeries.Reasons.Length);

        foreach (var reason in ShortlistSeries.Reasons)
        {
            var retired = await registrar.RetireAsync(reason, "a record at 400 resolved setups", $"live-retire-{reason}");

            Assert.Equal(CandidateRegistrar.Refused, retired.Outcome);
            Assert.Contains(FormattableString.Invariant($"once its record holds {ReasonVerdict.MinimumBeforeALiveReasonIsRetired} resolved setups"), retired.Detail, StringComparison.Ordinal);

            var registered = await registrar.RegisterAsync(
                reason.ToUpperInvariant(),
                "a rule",
                "a test",
                MomentumIndexReading.EvaluatorName,
                new Dictionary<string, double>(StringComparer.Ordinal) { [MomentumIndexReading.Level] = 30 },
                $"live-register-{reason}");

            Assert.Equal(CandidateRegistrar.Refused, registered.Outcome);
            Assert.Contains("is a live reason", registered.Detail, StringComparison.Ordinal);
        }

        // Nothing written, and every attempt on the run log as a refusal.
        Assert.Empty(await RowsAsync(store));
        Assert.Equal(12, Refusals(store).Count);

        // A candidate under a name no live reason carries is not caught by it.
        Assert.Null(CandidateRegistrar.LiveReasonRefusal("momentum index at thirty"));
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

    // The registrations no later row before the instant names, counted by a query.
    static int StandingAt(TemporaryStore store, DateTimeOffset at) =>
        Count(store, at, $@"
            SELECT COUNT(*) FROM {Table} AS standing
            WHERE standing.event = '{CandidateFamily.Registered}' AND standing.registered_at < $at
            AND NOT EXISTS (
                SELECT 1 FROM {Table} AS later
                WHERE later.registered_at < $at
                AND (later.registered_at > standing.registered_at
                    OR (later.registered_at = standing.registered_at AND later.id > standing.id))
                AND ((later.event = '{CandidateFamily.Registered}' AND later.candidate = standing.candidate)
                    OR (later.event = '{CandidateFamily.Retired}' AND later.retires = standing.candidate)));");

    static int NamesRegisteredLessNamesRetired(TemporaryStore store, DateTimeOffset at) =>
        Count(store, at, $@"
            SELECT COUNT(*) FROM (
                SELECT candidate FROM {Table}
                WHERE event = '{CandidateFamily.Registered}' AND registered_at < $at
                EXCEPT
                SELECT retires FROM {Table}
                WHERE event = '{CandidateFamily.Retired}' AND registered_at < $at
            );");

    static int Count(TemporaryStore store, DateTimeOffset at, string sql)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.AddWithValue("$at", at.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    static long RunLogRows(TemporaryStore store)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT COUNT(*) FROM run_log;";

        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    static IReadOnlyList<string> IndexesOn(TemporaryStore store)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = $"SELECT name FROM pragma_index_list('{Table}');";

        var names = new List<string>();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    static Task<RegistrationOutcome> RegisterAtAsync(TemporaryStore store, string candidate, double level, DateTimeOffset at) =>
        new CandidateRegistrar(Clock(at), store.DatabaseFile).RegisterAsync(
            candidate,
            "the relative strength index at or below the level",
            "the share of setups that beat their own break-even",
            MomentumIndexReading.EvaluatorName,
            new Dictionary<string, double>(StringComparer.Ordinal) { [MomentumIndexReading.Level] = level },
            "register-at-" + at.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture));

    static async Task RetireAtAsync(TemporaryStore store, string candidate, DateTimeOffset at) =>
        Assert.Equal(
            CandidateRegistrar.Retired,
            (await new CandidateRegistrar(Clock(at), store.DatabaseFile)
                .RetireAsync(candidate, "0 resolved setups of a minimum of 250", "retire-at-" + at.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture))).Outcome);

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
