using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Improving;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 22.15: the remedy's lines freeze the Fundamentals family on each index over a register shaped as
// the operator's stood, the S&P 500's at its starting setting and the S&P 400's and 600's at the proposals their runs
// stated, each with its eight one-dial neighbours, at one instant and at the version the code carries, and
// the form refuses a setting off the grid, an index it carries no rule for, other parameters, variants and a second freeze,
// writing nothing; each frozen setting is read on both sides of each dial its registration states and the rule's answer
// buys exactly where the grid's own walk buys; the night keeps the live rule and each version under its own name over a
// constructed store and a rule whose evaluator moved not at all; the answer command records a Fundamentals history
// test's proposal and its starting setting as ruled; the monthly run reads the family from its next run after the freeze;
// and no source another rule's version pins is one 22.15 writes, every other version standing as the operator's register
// names it.
// see: Each index's Fundamentals rule is frozen on the operator's answers of 2026-10-28 with its one-step neighbours as versions
// see: A Fundamentals rule is registered as the family on its index and kept by the family's own step alone
public partial class FixtureExpectations
{
    const string ImprovingRemedy = "2026-10-29-22.15-the-fundamentals-freezes.txt";

    static readonly DateTimeOffset ImprovingFrozenAt = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    // The operator's register as it stood before the remedy, read off their store on 2026-10-29 read-only: 672 rows, 96
    // rules standing, by evaluator, the version each names and how many.
    static readonly (string Evaluator, string Version, int Count)[] OperatorsStandingBeforeTheImprovingFreeze =
    [
        ("swing-filter", "b4425b7cbb05", 9),
        ("drift", "f6b52da6e0c6", 9),
        ("breakout", "c4cf32e70c59", 8),
        ("quality-500", "88dcf3013d88", 8),
        ("quality-400", "88dcf3013d88", 8),
        ("quality-600", "88dcf3013d88", 8),
        ("drift-400", "a0aac8d3d390", 5),
        ("dip-500", "e8c0399ca425", 5),
        ("dip-600", "e8c0399ca425", 5),
        ("heavyweight-600", "4aa690bef133", 4),
        ("pullback-600", "61634e51e013", 4),
        ("drift-600", "a0aac8d3d390", 4),
        ("heavyweight", "d78f1ca30465", 4),
        ("dip-400", "e8c0399ca425", 4),
        ("breakout-400", "fa012e2fbaba", 3),
        ("pullback-400", "61634e51e013", 3),
        ("breakout-600", "fa012e2fbaba", 3),
        ("heavyweight-400", "4aa690bef133", 2),
    ];

    // Each index's frozen setting as the operator answered: the S&P 500's starting setting, and the S&P 400's and 600's
    // proposals as their runs of 2026-10-27 stated them.
    static readonly ImprovingSweepSetting MidImprovingFrozen = new(7, true, true, false, 7, 63);

    static readonly ImprovingSweepSetting SmallImprovingFrozen = new(9, true, true, true, 6, 252);

    static IReadOnlyList<ImprovingSweepSetting> ImprovingNeighbours(ImprovingSweepSetting setting) => [.. ImprovingGrid.Settings.Where(one => one.Changes(setting) == 1)];

    static string ImprovingLive(string indexName, ImprovingSweepSetting setting) => FamilyRecords.LivePrefix + indexName + " Fundamentals rule at " + ImprovingRules.Short(setting);

    static string ImprovingVersion(string indexName, ImprovingSweepSetting setting) => "the " + indexName + " Fundamentals rule at " + ImprovingRules.Short(setting);

