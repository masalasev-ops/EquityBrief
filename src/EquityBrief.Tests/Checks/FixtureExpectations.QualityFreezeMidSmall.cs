using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Quality;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 22.11 on the S&P 400 and 600: the command recording a quality history test's answer records, as
// the operator ruled, its starting setting's figures in place of the proposal the test states, beside the run's own
// tester's verdict, and refuses a setting the grid did not walk and an entry; and the remedy's lines freeze both indices'
// families over a register shaped as the operator's stood after the S&P 500's freeze, each at the starting setting with
// its seven one-step neighbours at one instant, the S&P 500's family and every other rule standing as they stood.
// see: The S&P 400's and 600's quality rules are frozen at their starting settings on the operator's answer, as the S&P 500's was
// see: Each index's quality rule is frozen at its starting setting on the operator's word, with its one-step neighbours as versions
public partial class FixtureExpectations
{
    const string MidSmallQualityRemedy = "2026-10-25-22.11-the-s-and-p-400-and-600-quality-freezes.txt";

    static readonly DateTimeOffset MidSmallFrozenAt = new(2026, 10, 25, 22, 0, 0, TimeSpan.Zero);

    // The S&P 400's history test of 2026-10-25 as its run stated it, three of its 72 settings: the starting setting meeting
    // the floors at 113 holdings and +0.0224 after costs, the proposal two dials from it, the valuation off and sold on a
    // filing, at 83 and +0.0241, and one setting meeting none; luck alone passing 10.40625 of 72; and the tester not passed
    // at an adjusted p of 0.906.
    static object MidQualityRun(string proposal) => new
    {
        index = "MID",
        family = QualityRule.Name,
        luck = 10.40625,
        proposal,
        settings = new object[]
        {
            new { Key = QualityRule.Starting.Key, meetsFloors = true, Figures = new { Trades = 113, Edge = 0.0224, YearEdge = new double?[] { -0.004, -0.098, -0.104, -0.002, 0.052, -0.028, 0.421, -0.061 }, EdgeWithoutLargest = -0.0363 } },
            new { Key = proposal, meetsFloors = true, Figures = new { Trades = 83, Edge = 0.0241, YearEdge = new double?[] { 0.01, -0.05, 0.02, 0.03, 0.04, 0.05, 0.3, -0.02 }, EdgeWithoutLargest = -0.0508 } },
            new { Key = "floor=8|valuation=third|trend=on|book=20|sale=filing", meetsFloors = false, Figures = new { Trades = 40, Edge = -0.01, YearEdge = new double?[] { null, -0.01, -0.02, -0.01, 0.0, -0.03, 0.02, -0.01 }, EdgeWithoutLargest = -0.02 } },
        },
        tested = new { verdict = new { Adjusted = 0.906 }, passed = false },
    };

    [Fact]
    public async Task TheCommandRecordsAQualityTestsStartingSettingAsRuledInPlaceOfTheProposalItStates()
    {
        using var store = new TemporaryStore().Migrated();
        using var sweeps = new TemporaryDirectory();
        var said = new StringWriter();
        var command = new SweepAnswers(new SweepClock(MidSmallFrozenAt), store.DatabaseFile, sweeps.Path, said);
        const string run = "20261025T210000Z";
        const string proposal = "floor=7|valuation=off|trend=off|book=10|sale=filing";

        Directory.CreateDirectory(Path.Combine(sweeps.Path, run));
        File.WriteAllText(Path.Combine(sweeps.Path, run, IndexSweepRunner.FiguresFile), JsonSerializer.Serialize(MidQualityRun(proposal)));
        File.WriteAllText(Path.Combine(sweeps.Path, run, SweepAnswer.File), new SweepAnswer("MID", QualityRule.Name, null, true).Json());

        const string Columns = "SELECT setting || '|' || trades || '|' || edge || '|' || without_largest || '|' || settings_read || '|' || meeting || '|' || luck || '|' || tester_run || '|' || adjusted || '|' || tester_passed || '|' || COALESCE(ruled_on, '-') FROM sweep_answer;";

        // Recorded as the run states it: the proposal's figures, the two dials from the starting setting.
        Assert.Equal(0, await command.RecordAsync(run));
        Assert.Equal([$"{proposal}|83|0.0241|-0.0508|3|2|10.40625|{run}|0.906|0|-"], TextRows(store, Columns));

        // As ruled: the starting setting's figures, its edge each year as the run states it, the run's settings and those
        // meeting the floors against luck and its own tester's verdict beside them, and the day of the ruling.
        var on = new DateOnly(2026, 10, 25);

        command = new SweepAnswers(new SweepClock(MidSmallFrozenAt.AddMinutes(1)), store.DatabaseFile, sweeps.Path, said);

        Assert.Equal(0, await command.RecordAsync(run, ruled: new SweepAnswers.RuledSetting(on, QualityRule.Starting.Key, null)));
        Assert.Equal([$"{QualityRule.Starting.Key}|113|0.0224|-0.0363|3|2|10.40625|{run}|0.906|0|2026-10-25"], TextRows(store, Columns));
        Assert.Equal(
            "{\"2019\":-0.004,\"2020\":-0.098,\"2021\":-0.104,\"2022\":-0.002,\"2023\":0.052,\"2024\":-0.028,\"2025\":0.421,\"2026\":-0.061}",
            Text(store, "SELECT year_edges FROM sweep_answer;"));

        // A setting the grid did not walk and an entry, which the family reads none of, are each refused with nothing written.
        Assert.Equal(2, await command.RecordAsync(run, ruled: new SweepAnswers.RuledSetting(on, "floor=5|valuation=median|trend=off|book=10|sale=reshuffle", null)));
        Assert.Equal(2, await command.RecordAsync(run, ruled: new SweepAnswers.RuledSetting(on, QualityRule.Starting.Key, "the first pullback after the reaction within 10 sessions")));
        Assert.Contains($"'floor=5|valuation=median|trend=off|book=10|sale=reshuffle' is not a setting run {run}'s history test walked", said.ToString(), StringComparison.Ordinal);
        Assert.Contains($"run {run} is a quality history test, which reads no entry", said.ToString(), StringComparison.Ordinal);
        Assert.Equal(["113|2026-10-25"], TextRows(store, "SELECT trades || '|' || ruled_on FROM sweep_answer;"));
    }

