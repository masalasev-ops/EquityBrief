using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Quality;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 22.11: Quality at a fair price's freezes. The remedy's register line freezes the S&P 500's family
// over a register shaped as the operator's stood, its live rule at the starting setting and its seven one-step neighbours
// at one instant, and the form refuses an index it carries no rule for, a setting off the grid, other parameters and a
// family already standing, writing nothing; the frozen setting read on both sides of each dial its registration states;
// the family's correction for luck worked by hand over its own rules on its index alone; the night keeping the live rule
// and each version under its own name over a constructed store, a version reading the trend on both sides of the average
// and one selling on a filing, and a rule whose evaluator moved not kept; the monthly run reading the family from its next
// run after the freeze; and no source another rule's version pins one 22.11 writes.
// see: Each index's quality rule is frozen at its starting setting on the operator's word, with its one-step neighbours as versions
// see: A quality rule is registered as the family on its index and kept by the family's own step alone
public partial class FixtureExpectations
{
    // The operator's register as it stood before the remedy, read off their store's copy of 2026-10-23, which no remedy since
    // registered in: 634 rows, 58 rules standing, by evaluator, the version each names and how many.
    static readonly (string Evaluator, string Version, int Count)[] OperatorsStandingBeforeTheQualityFreeze =
    [
        ("swing-filter", "b4425b7cbb05", 9),
        ("drift", "f6b52da6e0c6", 9),
        ("breakout", "c4cf32e70c59", 8),
        ("drift-400", "a0aac8d3d390", 5),
        ("heavyweight-600", "4aa690bef133", 4),
        ("pullback-600", "61634e51e013", 4),
        ("drift-600", "a0aac8d3d390", 4),
        ("heavyweight", "d78f1ca30465", 4),
        ("breakout-400", "fa012e2fbaba", 3),
        ("pullback-400", "61634e51e013", 3),
        ("breakout-600", "fa012e2fbaba", 3),
        ("heavyweight-400", "4aa690bef133", 2),
    ];

    const string QualityRemedy = "2026-10-25-22.11-the-s-and-p-500-quality-freeze.txt";

    static readonly DateTimeOffset QualityFrozenAt = new(2026, 10, 25, 21, 0, 0, TimeSpan.Zero);

    // A store holding the operator's register's shape: each standing rule a row of its own, the first of each evaluator its
    // live rule, the ids running up to the 634 the register held.
    static TemporaryStore OperatorShapedRegister()
    {
        var store = new TemporaryStore().Migrated();
        var id = 634 - OperatorsStandingBeforeTheQualityFreeze.Sum(one => one.Count);

        foreach (var (evaluator, version, count) in OperatorsStandingBeforeTheQualityFreeze)
        {
            for (var at = 0; at < count; at++)
            {
                var name = FormattableString.Invariant($"{(at == 0 ? FamilyRecords.LivePrefix : "the ")}{evaluator} rule {at + 1}");

                store.Execute(
                    "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) " +
                    FormattableString.Invariant($"VALUES ({++id}, '{name}', 'the rule', 'the test', '{evaluator}', '{{}}', '{version}', 'registered', NULL, '2026-10-20T18:37:08Z', NULL);"));
            }
        }

        return store;
    }

    static string[] RemedyRegisterLines() =>
        [.. File.ReadAllLines(Path.Combine(Repository.Root, "tools", "remedies", QualityRemedy)).Where(line => line.StartsWith("register ", StringComparison.Ordinal))];

    // The settings one dial from the starting setting on the history test's grid, each worked by hand, in the grid's order,
    // the floor the outermost dial and the sale the innermost: the floor at 6, the valuation off, the sale on a filing, a
    // book of 20, the trend on, the valuation in its cheapest third and the floor at 8.
    static readonly QualitySettings[] StartingNeighbours =
    [
        QualityRule.Starting with { ScoreFloorInNine = 6 },
        QualityRule.Starting with { Valuation = QualityValuation.Off },
        QualityRule.Starting with { SellOnFiling = true },
        QualityRule.Starting with { Holdings = 20 },
        QualityRule.Starting with { Trend = true },
        QualityRule.Starting with { Valuation = QualityValuation.CheapestThird },
        QualityRule.Starting with { ScoreFloorInNine = 8 },
    ];

    static string VersionName(string indexName, QualitySettings setting) => "the " + indexName + " quality rule at " + QualityRules.Short(setting);

