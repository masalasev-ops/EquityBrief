using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Shortlist;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Shortlist;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// What the three registered conditions fire on, over name-nights worked by hand.
//
// The evaluators are put to the values directly rather than through a store, for the reason the
// shadow column's own expectation gives: what is being compared is a derivation against the code,
// and a store in between adds a way for the two to agree by accident.
public sealed class CandidateConditions
{
    internal static CheckReach Reach => new(
        "candidate-conditions",
        ["fixtures/membership-2026-09-05", "docs/ARCHITECTURE.html"],
        [
            CheckReach.Key(Scope.LimitsTable, "The three candidates' numbers"),
        ]);

    [Fact]
    public void EveryConditionFiresWhereTheExpectationWorkedByHandSaysItDoes()
    {
        var expectation = Expected("candidate-conditions");
        var nights = expectation.GetProperty("nameNights").EnumerateArray().ToArray();

        Assert.True(nights.Length >= 15, $"The expectation works {nights.Length} name-night(s), expected at least 15.");

        var registered = Registered(expectation);

        Assert.Equal(3, registered.Count);

        var firedSomewhere = registered.ToDictionary(one => one.Candidate, _ => 0, StringComparer.Ordinal);
        var readFor = registered.ToDictionary(one => one.Candidate, _ => 0, StringComparer.Ordinal);

        foreach (var one in nights)
        {
            var ticker = one.GetProperty("ticker").GetString()!;
            var candidate = one.GetProperty("candidate").GetString()!;
            var (_, evaluator, parameters) = registered.Single(row => row.Candidate == candidate);

            var values = one.GetProperty("values").EnumerateObject()
                .ToDictionary(pair => pair.Name, pair => pair.Value.GetDouble(), StringComparer.Ordinal);

            // Every key the evaluator reads is present, so a case is a measurement of the rule and
            // never of a value the night left out. The refusal is asserted on its own below.
            Assert.All(evaluator.Reads, key => Assert.Contains(key, (IReadOnlyDictionary<string, double>)values));

            var fired = evaluator.Evaluate(new CandidateNight(ticker, new DateOnly(2026, 9, 8), values), parameters).Fired;

            Assert.Equal((ticker, candidate, one.GetProperty("fires").GetBoolean()), (ticker, candidate, fired));

            readFor[candidate]++;

            if (fired)
            {
                firedSomewhere[candidate]++;
            }
        }

        // The totals, so a run where every flag happened to be false could not pass the loop above
        // by agreeing with an expectation that was also all false. Each condition fires on some of
        // its own cases and not on all of them.
        foreach (var counted in expectation.GetProperty("firedSomewhere").EnumerateObject())
        {
            Assert.Equal((counted.Name, counted.Value.GetInt32()), (counted.Name, firedSomewhere[counted.Name]));
            Assert.InRange(firedSomewhere[counted.Name], 1, readFor[counted.Name] - 1);
        }
    }

    [Fact]
    public void ACrossingIsReadAgainstTheWholeBandAndMeasuredFromTheEdgeItWentThrough()
    {
        var cases = Expected("candidate-conditions").GetProperty("crossings").EnumerateArray().ToArray();

        Assert.True(cases.Length >= 4, $"The expectation works {cases.Length} crossing(s), expected at least 4.");

        foreach (var one in cases)
        {
            var bands = one.GetProperty("bands").EnumerateArray()
                .Select(band => new Band(band.GetProperty("low").GetDecimal(), band.GetProperty("high").GetDecimal()))
                .ToArray();

            var (count, past) = NightReading.Crossings(
                one.GetProperty("close").GetDecimal(),
                one.GetProperty("previousClose").GetDecimal(),
                bands);

            var expected = one.GetProperty("past").ValueKind == JsonValueKind.Null
                ? (decimal?)null
                : one.GetProperty("past").GetDecimal();

            Assert.Equal((one.GetProperty("case").GetString(), one.GetProperty("count").GetInt32(), expected),
                (one.GetProperty("case").GetString(), count, past));
        }
    }

