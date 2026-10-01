using EquityBrief.Core.Configuration;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Research;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// The runner's own care: it waits for the night on a weekday from 23:00 UTC less the chunk about to start and
// until the night has taken its lock and let it go, and for a drain's lock whenever one is held; it gives up a
// night that never came; and a chunk that fails is tried once more, failing again stopping the run with where
// and why.
public partial class FixtureExpectations
{
    sealed class SweepClock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = start;

        public TimeZoneInfo SessionZone { get; } = SessionZones.ResolveSessionZone(SessionZones.UnitedStates);
    }

    static DateTimeOffset SweepUtc(int month, int day, int hour, int minute) => new(2026, month, day, hour, minute, 0, TimeSpan.Zero);

    // A runner over a folder of its own whose wait moves the clock a poll at a time and runs what the script
    // holds for each instant reached, so the locks come and go when a night and a drain would.
    static (SweepRunner Runner, SweepClock Clock, string Root) SweepWaiting(DateTimeOffset start, Func<DateTimeOffset, string, Task> script)
    {
        var root = Path.Combine(Path.GetTempPath(), "equitybrief-tests", Guid.NewGuid().ToString("n"));
        var folder = Path.Combine(root, EquityBrief.Core.Sweep.SweepFolder.Name);

        Directory.CreateDirectory(folder);

        var clock = new SweepClock(start);
        var runner = new SweepRunner(clock, root, Path.Combine(root, StoreLocation.DatabaseFileName), folder, TextWriter.Null, async (span, _) =>
        {
            clock.UtcNow += span;
            await script(clock.UtcNow, root);
        });

        return (runner, clock, root);
    }

    static Task SweepNothing(DateTimeOffset at, string root) => Task.CompletedTask;

    [Fact]
    public async Task OnAWeekdayTheRunPausesBeforeTwentyThreeHundredAndGoesOnTwoPollsAfterTheNightLetsItsLockGo()
    {
        // Monday 2026-09-28 at 22:57, a chunk of five minutes about to start would run past 23:00, so the run
        // pauses. The night takes its lock at 23:30 and lets it go at 00:40; the run reads it clear at 00:40 and
        // 00:41 and goes on at 00:41.
        IDisposable? night = null;
        var (runner, clock, _) = SweepWaiting(SweepUtc(9, 28, 22, 57), (at, root) =>
        {
            if (at == SweepUtc(9, 28, 23, 30))
            {
                night = NightLock.Take(root, "night-20260928");
            }

            if (at == SweepUtc(9, 29, 0, 40))
            {
                night!.Dispose();
            }

            return Task.CompletedTask;
        });

        var state = new SweepRunner.State();

        await runner.WaitForTheStoreAsync(state, TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.Equal(SweepUtc(9, 29, 0, 41), clock.UtcNow);
        Assert.Equal(["2026-09-28 22:57Z to 2026-09-29 00:41Z, the night's lock"], state.Pauses);

        // A minute earlier, with a chunk of two minutes, the run does not pause at all.
        var (early, earlyClock, _) = SweepWaiting(SweepUtc(9, 28, 22, 57), SweepNothing);

        await early.WaitForTheStoreAsync(new SweepRunner.State(), TimeSpan.FromMinutes(2), CancellationToken.None);

        Assert.Equal(SweepUtc(9, 28, 22, 57), earlyClock.UtcNow);

        // The window's edges: a chunk ending at 23:00 exactly pauses, one ending a minute before does not, and a
        // Saturday or a Sunday evening does not.
        Assert.True(SweepRunner.InTheNightsWindow(SweepUtc(9, 28, 22, 55), TimeSpan.FromMinutes(5)));
        Assert.False(SweepRunner.InTheNightsWindow(SweepUtc(9, 28, 22, 54), TimeSpan.FromMinutes(5)));
        Assert.False(SweepRunner.InTheNightsWindow(SweepUtc(9, 26, 23, 30), TimeSpan.FromMinutes(5)));
        Assert.False(SweepRunner.InTheNightsWindow(SweepUtc(9, 27, 23, 30), TimeSpan.FromMinutes(5)));
    }

    [Fact]
    public async Task ANightThatNeverComesIsGivenUpSevenHoursOnAndADrainIsWaitedForWhenever()
    {
        // Monday 2026-09-28 at 23:05 in the window with no night: the run goes on at 06:05 and says so.
        var (runner, clock, _) = SweepWaiting(SweepUtc(9, 28, 23, 5), SweepNothing);
        var state = new SweepRunner.State();

        await runner.WaitForTheStoreAsync(state, TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.Equal(SweepUtc(9, 29, 6, 5), clock.UtcNow);
        Assert.Equal(["2026-09-28 23:05Z to 2026-09-29 06:05Z, the night's window, no night having run"], state.Pauses);

        // Tuesday at noon a drain holds its lock until 12:10: the run goes on at 12:11.
        IDisposable? drain = null;
        var (daytime, daytimeClock, root) = SweepWaiting(SweepUtc(9, 29, 12, 0), (at, _) =>
        {
            if (at == SweepUtc(9, 29, 12, 10))
            {
                drain!.Dispose();
            }

            return Task.CompletedTask;
        });

        drain = await DrainLock.AcquireAsync(root, () => throw new InvalidOperationException("nothing else holds the lock"));

        var waited = new SweepRunner.State();

        await daytime.WaitForTheStoreAsync(waited, TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.Equal(SweepUtc(9, 29, 12, 11), daytimeClock.UtcNow);
        Assert.Equal(["2026-09-29 12:00Z to 2026-09-29 12:11Z, a drain's lock"], waited.Pauses);

        // With nothing held at noon the run goes on at once and records no wait.
        var (free, freeClock, _) = SweepWaiting(SweepUtc(9, 29, 12, 0), SweepNothing);
        var none = new SweepRunner.State();

        await free.WaitForTheStoreAsync(none, TimeSpan.FromMinutes(1), CancellationToken.None);

        Assert.Equal(SweepUtc(9, 29, 12, 0), freeClock.UtcNow);
        Assert.Empty(none.Pauses);
    }

    [Fact]
    public async Task AChunkThatFailsIsTriedOnceMoreAndFailingAgainStopsTheRunSayingWhereAndWhy()
    {
        var (runner, _, _) = SweepWaiting(SweepUtc(9, 29, 12, 0), SweepNothing);
        var state = new SweepRunner.State();
        var tries = 0;

        await runner.RetriedAsync(state, "stage 1 designs 1 to 50", () =>
        {
            tries++;

            return tries == 1 ? throw new IOException("the disk was busy") : Task.CompletedTask;
        });

        Assert.Equal(2, tries);
        Assert.Empty(state.Failures);

        var always = 0;
        var stopped = await Assert.ThrowsAsync<SweepStopped>(() => runner.RetriedAsync(state, "stage 2 for a design", () =>
        {
            always++;

            throw new InvalidDataException("a chunk file was cut short");
        }));

        Assert.Equal(2, always);
        Assert.Equal(["stage 2 for a design failed twice and the run stopped there: InvalidDataException: a chunk file was cut short"], state.Failures);
        Assert.Equal(state.Failures[0], stopped.Message);

        // The page a stopped run writes says where and why, and proposes nothing.
        var page = SweepReport.Stopped(state, stopped.Message);

        Assert.Contains("The run stopped before it finished.", page, StringComparison.Ordinal);
        Assert.Contains("stage 2 for a design failed twice and the run stopped there", page, StringComparison.Ordinal);
        Assert.Contains("Nothing is proposed and nothing registered.", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachRunHasAFolderOfItsOwnAndAFinishedRunOrOneFromAnotherBuildIsNeverWrittenAgain()
    {
        // A run's folder is named by the instant it started, newest first among the runs, and the newest report
        // is the newest run's that holds one, and none where no run does: a file at the root is no run's.
        var root = Path.Combine(Path.GetTempPath(), "equitybrief-tests", Guid.NewGuid().ToString("n"), EquityBrief.Core.Sweep.SweepFolder.Name);

        Directory.CreateDirectory(root);

        Assert.Equal("20260930T204732Z", EquityBrief.Core.Sweep.SweepFolder.RunName(new DateTimeOffset(2026, 9, 30, 20, 47, 32, TimeSpan.Zero)));
        Assert.True(EquityBrief.Core.Sweep.SweepFolder.IsRunName("20260930T204732Z"));
        Assert.False(EquityBrief.Core.Sweep.SweepFolder.IsRunName("candidates"));
        Assert.Empty(EquityBrief.Core.Sweep.SweepFolder.Runs(root));
        Assert.Null(EquityBrief.Core.Sweep.SweepFolder.NewestReport(root));

        File.WriteAllText(Path.Combine(root, EquityBrief.Core.Sweep.SweepFolder.ReportFile), "a file at the root");
        Directory.CreateDirectory(Path.Combine(root, "20261001T120000Z"));
        Directory.CreateDirectory(Path.Combine(root, "20261002T120000Z"));
        Directory.CreateDirectory(Path.Combine(root, EquityBrief.Core.Sweep.SweepFolder.CandidatesFolder));

        Assert.Equal(["20261002T120000Z", "20261001T120000Z"], EquityBrief.Core.Sweep.SweepFolder.Runs(root));
        Assert.Null(EquityBrief.Core.Sweep.SweepFolder.NewestReport(root));

        File.WriteAllText(Path.Combine(root, "20261001T120000Z", EquityBrief.Core.Sweep.SweepFolder.ReportFile), "the second run");

        Assert.Equal(Path.Combine(root, "20261001T120000Z", EquityBrief.Core.Sweep.SweepFolder.ReportFile), EquityBrief.Core.Sweep.SweepFolder.NewestReport(root));

        // A finished run is never written again, and a run started by another build is not gone on with; each
        // says so and computes nothing.
        var (finished, _, _) = SweepWaiting(SweepUtc(9, 29, 12, 0), SweepNothing);
        var folder = Path.Combine(root, "20261002T120000Z");
        var runner = new SweepRunner(new SweepClock(SweepUtc(9, 29, 12, 0)), root, Path.Combine(root, "none.db"), folder, TextWriter.Null);

        File.WriteAllText(Path.Combine(folder, "state.json"), "{\"Finished\": true}");
        Assert.Equal(2, await runner.RunAsync());

        File.WriteAllText(Path.Combine(folder, "state.json"), "{\"Build\": \"another build\"}");
        Assert.Equal(2, await runner.RunAsync());
        Assert.Contains("this run was started by build another build", File.ReadAllText(Path.Combine(folder, "sweep.log")), StringComparison.Ordinal);
        Assert.NotEqual("another build", SweepRunner.BuildId);
        Assert.Equal(32, SweepRunner.BuildId.Length);

        _ = finished;
    }
}