    [Fact]
    public async Task TheRemedysRegisterLineFreezesTheSAndP500QualityFamilyOverARegisterShapedAsTheOperatorsStood()
    {
        using var store = OperatorShapedRegister();
        var lines = RemedyRegisterLines();

        // One register line, the S&P 500's freeze at the starting setting; the S&P 400 and 600 are not frozen by it.
        var line = Assert.Single(lines);
        var words = Arguments(line);

        Assert.Equal([RegisterVerb.IndexFamily, QualityRule.Name, "--index", QualityRule.LargeIndex, "--parameters"], words[1..6]);
        Assert.Equal(QualityRule.Starting, QualityCandidate.SettingsOf(VerbArguments.Parameters(words[6])));

        var (code, said) = await RegisterVerbAt(store, QualityFrozenAt, words[1..]);

        Assert.True(code == 0, said);
        Assert.Contains("registered 8 at one instant", said, StringComparison.Ordinal);

        // Standing after it: the 58 as they stood and the family's eight, its live rule at the starting setting and each
        // neighbour a version named for its setting, every one at the version the code carries and at one instant.
        var rows = await new CandidateRegistrar(FixedClock.At(QualityFrozenAt.AddMinutes(1), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();
        var standing = CandidateFamily.Standing(rows, QualityFrozenAt.AddMinutes(1));
        var family = CandidateFamily.In(standing, "quality on the S&P 500");

        Assert.Equal(58 + 8, standing.Count);
        Assert.Equal(
            [FamilyRecords.LivePrefix + "S&P 500 quality rule at " + QualityRules.Short(QualityRule.Starting), .. StartingNeighbours.Select(setting => VersionName("S&P 500", setting))],
            family.OrderBy(row => row.Id).Select(row => row.Candidate));
        Assert.All(family, row => Assert.Equal(("quality-500", CandidateEvaluators.Find("quality-500")!.Version), (row.Evaluator, row.EvaluatorVersion)));
        Assert.Single(family.Select(row => row.RegisteredAt).Distinct());
        Assert.Equal(635, family.Min(row => row.Id));
        Assert.Equal(
            [QualityRule.Starting, .. StartingNeighbours],
            family.OrderBy(row => row.Id).Select(row => QualityCandidate.SettingsOf(CandidateEvaluator.Read(row.Parameters))));
        Assert.All(family.Where(row => !FamilyRecords.IsLive(row.Candidate)), row => Assert.Equal(1, QualityCandidate.SettingsOf(CandidateEvaluator.Read(row.Parameters)).Changes(QualityRule.Starting)));

        // The same line again is refused while the family stands, and writes nothing.
        var (again, refused) = await RegisterVerbAt(store, QualityFrozenAt.AddMinutes(2), words[1..]);

        Assert.NotEqual(0, again);
        Assert.Contains("the quality on the S&P 500 holds 8 rule(s) standing", refused, StringComparison.Ordinal);
        Assert.Equal(rows.Count, (await new CandidateRegistrar(FixedClock.At(QualityFrozenAt.AddMinutes(3), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync()).Count);
    }

    [Fact]
    public async Task AQualityFreezeIsRefusedOffTheGridOnAnIndexItCarriesNoRuleForWithOtherParametersOrWithVariantsAndWritesNothing()
    {
        using var store = new TemporaryStore().Migrated();
        const string Starting = "floor=7,valuation=1,trend=0,book=10,sale=0";

        foreach (var ((args, expected), at) in new (string[] Args, string Said)[]
        {
            ([RegisterVerb.IndexFamily, QualityRule.Name, "--index", "GSPC", "--parameters", "floor=5,valuation=1,trend=0,book=10,sale=0"], "the history test's grid does not hold"),
            ([RegisterVerb.IndexFamily, QualityRule.Name, "--index", "GSPC", "--parameters", "floor=7,valuation=1,trend=0,book=15,sale=0"], "the history test's grid does not hold"),
            ([RegisterVerb.IndexFamily, QualityRule.Name, "--index", "GSPC", "--parameters", "floor=7,valuation=1,trend=0,book=10"], "the quality rule reads [book, floor, sale, trend, valuation]"),
            ([RegisterVerb.IndexFamily, QualityRule.Name, "--index", "NDX", "--parameters", Starting], "no quality rule is carried for an index coded 'NDX'"),
            ([RegisterVerb.IndexFamily, QualityRule.Name, "--index", "GSPC", "--parameters", Starting, "--variants", "floor=8,valuation=1,trend=0,book=10,sale=0"], "takes neither --variants nor --replacing"),
        }.Select((one, at) => (one, at)))
        {
            var (code, said) = await RegisterVerbAt(store, QualityFrozenAt.AddSeconds(at), args);

            Assert.NotEqual(0, code);
            Assert.Contains(expected, said, StringComparison.Ordinal);
        }

        Assert.Empty(await new CandidateRegistrar(FixedClock.At(QualityFrozenAt.AddMinutes(1), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync());
    }

    // The frozen setting as its registration states it and as the rule reads it back, each dial on both sides: a score of 7
    // in 9 qualifies and 6 does not, a place of 0.5 qualifies and 0.4999 does not, the trend off qualifies a close under
    // its average, and the book holds 10 and sells at reshuffles alone.
    [Fact]
    public void TheFrozenSettingIsReadOnBothSidesOfEachDialItsRegistrationStates()
    {
        var stated = VerbArguments.Parameters(Arguments(Assert.Single(RemedyRegisterLines()))[6]);
        var frozen = QualityCandidate.SettingsOf(stated);

        Assert.Equal(
            new Dictionary<string, double>(StringComparer.Ordinal) { ["floor"] = 7, ["valuation"] = 1, ["trend"] = 0, ["book"] = 10, ["sale"] = 0 },
            stated);
        Assert.Equal(stated, QualityCandidate.ParametersOf(frozen));
        Assert.Equal(QualityRule.Starting, frozen);

        Assert.True(QualityRule.Answer(7, 9, 0.5, false, frozen).Passed);
        Assert.Equal(QualityRule.UnderTheFloor, QualityRule.Answer(6, 9, 0.5, false, frozen).Reason);
        Assert.Equal(QualityRule.TooDear, QualityRule.Answer(7, 9, 0.4999, false, frozen).Reason);
        Assert.True(QualityRule.Answer(7, 9, 0.5, null, frozen).Passed);
        Assert.Equal((10, false), (frozen.Holdings, frozen.SellOnFiling));

        // And each neighbour's parameters read back as the setting it was registered at, the valuation's three levels
        // and the trend's and the sale's two each read as written.
        Assert.All(StartingNeighbours, setting => Assert.Equal(setting, QualityCandidate.SettingsOf(QualityCandidate.ParametersOf(setting))));
        Assert.Equal([0.0, 1.0, 2.0], QualityRule.Valuations.Select(valuation => QualityCandidate.ParametersOf(frozen with { Valuation = valuation })[QualityCandidate.ValuationParameter]));
    }

    // The family's correction for luck over its own rules: after the freeze the S&P 500's quality family counts its eight
    // and nothing else, so each rule's share of the level is 0.05 over 8, 0.00625; no other family's count moves, and the
    // S&P 400's and 600's quality families count none.
    [Fact]
    public async Task TheFamilysLuckCorrectionCountsItsOwnEightRulesOnItsIndexAlone()
    {
        using var store = OperatorShapedRegister();
        var registrar = new CandidateRegistrar(FixedClock.At(QualityFrozenAt.AddMinutes(1), SessionZones.UnitedStates), store.DatabaseFile);
        var before = await registrar.RowsAsync();

        var (code, said) = await RegisterVerbAt(store, QualityFrozenAt, Arguments(Assert.Single(RemedyRegisterLines()))[1..]);

        Assert.True(code == 0, said);

        var after = await registrar.RowsAsync();
        var window = QualityFrozenAt.AddDays(1);

        int Count(IReadOnlyList<RegisterRow> rows, string family) => CandidateFamily.Divisor(CandidateFamily.In(rows, family), window);

        var expected = Expected("quality-freeze");

        Assert.Equal(expected.GetProperty("rules").GetInt32(), Count(after, "quality on the S&P 500"));
        Assert.Equal(expected.GetProperty("level").GetDouble(), 0.05 / Count(after, "quality on the S&P 500"), 12);
        Assert.Equal(0, Count(after, "quality on the S&P 400"));
        Assert.Equal(0, Count(after, "quality on the S&P 600"));

        foreach (var family in new[] { "pullback", "breakout", "drift", "heavyweight", "drift on the S&P 400", "heavyweight on the S&P 600" })
        {
            Assert.Equal(Count(before, family), Count(after, family));
        }

        Assert.Equal(9, Count(after, "drift"));
        Assert.Equal(5, Count(after, "drift on the S&P 400"));
    }

    // The S&P 400 frozen in a constructed store at the starting setting, its seven versions beside it, and a rule on the S&P
    // 600 named at a version the code no longer carries. On the first night each rule holds what its own setting chooses
    // over the twelve members: the live rule and the floor at 6, the valuation off, its cheapest third and the sale on a
    // filing the first ten in the order, the floor at 8 the five at 8 of 9, a book of 20 the eleven qualifying, and the trend
    // on Q01 alone, its close of 101 above its average of 100, Q02's 102 at its average and Q03's 103 under 103.5 and the
    // rest with none stored. On the second night Q04's score falls to 6 of 9: the version selling on a filing sells it at
    // that close, 106, and the live rule keeps it to the reshuffle. The provisional rule is kept on no index a rule stands
    // on, and the S&P 600's rule is not kept, its night naming why.
    [Fact]
    public async Task TheNightKeepsTheLiveRuleAndEachVersionUnderItsOwnNameAndARuleWhoseCodeMovedNotAtAll()
    {
        using var store = QualityStore();

        var (code, said) = await RegisterVerbAt(store, QualityFrozenAt, RegisterVerb.IndexFamily, QualityRule.Name, "--index", "MID", "--parameters", "floor=7,valuation=1,trend=0,book=10,sale=0");

        Assert.True(code == 0, said);

        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) VALUES " +
            "(100, 'the live S&P 600 quality rule at a moved version', 'the rule', 'the test', 'quality-600', '{\"book\": 10, \"floor\": 7, \"sale\": 0, \"trend\": 0, \"valuation\": 1}', '0123456789ab', 'registered', NULL, '2026-10-25T21:00:00Z', NULL);");
        store.Execute(
            "INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES " +
            $"('Q01', '{QualityFirst}', 'sma200', 100.0, 200), ('Q02', '{QualityFirst}', 'sma200', 102.0, 200), ('Q03', '{QualityFirst}', 'sma200', 103.5, 200);");

        var live = FamilyRecords.LivePrefix + "S&P 400 quality rule at " + QualityRules.Short(QualityRule.Starting);

        await new QualityFamily(QualityClock(QualityFirst), store.DatabaseFile).RunAsync("quality-1");

        string Held(string rule) => string.Join(",", FamilyRows(store, $"SELECT ticker FROM quality_holding WHERE index_code = 'MID' AND rule = '{rule.Replace("'", "''", StringComparison.Ordinal)}' ORDER BY ticker;"));

        var firstTen = string.Join(",", Enumerable.Range(1, 10).Select(number => FormattableString.Invariant($"Q{number:00}")));

        Assert.Equal(firstTen, Held(live));
        Assert.Equal(
            [firstTen, firstTen, firstTen, firstTen + ",Q11", "Q01", firstTen, "Q01,Q02,Q03,Q04,Q05"],
            StartingNeighbours.Select(setting => Held(VersionName("S&P 400", setting))));

        // The counts the expectations file derives by hand, rule by rule in the freeze's order.
        var expected = Expected("quality-freeze");

        Assert.Equal(
            new[] { "live", "floor6", "valuationOff", "onFiling", "book20", "trend", "cheapestThird", "floor8" }.Select(key => expected.GetProperty("firstNight").GetProperty(key).GetInt32()),
            new[] { live }.Concat(StartingNeighbours.Select(setting => VersionName("S&P 400", setting))).Select(rule => Held(rule).Split(',').Length));
        Assert.Equal(
            new[] { live }.Concat(StartingNeighbours.Select(setting => VersionName("S&P 400", setting))).Order(StringComparer.Ordinal),
            FamilyRows(store, $"SELECT rule FROM quality_night WHERE index_code = 'MID' AND session_date = '{QualityFirst}';").Order(StringComparer.Ordinal));
        Assert.Empty(FamilyRows(store, $"SELECT rule FROM quality_night WHERE index_code = 'MID' AND rule = '{QualityFamily.ProvisionalRule}';"));
        Assert.Equal(["provisional|null"], FamilyRows(store, $"SELECT rule, fault FROM quality_night WHERE index_code = 'GSPC' AND session_date = '{QualityFirst}';"));
        Assert.Equal([$"the live S&P 600 quality rule at a moved version|{QualityFamily.Moved}"], FamilyRows(store, $"SELECT rule, fault FROM quality_night WHERE index_code = 'SML' AND session_date = '{QualityFirst}';"));

        // The settings each night's row states are its rule's own.
        Assert.Equal("20", Assert.Single(FamilyRows(store, $"SELECT json_extract(settings, '$.holdings') FROM quality_night WHERE rule = '{VersionName("S&P 400", QualityRule.Starting with { Holdings = 20 })}';")));

        // The second night: Q04's filing drops its score to 6 of 9.
        for (var number = 1; number <= 12; number++)
        {
            if (number != 10)
            {
                QualityBar(store, FormattableString.Invariant($"Q{number:00}"), QualityNext, 101m + number, 102m + number);
            }
        }

        store.Execute($"UPDATE fundamental_score SET passed = 6 WHERE index_code = 'MID' AND ticker = 'Q04' AND session_date = '{QualityNext}';");

        await new QualityFamily(QualityClock(QualityNext), store.DatabaseFile).RunAsync("quality-2");

        var onFiling = VersionName("S&P 400", QualityRule.Starting with { SellOnFiling = true });

        var sale = expected.GetProperty("filingSale");
        var (fill, sold) = (sale.GetProperty("fill").GetString(), sale.GetProperty("sale").GetString());

        Assert.Equal(
            [$"{QualityNext}|{fill}|{QualityNext}|{sold}|{QualityFamily.NoLongerQualifies}"],
            FamilyRows(store, $"SELECT bought_on, fill, ended_on, sale, end_reason FROM quality_holding WHERE rule = '{onFiling}' AND ticker = 'Q04';"));
        Assert.Equal(
            [$"{QualityNext}|{fill}|null|open"],
            FamilyRows(store, $"SELECT bought_on, fill, ended_on, end_reason FROM quality_holding WHERE rule = '{live}' AND ticker = 'Q04';"));
        Assert.Equal([sale.GetProperty("heldAfter").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture)], FamilyRows(store, $"SELECT held FROM quality_night WHERE rule = '{onFiling}' AND session_date = '{QualityNext}';"));
        Assert.Equal([sale.GetProperty("liveHeld").GetInt32().ToString(System.Globalization.CultureInfo.InvariantCulture)], FamilyRows(store, $"SELECT held FROM quality_night WHERE rule = '{live}' AND session_date = '{QualityNext}';"));
    }

    // The monthly run reads the family on an index only once a rule of it stands there when the run begins: before the
    // freeze none, after it the eight walked at their own settings, the live rule first and each version in the order it
    // was registered, each a book in months, and none on an index no freeze stands on.
    [Fact]
    public async Task TheMonthlyRunReadsTheFamilyFromItsNextRunAfterTheFreeze()
    {
        using var store = OperatorShapedRegister();
        var registrar = new CandidateRegistrar(FixedClock.At(QualityFrozenAt.AddDays(7), SessionZones.UnitedStates), store.DatabaseFile);
        var before = ShadowColumn.StandingAt(await registrar.RowsAsync(), QualityFrozenAt.AddDays(7));

        Assert.False(WalkForwardTester.ReadsQuality(before, QualityRule.LargeIndex));
        Assert.Empty(RuleTests.Quality(before, QualityRule.LargeIndex, _ => []));

        var (code, said) = await RegisterVerbAt(store, QualityFrozenAt, Arguments(Assert.Single(RemedyRegisterLines()))[1..]);

        Assert.True(code == 0, said);

        // A run that began before the freeze still reads none; the first after it reads the eight.
        var rows = await registrar.RowsAsync();

        Assert.False(WalkForwardTester.ReadsQuality(ShadowColumn.StandingAt(rows, QualityFrozenAt.AddSeconds(-1)), QualityRule.LargeIndex));

        var standing = ShadowColumn.StandingAt(rows, QualityFrozenAt.AddDays(7));

        Assert.True(WalkForwardTester.ReadsQuality(standing, QualityRule.LargeIndex));
        Assert.False(WalkForwardTester.ReadsQuality(standing, "MID"));

        // Each rule's months as its own book, its holdings the stand-in figure here, so each walked rule is shown to read
        // its own setting.
        var walked = RuleTests.Quality(standing, QualityRule.LargeIndex, setting => [(0, setting.Holdings / 100.0)]).ToArray();

        Assert.Equal(8, walked.Length);
        Assert.Equal([RuleTests.Live, .. Enumerable.Repeat(RuleTests.Variant, 7)], walked.Select(rule => rule.Standing));
        Assert.All(walked, rule => Assert.True(rule.Book));
        Assert.Equal(QualityRule.Starting.Words, walked[0].Words);
        Assert.Equal([0.1, 0.1, 0.1, 0.1, 0.2, 0.1, 0.1, 0.1], walked.Select(rule => Assert.Single(rule.Months!).Edge!.Value));
        Assert.Empty(RuleTests.Quality(standing, "MID", _ => []));
    }

    // The sources 22.11 writes or edits beside the quality rules' own, which the rules pin.
    static readonly string[] QualityFreezeSources =
    [
        "src/EquityBrief.Core/Candidates/CandidateEvaluators.cs",
        "src/EquityBrief.Worker/Quality/QualityRules.cs",
        "src/EquityBrief.Worker/Candidates/RegisterVerb.cs",
        "src/EquityBrief.Worker/Candidates/CandidateRegistrar.cs",
        "src/EquityBrief.Worker/Cards/RuleCards.cs",
        "src/EquityBrief.Worker/Families/FamilyRecorder.cs",
        "src/EquityBrief.Worker/Families/FamilyReplay.cs",
        "src/EquityBrief.Worker/Loop/RuleTests.cs",
        "src/EquityBrief.Worker/Loop/WalkForwardTester.cs",
        "src/EquityBrief.Worker/Nightly.cs",
        "src/EquityBrief.Worker/Program.cs",
        "src/EquityBrief.Worker/Sweep/IndexSweepRunner.Quality.cs",
        "src/EquityBrief.Worker/Sweep/QualitySweep.cs",
        "src/EquityBrief.Worker/Sweep/SweepAnswers.cs",
        "src/EquityBrief.Api/Program.cs",
        "src/EquityBrief.Api/Reading/PaperScreen.cs",
        "src/EquityBrief.Api/Reading/ReadApi.Paper.cs",
        "src/EquityBrief.Api/Reading/ReadApi.Quality.cs",
        "src/EquityBrief.Api/Reading/RuleScreen.cs",
        "src/EquityBrief.Api/Reading/TonightScreen.Quality.cs",
        "src/EquityBrief.Core/Cards/RuleChips.cs",
        "src/EquityBrief.Web/App/SinglePageApp.Tonight.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Paper.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Quality.cs",
    ];

    // The quality rules' own sources, which no rule registered before them pins.
    static readonly string[] QualityRuleSources =
    [
        "src/EquityBrief.Core/Candidates/OwnStepCandidate.cs",
        "src/EquityBrief.Core/Candidates/QualityCandidate.cs",
        "src/EquityBrief.Core/Families/QualityRule.cs",
        "src/EquityBrief.Worker/Quality/QualityFamily.cs",
    ];

    // No source a rule registered before the freeze pins is one 22.11 writes, so no other rule's version moves, the reader
    // shown to find a source a rule pins; and the quality rules pin their own files and none of the others 22.11 writes.
    [Fact]
    public void NoSourceAnotherRulesVersionPinsIsOne22Point11Writes()
    {
        // The dip's rules aside from 22.3, registered after 22.11 and pinning the step-reading code it shares.
        // see: A dip rule is registered as the family on its index and kept by the dip's own step alone
        var pinned = CandidateEvaluators.All
            .Where(evaluator => evaluator is not (QualityCandidate or DipCandidate))
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(Core.Loop.RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var quality = CandidateEvaluators.All.OfType<QualityCandidate>().SelectMany(evaluator => evaluator.OwnSources).Distinct(StringComparer.Ordinal).ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(QualityFreezeSources.Concat(QualityRuleSources), path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(QualityFreezeSources.Concat(QualityRuleSources).Intersect(pinned, StringComparer.Ordinal));
        Assert.Empty(QualityFreezeSources.Intersect(quality, StringComparer.Ordinal));
        Assert.Superset(QualityRuleSources.Where(path => !path.EndsWith("/QualityCandidate.cs", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal), quality.ToHashSet(StringComparer.Ordinal));
        Assert.NotEmpty(new[] { "src/EquityBrief.Worker/Indices/IndexFamilies.cs" }.Intersect(pinned, StringComparer.Ordinal));
    }
}
