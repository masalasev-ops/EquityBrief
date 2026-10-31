using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Fills;
using EquityBrief.Worker.Improving;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.17: every swing trade judged against a benchmark whose members are sold as the trade is, each
// member's sale worked by hand in the core, the ledger's benchmark, the monthly tests' walk and the fill step, a member
// whose sale's open is not held keeping the benchmark waiting; no source a rule's version pins among those 21.17 writes; a
// rule or a proposal reading a release reading judged on the test years from the local model's cutoff's year alone and
// one reading none as before, worked by hand; and the Fundamentals family's live rule on the S&P 400 and 600 registered
// again without its flag by the remedy's own lines over a register shaped as the operator's, the S&P 500's refused.
// see: Every swing trade is judged against a benchmark whose members are sold as the trade is, and no rule's version moves because of it
// see: A rule or a change reading a release reading is judged on the test years from the model's cutoff's year alone, the earlier years printed beside it
// see: The Fundamentals family's live rule on the S&P 400 and 600 asks no flag, the flagged rule standing beside it as a version
public partial class FixtureExpectations
{
    const string WithoutFlagRemedy = "2026-11-01-21.17-the-three-findings.txt";

    // Three members of one plan bought at 100's close, a stop one typical move of 5 under it at 95 and a target two risks
    // over it at 110, given three sessions; closes and opens oldest first, the listing at bar 0. The stop's member closes
    // through 95 at 94 on bar 2 and opens at 92 on bar 3; the target's closes at 111 on bar 2 and opens at 114 on bar 3; the
    // cap's reaches neither in three sessions.
    static readonly double[] StopCloses = [100, 98, 94, 93];
    static readonly double[] StopOpens = [100, 99, 96, 92];
    static readonly double[] TargetCloses = [100, 104, 111, 112];
    static readonly double[] TargetOpens = [100, 101, 110, 114];
    static readonly double[] CapCloses = [100, 101, 102, 103, 104];
    static readonly double[] CapOpens = [100, 100, 101, 102, 103];

    static double? AfterOf(double[] closes, double[] opens, int at) => at + 1 < closes.Length && at + 1 < opens.Length && opens[at + 1] > 0 ? opens[at + 1] : null;

    [Fact]
    public void EachMembersSaleMovesItsResultByItsOwnGapAndTheCapsByNothingWorkedByHand()
    {
        var anchor = new SetupAnchor(DateOnly.MinValue, 100, 95, 110, null, 3, 1);
        var stop = SetupReplay.Replay(StopCloses, 0, anchor);
        var target = SetupReplay.Replay(TargetCloses, 0, anchor);
        var cap = SetupReplay.Replay(CapCloses, 0, anchor);

        // The stop's -1.2 at its close moved by (92 - 94) / 5 = -0.4; the target's 2.2 by (114 - 111) / 5 = 0.6; the cap's
        // close at 103, 0.6, by nothing.
        Assert.Equal((SetupEnds.Stop, -1.2, SetupEnds.Target, 2.2, SetupEnds.Cap, 0.6), (stop.End, Math.Round(stop.Result!.Value, 12), target.End, Math.Round(target.Result!.Value, 12), cap.End, Math.Round(cap.Result!.Value, 12)));

        var stopMove = ExitFills.SaleMove(stop, at => StopCloses[at], at => AfterOf(StopCloses, StopOpens, at), 0, 5);
        var targetMove = ExitFills.SaleMove(target, at => TargetCloses[at], at => AfterOf(TargetCloses, TargetOpens, at), 0, 5);
        var capMove = ExitFills.SaleMove(cap, at => CapCloses[at], at => AfterOf(CapCloses, CapOpens, at), 0, 5);

        Assert.Equal((-0.4, 0.6, 0.0), (Math.Round(stopMove!.Value, 12), Math.Round(targetMove!.Value, 12), capMove!.Value));

        // A stop on the newest close held waits on its open; a path still open moves nothing and is not counted.
        double[] newest = [100, 98, 94];
        var waiting = SetupReplay.Replay(newest, 0, anchor);

        Assert.Equal(SetupEnds.Stop, waiting.End);
        Assert.Null(ExitFills.SaleMove(waiting, at => newest[at], at => AfterOf(newest, StopOpens, at), 0, 5));
        Assert.Null(ExitFills.SaleMove(new SetupOutcome(null, 1, SetupEnds.Open), at => newest[at], _ => 1, 0, 5));

        // The mean over the members whose path ended with their sale held, (-0.4 + 0.6 + 0) / 3, the waiting one counted
        // apart and the open one not at all.
        var (mean, members, held) = ExitFills.SaleMoves([(stop, stopMove), (target, targetMove), (cap, capMove), (waiting, null), (new SetupOutcome(null, 1, SetupEnds.Open), null)]);

        Assert.Equal((0.066667, 3, 1), (Math.Round(mean, 6), members, held));
        Assert.Equal((0.0, 0, 0), ExitFills.SaleMoves([]));
    }