    // A store holding the operator's register's shape: each standing rule a row of its own, the first of each evaluator its
    // live rule, the ids running up to the 672 the register held.
    static TemporaryStore OperatorShapedRegisterBeforeTheImprovingFreeze()
    {
        var store = new TemporaryStore().Migrated();
        var id = 672 - OperatorsStandingBeforeTheImprovingFreeze.Sum(one => one.Count);

        foreach (var (evaluator, version, count) in OperatorsStandingBeforeTheImprovingFreeze)
        {
            for (var at = 0; at < count; at++)
            {
                var name = FormattableString.Invariant($"{(at == 0 ? FamilyRecords.LivePrefix : "the ")}{evaluator} rule {at + 1}");

                store.Execute(
                    "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) " +
                    FormattableString.Invariant($"VALUES ({++id}, '{name}', 'the rule', 'the test', '{evaluator}', '{{}}', '{version}', 'registered', NULL, '2026-10-25T23:27:24Z', NULL);"));
            }
        }

        return store;
    }

    static string[] ImprovingRemedyLines() =>
        [.. File.ReadAllLines(Path.Combine(Repository.Root, "tools", "remedies", ImprovingRemedy)).Where(line => line.Length > 0 && !line.StartsWith('#'))];

    [Fact]
    public async Task TheRemedysLinesFreezeTheFundamentalsFamilyOnEachIndexOverARegisterShapedAsTheOperatorsStood()
    {
        using var store = OperatorShapedRegisterBeforeTheImprovingFreeze();
        var lines = ImprovingRemedyLines();
        var expected = Expected("improving-freeze");

        // Each index's two lines in order: its answer recorded from its own run, the S&P 500's at the starting setting as
        // ruled by the key its test names it by, and the freeze at the setting the operator answered.
        Assert.Equal(6, lines.Length);
        Assert.Equal(["sweep-answer", "--run", "20261027T182510Z", "--ruled", "2026-10-28", "--setting", ImprovingGrid.Starting.Key], Arguments(lines[0]));
        Assert.Equal(["sweep-answer", "--run", "20261027T182542Z"], Arguments(lines[2]));
        Assert.Equal(["sweep-answer", "--run", "20261027T182701Z"], Arguments(lines[4]));

        foreach (var (index, at, setting) in new[] { ("GSPC", 1, ImprovingGrid.Starting), ("MID", 3, MidImprovingFrozen), ("SML", 5, SmallImprovingFrozen) })
        {
            var words = Arguments(lines[at]);

            Assert.Equal(["register", RegisterVerb.IndexFamily, ImprovingRule.Name, "--index", index, "--parameters"], words[..6]);
            Assert.Equal(setting, ImprovingCandidate.SettingsOf(VerbArguments.Parameters(words[6])));
        }

        foreach (var (line, at, count) in new[] { (lines[1], 0, 9), (lines[3], 1, 9), (lines[5], 2, 9) })
        {
            var (code, said) = await RegisterVerbAt(store, ImprovingFrozenAt.AddMinutes(at), Arguments(line)[1..]);

            Assert.True(code == 0, said);
            Assert.Contains(FormattableString.Invariant($"registered {count} at one instant"), said, StringComparison.Ordinal);
        }

        // Standing after them: the 96 as they stood and each index's rules, its live rule at the answered setting and each
        // one-step neighbour a version named for its setting in the grid's order, at the version the code carries and at
        // one instant, each family counting its own rules alone.
        var rows = await new CandidateRegistrar(FixedClock.At(ImprovingFrozenAt.AddMinutes(5), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();
        var standing = CandidateFamily.Standing(rows, ImprovingFrozenAt.AddMinutes(5));

        Assert.Equal(96 + 9 + 9 + 9, standing.Count);

        foreach (var (index, named, evaluator, setting) in new[] { ("GSPC", "S&P 500", "improving-500", ImprovingGrid.Starting), ("MID", "S&P 400", "improving-400", MidImprovingFrozen), ("SML", "S&P 600", "improving-600", SmallImprovingFrozen) })
        {
            var family = CandidateFamily.In(standing, "improving on the " + named);
            var count = expected.GetProperty("rules").GetProperty(index).GetInt32();

            Assert.Equal(count, family.Count);
            Assert.Equal(count, ImprovingNeighbours(setting).Count + 1);
            Assert.Equal(
                [ImprovingLive(named, setting), .. ImprovingNeighbours(setting).Select(one => ImprovingVersion(named, one))],
                family.OrderBy(row => row.Id).Select(row => row.Candidate));
            Assert.All(family, row => Assert.Equal((evaluator, CandidateEvaluators.Find(evaluator)!.Version), (row.Evaluator, row.EvaluatorVersion)));
            Assert.Single(family.Select(row => row.RegisteredAt).Distinct());
            Assert.Equal(count, CandidateFamily.Divisor(family, ImprovingFrozenAt.AddDays(1)));
            Assert.Equal(setting, ImprovingCandidate.SettingsOf(CandidateEvaluator.Read(family.Single(row => FamilyRecords.IsLive(row.Candidate)).Parameters)));
        }

        Assert.Equal(96, standing.Count(row => CandidateEvaluators.Find(row.Evaluator) is not ImprovingCandidate));
    }

    // The form refuses a second freeze on an index already frozen, a setting off the grid, an index the family carries no
    // rule for, other parameters, variants and a rule kept in the background, writing nothing each time.
    [Fact]
    public async Task AFundamentalsFreezeIsRefusedOffTheGridOnAnIndexItCarriesNoRuleForWithOtherParametersWithVariantsOrAgainAndWritesNothing()
    {
        using var store = OperatorShapedRegisterBeforeTheImprovingFreeze();
        var minute = 0;

        foreach (var (arguments, why) in new (string[] Arguments, string Why)[]
        {
            (["--index", "MID", "--parameters", "buy=6,flag=1,rise=1,faster=0,hold=7,limit=63"], "the history test's grid does not hold"),
            (["--index", "XYZ", "--parameters", "buy=7,flag=1,rise=1,faster=0,hold=7,limit=63"], "no Fundamentals rule is carried for an index coded 'XYZ'"),
            (["--index", "MID", "--parameters", "buy=7,flag=1,rise=1,faster=0,hold=7"], "the live rule states [buy, faster, flag, hold, rise]"),
            (["--index", "MID", "--parameters", "buy=7,flag=1,rise=1,faster=0,hold=7,limit=63", "--variants", "buy=8,flag=1,rise=1,faster=0,hold=7,limit=63"], "takes no --variants"),
            (["--index", "MID", "--parameters", "buy=7,flag=1,rise=1,faster=0,hold=7,limit=63", "--replacing", "the live improving-400 rule 1"], "no --replacing"),
        })
        {
            var (code, said) = await RegisterVerbAt(store, ImprovingFrozenAt.AddMinutes(++minute), [RegisterVerb.IndexFamily, ImprovingRule.Name, .. arguments]);

            Assert.True(code != 0, said);
            Assert.Contains(why, said, StringComparison.Ordinal);
        }

        Assert.Equal(["0"], TextRows(store, "SELECT COUNT(*) FROM candidate_register WHERE evaluator = 'improving-400';"));

        var (frozen, saidFrozen) = await RegisterVerbAt(store, ImprovingFrozenAt.AddMinutes(++minute), RegisterVerb.IndexFamily, ImprovingRule.Name, "--index", "MID", "--parameters", "buy=7,flag=1,rise=1,faster=0,hold=7,limit=63");

        Assert.True(frozen == 0, saidFrozen);

        var (again, saidAgain) = await RegisterVerbAt(store, ImprovingFrozenAt.AddMinutes(++minute), RegisterVerb.IndexFamily, ImprovingRule.Name, "--index", "MID", "--parameters", "buy=7,flag=1,rise=1,faster=0,hold=7,limit=63");

        Assert.True(again != 0, saidAgain);
        Assert.Contains("holds 9 rule(s) standing", saidAgain, StringComparison.Ordinal);
        Assert.Equal(["9"], TextRows(store, "SELECT COUNT(*) FROM candidate_register WHERE evaluator = 'improving-400';"));
    }

    // Each frozen setting as its registration states it and as the rule reads it back, each dial on both sides: the S&P
    // 400's buys a filing at 7 of 9 and not 6, above the filing before's and not level, with a flag the night before and not
    // without, its sales slower or faster alike; the S&P 600's at 9 and not 8 with the growth faster and not level; the S&P
    // 500's at 8 with no flag asked; each held at its floor and not one under. And at every setting of the grid the rule's
    // answer buys exactly where the grid's own walk buys, the starting setting's being the night's own answer.
    [Fact]
    public void TheFrozenSettingsAreReadOnBothSidesOfEachDialTheirRegistrationsState()
    {
        var mid = ImprovingCandidate.SettingsOf(VerbArguments.Parameters(Arguments(ImprovingRemedyLines()[3])[6]));
        var small = ImprovingCandidate.SettingsOf(VerbArguments.Parameters(Arguments(ImprovingRemedyLines()[5])[6]));
        var large = ImprovingCandidate.SettingsOf(VerbArguments.Parameters(Arguments(ImprovingRemedyLines()[1])[6]));

        Assert.Equal((MidImprovingFrozen, SmallImprovingFrozen, ImprovingGrid.Starting), (mid, small, large));
        Assert.All(ImprovingGrid.Settings, setting => Assert.Equal(setting, ImprovingCandidate.SettingsOf(ImprovingCandidate.ParametersOf(setting))));

        Assert.True(ImprovingRule.Answer(7, 9, 6, 9, -0.01, true, mid).Bought);
        Assert.Equal(ImprovingRule.UnderTheBar, ImprovingRule.Answer(6, 9, 5, 9, -0.01, true, mid).Reason);
        Assert.Equal(ImprovingRule.NotHigher, ImprovingRule.Answer(7, 9, 7, 9, 0.02, true, mid).Reason);
        Assert.Equal(ImprovingRule.NoFlagBefore, ImprovingRule.Answer(7, 9, 6, 9, 0.02, false, mid).Reason);
        Assert.True(ImprovingRule.Answer(7, 9, 6, 9, null, true, mid).Bought);
        Assert.True(ImprovingRule.Holds(7, 9, mid));
        Assert.False(ImprovingRule.Holds(6, 9, mid));

        Assert.True(ImprovingRule.Answer(9, 9, 8, 9, 0.01, true, small).Bought);
        Assert.Equal(ImprovingRule.UnderTheBar, ImprovingRule.Answer(8, 9, 7, 9, 0.01, true, small).Reason);
        Assert.Equal(ImprovingRule.NotFaster, ImprovingRule.Answer(9, 9, 8, 9, 0.0, true, small).Reason);
        Assert.Equal(ImprovingRule.GrowthNotRead, ImprovingRule.Answer(9, 9, 8, 9, null, true, small).Reason);
        Assert.True(ImprovingRule.Holds(6, 9, small));
        Assert.False(ImprovingRule.Holds(5, 9, small));

        Assert.True(ImprovingRule.Answer(8, 9, 7, 9, 0.01, false, large).Bought);
        Assert.Equal(ImprovingRule.UnderTheBar, ImprovingRule.Answer(7, 9, 6, 9, 0.01, false, large).Reason);
        Assert.Equal(ImprovingRule.NoWord, ImprovingRule.Answer(4, 4, 3, 4, 0.01, false, large).Reason);

        foreach (var setting in ImprovingGrid.Settings)
        {
            foreach (var (passed, before, change, flagged) in new (int, int, double?, bool)[] { (9, 8, 0.01, true), (8, 8, 0.01, true), (8, 7, 0.0, true), (8, 7, null, false), (7, 6, 0.02, false), (7, 6, 0.02, true), (6, 5, 0.02, true) })
            {
                var answer = ImprovingRule.Answer(passed, 9, before, 9, change, flagged, setting);

                Assert.Equal(ImprovingGrid.Buys(passed, 9, before, 9, change, flagged, setting), answer.Bought);

                if (setting == ImprovingGrid.Starting)
                {
                    Assert.Equal(ImprovingRule.Answer(passed, 9, before, 9, change, ImprovingRule.Starting), answer);
                }
            }
        }
    }

    // The night keeps the live rule and each version under its own name: frozen on the S&P 400 at the starting setting over
    // the improving store, the first night's buys under each rule are the ones the expectation derives by hand, a flag
    // asked first buying none on a first night, the growth not asked buying I02 whose sales slowed, the bar at 7 buying I03
    // and the bar at 9 none; the S&P 500 with no freeze keeps its provisional rule; and a rule whose evaluator moved is not
    // kept, its night's row naming why.
    [Fact]
    public async Task TheNightKeepsTheLiveFundamentalsRuleAndEachVersionUnderItsOwnNameAndARuleWhoseCodeMovedNotAtAll()
    {
        using var store = ImprovingStore();
        var frozenAt = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);

        var (code, said) = await RegisterVerbAt(store, frozenAt, RegisterVerb.IndexFamily, ImprovingRule.Name, "--index", "MID", "--parameters", "buy=8,flag=0,rise=1,faster=1,hold=7,limit=126");

        Assert.True(code == 0, said);

        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) VALUES " +
            "(100, 'the live S&P 600 Fundamentals rule at a moved version', 'the rule', 'the test', 'improving-600', '{\"buy\": 8, \"faster\": 1, \"flag\": 0, \"hold\": 7, \"limit\": 126, \"rise\": 1}', '0123456789ab', 'registered', NULL, '2026-10-25T12:00:00Z', NULL);");

