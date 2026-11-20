using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the long test's correction of 2026-11-19: the long per-rule test walks the pullback over the long
// history's years before 2019 as it walks the breakout and the dip. The S&P 500's swing filter reads its candidates from the
// long history's first session, so a setup of 2010 walks to a trade; the S&P 400's and 600's own search counts a pick of
// those years as a trade where its walk ended it; and Quality at a fair price's and the Fundamentals family's rules, whose
// books the long run lays out none of, are each kept with why rather than left without a row.
// see: The monthly run's last step judges every registered rule over the long history from each family's own first test year, capped at three hours
public partial class FixtureExpectations
{
    // The constructed S&P 500 history's strong and weaker members over the weekdays from 2009-01-05 to 2019-03-01, every one a
    // member from 2005: the shape a pullback sets up in every twenty sessions of, reaching past the sweep's first scored
    // session as the long history does.
    static SweepHistoryInputs LongPullbackInputs()
    {
        var days = new List<DateOnly>();

        for (var day = new DateOnly(2009, 1, 5); day <= new DateOnly(2019, 3, 1); day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                days.Add(day);
            }
        }

        DateOnly[] calendar = [.. days];

        return new SweepHistoryInputs(
            calendar[^1],
            calendar,
            [.. BusinessFirstHistory.Tickers.Select(ticker => new SweepName(ticker, [.. Enumerable.Range(0, calendar.Length).Select(at => BusinessFirstHistory.Bar(calendar, ticker, at))], [(new DateOnly(2005, 1, 3), null)], []))],
            0,
            0,
            0,
            0,
            "constructed");
    }

    [Fact]
    public async Task TheLongTestWalksTheSAndP500sSwingFilterOverASetupOf2010ToATrade()
    {
        using var store = new TemporaryStore().Migrated();
        var history = new SweepHistory(store.DatabaseFile);
        var inputs = LongPullbackInputs();
        var through = inputs.Through;
        var standing = await WalkForwardTester.ReadAsync("GSPC", history, through, inputs, default);
        var longRead = await WalkForwardTester.ReadLongAsync("GSPC", history, through, inputs, default);

        // The standing read's candidates are the sweep's own, from its first scored session, as before the correction; the
        // long read's reach back to the long history's first session.
        var (standingReplay, standingCandidates) = WalkForwardTester.PullbackCandidates(standing, []);
        var (_, _, sweeps) = SweepIdeasRunner.ReadWithCandidates(standing.Inputs, []);
        var (longReplay, longCandidates) = WalkForwardTester.PullbackCandidates(longRead, []);

        Assert.Equal(sweeps.Select(one => (one.Name, one.Session)), standingCandidates.Select(one => (one.Name, one.Session)));
        Assert.All(standingCandidates, one => Assert.True(standing.Calendar[one.Session] >= SweepColumns.FirstScored));
        Assert.Contains(longCandidates, one => longRead.Calendar[one.Session].Year == 2010);

        // The live swing filter at version five walked by the per-rule test over each read: on the long read a trade bought in
        // 2010 and ended with an edge, on the standing read none before 2019.
        var live = TheSwingFamily.For("1", VersionFive)[0];
        RegisterRow[] register =
        [
            new(1, live.Candidate, live.Rule, live.Test, live.Evaluator, CandidateEvaluator.Write(live.Parameters), new SwingFilterRule().Version, CandidateFamily.Registered, null, new DateTimeOffset(2009, 1, 2, 12, 0, 0, TimeSpan.Zero), null),
        ];

        RuleUnderTest Walked(LoopRead read, IdeaReplay replay, IReadOnlyList<SweepCandidate> candidates) =>
            RuleTests.LargePullbacks(new RuleTestInputs(read, register, new Dictionary<string, LoopChange>(StringComparer.Ordinal), [], null!, null!, _ => null, ("1", VersionFive, new LargePullback(replay.Series, candidates)), _ => { }, Candidates: replay)).Single();

        var overLong = Walked(longRead, longReplay, longCandidates);
        var overStanding = Walked(standing, standingReplay, standingCandidates);

        Assert.Null(overLong.Why);
        Assert.Contains(overLong.Trades!, trade => longRead.Calendar[trade.Entry].Year == 2010 && trade.Edge is not null);
        Assert.DoesNotContain(overStanding.Trades!, trade => standing.Calendar[trade.Entry] < SweepColumns.FirstScored);
        Assert.Equal(
            overStanding.Trades!.Select(trade => (standing.Calendar[trade.Entry], trade.Edge is null)),
            overLong.Trades!.Where(trade => longRead.Calendar[trade.Entry] >= SweepColumns.FirstScored).Select(trade => (longRead.Calendar[trade.Entry], trade.Edge is null)));
    }

    // The S&P 400's and 600's own search: a pick its walk ended won under the exit, its multiple 1.5 risks and the same plan on
    // every member 0.25, is a trade at an edge of 1.25 whether it was picked in 2010, the year nine before the sweep's first,
    // or in 2019; a pick its walk did not end is none; and a pick of the year after the sweep's eight is none, as before.
    [Fact]
    public void TheOwnSearchCountsAPickOf2010AsATradeWhereItsWalkEndedIt()
    {
        const int exit = 0;
        var plan = new SweepPlanOutcomes();

        plan.Code[exit] = SweepPlanOutcomes.Win;
        plan.Multiple[exit] = 1.5f;
        plan.Benchmark[exit] = 0.25f;

        var year2010 = 2010 - SweepColumns.FirstScored.Year;

        Assert.Equal(-9, year2010);
        Assert.Equal(1.25, RuleTests.OwnEdge(plan, year2010, exit, 0f)!.Value, 6);
        Assert.Equal(1.25, RuleTests.OwnEdge(plan, 0, exit, 0f)!.Value, 6);
        Assert.Null(RuleTests.OwnEdge(plan, SweepFigures.Years, exit, 0f));

        var unended = new SweepPlanOutcomes();

        unended.Code[exit] = SweepPlanOutcomes.NeverEntered;
        unended.Multiple[exit] = 1.5f;
        unended.Benchmark[exit] = 0.25f;

        Assert.Null(RuleTests.OwnEdge(unended, year2010, exit, 0f));
    }

    // A long run laying out no book keeps each Quality and Fundamentals rule standing on the index with why, read on the
    // standing years alone; the standing run, which lays out a book wherever such a rule stands, keeps none of them that way.
    [Fact]
    public async Task ALongRunKeepsEachQualityAndFundamentalsRuleWithWhyRatherThanNoRow()
    {
        using var store = new TemporaryStore().Migrated();
        var history = new SweepHistory(store.DatabaseFile);
        var inputs = LongPullbackInputs();
        var longRead = await WalkForwardTester.ReadLongAsync("GSPC", history, inputs.Through, inputs, default);
        var standing = await WalkForwardTester.ReadAsync("GSPC", history, inputs.Through, inputs, default);
        var at = new DateTimeOffset(2018, 1, 2, 12, 0, 0, TimeSpan.Zero);
        var quality = new QualityCandidate(MarketCloses.Index);
        var improving = new ImprovingCandidate(MarketCloses.Index);

        RegisterRow Row(long id, CandidateEvaluator evaluator, string candidate, IReadOnlyDictionary<string, double> parameters) =>
            new(id, candidate, "rule", "test", evaluator.Name, CandidateEvaluator.Write(parameters), evaluator.Version, "registered", null, at, null);

        RegisterRow[] register =
        [
            Row(1, quality, FamilyRecords.LivePrefix + "the S&P 500 quality rule", QualityCandidate.ParametersOf(QualityRule.Starting)),
            Row(2, improving, FamilyRecords.LivePrefix + "the S&P 500 Fundamentals rule", ImprovingCandidate.ParametersOf(ImprovingGrid.Starting)),
        ];

        IReadOnlyList<RuleUnderTest> Walk(LoopRead read) =>
            RuleTests.Walk(new RuleTestInputs(read, register, new Dictionary<string, LoopChange>(StringComparer.Ordinal), [], null!, null!, _ => null, null, _ => { }), choosing: false);

        var kept = Walk(longRead);

        Assert.Equal([(QualityRule.Name, register[0].Candidate), (ImprovingRule.Name, register[1].Candidate)], kept.Select(rule => (rule.Family, rule.Rule)));
        Assert.All(kept, rule => Assert.Equal(RuleTests.BooksOnTheStanding, rule.Why));
        Assert.Empty(Walk(standing));
    }
}