    [Fact]
    public void TheLedgersBenchmarkSellsItsMembersAsTheSetupIsAndWaitsOnAMembersSale()
    {
        var plan = new SetupPlan(1, 2, null, 3);
        var day = new DateOnly(2026, 1, 5);

        (double[], double[], int, double, bool)[] members = [(StopCloses, StopOpens, 0, 5, false), (TargetCloses, TargetOpens, 0, 5, false)];

        // At their closes (-1.2 + 2.2) / 2 = 0.5; sold as the setup is (-1.6 + 2.8) / 2 = 0.6, both settled.
        var (closes, _) = SetupBenchmark.WithMembers(members, day, plan);
        var (sold, listed) = SetupBenchmark.WithMembers(members, day, plan, sold: true);

        Assert.Equal((0.5, 2, true), (Math.Round(closes.Mean!.Value, 12), closes.Ended, closes.Settled));
        Assert.Equal((0.6, 2, true), (Math.Round(sold.Mean!.Value, 12), sold.Ended, sold.Settled));
        Assert.All(listed, member => Assert.True(member.Ended));

        // A third member through its stop on the newest close: at its closes it has ended, sold it waits on its open, so
        // the benchmark is not settled and the member is not bought into the entry's gap.
        double[] newest = [100, 98, 94];
        var (waitingAtCloses, _) = SetupBenchmark.WithMembers([.. members, (newest, StopOpens, 0, 5, false)], day, plan);
        var (waiting, waitingListed) = SetupBenchmark.WithMembers([.. members, (newest, StopOpens, 0, 5, false)], day, plan, sold: true);

        Assert.Equal((3, true), (waitingAtCloses.Ended, waitingAtCloses.Settled));
        Assert.Equal((2, 3, false), (waiting.Ended, waiting.Entered, waiting.Settled));
        Assert.False(waitingListed[^1].Ended);
    }

    // A member's series for the monthly walk: four sessions from 2026-01-05, its typical move 5 on the listing's bar.
    static SweepSeries SoldMember(double[] closes, double[] opens)
    {
        var bars = closes.Select((close, at) => new SweepBar(new DateOnly(2026, 1, 5).AddDays(at), (decimal)close + 1, (decimal)close - 1, (decimal)close, 1_000, (decimal)opens[at])).ToArray();
        var series = SweepSeriesOf(bars);

        series.Atr[0] = 5;

        return series;
    }