        await new ImprovingFamily(QualityClock(ImprovingFirst), store.DatabaseFile).RunAsync("improving-freeze-1");

        string Bought(string rule) => string.Join(",", FamilyRows(store, $"SELECT ticker FROM improving_trade WHERE index_code = 'MID' AND rule = '{rule.Replace("'", "''", StringComparison.Ordinal)}' AND chosen_on = '{ImprovingFirst}' ORDER BY ticker;"));

        var live = ImprovingLive("S&P 400", ImprovingGrid.Starting);
        var expected = Expected("improving-freeze").GetProperty("firstNight");
        var byKey = new Dictionary<string, ImprovingSweepSetting>(StringComparer.Ordinal)
        {
            ["buy7"] = ImprovingGrid.Starting with { BuyInNine = 7 },
            ["buy9"] = ImprovingGrid.Starting with { BuyInNine = 9 },
            ["flagFirst"] = ImprovingGrid.Starting with { FlagFirst = true },
            ["riseOff"] = ImprovingGrid.Starting with { Rise = false },
            ["fasterOff"] = ImprovingGrid.Starting with { Faster = false },
            ["hold6"] = ImprovingGrid.Starting with { HoldInNine = 6 },
            ["limit63"] = ImprovingGrid.Starting with { Sessions = 63 },
            ["limit252"] = ImprovingGrid.Starting with { Sessions = 252 },
        };

        Assert.Equal("I01,I08", Bought(live));
        Assert.Equal("I01,I03,I08", Bought(ImprovingVersion("S&P 400", byKey["buy7"])));
        Assert.Equal("I01,I02,I08", Bought(ImprovingVersion("S&P 400", byKey["fasterOff"])));
        Assert.Equal(string.Empty, Bought(ImprovingVersion("S&P 400", byKey["flagFirst"])));
        Assert.Equal(string.Empty, Bought(ImprovingVersion("S&P 400", byKey["buy9"])));

        // The counts the expectations file derives by hand, rule by rule.
        Assert.Equal(expected.GetProperty("live").GetInt32(), Bought(live).Split(',', StringSplitOptions.RemoveEmptyEntries).Length);

        foreach (var (key, setting) in byKey)
        {
            Assert.Equal(expected.GetProperty(key).GetInt32(), Bought(ImprovingVersion("S&P 400", setting)).Split(',', StringSplitOptions.RemoveEmptyEntries).Length);
        }

        Assert.Equal(
            new[] { live }.Concat(byKey.Values.Select(setting => ImprovingVersion("S&P 400", setting))).Order(StringComparer.Ordinal),
            FamilyRows(store, $"SELECT rule FROM improving_night WHERE index_code = 'MID' AND session_date = '{ImprovingFirst}';").Order(StringComparer.Ordinal));
        Assert.Empty(FamilyRows(store, $"SELECT rule FROM improving_night WHERE index_code = 'MID' AND rule = '{ImprovingFamily.ProvisionalRule}';"));
        Assert.Equal(["provisional|null"], FamilyRows(store, $"SELECT rule, fault FROM improving_night WHERE index_code = 'GSPC' AND session_date = '{ImprovingFirst}';"));
        Assert.Equal([$"the live S&P 600 Fundamentals rule at a moved version|{ImprovingFamily.Moved}"], FamilyRows(store, $"SELECT rule, fault FROM improving_night WHERE index_code = 'SML' AND session_date = '{ImprovingFirst}';"));

        // Each night's row states its own rule's dials, and the provisional rule's words are the family's starting rule's
        // where a registered rule's are its grid setting's.
        Assert.Equal(["7"], FamilyRows(store, $"SELECT json_extract(settings, '$.buy') FROM improving_night WHERE rule = '{ImprovingVersion("S&P 400", byKey["buy7"])}';"));
        Assert.Equal(["1"], FamilyRows(store, $"SELECT json_extract(settings, '$.flag') FROM improving_night WHERE rule = '{ImprovingVersion("S&P 400", byKey["flagFirst"])}';"));
        Assert.Equal([ImprovingRule.Starting.Words], FamilyRows(store, "SELECT json_extract(settings, '$.words') FROM improving_night WHERE index_code = 'GSPC';"));
        Assert.Equal([ImprovingGrid.Starting.Words], FamilyRows(store, $"SELECT json_extract(settings, '$.words') FROM improving_night WHERE rule = '{live}';"));
        Assert.Equal([ImprovingRule.NoFlagBefore], FamilyRows(store, $"SELECT DISTINCT reason FROM improving_result WHERE rule = '{ImprovingVersion("S&P 400", byKey["flagFirst"])}' AND ticker IN ('I01', 'I08');"));
    }

    // The monthly run reads the family on an index only once a rule of it stands there when the run begins, and its per-rule
    // test walks each registered rule's book at its own setting, the live rule first.
    [Fact]
    public async Task TheMonthlyRunReadsTheFundamentalsFamilyFromItsNextRunAfterTheFreeze()
    {
        using var store = OperatorShapedRegisterBeforeTheImprovingFreeze();
        var before = await new CandidateRegistrar(FixedClock.At(ImprovingFrozenAt, SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();

        Assert.All(new[] { "GSPC", "MID", "SML" }, index => Assert.False(WalkForwardTester.ReadsImproving(ShadowColumn.StandingAt(before, ImprovingFrozenAt), index)));

        var (code, said) = await RegisterVerbAt(store, ImprovingFrozenAt, RegisterVerb.IndexFamily, ImprovingRule.Name, "--index", "MID", "--parameters", "buy=7,flag=1,rise=1,faster=0,hold=7,limit=63");

        Assert.True(code == 0, said);

        var after = ShadowColumn.StandingAt(await new CandidateRegistrar(FixedClock.At(ImprovingFrozenAt.AddMinutes(1), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync(), ImprovingFrozenAt.AddMinutes(1));

        Assert.True(WalkForwardTester.ReadsImproving(after, "MID"));
        Assert.False(WalkForwardTester.ReadsImproving(after, "GSPC"));
        Assert.False(WalkForwardTester.ReadsImproving(after, "SML"));

        var walked = new List<ImprovingSweepSetting>();
        var rules = RuleTests.Improving(after, "MID", setting => { walked.Add(setting); return [(1, 0.01), (2, null)]; }).ToArray();

        Assert.Equal(9, rules.Length);
        Assert.Equal(ImprovingLive("S&P 400", MidImprovingFrozen), rules[0].Rule);
        Assert.Equal(MidImprovingFrozen, walked[0]);
        Assert.All(rules, rule => Assert.Equal(ImprovingRule.Name, rule.Family));
        Assert.Equal([MidImprovingFrozen, .. ImprovingNeighbours(MidImprovingFrozen)], walked);
        Assert.Empty(RuleTests.Improving(after, "SML", _ => []));
    }

    // From the answers of 2026-10-28 a Fundamentals history test's figures are recorded as a quality test's are: the proposal
    // its run states, or the starting setting as ruled by its key, beside the run's own tester's verdict; an entry is
    // refused, which the family reads none of.
    [Fact]
    public async Task TheAnswerCommandRecordsAFundamentalsHistoryTestsProposalAndItsStartingSettingAsRuled()
    {
        using var store = new TemporaryStore().Migrated();
        using var sweeps = new TemporaryDirectory();
        var said = new StringWriter();
        const string run = "20261027T182542Z";
        var proposal = MidImprovingFrozen.Key;
        var figures = new
        {
            index = "MID",
            family = ImprovingRule.Name,
            luck = 20.8125,
            proposal,
            settings = new object[]
            {
                new { Key = ImprovingGrid.Starting.Key, meetsFloors = false, Figures = new { Trades = 357, Edge = 0.0058, YearEdge = new double?[] { 0.128, -0.048, 0.016, 0.002, -0.037, 0.018, -0.054, 0.079 }, EdgeWithoutLargest = -0.0127 } },
                new { Key = proposal, meetsFloors = true, Figures = new { Trades = 517, Edge = 0.0118, YearEdge = new double?[] { 0.021, -0.009, 0.030, 0.025, 0.021, -0.005, -0.011, 0.011 }, EdgeWithoutLargest = 0.0035 } },
            },
            tested = new { verdict = new { Adjusted = 0.69 }, passed = false },
        };

        Directory.CreateDirectory(Path.Combine(sweeps.Path, run));
        File.WriteAllText(Path.Combine(sweeps.Path, run, IndexSweepRunner.FiguresFile), JsonSerializer.Serialize(figures));
        File.WriteAllText(Path.Combine(sweeps.Path, run, SweepAnswer.File), new SweepAnswer("MID", ImprovingRule.Name, null, true).Json());

        const string Columns = "SELECT setting || '|' || trades || '|' || edge || '|' || without_largest || '|' || settings_read || '|' || meeting || '|' || luck || '|' || tester_run || '|' || adjusted || '|' || tester_passed || '|' || COALESCE(ruled_on, '-') FROM sweep_answer ORDER BY recorded_at;";

        var command = new SweepAnswers(new SweepClock(ImprovingFrozenAt), store.DatabaseFile, sweeps.Path, said);

        Assert.Equal(0, await command.RecordAsync(run));
        Assert.Equal([$"{proposal}|517|0.0118|0.0035|2|1|20.8125|{run}|0.69|0|-"], TextRows(store, Columns));

        command = new SweepAnswers(new SweepClock(ImprovingFrozenAt.AddMinutes(1)), store.DatabaseFile, sweeps.Path, said);

        // As ruled, in the run's one row: the starting setting's figures and the day of the ruling.
        Assert.Equal(0, await command.RecordAsync(run, ruled: new SweepAnswers.RuledSetting(new DateOnly(2026, 10, 28), ImprovingGrid.Starting.Key, null)));
        Assert.Equal([$"{ImprovingGrid.Starting.Key}|357|0.0058|-0.0127|2|1|20.8125|{run}|0.69|0|2026-10-28"], TextRows(store, Columns));
        Assert.Equal(2, await command.RecordAsync(run, ruled: new SweepAnswers.RuledSetting(new DateOnly(2026, 10, 28), ImprovingGrid.Starting.Key, "an entry")));
        Assert.Contains($"run {run} is a Fundamentals history test, which reads no entry", said.ToString(), StringComparison.Ordinal);
    }

    static readonly string[] ImprovingFreezeWrites =
    [
        "src/EquityBrief.Worker/Improving/ImprovingRules.cs",
        "src/EquityBrief.Worker/Candidates/RegisterVerb.cs",
        "src/EquityBrief.Worker/Sweep/SweepAnswers.cs",
        "src/EquityBrief.Worker/Loop/RuleTests.cs",
        "src/EquityBrief.Worker/Loop/WalkForwardTester.cs",
        "src/EquityBrief.Worker/Nightly.cs",
        "src/EquityBrief.Api/Reading/ReadApi.Improving.cs",
        "src/EquityBrief.Api/Reading/ReadApi.Paper.cs",
        "src/EquityBrief.Api/Reading/PaperScreen.cs",
        "src/EquityBrief.Api/Reading/RuleScreen.cs",
        "src/EquityBrief.Api/Reading/TonightScreen.Improving.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Improving.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Quality.cs",
        "src/EquityBrief.Web/App/SinglePageApp.Paper.cs",
        "src/EquityBrief.Web/App/SinglePageApp.Tonight.cs",
        "src/EquityBrief.Core/Cards/RuleChips.cs",
    ];

    static readonly string[] ImprovingRuleWrites =
    [
        "src/EquityBrief.Core/Candidates/ImprovingCandidate.cs",
        "src/EquityBrief.Core/Families/ImprovingRule.cs",
        "src/EquityBrief.Core/Families/ImprovingGrid.cs",
        "src/EquityBrief.Worker/Improving/ImprovingFamily.cs",
    ];

    // No source a rule registered before the freeze pins is one 22.15 writes, so no other rule's version moves, and every
    // evaluator the operator's register names stands at the version it names; the Fundamentals rules pin their own files
    // and none of the others 22.15 writes.
    [Fact]
    public void NoSourceAnotherRulesVersionPinsIsOne22Point15WritesAndEveryOtherVersionStands()
    {
        var pinned = CandidateEvaluators.All
            .Where(evaluator => evaluator is not ImprovingCandidate)
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(Core.Loop.RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var improving = CandidateEvaluators.All.OfType<ImprovingCandidate>().SelectMany(evaluator => evaluator.OwnSources).Distinct(StringComparer.Ordinal).ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(ImprovingFreezeWrites.Concat(ImprovingRuleWrites), path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(ImprovingFreezeWrites.Concat(ImprovingRuleWrites).Intersect(pinned, StringComparer.Ordinal));
        Assert.Empty(ImprovingFreezeWrites.Intersect(improving, StringComparer.Ordinal));
        Assert.Superset(ImprovingRuleWrites.Where(path => !path.EndsWith("/ImprovingCandidate.cs", StringComparison.Ordinal)).ToHashSet(StringComparer.Ordinal), improving.ToHashSet(StringComparer.Ordinal));
        // From 21.4 every version the register named but the swing filter's and the S&P 500 heavyweights' moves, in the
        // one checkpoint that registers every rule reading the moved sources again; those two stand as the register named
        // them and every other differs from it.
        string[] standingThroughTheMove = ["swing-filter", "heavyweight"];

        Assert.All(OperatorsStandingBeforeTheImprovingFreeze.Where(row => standingThroughTheMove.Contains(row.Evaluator, StringComparer.Ordinal)), row => Assert.Equal(row.Version, CandidateEvaluators.Find(row.Evaluator)!.Version));
        Assert.All(OperatorsStandingBeforeTheImprovingFreeze.Where(row => !standingThroughTheMove.Contains(row.Evaluator, StringComparer.Ordinal)), row => Assert.NotEqual(row.Version, CandidateEvaluators.Find(row.Evaluator)!.Version));
        Assert.Equal(18, OperatorsStandingBeforeTheImprovingFreeze.Length);
    }
}