    [Fact]
    public void ANightMissingAValueAConditionReadsIsRefusedRatherThanReadAsANoughtThatDidNotFire()
    {
        var standing = Registered(Expected("candidate-conditions"))
            .Select(one => Register(one.Candidate, one.Evaluator, one.Parameters))
            .ToArray();

        // A name-night carrying every value but the typical move, which two of the three read.
        var missing = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [NightValues.Close] = 100,
            [NightValues.PreviousClose] = 105,
            [NightValues.Zones] = 1,
            [NightValues.ZoneLow] = 99,
            [NightValues.ZoneHigh] = 101,
            [NightValues.Crossings] = 1,
            [NightValues.CrossingDistance] = 1,
            [NightValues.VolumeRatio] = 3,
            [NightValues.NightMedianRatio] = 0.9,
        };

        var withoutTheMove = ShadowColumn.Evaluate(standing, new CandidateNight("AAAA", new DateOnly(2026, 9, 8), missing));

        // The one condition reading neither the typical move nor a zone still scores, and the two
        // that read it are skipped by name with the key they wanted, not written as not fired.
        Assert.Equal("volume against the night", Assert.Single(withoutTheMove.Outcomes).Candidate);
        Assert.Equal(2, withoutTheMove.Skipped.Count);
        Assert.All(withoutTheMove.Skipped, skip => Assert.Contains(IndicatorSeries.Atr14, skip.Reason, StringComparison.Ordinal));
        Assert.All(withoutTheMove.Skipped, skip => Assert.Equal(ShadowSkipCause.NotAvailable, skip.Cause));
        Assert.All(withoutTheMove.Skipped, skip => Assert.False(skip.IsFault));

        // And the two counts are read as measurements rather than as absences: a name whose plan
        // held no zone and a name that crossed nothing are both scored, and neither fires.
        var nothingToArriveAt = new Dictionary<string, double>(missing, StringComparer.Ordinal)
        {
            [IndicatorSeries.Atr14] = 3,
            [NightValues.Zones] = 0,
            [NightValues.Crossings] = 0,
        };

        nothingToArriveAt.Remove(NightValues.ZoneLow);
        nothingToArriveAt.Remove(NightValues.ZoneHigh);
        nothingToArriveAt.Remove(NightValues.CrossingDistance);

        var scored = ShadowColumn.Evaluate(standing, new CandidateNight("AAAA", new DateOnly(2026, 9, 8), nothingToArriveAt));

        Assert.Equal(3, scored.Outcomes.Count);
        Assert.Empty(scored.Skipped);
        Assert.Contains(scored.Outcomes, outcome => outcome.Candidate == "arrived and narrow" && !outcome.Fired);
        Assert.Contains(scored.Outcomes, outcome => outcome.Candidate == "crossed by a margin" && !outcome.Fired);

        // The value the night did write is recorded whichever way the condition went, and the one
        // it did not is recorded as not stored rather than as a nought.
        var record = scored.Outcomes.Single(outcome => outcome.Candidate == "arrived and narrow").Values;