    [Fact]
    public void TheMonthlyWalksBenchmarkAddsItsMembersSalesToTheirBuyAtTheOpen()
    {
        SweepSeries[] series = [SoldMember(StopCloses, StopOpens), SoldMember(TargetCloses, TargetOpens), SoldMember(CapCloses[..4], CapOpens[..4])];
        var members = new SweepBenchmark.Members([[0, 1, 2]], [[0, 0, 0]]);
        var fills = new OpenFills(series, members);

        // Under the plan's own exit the stop's -0.4, the target's 0.6 and the cap's nothing: 0.2 / 3.
        Assert.Equal(0.066667, Math.Round(fills.Sale(0, 1, 2, null, 3, null), 6));

        // Bought at the next open the members' gaps over their own moves, (99 - 100) / 5, (101 - 100) / 5 and
        // (100 - 100) / 5, average nothing, so a listing's benchmark at the close of 0.5 is 0.5 bought and 0.5667 sold.
        var listing = new FamilyListing(0, 0, 0, 2, 0, 100, 95, 110, double.NaN, 3, 5);

        Assert.Equal(0.5, Math.Round(fills.Benchmark(0.5, 0, 1), 12));
        Assert.Equal(0.566667, Math.Round(fills.Benchmark(0.5, listing), 6));
        Assert.Equal(0.5, Math.Round(fills.BenchmarkBought(0.5, listing), 12));

        // The pullback's walk over the same members, held to ten sessions with no move of the stop: the cap's member runs
        // out of closes before its hold and is not counted, so (-0.4 + 0.6) / 2.
        Assert.Equal(0.1, Math.Round(fills.PullbackSale(0, 1, 2, SweepAxes.ExitIndex(10, breakEven: false)), 12));
    }

    [Fact]
    public void TheFillStepsBenchmarkSellsEachMemberAsTheTradeIsAndWaitsOnAMembersSale()
    {
        var night = new DateOnly(2026, 1, 5);
        DateOnly[] Sessions(int count) => [.. Enumerable.Range(0, count).Select(at => night.AddDays(at))];
        var trade = new TradeFiller.StoredTrade("index_rule_trade", "MID", "the rule", "AAA", night, 100, 95, 110, 1.0, 0.4, 0.5, null) { Cap = 3 };
        var read = new TradeFiller.FillPrices(
            new Dictionary<(string, DateOnly), (double, double)>(),
            new Dictionary<(string, DateOnly), double> { [("AAA", night)] = 5, [("BBB", night)] = 5, [("CCC", night)] = 5 },
            [("MID", "AAA", null, null), ("MID", "BBB", null, null), ("MID", "CCC", null, null), ("SML", "DDD", null, null)],
            new Dictionary<DateOnly, DateOnly?>());
        var paths = new Dictionary<string, TradeFiller.MemberPath>(StringComparer.Ordinal)
        {
            ["AAA"] = new(Sessions(4), StopOpens, StopCloses),
            ["BBB"] = new(Sessions(4), TargetOpens, TargetCloses),
            ["CCC"] = new(Sessions(4), CapOpens[..4], CapCloses[..4]),
        };

        // The three members of the S&P 400 that night, the S&P 600's left out: (-0.4 + 0.6 + 0) / 3.
        Assert.Equal(0.066667, Math.Round(TradeFiller.BenchmarkSale(trade, read, paths)!.Value, 6));

        // The target's member's open after its close not stored yet: the benchmark waits.
        paths["BBB"] = new(Sessions(3), TargetOpens[..3], TargetCloses[..3]);

        Assert.Null(TradeFiller.BenchmarkSale(trade, read, paths));

        // A book's holding is sold at its close and moves nothing.
        Assert.Equal(0.0, TradeFiller.BenchmarkSale(trade with { Cut = ["AAA"] }, read, paths));
    }

    // The sources 21.17 writes, none of which a rule's version pins.
    static readonly string[] BenchmarkSoldSources =
    [
        "src/EquityBrief.Core/Ledger/ExitFills.cs",
        "src/EquityBrief.Core/Ledger/SetupBenchmark.cs",
        "src/EquityBrief.Core/Loop/ReleaseJudging.cs",
        "src/EquityBrief.Core/Loop/LoopDecisions.cs",
        "src/EquityBrief.Core/Loop/LoopJudge.cs",
        "src/EquityBrief.Worker/Fills/TradeFiller.cs",
        "src/EquityBrief.Worker/Loop/OpenFills.cs",
        "src/EquityBrief.Worker/Loop/ExitProcedures.cs",
        "src/EquityBrief.Worker/Loop/RuleWalk.cs",
        "src/EquityBrief.Worker/Loop/RuleTests.cs",
        "src/EquityBrief.Worker/Loop/WalkForwardTester.cs",
        "src/EquityBrief.Worker/Loop/MonthlyRun.cs",
        "src/EquityBrief.Worker/Ledger/SetupLedger.cs",
        "src/EquityBrief.Worker/Ledger/SetupLedger.Exits.cs",
        "src/EquityBrief.Worker/Sweep/IndexSweepRunner.Own.cs",
        "src/EquityBrief.Worker/Sweep/IndexSweepRunner.OwnPullback.cs",
        "src/EquityBrief.Worker/Sweep/IndexSweepRunner.Dip.cs",
        "src/EquityBrief.Worker/Improving/ImprovingRules.cs",
        "src/EquityBrief.Worker/Candidates/CandidateRegistrar.cs",
        "src/EquityBrief.Worker/Candidates/RegisterVerb.cs",
    ];

