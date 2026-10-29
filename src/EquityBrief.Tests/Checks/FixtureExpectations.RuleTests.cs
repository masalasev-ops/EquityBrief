using EquityBrief.Core.Loop;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Loop;
using TestVerdict = EquityBrief.Core.Loop.RuleVerdict;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 19.4: the monthly per-rule test worked by hand over a constructed ledger. A provisional rule is
// read against its benchmark unit for unit, each variant against it with the family's variants read together by the
// step-down, a rule the history cannot walk kept with the reading it lacks and no figure, its verdict from 21.1 naming what
// judges it instead, a variant whose head cannot be walked and one with no head each naming why, and the run written whole
// and read back row for row.
// see: Every registered rule is tested each month on the test years, each variant against its live rule and each live rule against its benchmark
// see: Every registered rule is walked by the per-rule test at what it reads, and one the history cannot walk names what judges it instead
public partial class FixtureExpectations
{
    // The row 19.4 adds that this check reaches: section 18's rule the per-rule test cannot walk.
    internal static readonly string[] RuleTestClaims =
    [
        CheckReach.Key(Scope.FailureTable, "A rule the per-rule test cannot walk"),
    ];

    // A unit a session over the test span, its edge given by the session's place from where the span opens.
    static (int Entry, double? Edge)[] RuleUnits(int opens, int newest, Func<int, double> edge) =>
        [.. Enumerable.Range(opens, newest - opens + 1).Select(at => (at, (double?)edge(at - opens)))];