        Assert.Equal("0", record[NightValues.Zones]);
        Assert.Equal("not stored", record[NightValues.ZoneLow]);
        Assert.Equal("not stored", record[NightValues.ZoneStrength]);
    }

    [Fact]
    public void EachConditionRecordsTheFiguresItIsTestedOnAndTheOnesItIsNot()
    {
        var registered = Registered(Expected("candidate-conditions")).ToDictionary(one => one.Candidate, StringComparer.Ordinal);

        var arrived = registered["arrived and narrow"];
        var values = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [NightValues.Close] = 100,
            [NightValues.PreviousClose] = 105,
            [NightValues.Zones] = 2,
            [NightValues.ZoneLow] = 99,
            [NightValues.ZoneHigh] = 101,
            [NightValues.ZoneStrength] = 4,
            [NightValues.ZoneNewestMemberSessions] = 37,
            [IndicatorSeries.Atr14] = 3,
        };

        var verdict = arrived.Evaluator.Evaluate(new CandidateNight("AAAA", new DateOnly(2026, 9, 8), values), arrived.Parameters);

        Assert.True(verdict.Fired);
        Assert.Equal("4", verdict.Values[NightValues.ZoneStrength]);
        Assert.Equal("37", verdict.Values[NightValues.ZoneNewestMemberSessions]);
        Assert.Equal("1", verdict.Values[ArrivedAndNarrow.Width]);

        // The strength and the age are recorded and decide nothing: the same night with a band of
        // no strength and no member at all fires exactly as this one did.
        values[NightValues.ZoneStrength] = 0;
        values.Remove(NightValues.ZoneNewestMemberSessions);

        var withoutThem = arrived.Evaluator.Evaluate(new CandidateNight("AAAA", new DateOnly(2026, 9, 8), values), arrived.Parameters);

        Assert.True(withoutThem.Fired);
        Assert.Equal("not stored", withoutThem.Values[NightValues.ZoneNewestMemberSessions]);

        // The same for the volume rank, which the night writes and the condition does not read.
        var volume = registered["volume against the night"];
        var ranked = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [NightValues.VolumeRatio] = 2.5,
            [NightValues.NightMedianRatio] = 0.9,
            [NightValues.VolumeRank] = 1,
        };

        var withRank = volume.Evaluator.Evaluate(new CandidateNight("AAAA", new DateOnly(2026, 9, 8), ranked), volume.Parameters);

        ranked[NightValues.VolumeRank] = 50;

        var lastInTheWindow = volume.Evaluator.Evaluate(new CandidateNight("AAAA", new DateOnly(2026, 9, 8), ranked), volume.Parameters);

        Assert.True(withRank.Fired);
        Assert.Equal(withRank.Fired, lastInTheWindow.Fired);
        Assert.Equal("1", withRank.Values[NightValues.VolumeRank]);
        Assert.Equal("50", lastInTheWindow.Values[NightValues.VolumeRank]);
    }

    [Fact]
    public void AVolumeRankIsTakenInsideTheNamesOwnWindowAndTiesShareTheBetterRank()
    {
        // Worked by hand over a window of five sessions.
        IReadOnlyList<long> window = [900, 400, 700, 400, 500];

        Assert.Equal(1, NightReading.RankOf(900, window));
        Assert.Equal(2, NightReading.RankOf(700, window));
        Assert.Equal(3, NightReading.RankOf(500, window));
        Assert.Equal(4, NightReading.RankOf(400, window));

        // The two sessions of 400 are both fourth rather than fourth and fifth, which is what the
        // three sessions above them make them, and a volume above every session in the window is
        // first whether or not the window holds it.
        Assert.Equal(1, NightReading.RankOf(1_000, window));
        Assert.Equal(6, NightReading.RankOf(1, window));
    }

    [Fact]
    public void TheThreeConditionsAreTheOnesTheCodeOffersAndEachIsRegisteredUnderTheNumbersTheSpecPins()
    {
        // The catalogue resolves each name, both ways, so a registration cannot name a condition
        // nothing implements and a condition cannot be registered under a parameter it does not read.
        foreach (var (candidate, evaluator, parameters) in Registered(Expected("candidate-conditions")))
        {
            Assert.Same(evaluator, CandidateEvaluators.Find(evaluator.Name));
            Assert.Equal(evaluator.Parameters.Order(StringComparer.Ordinal), parameters.Keys.Order(StringComparer.Ordinal));
            Assert.NotEqual(candidate, evaluator.Name);
        }

        // And the numbers the registration command writes are the ones the code carries.
        Assert.Equal(1, ArrivedAndNarrow.ProposedWidth);
        Assert.Equal(2, VolumeAgainstTheNight.ProposedMultiple);
        Assert.Equal(0.5, CrossedByAMargin.SettledMargin);

        Assert.Equal(
            ArrivedAndNarrow.ProposedWidth,
            Registered(Expected("candidate-conditions")).Single(one => one.Evaluator is ArrivedAndNarrow).Parameters[ArrivedAndNarrow.Width]);

        Assert.Equal(
            VolumeAgainstTheNight.ProposedMultiple,
            Registered(Expected("candidate-conditions")).Single(one => one.Evaluator is VolumeAgainstTheNight).Parameters[VolumeAgainstTheNight.Multiple]);

        Assert.Equal(
            CrossedByAMargin.SettledMargin,
            Registered(Expected("candidate-conditions")).Single(one => one.Evaluator is CrossedByAMargin).Parameters[CrossedByAMargin.Margin]);
    }

    [Fact]
    public async Task TheNightEvaluatesTheThreeOverEveryMemberFromTheStoreAloneAndOneMedianReachesEveryVerdict()
    {
        using var store = await FixtureExpectations.WithListings();

        // The stage takes a clock and a store file and no feed at all, and declares none, so the
        // median it takes before the loop cannot be a request however it is computed. What the
        // declaration is worth is asserted by the conformance check, which fails a component whose
        // code reaches a feed it does not declare.
        Assert.Empty(ShortlistBuilder.Access.Feeds);

        foreach (var (candidate, evaluator, parameters) in Registered(Expected("candidate-conditions")))
        {
            var outcome = await new CandidateRegistrar(
                FixedClock.At(RegisteredAt, SessionZones.UnitedStates),
                store.DatabaseFile).RegisterAsync(candidate, "a rule", "a test", evaluator.Name, parameters, "conditions-" + evaluator.Name);

            Assert.Equal(CandidateRegistrar.Registered, outcome.Outcome);
        }

        await new ShortlistBuilder(FixedClock.At(StageStartedAt, SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync("GSPC", "candidate-conditions", NightStartedAt);

        // The median the night wrote, read back off every verdict that carries one, against the
        // same figure derived here from the store: one number for the night, and the night's own
        // rather than this test's, since a test that recomputed it from its own population would
        // agree with any median the stage happened to take.
        var medians = new HashSet<string>(StringComparer.Ordinal);
        var evaluated = 0;

        foreach (var column in Query(store, "SELECT shadow_reasons FROM listing;"))
        {
            foreach (var one in JsonDocument.Parse(column).RootElement.GetProperty("candidates").EnumerateArray())
            {
                evaluated++;

                if (one.GetProperty("values").TryGetProperty(NightValues.NightMedianRatio, out var median))
                {
                    medians.Add(median.GetString()!);
                }
            }
        }

        var ratios = Query(store, """
            SELECT CAST(b.volume AS REAL) / i.value
            FROM bar b
            JOIN indicator i ON i.ticker = b.ticker AND i.session_date = b.session_date AND i.name = 'vol_avg50'
            JOIN membership m ON m.ticker = b.ticker AND m.index_code = 'GSPC'
            WHERE b.session_date = (SELECT MAX(session_date) FROM bar) AND b.volume > 0 AND i.value > 0
            ORDER BY 1;
            """).Select(value => double.Parse(value, CultureInfo.InvariantCulture)).ToArray();

        Assert.NotEmpty(ratios);

        var middle = ratios.Length % 2 == 1
            ? ratios[ratios.Length / 2]
            : (ratios[(ratios.Length / 2) - 1] + ratios[ratios.Length / 2]) / 2;

        Assert.Equal(middle.ToString("0.####", CultureInfo.InvariantCulture), Assert.Single(medians));

        // Three conditions over every member, and the fixture's names carry the figures the three
        // read, so each name-night is a measurement rather than a skip.
        Assert.Equal(3 * FixtureExpectation.CurrentMembers.Length, evaluated);

        // And what each of the fixture's names came to, against the night worked by hand from its
        // own closes, bands and plans: the median above, one arrival inside a zone narrow enough,
        // two falls through a band by more than half a typical move, and no unusual volume.
        var night = Expected("candidate-conditions").GetProperty("overTheFixtureNight");

        Assert.Equal(night.GetProperty("nightMedianRatio").GetString(), Assert.Single(medians));
        Assert.Equal(night.GetProperty("session").GetString(), Assert.Single(Query(store, "SELECT DISTINCT session_date FROM listing;")));

        var fired = new List<string>();

        foreach (var name in night.GetProperty("byName").EnumerateObject())
        {
            var column = JsonDocument.Parse(
                Assert.Single(Query(store, $"SELECT shadow_reasons FROM listing WHERE ticker = '{name.Name}';"))).RootElement;

            var scored = column.GetProperty("candidates").EnumerateArray()
                .ToDictionary(one => one.GetProperty("candidate").GetString()!, one => one.GetProperty("fired").GetBoolean(), StringComparer.Ordinal);

            Assert.Empty(column.GetProperty("skipped").EnumerateArray());

            foreach (var candidate in name.Value.EnumerateObject())
            {
                Assert.Equal(
                    (name.Name, candidate.Name, candidate.Value.GetBoolean()),
                    (name.Name, candidate.Name, scored[candidate.Name]));

                if (scored[candidate.Name])
                {
                    fired.Add($"{name.Name} {candidate.Name}");
                }
            }
        }

        // Three of the twelve, stated as a count so a night where nothing fired could not agree
        // with an expectation that was also all false.
        Assert.Equal(3, fired.Count);
    }

    [Fact]
    public async Task TheRegistrationCommandWritesTheThreeAtOneInstantOrNoneOfThem()
    {
        using var store = new TemporaryStore().Migrated();

        var run = new StringWriter();
        var said = new StringWriter();
        var code = await RegisterVerb.RunAsync(
            [RegisterVerb.Name, RegisterVerb.TheThree],
            FixedClock.At(RegisteredAt, SessionZones.UnitedStates),
            store.DatabaseFile,
            run,
            said);

        Assert.Equal((0, string.Empty), (code, said.ToString().TrimEnd()));

        // Three rows, at one instant, each on the evaluator the code carries at the version it
        // carries now, and each with the rule and the test the registration states.
        var rows = Query(store, "SELECT candidate || '|' || evaluator || '|' || parameters || '|' || registered_at || '|' || event FROM candidate_register ORDER BY id;");

        Assert.Equal(3, rows.Count);
        Assert.Single(rows.Select(row => row.Split('|')[3]).Distinct(StringComparer.Ordinal));
        Assert.All(rows, row => Assert.EndsWith("|" + CandidateFamily.Registered, row, StringComparison.Ordinal));
        Assert.Empty(Query(store, "SELECT candidate FROM candidate_register WHERE rule = '' OR test = '';"));

        Assert.Equal(
            TheThreeCandidates.All.Select(one => one.Evaluator).Order(StringComparer.Ordinal),
            rows.Select(row => row.Split('|')[1]).Order(StringComparer.Ordinal));

        // One row on the run log for the command, naming what it wrote and the family it leaves.
        Assert.Equal(
            $"{CandidateRegistrar.Registered}|3",
            Assert.Single(Query(store, "SELECT outcome || '|' || rows_written FROM run_log WHERE stage = 'candidate-register';")));

        Assert.Contains("registered 3 at one instant, family of 3 of 8", run.ToString(), StringComparison.Ordinal);

        // And run again it is refused whole: the three already stand, and a refusal that had
        // written the first two would leave a family nobody registered.
        var refusal = new StringWriter();

        Assert.Equal(1, await RegisterVerb.RunAsync(
            [RegisterVerb.Name, RegisterVerb.TheThree],
            FixedClock.At(RegisteredAt.AddHours(1), SessionZones.UnitedStates),
            store.DatabaseFile,
            new StringWriter(),
            refusal));

        Assert.Contains("already stands registered", refusal.ToString(), StringComparison.Ordinal);
        Assert.Contains("so none of the 3 was registered", refusal.ToString(), StringComparison.Ordinal);
        Assert.Equal(3, Query(store, "SELECT id FROM candidate_register;").Count);
        Assert.Equal(2, Query(store, "SELECT run_id FROM run_log WHERE stage = 'candidate-register';").Count);
    }

    static readonly DateTimeOffset RegisteredAt = new(2026, 9, 8, 12, 0, 0, TimeSpan.Zero);

    static readonly DateTimeOffset NightStartedAt = new(2026, 9, 8, 21, 0, 0, TimeSpan.Zero);

    static readonly DateTimeOffset StageStartedAt = new(2026, 9, 8, 21, 5, 0, TimeSpan.Zero);

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
            rows.Add(reader.IsDBNull(0) ? "null" : reader.GetValue(0).ToString()!);
        }

        return rows;
    }

    static IReadOnlyList<(string Candidate, CandidateEvaluator Evaluator, IReadOnlyDictionary<string, double> Parameters)> Registered(
        JsonElement expectation) =>
        [
            .. expectation.GetProperty("registrations").EnumerateArray()
                .Select(one => (
                    Candidate: one.GetProperty("candidate").GetString()!,
                    Evaluator: CandidateEvaluators.Find(one.GetProperty("evaluator").GetString()!)!,
                    Parameters: (IReadOnlyDictionary<string, double>)one.GetProperty("parameters").EnumerateObject()
                        .ToDictionary(pair => pair.Name, pair => pair.Value.GetDouble(), StringComparer.Ordinal))),
        ];

    static RegisterRow Register(string candidate, CandidateEvaluator evaluator, IReadOnlyDictionary<string, double> parameters) =>
        new(
            0,
            candidate,
            "the rule, as the registration states it",
            "the test, as the registration states it",
            evaluator.Name,
            CandidateEvaluator.Write(parameters),
            evaluator.Version,
            CandidateFamily.Registered,
            null,
            new DateTimeOffset(2026, 9, 8, 20, 0, 0, TimeSpan.Zero),
            null);

    // The fixture's own expectation, opened by the stage's name, which is the form the expectation
    // reader looks for when it asks which test reads a file.
    static JsonElement Expected(string stage = "candidate-conditions") =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(
            Repository.Root, "fixtures", "membership-2026-09-05", "expectations", stage + ".json"))).RootElement;
}
