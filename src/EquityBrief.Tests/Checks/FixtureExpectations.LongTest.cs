using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Loop;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 23.7: each family judged over the long history from its own first test year, worked by hand over a
// constructed history; a block spanning 2018 and 2019 summed once; the fewest sign vectors worked from the bar and the
// margin, cut once half the cap is used and no index started past it; and the long step's failure leaving the month's
// report standing.
// see: The monthly run's last step judges every registered rule over the long history from each family's own first test year, capped at three hours
public partial class FixtureExpectations
{
    internal static string[] LongTestClaims =>
    [
        CheckReach.Key(Scope.LimitsTable, "The per-rule test over the long history"),
    ];

    internal static string[] LongTestRows => [.. LongTestClaims, .. Reading.ReadSurface.LongVerdictRows];

    // Every weekday from 2005 through 2022, read as the long history's sessions.
    static DateOnly[] LongCalendar() =>
    [
        .. Enumerable.Range(0, (new DateOnly(2022, 12, 31).DayNumber - new DateOnly(2005, 1, 3).DayNumber) + 1)
            .Select(day => new DateOnly(2005, 1, 3).AddDays(day))
            .Where(day => day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday),
    ];

    // A family's first test year is its own: the pullback's 2008, the drift's 2016, the S&P 600's Fundamentals 2017 and the
    // S&P 500's heavyweights 2023; a pullback and a drift walked over the same trades, one a month from 2005, are judged
    // from their own first test years, and the heavyweights, the history ending in 2022, from none.
    [Fact]
    public void EachFamilyIsJudgedOverTheLongHistoryFromItsOwnFirstTestYear()
    {
        Assert.Equal(2008, LongTest.FirstTestYear(SetupFamilies.Pullback, "GSPC"));
        Assert.Equal(2008, LongTest.FirstTestYear(BreakoutRule.Name, "SML"));
        Assert.Equal(2016, LongTest.FirstTestYear(DriftRule.Name, "MID"));
        Assert.Equal(2017, LongTest.FirstTestYear(ImprovingRule.Name, "SML"));
        Assert.Equal(2023, LongTest.FirstTestYear(HeavyweightRule.Name, "GSPC"));

        var calendar = LongCalendar();
        var through = calendar[^1];
        var read = new LoopRead("GSPC", null!, null!, null!, null!, null!, null!, calendar, 0, LoopFolds.Of(calendar, through, LongTest.PriceFirstTestYear), Long: true);
        (int Entry, double? Edge)[] trades = [.. Enumerable.Range(0, calendar.Length).Where(at => at % 21 == 0).Select(at => (at, (double?)0.5))];

        RuleUnderTest Rule(string family) => new(family, family + " live", "live", family, LoopProcedures.Risks, 10, trades);

        var tested = RuleTests.JudgeLong(read, [Rule(SetupFamilies.Pullback), Rule(DriftRule.Name), Rule(HeavyweightRule.Name)]);
        var pullback = tested.Single(one => one.Rule.Family == SetupFamilies.Pullback);
        var drift = tested.Single(one => one.Rule.Family == DriftRule.Name);
        var heavyweights = tested.Single(one => one.Rule.Family == HeavyweightRule.Name);

        Assert.Equal(2008, pullback.Evidence!.Units.Min(unit => calendar[unit.Session].Year));
        Assert.Equal(2016, drift.Evidence!.Units.Min(unit => calendar[unit.Session].Year));
        Assert.Equal(trades.Count(trade => calendar[trade.Entry].Year >= 2016), drift.Evidence.Units.Count(unit => unit.Proposed is not null));
        Assert.Null(heavyweights.Evidence);
        Assert.Equal("the long history holds no test year from its family's first, 2023", heavyweights.Why);
        Assert.Equal(1, read.YearOf(Array.FindIndex(calendar, day => day.Year == 2006)));
        Assert.Equal(7, read.YearOf(calendar.Length - 1));
        Assert.Equal(17, read.LearningYears(read.Folds[^1]));
    }

