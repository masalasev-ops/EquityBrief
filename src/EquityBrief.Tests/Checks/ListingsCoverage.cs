using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Shortlist;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Shortlist;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// listings-coverage. A listings row exists for every index member on every night
// the run completed.
//
// Rostered from 5.4 rather than from 5.1, because 5.4 is the checkpoint that
// creates `listing` and a roster row naming a checkpoint the record shows as
// landed fails `coverage-reported`. It was one of four rows that named a
// checkpoint which does not create the store its own reason reads.
//
// The property is section 17's row coverage limit and it is what section 13's
// shadow mechanism rests on: a shadow candidate has to be evaluated on the
// nights it would have fired, and most of those are nights no live reason
// surfaced that name. Writing rows only for listed names would make that
// impossible without anything announcing it.
// see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
//
// It reads a store the suite built rather than the live one, because a check
// that reads the live store is a check whose result depends on last night.
public class ListingsCoverage
{
    internal static CheckReach Reach => new(
        "listings-coverage",
        ["fixtures/membership-2026-09-05"],
        [
            CheckReach.Key(Scope.LimitsTable, "Nightly row coverage"),
        ]);

    [Fact]
    public async Task EveryIndexMemberHasARowOnEveryNightTheRunCompleted()
    {
        using var store = await FixtureExpectations.WithListings();

        var members = FixtureExpectation.CurrentMembers;
        var nights = Query(store, "SELECT DISTINCT session_date FROM listing ORDER BY session_date;");

        // The scope, stated in advance and floored on the thing carrying the
        // property. The nights are context; the rows per night are the claim.
        Assert.True(nights.Count >= 1, $"Read {nights.Count} night(s) of listings, expected at least 1.");
        Assert.True(members.Length >= 4, $"Read {members.Length} index member(s), expected at least 4.");

        foreach (var night in nights)
        {
            Assert.Equal(
                [.. members],
                Query(store, $"SELECT ticker FROM listing WHERE session_date = '{night}' ORDER BY ticker;"));
        }
    }

    [Fact]
    public async Task ANameThatFiredNothingStillHasARowAndSaysSo()
    {
        // The half a coverage count over the fired names would also satisfy. A
        // row with a fired count of zero is what the shadow column is written
        // against. SCHEMA's note said zero for most rows until the 5.4
        // correction, which the operator's store contradicted on every
        // whole-index night, and it states the mechanism now rather than a share.
        using var store = await FixtureExpectations.WithListings();

        var quiet = int.Parse(Query(store, "SELECT COUNT(*) FROM listing WHERE fired_count = 0;").Single(), CultureInfo.InvariantCulture);
        var loud = int.Parse(Query(store, "SELECT COUNT(*) FROM listing WHERE fired_count > 0;").Single(), CultureInfo.InvariantCulture);
        var all = int.Parse(Query(store, "SELECT COUNT(*) FROM listing;").Single(), CultureInfo.InvariantCulture);

        Assert.True(all > 0, "no listings were written at all, so the coverage above compared nothing.");

        // The two halves account for every row exactly once, which is what the
        // every-name grain means and what a count over the fired names alone
        // would not show. It is stated as a partition rather than as a
        // requirement that some row be quiet: over four names of real bars every
        // one of them fires something, and asserting otherwise would be an
        // assertion over a shape this fixture cannot take. The quiet row is
        // constructed below instead.
        Assert.Equal(all, quiet + loud);

        // Every row carries all six reasons whether or not any fired, so a
        // reason that never fires is still a reason a later session can score.
        Assert.All(
            Query(store, "SELECT reasons FROM listing;"),
            reasons => Assert.All(
                ShortlistSeries.Reasons,
                name => Assert.Contains(name, reasons, StringComparison.Ordinal)));

        // And the fired count on the row is the number of reasons that fired,
        // rather than a figure the builder stated beside them.
        foreach (var row in Query(store, "SELECT fired_count, reasons FROM listing;"))
        {
            var parts = row.Split('|', 2);
            var counted = System.Text.Json.JsonDocument.Parse(parts[1]).RootElement
                .EnumerateArray()
                .Count(reason => reason.GetProperty("fired").GetBoolean());

            Assert.Equal(int.Parse(parts[0], CultureInfo.InvariantCulture), counted);
        }

    }

    [Fact]
    public async Task AMemberTheNightComputedNothingForStillGetsARow()
    {
        // The case the committed fixture cannot reach on its own: every current
        // member of it has bars. A member with none is what the every-name grain
        // is for, and a builder that skipped it would leave a count that is
        // wrong in the direction nobody looks.
        using var store = await FixtureExpectations.WithListings();

        Insert(
            store,
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at, sector) " +
            "VALUES ('GSPC', 'ZZZZ', '2026-01-02', NULL, '2026-09-08T00:00:00Z', 'Utilities');");

        // A print inside the horizon, which a member with no bar is not counted to.
        Insert(
            store,
            "INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) " +
            "VALUES ('ZZZZ', '2026-09-15', 'earnings', 'after', '{}', '2026-09-08T00:00:00Z');");