    [Fact]
    public async Task ThePerRuleTestReadsEachRuleAgainstWhatItsStandingNamesAndKeepsOneItCannotWalkWithTheReadingItLacks()
    {
        var calendar = LoopCalendar();
        var folds = LoopFolds.Of(calendar, calendar[^1]);
        var read = new LoopRead("MID", null!, [], [], null!, null!, new Dictionary<string, IReadOnlyList<FiledIncome>>(), calendar, 0, folds);
        var (opens, newest) = (read.Opens, read.Newest);

        // The provisional pullback earns between half a risk and a risk on every session's trade over its benchmark. The
        // first variant earns more on each, half a risk, a risk or a risk and a half by its block of 63 sessions in turn,
        // so every block is above it by a different amount; the second exactly what the provisional rule earns; the third
        // a quarter of a risk more in its first seven blocks and a quarter less in the two after, every block's
        // difference the same size; and the fourth reads the analysts' estimates, which the history keeps only from
        // 2026-10-03.
        var head = new RuleUnderTest("pullback", "provisional", RuleTests.Provisional, "the provisional pullback", LoopProcedures.Risks, 0, RuleUnits(opens, newest, at => 0.5 + (0.25 * (at % 3))));
        var richer = new RuleUnderTest("pullback", "the richer variant", RuleTests.Variant, "more each block", LoopProcedures.Risks, 0, [.. head.Trades!.Select(one => (one.Entry, one.Edge + 1 + (0.5 * ((((one.Entry - opens) / 63) % 3) - 1))))]);
        var same = new RuleUnderTest("pullback", "the same variant", RuleTests.Variant, "the same trades", LoopProcedures.Risks, 0, head.Trades);
        var mixed = new RuleUnderTest("pullback", "the mixed variant", RuleTests.Variant, "a quarter either way", LoopProcedures.Risks, 0, [.. head.Trades!.Select(one => (one.Entry, one.Edge + ((one.Entry - opens) / 63 < 7 ? 0.25 : -0.25)))]);
        var estimates = new RuleUnderTest("pullback", "the estimates variant", RuleTests.Variant, "estimates raised", LoopProcedures.Risks, 0, Why: RuleTests.Estimates);

        // The drift's provisional rule cannot be walked, so its variant names why; the breakout holds a variant alone.
        var unwalked = new RuleUnderTest("drift", "provisional", RuleTests.Provisional, "the provisional drift", LoopProcedures.Risks, 0, Why: RuleTests.BusinessState);
        var orphan = new RuleUnderTest("drift", "the drift variant", RuleTests.Variant, "a drift variant", LoopProcedures.Risks, 0, head.Trades);
        var alone = new RuleUnderTest("breakout", "the breakout variant", RuleTests.Variant, "a breakout variant", LoopProcedures.Risks, 0, head.Trades);

        var tested = RuleTests.Judge(read, [head, richer, same, mixed, estimates, unwalked, orphan, alone]);

        RuleTested Of(RuleUnderTest rule) => tested.Single(one => one.Rule == rule);

        // The span opens on 2022-01-03 and the history ends 2024-03-08, 570 sessions: nine whole blocks of 63 with no cap
        // to wait on, so the smallest p-value an arrangement allows is one in 512, under the bar of 0.05 over 12.
        Assert.Equal(new DateOnly(2022, 1, 3), calendar[opens]);
        Assert.Equal(new DateOnly(2024, 3, 8), calendar[newest]);

        // The provisional rule against its benchmark: every unit paired with its benchmark at nothing, so each test year
        // counts the rule's own trades on both sides and reads better where its total stands above nothing. Every block is
        // positive and no other arrangement reaches it, so its p-value is one in 512; two complete years better of two,
        // the trimmed total above nothing and 570 trades past the 300 a swing family is judged on.
        var provisional = Of(head);

        Assert.Equal(RuleTests.Benchmark, provisional.Against);
        Assert.All(provisional.Evidence!.Years, year => Assert.Equal(year.ProposedUnits, year.CurrentUnits));
        Assert.All(provisional.Evidence.Years, year => Assert.Equal(0.0, year.CurrentTotal));
        Assert.Equal(570, newest - opens + 1);
        Assert.Equal(9, provisional.Verdict!.Blocks);
        Assert.Equal(1.0 / 512, provisional.Verdict.Adjusted!.Value, 12);
        Assert.True(provisional.Verdict.Gate);
        Assert.Equal((2, 2), (provisional.Verdict.Counted, provisional.Verdict.Better));
        Assert.True(provisional.Verdict.Stable);
        Assert.Equal(newest - opens + 1, provisional.Verdict.Units);
        Assert.True(provisional.Passed);
        Assert.Equal(TestVerdict.Passed, TestVerdict.Of(provisional.Passed, provisional.Why, provisional.Verdict.Adjusted, provisional.Verdict.Gate, provisional.Verdict.Stable, provisional.Verdict.Trimmed, provisional.Verdict.Counts));

        // The variants against the provisional rule, read together: their adjusted p-values are the step-down's over the
        // three walked. Alone, the richer one's every block positive is the one arrangement of 512 reaching it; together,
        // the arrangement turning the mixed one's two negative blocks positive gives that one blocks all of one size, a
        // statistic no scatter bounds, so it reaches the richer one's too: two in 512, still under the bar, and it
        // passes. The one earning the same holds a difference of nothing in every block, so every arrangement reaches it
        // and it reads within luck.
        var evidences = new[] { RuleTests.Against(read, richer, head), RuleTests.Against(read, same, head), RuleTests.Against(read, mixed, head) };
        var together = LoopJudge.Judge(evidences, opens, newest, 0);
        var apart = LoopJudge.Judge([evidences[0]], opens, newest, 0)[0];

        Assert.Equal("provisional", Of(richer).Against);
        Assert.Equal(together[0].Adjusted, Of(richer).Verdict!.Adjusted);
        Assert.Equal(together[1].Adjusted, Of(same).Verdict!.Adjusted);
        Assert.Equal(together[2].Adjusted, Of(mixed).Verdict!.Adjusted);
        Assert.Equal(1.0 / 512, apart.Adjusted!.Value, 12);
        Assert.Equal(2.0 / 512, Of(richer).Verdict!.Adjusted!.Value, 12);
        Assert.True(Of(richer).Passed);
        Assert.Equal(1.0, Of(same).Verdict!.Adjusted);
        Assert.False(Of(same).Verdict!.Gate);
        Assert.Equal(TestVerdict.WithinLuck, TestVerdict.Of(Of(same).Passed, Of(same).Why, Of(same).Verdict!.Adjusted, Of(same).Verdict!.Gate, Of(same).Verdict!.Stable, Of(same).Verdict!.Trimmed, Of(same).Verdict!.Counts));

        // The estimates variant is kept with the reading it lacks and no figure, its verdict from 21.1 naming what judges it
        // instead; the drift's provisional rule likewise, its variant naming that its head cannot be walked; and the
        // breakout's variant naming that no head stands.
        Assert.Null(Of(estimates).Evidence);
        Assert.Equal(RuleTests.Estimates, Of(estimates).Why);
        Assert.Equal("not testable: " + RuleTests.Estimates + "; judged by its record from its registration, on paper trading and the Run page", TestVerdict.Of(false, Of(estimates).Why, null, null, null, null, null));
        Assert.Equal(3, TestVerdict.Rank(TestVerdict.Of(false, Of(estimates).Why, null, null, null, null, null)));
        Assert.Equal(RuleTests.BusinessState, Of(unwalked).Why);
        Assert.Equal(RuleTests.HeadUnwalked(unwalked), Of(orphan).Why);
        Assert.Equal("its provisional rule cannot be walked: " + RuleTests.BusinessState, Of(orphan).Why);
        Assert.Equal("no live or provisional rule of its family stands on the index", Of(alone).Why);
        Assert.Equal(RuleTests.NoHead, Of(alone).Against);

        // Written whole with its run log row, and read back row for row.
        using var store = new TemporaryStore().Migrated();
        using var output = new StringWriter();
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var tester = new WalkForwardTester(clock, store.DatabaseFile, store.Root, output, (_, _) => Task.CompletedTask);

        await tester.WriteRulesAsync("loop-rules-MID-20261013T120000Z", "2026-10", "MID", calendar[^1], clock.UtcNow, folds.Count, tested);

        Assert.Equal(
            [
                "pullback|provisional|provisional|its benchmark|1|1",
                "pullback|the richer variant|variant|provisional|1|1",
                "pullback|the same variant|variant|provisional|0|0",
                FormattableString.Invariant($"pullback|the mixed variant|variant|provisional|{(Of(mixed).Verdict!.Gate ? 1 : 0)}|{(Of(mixed).Passed ? 1 : 0)}"),
                "pullback|the estimates variant|variant|provisional|null|0",
                "drift|provisional|provisional|its benchmark|null|0",
                "drift|the drift variant|variant|provisional|null|0",
                "breakout|the breakout variant|variant|its live rule|null|0",
            ],
            Query(store, "SELECT family || '|' || rule || '|' || standing || '|' || against || '|' || COALESCE(gate, 'null') || '|' || passed FROM loop_rule ORDER BY rowid;"));
        Assert.Equal(
            [RuleTests.Estimates, RuleTests.BusinessState],
            Query(store, "SELECT why FROM loop_rule WHERE rule IN ('the estimates variant', 'provisional') AND why IS NOT NULL ORDER BY rowid;"));

        // A year row a test year for each rule walked and none for one that was not: three each for the four walked.
        Assert.Equal(
            ["pullback|provisional|3", "pullback|the richer variant|3", "pullback|the same variant|3", "pullback|the mixed variant|3"],
            Query(store, "SELECT family || '|' || rule || '|' || COUNT(*) FROM loop_rule_year GROUP BY family, rule ORDER BY MIN(rowid);"));
        // Worked by hand: the edges run half a risk, three quarters and a risk, 2.25 every three sessions. 2022's 260
        // sessions hold 86 runs of three and two more, 194.75; 2023's open on the third of a run, a risk and then 86 runs
        // and one more, 195; 2024's 50 open on the second, three quarters and a risk and then 16 runs, 37.75. The
        // benchmark's side holds the same units at nothing.
        Assert.Equal(
            ["2022|1|260|194.75|260|0.0", "2023|1|260|195.0|260|0.0", "2024|0|50|37.75|50|0.0"],
            Query(store, "SELECT year || '|' || complete || '|' || units || '|' || total || '|' || compared_units || '|' || compared_total FROM loop_rule_year WHERE rule = 'provisional' ORDER BY year;"));
        Assert.Equal(
            ["loop-rules|ok|20"],
            Query(store, "SELECT stage || '|' || outcome || '|' || rows_written FROM run_log WHERE run_id = 'loop-rules-MID-20261013T120000Z';"));
    }