    [Fact]
    public void NoSourceARulesVersionPinsIsOne21Point17Writes()
    {
        var pinned = CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(BenchmarkSoldSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(BenchmarkSoldSources.Intersect(pinned, StringComparer.Ordinal));
        Assert.NotEmpty(new[] { "src/EquityBrief.Core/Ledger/TradeFills.cs", "src/EquityBrief.Worker/Sweep/SweepBenchmark.cs", "src/EquityBrief.Core/Loop/ExitMenu.cs" }.Intersect(pinned, StringComparer.Ordinal));

        // Nor any the ledger's readings version pins, whose declaration a rule's pinned catalogue carries: the release
        // readings' own sources among them, which the words a flag first reads are kept apart from.
        string[] readings = [.. LedgerSources.Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'))];

        Assert.Contains("src/EquityBrief.Core/Ledger/LedgerReleases.cs", readings);
        Assert.Empty(BenchmarkSoldSources.Intersect(readings, StringComparer.Ordinal));
    }

    // Weekdays from 2022 to the end of June 2026, a trade entered every fifth: the proposal a whole risk better than the rule
    // in every year before 2025 and half a risk worse from it.
    static (LoopRead Read, LoopEvidence Evidence) CutoffEvidence()
    {
        DateOnly[] calendar = [.. Enumerable.Range(0, 1700).Select(at => new DateOnly(2022, 1, 3).AddDays(at)).Where(day => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && day <= new DateOnly(2026, 6, 30))];
        var folds = LoopFolds.Of(calendar, calendar[^1]);
        var read = new LoopRead("MID", null!, [], [], null!, null!, new Dictionary<string, IReadOnlyList<Core.Readings.FiledIncome>>(), calendar, 0, folds);
        (int, double?)[] entries = [.. Enumerable.Range(0, calendar.Length).Where(at => at % 5 == 0).Select(at => (at, (double?)(calendar[at].Year < 2025 ? 1.0 : -0.5)))];
        (int, double?)[] current = [.. entries.Select(entry => (entry.Item1, (double?)0.0))];

        return (read, LoopProcedures.Evidence(folds, calendar, [.. folds.Select(_ => (IReadOnlyList<(int, double?)>)entries)], current));
    }

    [Fact]
    public void ARuleOrAProposalReadingAReleaseReadingIsJudgedOnTheYearsFromTheCutoffsYearAloneWorkedByHand()
    {
        var (read, evidence) = CutoffEvidence();
        var fromCutoff = evidence.Units.Count(unit => read.Calendar[unit.Session].Year >= 2025 && unit.Proposed is not null);
        var all = evidence.Units.Count(unit => unit.Proposed is not null);

        // Trimmed to 2025: only its units entered from that year, every one half a risk worse, and its two test years.
        var trimmed = LoopJudge.From(evidence, read.Calendar, 2025);

        Assert.True(fromCutoff > 0 && fromCutoff < all, $"{fromCutoff} of {all}");
        Assert.Equal((fromCutoff, fromCutoff), (trimmed.ProposedResults.Count, trimmed.CurrentResults.Count));
        Assert.Equal([2025, 2026], trimmed.Years.Select(year => year.Year));
        Assert.All(trimmed.ProposedResults, result => Assert.Equal(-0.5, result));

        // Through the tester: the proposal whose change asks a flag first is judged on its units from 2025 alone and reads
        // worse; the same evidence read by a change naming no release reading is judged over every year and reads better.
        LoopProposalRead Proposal(string family, string words) => new(family, "a change", words, "the rule", "risks", 63, [], 0, evidence);

        var tested = WalkForwardTester.Judge(read, [Proposal("improving", "the buy asked " + ReleaseJudging.FlagWords), Proposal("quality", "the buy at 8 in 9")], new DateOnly(2025, 1, 31));

        Assert.Equal((fromCutoff, all), (tested[0].Verdict.Units, tested[1].Verdict.Units));
        Assert.True(tested[0].Verdict.Trimmed < 0 && tested[1].Verdict.Trimmed > 0, $"{tested[0].Verdict.Trimmed}, {tested[1].Verdict.Trimmed}");

        // With no reading version's cutoff the release reading's proposal is judged over every year, as before.
        Assert.Equal(all, WalkForwardTester.Judge(read, [Proposal("improving", "the buy asked " + ReleaseJudging.FlagWords)])[0].Verdict.Units);

        // Through the per-rule test: a Fundamentals version asking a flag first against its live rule asking none is judged
        // from the cutoff's year, the live rule over every year, and the evidence kept for its printed years is every year's.
        var months = Enumerable.Range(0, read.Calendar.Length).Where(at => at % 21 == 0).ToArray();
        var live = new RuleUnderTest("improving", "the live rule", "live", "a newly read filing lifting its score to at least 7 in 9", LoopProcedures.Points, 63, Months: [.. months.Select(at => (at, (double?)0.0))]);
        var version = new RuleUnderTest("improving", "the version", RuleTests.Variant, "a newly read filing lifting its score to at least 7 in 9, " + ReleaseJudging.FlagWords, LoopProcedures.Points, 63, Months: [.. months.Select(at => (at, (double?)(read.Calendar[at].Year < 2025 ? 0.02 : -0.01)))]);
        var rules = RuleTests.Judge(read, [live, version], new DateOnly(2025, 1, 31));

        Assert.Equal((null as DateOnly?, new DateOnly(2025, 1, 31)), (RuleTests.CutoffOf(live, new DateOnly(2025, 1, 31)), RuleTests.CutoffOf(version, new DateOnly(2025, 1, 31))));
        Assert.Equal(months.Count(at => read.Calendar[at].Year >= 2025), rules[1].Verdict!.Units);
        Assert.Contains(rules[1].Evidence!.Years, year => year.Year < 2025);
        Assert.True(rules[1].Verdict!.Trimmed < 0, $"{rules[1].Verdict!.Trimmed}");
    }

    [Fact]
    public void TheMonthsReportJudgesTheCutoffsSideFirstAndLabelsTheYearsBeforeIt()
    {
        var sides = LedgerReleases.Split([(2023, 10, 2.0, 1.0), (2024, 20, 1.5, 1.0), (2025, 15, 0.5, 1.0)], new DateOnly(2025, 1, 31));

        Assert.Equal(
            "Judged on the test years from the local model's cutoff of 2025-01-31 alone: 2025, -0.500 risks over 15 units; beside it, the years before, read by a model trained after them: 2023 to 2024, +1.500 risks over 30 units.",
            MonthlyRun.CutoffLine(sides, new DateOnly(2025, 1, 31), "risks"));
    }

    static string[] WithoutFlagRemedyLines() =>
        [.. File.ReadAllLines(Path.Combine(Repository.Root, "tools", "remedies", WithoutFlagRemedy)).Where(line => line.Length > 0 && !line.StartsWith('#'))];

    [Fact]
    public async Task TheRemedysLinesRegisterTheFundamentalsLiveRuleAgainWithoutItsFlagOnTheSandP400And600()
    {
        using var store = OperatorShapedRegisterBeforeTheImprovingFreeze();
        var freeze = ImprovingRemedyLines();

        foreach (var (line, at) in new[] { (freeze[1], 0), (freeze[3], 1), (freeze[5], 2) })
        {
            var (code, said) = await RegisterVerbAt(store, ImprovingFrozenAt.AddMinutes(at), Arguments(line)[1..]);

            Assert.True(code == 0, said);
        }

        // The remedy's lines: each index's answer recorded at its live setting without the flag from its own run, then the
        // live rule registered again there.
        var lines = WithoutFlagRemedyLines();

        Assert.Equal(["sweep-answer", "--run", "20261027T182542Z", "--ruled", "2026-10-31", "--setting", (MidImprovingFrozen with { FlagFirst = false }).Key], Arguments(lines[0]));
        Assert.Equal(["register", RegisterVerb.WithoutFlag, "MID"], Arguments(lines[1])[..3]);
        Assert.Equal(["sweep-answer", "--run", "20261027T182701Z", "--ruled", "2026-10-31", "--setting", (SmallImprovingFrozen with { FlagFirst = false }).Key], Arguments(lines[2]));
        Assert.Equal(["register", RegisterVerb.WithoutFlag, "SML"], Arguments(lines[3])[..3]);

        var after = ImprovingFrozenAt.AddDays(3);

        foreach (var (line, at) in new[] { (lines[1], 0), (lines[3], 1) })
        {
            var (code, said) = await RegisterVerbAt(store, after.AddMinutes(at), Arguments(line)[1..]);

            Assert.True(code == 0, said);
            Assert.Contains("retired 2 and registered 2 at one instant, family of 9 of 9", said, StringComparison.Ordinal);
        }

        // Each family still nine, none retired from it: its live rule at the frozen setting without its flag, the flagged
        // setting a version beside it, the other seven as they stood.
        var rows = await new CandidateRegistrar(FixedClock.At(after.AddMinutes(5), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();
        var standing = CandidateFamily.Standing(rows, after.AddMinutes(5));

        foreach (var (named, setting) in new[] { ("S&P 400", MidImprovingFrozen), ("S&P 600", SmallImprovingFrozen) })
        {
            var family = CandidateFamily.In(standing, "improving on the " + named);
            var unflagged = setting with { FlagFirst = false };

            Assert.Equal(9, family.Count);
            Assert.Equal(ImprovingLive(named, unflagged), FamilyRecords.LiveOf(family)!.Candidate);
            Assert.Equal(unflagged, ImprovingCandidate.SettingsOf(CandidateEvaluator.Read(FamilyRecords.LiveOf(family)!.Parameters)));
            Assert.Contains(family, row => row.Candidate == ImprovingVersion(named, setting) && ImprovingCandidate.SettingsOf(CandidateEvaluator.Read(row.Parameters)) == setting);
            Assert.DoesNotContain(family, row => row.Candidate == ImprovingVersion(named, unflagged) || row.Candidate == ImprovingLive(named, setting));
            Assert.Equal(
                [.. ImprovingNeighbours(setting).Where(one => one != unflagged).Select(one => ImprovingVersion(named, one)).Order(StringComparer.Ordinal)],
                family.Where(row => row.RegisteredAt < after).Select(row => row.Candidate).Order(StringComparer.Ordinal));
        }

        // The S&P 500's live rule asks no flag and stands as the ruling asks, with nothing written; and a second run on the
        // S&P 400 finds its live rule asking none either.
        foreach (var (index, minute) in new[] { ("GSPC", 10), ("MID", 11) })
        {
            var (code, said) = await RegisterVerbAt(store, after.AddMinutes(minute), RegisterVerb.WithoutFlag, index, "--evidence", "again");

            Assert.True(code != 0, said);
            Assert.Contains("asks no flag first", said, StringComparison.Ordinal);
        }

        Assert.Equal(standing.Count, CandidateFamily.Standing(await new CandidateRegistrar(FixedClock.At(after.AddMinutes(20), SessionZones.UnitedStates), store.DatabaseFile).RowsAsync(), after.AddMinutes(20)).Count);
    }
}