    // The remedy's lines as the remedy script reads them: on each index the history test run again, its answer recorded at
    // the starting setting as ruled, and the family frozen at it.
    static string[] MidSmallRemedyLines() =>
        [.. File.ReadAllLines(Path.Combine(Repository.Root, "tools", "remedies", MidSmallQualityRemedy)).Where(line => line.Length > 0 && !line.StartsWith('#'))];

    [Fact]
    public async Task TheRemedysLinesFreezeTheSAndP400And600QualityFamiliesOverARegisterShapedAsTheOperatorsStood()
    {
        using var store = OperatorShapedRegister();
        var lines = MidSmallRemedyLines();

        // Each index's three lines in order, the answer recorded at the starting setting by the key its test names it by.
        Assert.Equal(6, lines.Length);

        foreach (var (index, at) in new[] { ("MID", 0), ("SML", 3) })
        {
            Assert.Equal(["sweep-index", "--index", index, "--family", QualityRule.Name], Arguments(lines[at]));
            Assert.Equal(["sweep-answer", "--newest", QualityRule.Name, "--index", index, "--ruled", "2026-10-25", "--setting", QualityRule.Starting.Key], Arguments(lines[at + 1]));

            var words = Arguments(lines[at + 2]);

            Assert.Equal(["register", RegisterVerb.IndexFamily, QualityRule.Name, "--index", index, "--parameters"], words[..6]);
            Assert.Equal(QualityRule.Starting, QualityCandidate.SettingsOf(VerbArguments.Parameters(words[6])));
        }

        // The S&P 500's freeze as 22.11's remedy wrote it, then the two indices' lines an hour after it.
        var (large, saidLarge) = await RegisterVerbAt(store, QualityFrozenAt, Arguments(Assert.Single(RemedyRegisterLines()))[1..]);

        Assert.True(large == 0, saidLarge);

        foreach (var (line, at) in new[] { (lines[2], 0), (lines[5], 1) })
        {
            var (code, said) = await RegisterVerbAt(store, MidSmallFrozenAt.AddMinutes(at), Arguments(line)[1..]);

            Assert.True(code == 0, said);
            Assert.Contains("registered 8 at one instant", said, StringComparison.Ordinal);
        }

        // Standing after them: the 58 as they stood, the S&P 500's eight, and each index's eight, its live rule at the
        // starting setting and each one-step neighbour a version named for its setting, at the version the code carries
        // and at one instant, the S&P 400's valuation-off neighbour among them.
        var rows = await new CandidateRegistrar(FixedClock.At(MidSmallFrozenAt.AddMinutes(5), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();
        var standing = CandidateFamily.Standing(rows, MidSmallFrozenAt.AddMinutes(5));

        Assert.Equal(58 + 8 + 8 + 8, standing.Count);
        Assert.Equal(8, CandidateFamily.In(standing, "quality on the S&P 500").Count);

        foreach (var (index, named, evaluator) in new[] { ("MID", "S&P 400", "quality-400"), ("SML", "S&P 600", "quality-600") })
        {
            var family = CandidateFamily.In(standing, "quality on the " + named);

            Assert.Equal(
                [FamilyRecords.LivePrefix + named + " quality rule at " + QualityRules.Short(QualityRule.Starting), .. StartingNeighbours.Select(setting => VersionName(named, setting))],
                family.OrderBy(row => row.Id).Select(row => row.Candidate));
            Assert.All(family, row => Assert.Equal((evaluator, CandidateEvaluators.Find(evaluator)!.Version), (row.Evaluator, row.EvaluatorVersion)));
            Assert.Single(family.Select(row => row.RegisteredAt).Distinct());
            Assert.Equal(8, CandidateFamily.Divisor(family, MidSmallFrozenAt.AddDays(1)));
            Assert.Contains(family, row => QualityCandidate.SettingsOf(CandidateEvaluator.Read(row.Parameters)) == (QualityRule.Starting with { Valuation = QualityValuation.Off }));
        }
    }
}