    [Fact]
    public void AnAccountsVerdictWordsItsTestAndItsRecordSinceRegistrationIsTooFewUntilTheTestsCountIsMet()
    {
        // Each part of the verdict in the tester's own order.
        Assert.Equal(TestVerdict.Passed, TestVerdict.Of(true, null, 0.001, true, true, 0.5, true));
        Assert.Equal("failed the test's block floor", TestVerdict.Of(false, null, null, false, false, null, false));
        Assert.Equal(TestVerdict.WithinLuck, TestVerdict.Of(false, null, 0.2, false, true, 0.5, true));
        Assert.Equal("failed the test's stability screen", TestVerdict.Of(false, null, 0.001, true, false, 0.5, true));
        Assert.Equal("failed the test's trimmed screen", TestVerdict.Of(false, null, 0.001, true, true, 0, true));
        Assert.Equal("failed the test's count", TestVerdict.Of(false, null, 0.001, true, true, 0.5, false));

        // The order the paper accounts' table reads them in.
        Assert.Equal(
            [TestVerdict.Passed, TestVerdict.WithinLuck, TestVerdict.Failed(TestVerdict.Count), TestVerdict.NotTestable("a reading"), TestVerdict.NotTested, TestVerdict.Yours],
            new[] { TestVerdict.Yours, TestVerdict.NotTested, TestVerdict.NotTestable("a reading"), TestVerdict.Failed(TestVerdict.Count), TestVerdict.WithinLuck, TestVerdict.Passed }.OrderBy(TestVerdict.Rank));

        // The record's label at the count screen's own edges: 299 trades and 35 months too few, 300 and 36 met.
        Assert.Equal("too few trades (299 of 300)", TestVerdict.TooFew(false, 299, 0));
        Assert.Null(TestVerdict.TooFew(false, 300, 0));
        Assert.Equal("too few months (35 of 36)", TestVerdict.TooFew(true, 0, 35));
        Assert.Null(TestVerdict.TooFew(true, 0, 36));

        // Whole months from a record's first session: a day short of a month is none, the same day a month later one.
        Assert.Equal(0, TestVerdict.MonthsBetween(new DateOnly(2026, 10, 13), new DateOnly(2026, 11, 12)));
        Assert.Equal(1, TestVerdict.MonthsBetween(new DateOnly(2026, 10, 13), new DateOnly(2026, 11, 13)));
        Assert.Equal(36, TestVerdict.MonthsBetween(new DateOnly(2026, 10, 2), new DateOnly(2029, 10, 2)));
    }
}