    // A block of 63 sessions spanning the last of 2018 and the first of 2019 holds each unit once: its sum is the units' sum,
    // and no unit is read in two blocks.
    [Fact]
    public void ABlockSpanningTheLongYearsAndTheStandingOnesIsSummedOnce()
    {
        var calendar = LongCalendar();
        var opens = Array.FindIndex(calendar, day => day.Year == 2018) + 40;
        var join = Array.FindIndex(calendar, day => day.Year == 2019);
        PairedUnit[] units = [.. Enumerable.Range(join - 10, 20).Select(at => new PairedUnit(at, 1.0, null))];

        var blocks = LoopGate.Blocks(units, opens, calendar.Length - 1, 10);

        Assert.True(calendar[opens + ((join - 10 - opens) / 63 * 63)] < new DateOnly(2019, 1, 1));
        Assert.Equal(20.0, blocks.Sum(block => block.Sum), 9);
        Assert.Equal(blocks.Count, blocks.Select(block => block.Block).Distinct().Count());
    }

    // The fewest sign vectors: two standard errors of a share at the bar of 1 in 240 coming to 0.15 of it, 4 times 239/240
    // over 0.0225 times 1/240, 42,488.9, so 42,489, above 20,000; every one while the step has used under half its three
    // hours, the fewest from half, and no index started once all three are used.
    [Fact]
    public void TheFewestSignVectorsTellAPValueAtTheBarWithinTheMarginAndTheCapCutsThenStops()
    {
        Assert.Equal(1.0 / 240, LoopGate.Bar, 12);
        Assert.Equal(42_489, LongTest.FewestDraws);
        Assert.True(LongTest.FewestDraws >= LongTest.LeastDraws);
        Assert.Equal(1L << 24, LongTest.FullDraws);
        Assert.Equal(LongTest.FullDraws, LongTest.DrawsAt(TimeSpan.FromMinutes(89)));
        Assert.Equal(LongTest.FewestDraws, LongTest.DrawsAt(TimeSpan.FromMinutes(90)));
        Assert.True(LongTest.GoesOn(TimeSpan.FromMinutes(179)));
        Assert.False(LongTest.GoesOn(TimeSpan.FromHours(3)));

        // The gate reads the draws it is handed past its enumerated blocks: over 30 blocks of one sign, the observed sum is
        // the largest any flip gives, so its p-value is about one over the draws read.
        IReadOnlyList<double>[] sums = [[.. Enumerable.Range(0, 30).Select(_ => 1.0)], [.. Enumerable.Range(0, 30).Select(at => at % 2 == 0 ? 1.0 : 0.5)]];

        Assert.Equal(1.0 / 1000, LoopGate.Adjusted(sums, 1000)[0], 9);
    }

    // The long step runs after the month's report: where it does not hold, the run stops on it with the report written.
    [Fact]
    public async Task TheLongStepsFailureLeavesTheMonthsReportStanding()
    {
        using var store = new TemporaryStore().Migrated();
        using var output = new StringWriter();
        var clock = new CallClock(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero));
        var ran = new List<string>();

        var run = await new MonthlyRun(clock, store.DatabaseFile, store.Root, output, MonthlySteps(ran, name => name != WalkForwardTester.LongStep)).RunAsync("2026-11");

        Assert.Equal(WalkForwardTester.LongStep, MonthlyRun.StepNames[^1]);
        Assert.Equal(WalkForwardTester.LongStep, run.Stopped);
        Assert.Equal(["report|ok", "long|failed"], Query(store, $"SELECT replace(stage, 'loop-month-', '') || '|' || outcome FROM run_log WHERE run_id = '{run.RunId}' AND stage IN ('loop-month-report', 'loop-month-long') ORDER BY rowid;"));
    }
}