        await new ShortlistBuilder(
            FixedClock.At(new DateTimeOffset(2026, 9, 8, 21, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync("GSPC", "coverage-check", new DateTimeOffset(2026, 9, 8, 21, 0, 0, TimeSpan.Zero));

        var rows = Query(store, "SELECT COUNT(*) FROM listing WHERE ticker = 'ZZZZ';").Single();

        Assert.Equal("1", rows);

        // Its reasons are all present and none fired, which is what a name with
        // nothing computed for it should say rather than being absent.
        var reasons = Query(store, "SELECT reasons FROM listing WHERE ticker = 'ZZZZ';").Single();

        Assert.All(ShortlistSeries.Reasons, name => Assert.Contains(name, reasons, StringComparison.Ordinal));
        Assert.Equal("0", Query(store, "SELECT fired_count FROM listing WHERE ticker = 'ZZZZ';").Single());

        // And its plan says why it is empty rather than being a blank column.
        Assert.Contains(
            "no ladder row",
            Query(store, "SELECT plan_at_listing FROM listing WHERE ticker = 'ZZZZ';").Single(),
            StringComparison.Ordinal);

        // Its earnings soon states the date the calendar holds and that no count was made.
        var soon = EarningsSoonValues(reasons);

        Assert.Equal("2026-09-15", soon[ShortlistSeries.NextDatedEventValue]);
        Assert.Equal($"{ShortlistSeries.NotCounted}: no bar is stored for the name", soon["sessions to the next dated event"]);
    }

    static IReadOnlyDictionary<string, string> EarningsSoonValues(string reasons) =>
        System.Text.Json.JsonDocument.Parse(reasons).RootElement.EnumerateArray()
            .Single(reason => reason.GetProperty("name").GetString() == ShortlistSeries.EarningsSoon)
            .GetProperty("values").EnumerateObject()
            .ToDictionary(value => value.Name, value => value.Value.GetString()!, StringComparer.Ordinal);

    [Fact]
    public async Task AMemberTheDaysFileCarriedNothingForIsListedTonightWithNothingFired()
    {
        // The other case the committed fixture cannot reach: a member whose bars
        // stop before tonight, being a name the day's file carried nothing for.
        // Two of the operator's 503 are such names every night. Until the phase 5
        // sign-off the row was dated by the name's own last session, so the
        // member had no row for tonight and one row months back that went on
        // firing on old prices, and this check, whose fixture holds no such
        // member, could not see it.
        using var store = await FixtureExpectations.WithListings();

        var name = FixtureExpectation.CurrentMembers.Order(StringComparer.Ordinal).First();
        var tonight = Query(store, "SELECT MAX(session_date) FROM bar;").Single();
        var before = Query(store, $"SELECT MAX(session_date) FROM bar WHERE ticker = '{name}' AND session_date < '{tonight}';").Single();

        // The name's bar for the night goes, and so does any facts file newer
        // than the bar it is left with, since a name the file carried nothing
        // for has no facts for that night either.
        Insert(store, $"DELETE FROM bar WHERE ticker = '{name}' AND session_date = '{tonight}';");
        Insert(store, $"DELETE FROM facts WHERE ticker = '{name}' AND session_date > '{before}';");
        Insert(store, "DELETE FROM listing;");

        await new ShortlistBuilder(
            FixedClock.At(new DateTimeOffset(2026, 9, 8, 21, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync("GSPC", "coverage-stale", new DateTimeOffset(2026, 9, 8, 21, 0, 0, TimeSpan.Zero));

        // Every member has the night's row, the stale one included, and no row
        // is dated by the stale member's last session.
        Assert.Equal(
            [.. FixtureExpectation.CurrentMembers.Order(StringComparer.Ordinal)],
            Query(store, $"SELECT ticker FROM listing WHERE session_date = '{tonight}' ORDER BY ticker;"));
        Assert.Equal(["0"], Query(store, $"SELECT COUNT(*) FROM listing WHERE session_date = '{before}';"));

        // Nothing fired on prices from a session it did not trade tonight, and
        // the plan says which session its last bar is.
        Assert.Equal(["0"], Query(store, $"SELECT fired_count FROM listing WHERE ticker = '{name}';"));
        Assert.Contains(
            $"no bar for this session; the last session stored for the name is {before}",
            Query(store, $"SELECT plan_at_listing FROM listing WHERE ticker = '{name}';").Single(),
            StringComparison.Ordinal);

        // Its earnings soon states the date the calendar holds on or after the night, as the
        // listings expectation walks it, and that no count was made, with the plan's reason.
        var soon = EarningsSoonValues(Query(store, $"SELECT reasons FROM listing WHERE ticker = '{name}';").Single());
        var walked = Expected("listings").GetProperty("earningsSoon");

        Assert.Equal(tonight, walked.GetProperty("night").GetString());
        Assert.Equal(walked.GetProperty(name).GetProperty("nextDatedEvent").GetString(), soon[ShortlistSeries.NextDatedEventValue]);
        Assert.NotEqual(ShortlistSeries.NotOnFile, soon[ShortlistSeries.NextDatedEventValue]);
        Assert.Equal(
            $"{ShortlistSeries.NotCounted}: no bar for this session; the last session stored for the name is {before}",
            soon["sessions to the next dated event"]);
    }

    // ---- 8.4, the shadow column ----

    static readonly DateTimeOffset NightStart = new(2026, 9, 8, 21, 0, 0, TimeSpan.Zero);

    // A candidate registered before the night, through the registrar rather than
    // by an insert, so what the night reads is what a registration actually
    // writes rather than a row a test made up.
    static async Task RegisterAsync(TemporaryStore store, string candidate, double level, DateTimeOffset at)
    {
        var outcome = await new CandidateRegistrar(
            FixedClock.At(at, SessionZones.UnitedStates),
            store.DatabaseFile).RegisterAsync(
                candidate,
                "the relative strength index at or below the level",
                "the share of its setups that beat their own break-even",
                MomentumIndexReading.EvaluatorName,
                new Dictionary<string, double>(StringComparer.Ordinal) { [MomentumIndexReading.Level] = level },
                "coverage-register-" + candidate.Replace(' ', '-'));

        Assert.Equal(CandidateRegistrar.Registered, outcome.Outcome);
    }

    // A member that trades on the same two sessions the fixture's newest are and
    // fires nothing: bars so it is neither stale nor gapped, the momentum
    // readings so a candidate can be evaluated over it, and no level, ladder,
    // calendar row or volume average, so every live reason reads nothing.
    static void QuietMemberWithBars(TemporaryStore store)
    {
        var sessions = Query(store, "SELECT DISTINCT session_date FROM bar ORDER BY session_date DESC LIMIT 2;");

        Assert.Equal(2, sessions.Count);

        Insert(
            store,
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at, sector) " +
            "VALUES ('GSPC', 'QUIET', '2026-01-02', NULL, '2026-09-08T00:00:00Z', 'Utilities');");

        foreach (var (session, close) in sessions.Select((session, at) => (session, at == 0 ? "100.0000" : "100.5000")))
        {
            Insert(
                store,
                "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES " +
                $"('QUIET', '{session}', '100.0000', '101.0000', '99.0000', '{close}', 1000, 'test', '2026-09-08T21:00:00Z', '{close}');");

            // The two readings the registered candidate names, and nothing else.
            // A volume average is deliberately absent, because it is what the
            // unusual volume reason needs and this name is meant to fire nothing.
            foreach (var (name, value) in new[] { (MomentumIndexReading.Reading, 42.0), (MomentumHistogramTurn.Histogram, 0.5) })
            {
                Insert(
                    store,
                    "INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES " +
                    $"('QUIET', '{session}', '{name}', {value.ToString(CultureInfo.InvariantCulture)}, 2);");
            }
        }
    }

    // The stage runs under a clock later than the night's start, as it does on a night, and is handed that start.
    static async Task<IReadOnlyList<string>> RunTheNightAsync(TemporaryStore store, string runId, DateTimeOffset? stageAt = null)
    {
        Insert(store, "DELETE FROM listing;");

        await new ShortlistBuilder(
            FixedClock.At(stageAt ?? NightStart.AddMinutes(15), SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync("GSPC", runId, NightStart);

        return Query(store, "SELECT ticker FROM listing ORDER BY ticker;");
    }

    // The two lists a shadow column holds, read apart.
    static (string[] Candidates, (string Candidate, string Reason)[] Skipped) Lists(string column)
    {
        var root = System.Text.Json.JsonDocument.Parse(column).RootElement;

        return (
            [.. root.GetProperty("candidates").EnumerateArray().Select(one => one.GetProperty("candidate").GetString()!)],
            [.. root.GetProperty("skipped").EnumerateArray().Select(one => (one.GetProperty("candidate").GetString()!, one.GetProperty("reason").GetString()!))]);
    }

    // The night's start on the first read and a quarter of an hour on after it, so every stage runs later than the night began.
    sealed class StartedEarlierClock(DateTimeOffset start) : IClock
    {
        int reads;

        public DateTimeOffset UtcNow => Interlocked.Increment(ref reads) == 1 ? start : start.AddMinutes(15);

        public TimeZoneInfo SessionZone { get; } = SessionZones.ResolveSessionZone(SessionZones.UnitedStates);
    }

    [Fact]
    public async Task AShadowCandidateIsEvaluatedOnTheNightsNoLiveReasonFired()
    {
        // The done condition 8.4 rests on, and the reason this check owns it: a
        // shadow candidate has to be evaluated on the nights it would have
        // fired, and most of those are nights nothing surfaced the name. A
        // builder that evaluated candidates only where a live reason fired would
        // leave the candidate's record over the population the live reasons
        // already chose, which is the comparison being made.
        using var store = await FixtureExpectations.WithListings();

        // A level high enough that the reading fires wherever it is computed, so
        // what is being read is which name-nights were reached rather than which
        // happened to clear a threshold.
        await RegisterAsync(store, "momentum index at one hundred", 100, NightStart.AddDays(-1));

        // A member that trades tonight and fires nothing. The committed fixture
        // cannot supply one: over four names of real bars every one of them
        // fires something, which the neighbouring test states as the shape this
        // fixture takes. So it is constructed, and it is constructed as a name
        // with bars rather than as a name without, because a name with no bars
        // is evaluated over nothing by the live reasons and by the shadow column
        // alike and would prove the opposite of what this is about.
        QuietMemberWithBars(store);

        var listed = await RunTheNightAsync(store, "coverage-shadow");

        Assert.True(listed.Count >= 5, $"Read {listed.Count} listing row(s), expected at least 5.");

        var quiet = Query(store, "SELECT ticker FROM listing WHERE fired_count = 0 ORDER BY ticker;");

        // The population the property is about. A run where every name fired
        // something would assert nothing here, so it is stated rather than
        // assumed.
        Assert.Equal(["QUIET"], quiet);

        foreach (var ticker in listed)
        {
            var shadow = Query(store, $"SELECT shadow_reasons FROM listing WHERE ticker = '{ticker}';").Single();

            Assert.Contains("momentum index at one hundred", shadow, StringComparison.Ordinal);
            Assert.Contains("\"skipped\":[]", shadow.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        }

        // And the evaluation carries the values it was made on, whichever way it
        // went, for the reason a listings row carries a live reason's values: a
        // night where nothing fired is the night that says how close it came.
        var one = Query(store, $"SELECT shadow_reasons FROM listing WHERE ticker = '{quiet[0]}';").Single();

        Assert.Contains(MomentumIndexReading.Reading, one, StringComparison.Ordinal);
        Assert.Contains("\"fired\":true", one.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACandidateRegisteredAfterTheNightStartedIsNotEvaluatedByIt()
    {
        // The instant is the night's start rather than now, and that is the whole
        // rule. A candidate registered while the night was running would be
        // scored on a name-night it was not registered before, and the register
        // could not afterwards say which names were reached before it landed.
        using var store = await FixtureExpectations.WithListings();

        await RegisterAsync(store, "registered before the night", 100, NightStart.AddSeconds(-1));
        await RegisterAsync(store, "registered after the night started", 100, NightStart.AddSeconds(1));

        // The stage's clock is past both, so only the instant it is handed can exclude the second.
        var listed = await RunTheNightAsync(store, "coverage-shadow-after", stageAt: NightStart.AddMinutes(15));

        foreach (var ticker in listed)
        {
            var shadow = Query(store, $"SELECT shadow_reasons FROM listing WHERE ticker = '{ticker}';").Single();

            Assert.Equal(["registered before the night"], Lists(shadow).Candidates);
            Assert.DoesNotContain("registered after the night started", shadow, StringComparison.Ordinal);
        }

        // Both stand registered, so what excluded the second is its instant and
        // not its standing. Without this the test would pass over a register the
        // night could not read at all.
        var rows = await new CandidateRegistrar(
            FixedClock.At(NightStart, SessionZones.UnitedStates),
            store.DatabaseFile).RowsAsync();

        Assert.Equal(2, rows.Count);
        Assert.True(CandidateFamily.StandsAt(rows, "registered after the night started", NightStart.AddDays(1)));
        Assert.Equal(["registered before the night"], [.. ShadowColumn.StandingAt(rows, NightStart).Select(row => row.Candidate)]);
    }

    [Fact]
    public async Task ACandidateWhoseEvaluatorHasMovedOnIsNotEvaluatedAndIsNamedAsAFailureOnTheRunLog()
    {
        // A note nobody reads is not a record of having skipped a name-night. The
        // night does not stop, because every listing row was written and every
        // live reason was scored; what it could not do is named where the run
        // page draws failures, and the stage's outcome is what puts it there.
        using var store = await FixtureExpectations.WithListings();

        await RegisterAsync(store, "momentum index at one hundred", 100, NightStart.AddDays(-1));

        // The row's version moved to one the code does not carry. Written
        // straight into the table, because the registrar writes the version the
        // code holds and the state being asserted is the one that arrives when
        // the code moves afterwards. The register refuses an update, so the
        // standing row is retired and a new one written in its place, which is
        // what a change to a registered candidate is.
        Insert(
            store,
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, " +
            "evaluator_version, event, retires, registered_at, evidence) VALUES " +
            "(90, 'a candidate whose evaluator moved', 'a rule', 'a test', '" + MomentumIndexReading.EvaluatorName +
            "', '{\"level\": 100}', '000000000000', 'registered', NULL, '2026-09-07T21:00:00Z', NULL);");

        var listed = await RunTheNightAsync(store, "coverage-shadow-drift");

        foreach (var ticker in listed)
        {
            var shadow = Query(store, $"SELECT shadow_reasons FROM listing WHERE ticker = '{ticker}';").Single();

            // The one that stands is evaluated and the drifted one is skipped
            // with its reason, on every row, so a later reader of any name-night
            // can see the hole rather than reading an absence as a non-fire.
            var (candidates, skipped) = Lists(shadow);

            Assert.Equal(["momentum index at one hundred"], candidates);
            Assert.Equal("a candidate whose evaluator moved", Assert.Single(skipped).Candidate);
            Assert.Contains("000000000000", skipped[0].Reason, StringComparison.Ordinal);
            Assert.Contains("the register does not name", skipped[0].Reason, StringComparison.Ordinal);
        }

        // The stage's own row: a failure rather than a note, with the candidate
        // named, and its own outcome rather than the night's failure word so a
        // reader can tell a night that stopped from a night that skipped.
        var row = Query(store, "SELECT outcome || ' :: ' || detail FROM run_log WHERE stage = 'listings' AND run_id = 'coverage-shadow-drift';").Single();

        Assert.StartsWith(ShortlistBuilder.Failed + " :: ", row, StringComparison.Ordinal);
        Assert.Contains("FAILURE: 1 registered candidate(s) skipped on every name", row, StringComparison.Ordinal);
        Assert.Contains("a candidate whose evaluator moved", row, StringComparison.Ordinal);

        // And the same night once the drifted candidate is withdrawn says ok, so
        // the outcome is a property of the skip rather than of the shadow column
        // existing. Withdrawn through a retirement, because the register refuses
        // a delete: the way to stop evaluating a candidate is to retire it, and
        // that is the path a person would actually take.
        var withdrawn = await new CandidateRegistrar(
            FixedClock.At(NightStart.AddMinutes(-1), SessionZones.UnitedStates),
            store.DatabaseFile).RetireAsync(
                "a candidate whose evaluator moved",
                "its evaluator moved and it was registered under a version the code no longer carries",
                "coverage-retire-drifted");

        Assert.Equal(CandidateRegistrar.Retired, withdrawn.Outcome);

        Insert(store, "DELETE FROM listing;");

        await new ShortlistBuilder(
            FixedClock.At(NightStart, SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync("GSPC", "coverage-shadow-clean", NightStart);

        var clean = Query(store, "SELECT outcome FROM run_log WHERE stage = 'listings' AND run_id = 'coverage-shadow-clean';").Single();

        Assert.Equal(ShortlistBuilder.Ok, clean);

        // The retirement took the drifted candidate out and left the standing one
        // in, so the night went green by withdrawing a candidate rather than by
        // the shadow column stopping.
        var after = Query(store, $"SELECT shadow_reasons FROM listing WHERE ticker = '{listed[0]}';").Single();

        Assert.Contains("momentum index at one hundred", after, StringComparison.Ordinal);
        Assert.DoesNotContain("a candidate whose evaluator moved", after, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACandidateRegisteredWhileTheNightRanIsNotEvaluatedByItsListingsStage()
    {
        // The night's start is taken before its first step, and its listings stage starts later.
        using var store = new TemporaryStore().Migrated();

        var night = new DateTimeOffset(2026, 9, 8, 21, 10, 0, TimeSpan.Zero);

        await RegisterAsync(store, "registered before the night", 100, night.AddHours(-1));
        await RegisterAsync(store, "registered while the night ran", 100, night.AddMinutes(5));

        var code = await Worker.Nightly.RunAsync(
            new Core.Configuration.StoreLocation(Path.GetDirectoryName(store.DatabaseFile)!),
            Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05"),
            "GSPC",
            new StartedEarlierClock(night),
            new StringWriter(),
            new StringWriter(),
            "coverage-night-started");

        Assert.Equal(0, code);

        // The stage started after the second registration, so a stage reading its own start would evaluate it.
        var started = Query(store, "SELECT started_at FROM run_log WHERE run_id = 'coverage-night-started' AND stage = 'listings';").Single();
        var stageStarted = DateTimeOffset.Parse(started, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        Assert.True(stageStarted > night.AddMinutes(5), $"The listings stage started at {stageStarted.ToString("O", CultureInfo.InvariantCulture)}, which is not after the registration it is meant to exclude.");

        var rows = Query(store, "SELECT shadow_reasons FROM listing;");

        Assert.Equal(FixtureExpectation.CurrentMembers.Length, rows.Count);
        Assert.All(rows, row => Assert.Equal(["registered before the night"], Lists(row).Candidates));
        Assert.All(rows, row => Assert.DoesNotContain("registered while the night ran", row, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AStaleOrGappedNameIsSkippedOnItsOwnRowAndCountedWithoutFailingTheStage()
    {
        using var store = await FixtureExpectations.WithListings();

        await RegisterAsync(store, "momentum index at one hundred", 100, NightStart.AddDays(-1));

        var sessions = Query(store, "SELECT DISTINCT session_date FROM bar ORDER BY session_date DESC LIMIT 4;");

        Assert.Equal(4, sessions.Count);

        // Readings of 20 fire at a level of 100, so a stale or gapped name that was read shows up as a fire.
        ConstructedMember(store, "STALE", [sessions[2], sessions[1]], new() { [sessions[1]] = (20, 0.3), [sessions[2]] = (22, -0.4) });
        ConstructedMember(store, "GAPPED", [sessions[3], sessions[1], sessions[0]], new() { [sessions[0]] = (20, 0.3), [sessions[1]] = (22, -0.4) });

        var listed = await RunTheNightAsync(store, "coverage-stale-gapped");

        Assert.Equal([.. FixtureExpectation.CurrentMembers.Append("GAPPED").Append("STALE").Order(StringComparer.Ordinal)], listed);

        var stale = Lists(Query(store, "SELECT shadow_reasons FROM listing WHERE ticker = 'STALE';").Single());
        var gapped = Lists(Query(store, "SELECT shadow_reasons FROM listing WHERE ticker = 'GAPPED';").Single());

        Assert.Empty(stale.Candidates);
        Assert.Equal([("momentum index at one hundred", $"no bar for this session; the last session stored for the name is {sessions[1]}")], stale.Skipped);
        Assert.Empty(gapped.Candidates);
        Assert.Equal([("momentum index at one hundred", $"the stored series has a gap at {sessions[2]}, so nothing is computed across it")], gapped.Skipped);

        foreach (var ticker in FixtureExpectation.CurrentMembers)
        {
            var other = Lists(Query(store, $"SELECT shadow_reasons FROM listing WHERE ticker = '{ticker}';").Single());

            Assert.Equal(["momentum index at one hundred"], other.Candidates);
            Assert.Empty(other.Skipped);
        }

        var row = Query(store, "SELECT outcome || ' :: ' || detail FROM run_log WHERE stage = 'listings' AND run_id = 'coverage-stale-gapped';").Single();

        Assert.StartsWith(ShortlistBuilder.Ok + " :: ", row, StringComparison.Ordinal);
        Assert.Contains("1 candidate(s) registered, 4 shadow evaluation(s) written, 2 skipped on a name-night without the readings: 1 stale, 1 gapped, 0 with a reading not available", row, StringComparison.Ordinal);
        Assert.DoesNotContain("FAILURE", row, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryReadingAnEvaluatorReadsIsOneTheNightHandsOver()
    {
        var handed = IndicatorSeries.Names
            .Concat(IndicatorSeries.Names.Select(name => name + ShortlistBuilder.PreviousSuffix))
            .ToHashSet(StringComparer.Ordinal);

        Assert.True(CandidateEvaluators.All.Count >= 2, $"Read {CandidateEvaluators.All.Count} evaluator(s), expected at least 2.");
        Assert.All(CandidateEvaluators.All, evaluator => Assert.All(evaluator.Reads, key => Assert.Contains(key, handed)));
        Assert.Contains(CandidateEvaluators.All, evaluator => evaluator.Reads.Any(key => key.EndsWith(ShortlistBuilder.PreviousSuffix, StringComparison.Ordinal)));
    }

    // A member with bars on the given sessions, and a momentum reading and histogram on each session given readings.
    static void ConstructedMember(TemporaryStore store, string ticker, IReadOnlyList<string> bars, Dictionary<string, (double? Reading, double? Histogram)> readings)
    {
        Insert(
            store,
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at, sector) " +
            $"VALUES ('GSPC', '{ticker}', '2026-01-02', NULL, '2026-09-08T00:00:00Z', 'Utilities');");

        foreach (var session in bars)
        {
            Insert(
                store,
                "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES " +
                $"('{ticker}', '{session}', '100.0000', '101.0000', '99.0000', '100.0000', 1000, 'test', '2026-09-08T21:00:00Z', '100.0000');");
        }

        foreach (var (session, (reading, histogram)) in readings)
        {
            Insert(
                store,
                "INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES " +
                $"('{ticker}', '{session}', '{MomentumIndexReading.Reading}', {Sql(reading)}, 2), " +
                $"('{ticker}', '{session}', '{MomentumHistogramTurn.Histogram}', {Sql(histogram)}, 2);");
        }

        static string Sql(double? value) => value is { } read ? read.ToString(CultureInfo.InvariantCulture) : "NULL";
    }

    static double? Reading(System.Text.Json.JsonElement session, string name) =>
        session.GetProperty(name).ValueKind == System.Text.Json.JsonValueKind.Null ? null : session.GetProperty(name).GetDouble();

    [Fact]
    public async Task EveryShadowRowOverTheReplayedFixtureMatchesTheOneWorkedByHand()
    {
        var expected = Expected("shadow-column").GetProperty("overTheReplayedNight");
        var nightStarted = DateTimeOffset.Parse(expected.GetProperty("nightStartedAt").GetString()!, CultureInfo.InvariantCulture);
        var stageStarted = DateTimeOffset.Parse(expected.GetProperty("stageStartedAt").GetString()!, CultureInfo.InvariantCulture);
        var session = expected.GetProperty("session").GetString()!;

        using var store = await FixtureExpectations.WithListings();

        Assert.Equal(session, Query(store, "SELECT MAX(session_date) FROM bar;").Single());

        foreach (var one in expected.GetProperty("registrations").EnumerateArray())
        {
            var outcome = await new CandidateRegistrar(
                FixedClock.At(DateTimeOffset.Parse(one.GetProperty("registeredAt").GetString()!, CultureInfo.InvariantCulture), SessionZones.UnitedStates),
                store.DatabaseFile).RegisterAsync(
                    one.GetProperty("candidate").GetString()!,
                    "a rule",
                    "a test",
                    one.GetProperty("evaluator").GetString()!,
                    one.GetProperty("parameters").EnumerateObject().ToDictionary(pair => pair.Name, pair => pair.Value.GetDouble(), StringComparer.Ordinal),
                    "shadow-expectation-" + one.GetProperty("candidate").GetString()!.Replace(' ', '-'));

            Assert.Equal(CandidateRegistrar.Registered, outcome.Outcome);
        }

        foreach (var member in expected.GetProperty("members").EnumerateArray())
        {
            ConstructedMember(
                store,
                member.GetProperty("ticker").GetString()!,
                [.. member.GetProperty("bars").EnumerateArray().Select(bar => bar.GetString()!)],
                member.GetProperty("readings").EnumerateObject().ToDictionary(
                    on => on.Name,
                    on => (Reading(on.Value, MomentumIndexReading.Reading), Reading(on.Value, MomentumHistogramTurn.Histogram))));
        }

        var rows = expected.GetProperty("rows");

        async Task<string> NightAsync(string runId)
        {
            Insert(store, "DELETE FROM listing;");

            await new ShortlistBuilder(FixedClock.At(stageStarted, SessionZones.UnitedStates), store.DatabaseFile).RunAsync("GSPC", runId, nightStarted);

            Assert.Equal(
                expected.GetProperty("rowsWritten").GetInt32(),
                int.Parse(Query(store, $"SELECT COUNT(*) FROM listing WHERE session_date = '{session}';").Single(), CultureInfo.InvariantCulture));

            return Query(store, $"SELECT outcome || ' :: ' || detail FROM run_log WHERE stage = 'listings' AND run_id = '{runId}';").Single();
        }

        var line = await NightAsync("shadow-expectation");

        Assert.StartsWith((expected.GetProperty("fails").GetBoolean() ? ShortlistBuilder.Failed : ShortlistBuilder.Ok) + " :: ", line, StringComparison.Ordinal);
        Assert.EndsWith("; " + expected.GetProperty("line").GetString(), line, StringComparison.Ordinal);

        var evaluations = 0;
        var skips = 0;

        foreach (var ticker in Query(store, "SELECT ticker FROM listing ORDER BY ticker;"))
        {
            var column = System.Text.Json.JsonDocument.Parse(Query(store, $"SELECT shadow_reasons FROM listing WHERE ticker = '{ticker}';").Single()).RootElement;

            evaluations += column.GetProperty("candidates").GetArrayLength();
            skips += column.GetProperty("skipped").GetArrayLength();

            Assert.DoesNotContain("registered while the night ran", column.GetRawText(), StringComparison.Ordinal);

            if (rows.TryGetProperty(ticker, out var worked))
            {
                Assert.Equal((ticker, Canonical(worked.GetProperty("candidates"))), (ticker, Canonical(column.GetProperty("candidates"))));
                Assert.Equal((ticker, Canonical(worked.GetProperty("skipped"))), (ticker, Canonical(column.GetProperty("skipped"))));

                continue;
            }

            // A fixture name: both evaluated and neither skipped, the reading fires, and the turn is recomputed from its own two newest histograms.
            Assert.Contains(ticker, FixtureExpectation.CurrentMembers);
            Assert.Equal(0, column.GetProperty("skipped").GetArrayLength());

            var fired = column.GetProperty("candidates").EnumerateArray()
                .ToDictionary(one => one.GetProperty("candidate").GetString()!, one => one.GetProperty("fired").GetBoolean(), StringComparer.Ordinal);

            var latest = double.Parse(
                Query(store, $"SELECT value FROM indicator WHERE ticker = '{ticker}' AND name = '{MomentumHistogramTurn.Histogram}' AND session_date = '{session}';").Single(),
                CultureInfo.InvariantCulture);
            var before = double.Parse(
                Query(store, $"SELECT value FROM indicator WHERE ticker = '{ticker}' AND name = '{MomentumHistogramTurn.Histogram}' AND session_date = (SELECT MAX(session_date) FROM bar WHERE ticker = '{ticker}' AND session_date < '{session}');").Single(),
                CultureInfo.InvariantCulture);

            Assert.Equal(2, fired.Count);
            Assert.Equal((ticker, true), (ticker, fired["momentum index at one hundred"]));
            Assert.Equal((ticker, before <= 0 && latest >= 0), (ticker, fired["momentum histogram turning up"]));
        }

        Assert.Equal(expected.GetProperty("evaluations").GetInt32(), evaluations);
        Assert.Equal(expected.GetProperty("skips").EnumerateObject().Sum(cause => cause.Value.GetInt32()), skips);

        // The same night with a candidate whose evaluator moved: the one skip that fails the stage, on every row, named once.
        var moved = expected.GetProperty("withAMovedEvaluator");
        var name = moved.GetProperty("candidate").GetString()!;
        var parameters = moved.GetProperty("parameters").EnumerateObject().ToDictionary(pair => pair.Name, pair => pair.Value.GetDouble(), StringComparer.Ordinal);

        Insert(
            store,
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) VALUES " +
            $"(90, '{name}', 'a rule', 'a test', '{moved.GetProperty("evaluator").GetString()}', '{CandidateEvaluator.Write(parameters)}', " +
            $"'{moved.GetProperty("evaluatorVersion").GetString()}', 'registered', NULL, '{moved.GetProperty("registeredAt").GetString()}', NULL);");

        var failed = await NightAsync("shadow-expectation-moved");

        Assert.StartsWith((moved.GetProperty("fails").GetBoolean() ? ShortlistBuilder.Failed : ShortlistBuilder.Ok) + " :: ", failed, StringComparison.Ordinal);
        Assert.Contains("; " + moved.GetProperty("line").GetString(), failed, StringComparison.Ordinal);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(failed, "'" + name + "'"));

        var reason = moved.GetProperty("reason").GetString()!.Replace("{version}", CandidateEvaluators.Find(moved.GetProperty("evaluator").GetString()!)!.Version, StringComparison.Ordinal);

        Assert.Equal(
            moved.GetProperty("rowsSkipped").GetInt32(),
            Query(store, "SELECT shadow_reasons FROM listing;").Count(column => Lists(column).Skipped.Contains((name, reason)) && !Lists(column).Candidates.Contains(name)));
    }

    // A list of objects keyed in a fixed order and sorted, so two lists holding the same entries compare equal.
    static string Canonical(System.Text.Json.JsonElement list) =>
        string.Join(
            "\n",
            list.EnumerateArray()
                .Select(one => string.Join(
                    ";",
                    one.EnumerateObject()
                        .OrderBy(pair => pair.Name, StringComparer.Ordinal)
                        .Select(pair => pair.Name + "=" + (pair.Value.ValueKind == System.Text.Json.JsonValueKind.Object
                            ? string.Join(",", pair.Value.EnumerateObject().OrderBy(inner => inner.Name, StringComparer.Ordinal).Select(inner => inner.Name + ":" + inner.Value.ToString()))
                            : pair.Value.ToString()))))
                .Order(StringComparer.Ordinal));

    // A member with bars on the two newest sessions, a null sma200 on both, and the given momentum readings.
    static void ShortHistoryMember(TemporaryStore store, double? tonightsReading, double? previousReading)
    {
        var sessions = Query(store, "SELECT DISTINCT session_date FROM bar ORDER BY session_date DESC LIMIT 2;");

        Assert.Equal(2, sessions.Count);

        Insert(
            store,
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at, sector) " +
            "VALUES ('GSPC', 'SHORT', '2026-01-02', NULL, '2026-09-08T00:00:00Z', 'Utilities');");

        foreach (var (session, reading) in new[] { (sessions[0], tonightsReading), (sessions[1], previousReading) })
        {
            Insert(
                store,
                "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES " +
                $"('SHORT', '{session}', '100.0000', '101.0000', '99.0000', '100.0000', 1000, 'test', '2026-09-08T21:00:00Z', '100.0000');");

            Insert(
                store,
                "INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES " +
                $"('SHORT', '{session}', 'sma200', NULL, 2), " +
                $"('SHORT', '{session}', '{MomentumIndexReading.Reading}', " +
                (reading is { } value ? value.ToString(CultureInfo.InvariantCulture) : "NULL") + ", 2);");
        }
    }

    [Fact]
    public async Task AMemberWithAReadingNotAvailableIsListedAndTheStageCompletes()
    {
        using var store = await FixtureExpectations.WithListings();

        ShortHistoryMember(store, tonightsReading: 42, previousReading: 41);

        var listed = await RunTheNightAsync(store, "coverage-not-available");

        Assert.Equal([.. FixtureExpectation.CurrentMembers.Append("SHORT").Order(StringComparer.Ordinal)], listed);

        var row = Query(store, "SELECT outcome || ' :: ' || detail FROM run_log WHERE stage = 'listings' AND run_id = 'coverage-not-available';").Single();

        Assert.StartsWith(ShortlistBuilder.Ok + " :: ", row, StringComparison.Ordinal);
        Assert.Contains("no candidate stands registered", row, StringComparison.Ordinal);

        // A null the candidate does not read skips nothing.
        await RegisterAsync(store, "momentum index at one hundred", 100, NightStart.AddDays(-1));

        listed = await RunTheNightAsync(store, "coverage-not-available-registered");

        Assert.Contains("SHORT", listed);

        var shadow = Query(store, "SELECT shadow_reasons FROM listing WHERE ticker = 'SHORT';").Single().Replace(" ", string.Empty, StringComparison.Ordinal);

        Assert.Contains("momentumindexatonehundred", shadow, StringComparison.Ordinal);
        Assert.Contains("\"fired\":true", shadow, StringComparison.Ordinal);
        Assert.Contains("\"skipped\":[]", shadow, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACandidateWhoseReadingTonightIsNotAvailableIsSkippedAndNeverReadFromTheSessionBefore()
    {
        using var store = await FixtureExpectations.WithListings();

        await RegisterAsync(store, "momentum index at one hundred", 100, NightStart.AddDays(-1));

        // 42 fires at a level of 100, so a read of the previous session shows up as a fire.
        ShortHistoryMember(store, tonightsReading: null, previousReading: 42);

        var listed = await RunTheNightAsync(store, "coverage-not-available-tonight");

        Assert.Equal([.. FixtureExpectation.CurrentMembers.Append("SHORT").Order(StringComparer.Ordinal)], listed);

        var shadow = Query(store, "SELECT shadow_reasons FROM listing WHERE ticker = 'SHORT';").Single();

        Assert.Contains(
            $"the night computed no '{MomentumIndexReading.Reading}' for SHORT",
            System.Text.Json.JsonDocument.Parse(shadow).RootElement.GetProperty("skipped")[0].GetProperty("reason").GetString(),
            StringComparison.Ordinal);
        Assert.Equal(0, System.Text.Json.JsonDocument.Parse(shadow).RootElement.GetProperty("candidates").GetArrayLength());
        Assert.DoesNotContain("\"fired\":true", shadow.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);

        foreach (var ticker in listed.Where(ticker => ticker != "SHORT"))
        {
            var other = Query(store, $"SELECT shadow_reasons FROM listing WHERE ticker = '{ticker}';").Single();

            Assert.Contains("\"skipped\":[]", other.Replace(" ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
        }

        // A reading not available is counted on the stage's line and fails nothing.
        var row = Query(store, "SELECT outcome || ' :: ' || detail FROM run_log WHERE stage = 'listings' AND run_id = 'coverage-not-available-tonight';").Single();

        Assert.StartsWith(ShortlistBuilder.Ok + " :: ", row, StringComparison.Ordinal);
        Assert.Contains("1 skipped on a name-night without the readings: 0 stale, 0 gapped, 1 with a reading not available", row, StringComparison.Ordinal);
    }

    static System.Text.Json.JsonElement Expected(string stage) =>
        System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Repository.Root, "fixtures", "membership-2026-09-05", "expectations", stage + ".json"))).RootElement;

    [Fact]
    public void EveryCandidateFlagMatchesTheOneWorkedByHand()
    {
        // The derived expectation. Each name-night's flags are worked in the file
        // from the evaluators' own rules, and the evaluators are put to them
        // directly rather than through a store, because what is being compared is
        // a derivation against the code and a store in between would only add a
        // way for the two to agree by accident.
        var expectation = Expected("shadow-column");
        var nights = expectation.GetProperty("nameNights").EnumerateArray().ToArray();

        Assert.True(nights.Length >= 5, $"The expectation works {nights.Length} name-night(s), expected at least 5.");

        var registered = expectation.GetProperty("registrations").EnumerateArray()
            .Select(one => (
                Candidate: one.GetProperty("candidate").GetString()!,
                Evaluator: CandidateEvaluators.Find(one.GetProperty("evaluator").GetString()!)!,
                Parameters: (IReadOnlyDictionary<string, double>)one.GetProperty("parameters").EnumerateObject()
                    .ToDictionary(pair => pair.Name, pair => pair.Value.GetDouble(), StringComparer.Ordinal)))
            .ToArray();

        Assert.Equal(2, registered.Length);
        Assert.All(registered, one => Assert.NotNull(one.Evaluator));

        var firedSomewhere = registered.ToDictionary(one => one.Candidate, _ => 0, StringComparer.Ordinal);

        foreach (var one in nights)
        {
            var ticker = one.GetProperty("ticker").GetString()!;

            var values = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                [MomentumIndexReading.Reading] = one.GetProperty(MomentumIndexReading.Reading).GetDouble(),
                [MomentumHistogramTurn.Histogram] = one.GetProperty(MomentumHistogramTurn.Histogram).GetDouble(),
                [MomentumHistogramTurn.Previous] = one.GetProperty(MomentumHistogramTurn.Previous).GetDouble(),
            };

            var night = new CandidateNight(ticker, new DateOnly(2026, 9, 8), values);

            foreach (var (candidate, evaluator, parameters) in registered)
            {
                var fired = evaluator.Evaluate(night, parameters).Fired;

                Assert.Equal((ticker, candidate, one.GetProperty(candidate).GetBoolean()), (ticker, candidate, fired));

                if (fired)
                {
                    firedSomewhere[candidate]++;
                }
            }
        }

        // The totals, so a run where every flag happened to be false could not
        // pass the loop above by agreeing with an expectation that was also all
        // false. Both candidates fire somewhere and neither fires everywhere.
        foreach (var counted in expectation.GetProperty("firedSomewhere").EnumerateObject())
        {
            Assert.Equal((counted.Name, counted.Value.GetInt32()), (counted.Name, firedSomewhere[counted.Name]));
            Assert.InRange(firedSomewhere[counted.Name], 1, nights.Length - 1);
        }
    }

    static IReadOnlyList<string> Query(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount)
                .Select(field => reader.IsDBNull(field) ? "null" : reader.GetValue(field).ToString())));
        }

        return rows;
    }

    static void Insert(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
