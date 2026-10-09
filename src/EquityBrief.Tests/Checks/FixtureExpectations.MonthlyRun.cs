using EquityBrief.Core.Configuration;
using EquityBrief.Core.Research;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.10: the monthly run over constructed steps and a constructed store. Its steps run in order,
// each writing one row under a stage of its own; a step that does not hold stops the run with why, and a run started again
// for the month goes on from the first step no try of the month held, while another month runs every step. Before each
// step it waits, by a clock its own wait moves, while the night holds its lock and while a step would run into the
// night's window, and it holds the drain's lock while a step runs. Its report names for each family on each index the
// one proposal the month's newest run puts to the operator, the strongest that passed by its adjusted p-value, or that
// none passed, and an index with no run for the month, written into the month's folder and written again over it.
// see: The monthly run puts at most one proposal a family an index to the operator, from a clean copy of main's commit on the first Saturday of the month
public partial class FixtureExpectations
{
    // The rows 17.10 adds that this check reaches: section 17's monthly run and section 18's run that fails.
    internal static readonly string[] MonthlyClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The monthly run"),
        CheckReach.Key(Scope.FailureTable, "A monthly run that fails"),
    ];

    // Every row 17.10's monthly run adds, named after phase 16's report until phase 17's own pair is checked: its
    // catalogue and matrix rows and the two above.
    internal static string[] MonthlyRows =>
    [
        CheckReach.Key(Scope.CatalogueTable, "Monthly run"),
        CheckReach.Key(Scope.MatrixTable, "Monthly run"),
        .. MonthlyClaims,
    ];

    static MonthlyStep[] MonthlySteps(List<string> ran, Func<string, bool> holds) =>
        [.. MonthlyRun.StepNames.Select(name => new MonthlyStep(name, (_, _) =>
        {
            ran.Add(name);

            return Task.FromResult(holds(name) ? (true, name + " held") : (false, name + " did not hold"));
        }))];

    [Fact]
    public async Task TheMonthlyRunsStepsRunInOrderAStepThatDoesNotHoldStopsItAndARunAgainGoesOnFromTheFirstStepNoTryHeld()
    {
        using var store = new TemporaryStore().Migrated();
        using var output = new StringWriter();
        var clock = new CallClock(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));
        var ran = new List<string>();
        var failing = "facts";
        var steps = MonthlySteps(ran, name => name != failing);

        Assert.Equal(["check-GSPC", "check-MID", "check-SML", "facts", "test-GSPC", "test-MID", "test-SML", "report"], MonthlyRun.StepNames);

        // The first Saturday of October: the three checks hold and the facts do not, so the run stops there.
        var first = await new MonthlyRun(clock, store.DatabaseFile, store.Root, output, steps).RunAsync("2026-10");

        Assert.Equal(["check-GSPC", "check-MID", "check-SML", "facts"], ran);
        Assert.Equal("facts", first.Stopped);
        Assert.Equal("loop-month-2026-10-20261003T120000Z", first.RunId);

        // A run of the month is a run by hand to the Run page, which opens on no Saturday for it.
        Assert.Contains(MonthlyRun.Verb + "-", EquityBrief.Api.Reading.RunScreen.RunsByHand);
        Assert.Equal(
            ["loop-month-check-GSPC|ok", "loop-month-check-MID|ok", "loop-month-check-SML|ok", "loop-month-facts|failed|facts did not hold"],
            Query(store, $"SELECT stage || '|' || outcome || CASE outcome WHEN 'ok' THEN '' ELSE '|' || detail END FROM run_log WHERE run_id = '{first.RunId}' ORDER BY rowid;"));

        // Started again an hour later with the facts holding: the checks are not run again and the rest run in order.
        ran.Clear();
        failing = "none";
        clock.Advance(TimeSpan.FromHours(1));

        var second = await new MonthlyRun(clock, store.DatabaseFile, store.Root, output, steps).RunAsync("2026-10");

        Assert.Equal(["facts", "test-GSPC", "test-MID", "test-SML", "report"], ran);
        Assert.Equal(["check-GSPC", "check-MID", "check-SML"], second.Skipped);
        Assert.Null(second.Stopped);

        // Started a third time for the month, every step held already and none runs; November's run runs every step.
        ran.Clear();

        var third = await new MonthlyRun(clock, store.DatabaseFile, store.Root, output, steps).RunAsync("2026-10");

        Assert.Empty(ran);
        Assert.Equal(MonthlyRun.StepNames, third.Skipped);

        await new MonthlyRun(clock, store.DatabaseFile, store.Root, output, steps).RunAsync("2026-11");

        Assert.Equal(MonthlyRun.StepNames, ran);
    }

    [Fact]
    public async Task TheMonthlyRunWaitsForTheNightsLockAndWindowByAClockItsOwnWaitMovesAndHoldsTheDrainsLockWhileAStepRuns()
    {
        using var store = new TemporaryStore().Migrated();
        using var output = new StringWriter();
        var events = new List<string>();
        var drainLock = Path.Combine(store.Root, WorkerDrainLauncher.CopiesFolder, DrainLock.FileName);

        // Each step records the clock and whether the drain's lock is held while it runs, a drain's own open of the
        // file failing while the run holds it.
        CallClock? clock = null;
        MonthlyStep[] Steps() =>
        [
            .. MonthlyRun.StepNames.Select(name => new MonthlyStep(name, (_, _) =>
            {
                bool held;

                try
                {
                    using var probe = new FileStream(drainLock, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    held = false;
                }
                catch (IOException)
                {
                    held = true;
                }

                events.Add(FormattableString.Invariant($"{name} at {clock!.UtcNow:ddd HH:mm}, {(held ? "drain lock held" : "drain lock free")}"));

                return Task.FromResult((true, name + " held"));
            })),
        ];

        // A Friday at 22:50 UTC: a step of thirty minutes would run into the night's window from 23:00, so the run waits,
        // a minute each look, until Saturday begins, and starts no step before.
        clock = new CallClock(new DateTimeOffset(2026, 10, 2, 22, 50, 0, TimeSpan.Zero));

        Task Waited(TimeSpan by, CancellationToken _)
        {
            clock.Advance(by);

            return Task.CompletedTask;
        }

        await new MonthlyRun(clock, store.DatabaseFile, store.Root, output, Steps(), Waited).RunAsync("2026-10");

        Assert.Equal("check-GSPC at Sat 00:00, drain lock held", events[0]);
        Assert.All(events, line => Assert.EndsWith("drain lock held", line, StringComparison.Ordinal));
        Assert.Contains("loop-month: pausing for the night's window", output.ToString(), StringComparison.Ordinal);

        // A Saturday at noon with the night's lock held: the run waits while it is held, and the first step runs on the
        // look after the night gives it up, three looks later.
        events.Clear();
        clock = new CallClock(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero));

        var night = NightLock.Take(store.Root, "night-20261002T233000Z")!;
        var looks = 0;

        Task WaitedForTheNight(TimeSpan by, CancellationToken _)
        {
            clock.Advance(by);

            if (++looks == 3)
            {
                night.Dispose();
            }

            return Task.CompletedTask;
        }

        await new MonthlyRun(clock, store.DatabaseFile, store.Root, output, Steps(), WaitedForTheNight).RunAsync("2026-11");

        Assert.Equal("check-GSPC at Sat 12:03, drain lock held", events[0]);
        Assert.Contains("loop-month: waiting while night-20261002T233000Z holds the night's lock", output.ToString(), StringComparison.Ordinal);

        // Between steps the drain's lock is given up, so a drain started then takes it.
        Assert.False(File.Exists(drainLock) && IsHeld(drainLock));
    }

    static bool IsHeld(string path)
    {
        try
        {
            using var probe = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    [Fact]
    public async Task TheMonthlyReportPutsEachFamilysStrongestPassingProposalToTheOperatorAndSaysWhereNoneOrNoRunStands()
    {
        using var store = new TemporaryStore().Migrated();
        using var output = new StringWriter();
        var clock = new CallClock(new DateTimeOffset(2026, 10, 3, 14, 0, 0, TimeSpan.Zero));

        // The S&P 600's September run and two October runs, the later one the newest; the S&P 400's October run; no run
        // for the S&P 500. On the S&P 600's newest run the breakout's two exits passed at adjusted p-values of 0.003 and
        // 0.001, and the drift's two did not pass; on the S&P 400's the pullback's condition passed at 0.002.
        LoopRun(store, "loop-test-SML-20260905T120000Z", "SML", "2026-09");
        LoopRun(store, "loop-test-SML-20261003T120000Z", "SML", "2026-10");
        LoopRun(store, "loop-test-SML-20261003T130000Z", "SML", "2026-10");
        LoopRun(store, "loop-test-MID-20261003T120000Z", "MID", "2026-10");
        LoopProposed(store, "loop-test-SML-20261003T120000Z", "SML", "breakout", "an exit the older run passed", true, ExitChange(9), adjusted: 0.0001);
        LoopProposed(store, "loop-test-SML-20261003T130000Z", "SML", "breakout", "the autopsy's exit, ranked 1", true, ExitChange(7), adjusted: 0.003);
        LoopProposed(store, "loop-test-SML-20261003T130000Z", "SML", "breakout", "the autopsy's exit, ranked 2", true, ExitChange(8), adjusted: 0.001);
        LoopProposed(store, "loop-test-SML-20261003T130000Z", "SML", "drift", "the autopsy's exit, ranked 1", false, ExitChange(7), adjusted: 0.2);
        LoopProposed(store, "loop-test-SML-20261003T130000Z", "SML", "drift", "the autopsy's exit, ranked 2", false, ExitChange(8), adjusted: 0.5);
        LoopProposed(store, "loop-test-MID-20261003T120000Z", "MID", "pullback", "winners against losers, ranked 1", true, ExitChange(7), adjusted: 0.002);

        var run = new MonthlyRun(clock, store.DatabaseFile, store.Root, output, []);
        var (held, words) = await run.ReportAsync("2026-10");
        var report = File.ReadAllText(Path.Combine(store.Root, MonthlyRun.Folder, "2026-10", MonthlyRun.ReportFile));

        Assert.True(held);
        Assert.Equal("2 proposal(s) put to you over 2 index run(s), the report in loop/2026-10/report.txt", words);
        Assert.Contains("The S&P 500: no tester run for 2026-10.", report, StringComparison.Ordinal);
        Assert.Contains("The S&P 400, run loop-test-MID-20261003T120000Z:", report, StringComparison.Ordinal);
        Assert.Contains("  pullback: put to you, winners against losers, ranked 1: the change; its adjusted p 0.002 against 0.0042", report, StringComparison.Ordinal);
        Assert.Contains("The S&P 600, run loop-test-SML-20261003T130000Z:", report, StringComparison.Ordinal);
        Assert.Contains("  breakout: put to you, the autopsy's exit, ranked 2: the change; its adjusted p 0.001 against 0.0042", report, StringComparison.Ordinal);
        Assert.DoesNotContain("ranked 1: the change; its adjusted p 0.003", report, StringComparison.Ordinal);
        Assert.DoesNotContain("an exit the older run passed", report, StringComparison.Ordinal);
        Assert.Contains("  drift: none of its 2 proposals passed.", report, StringComparison.Ordinal);

        // A tie on the adjusted p-value is settled by the proposal's name, and a run again writes the report over the
        // one before.
        LoopProposed(store, "loop-test-SML-20261003T130000Z", "SML", "breakout", "a proposal first by name", true, ExitChange(10), adjusted: 0.001);

        await run.ReportAsync("2026-10");

        Assert.Contains(
            "  breakout: put to you, a proposal first by name: the change; its adjusted p 0.001 against 0.0042",
            File.ReadAllText(Path.Combine(store.Root, MonthlyRun.Folder, "2026-10", MonthlyRun.ReportFile)),
            StringComparison.Ordinal);
    }
}
