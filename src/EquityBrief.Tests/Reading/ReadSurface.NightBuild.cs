using System.Diagnostics;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// The pages' half of the night built from a clean copy: a refusal the script wrote before any worker
// existed is tonight's notice and the Run page's headline for its own session, the Run page shows the
// commit the night recorded, and a press starts the drain and the rest of the night from the newest
// night's own build.
// see: Each night is built from a clean copy of the main checkout's own commit and never from its working tree, and refuses only a checkout off main or ahead of the remote's main
public partial class ReadSurface
{
    // The claims the 12.3 build of the clean-copy night adds to the two pages, which this check reaches.
    // Declared before the reach that takes them in.
    internal static readonly string[] NightBuildClaims =
    [
        CheckReach.Key("15.7 Tonight", "The night's state, a refusal before its first step with its reason"),
        CheckReach.Key("15.10 Run", "How last night went, the commit the night was built from"),
    ];

    [Fact]
    public async Task ARefusalBeforeTheFirstStepIsShownOnTonightAndTheRunPageWithItsReasonForItsOwnSessionAlone()
    {
        RunStageRow[] none = [.. FinishedNight.Where(row => !row.RunId.StartsWith("night-", StringComparison.Ordinal))];

        using var store = Logged(none);

        const string reason = "the checkout is on 'phase-13-0-draft' and not on main, so no night was built from it";

        // The refusal fell on the evening of 2026-09-10, whose night is otherwise never ran.
        File.WriteAllText(Path.Combine(store.Root, NightBuild.RefusalFileName), "2026-09-10T23:30:05Z " + reason + "\n");

        var (run, tonight) = await BothPagesAsync(store, "2026-09-11T01:00:00Z");

        Assert.Contains($"data-state=\"{NightStates.Refused}\" data-tone=\"fail\" data-session=\"2026-09-10\"", tonight, StringComparison.Ordinal);
        Assert.Contains("The night of Thu 2026-09-10: Refused before its first step", tonight, StringComparison.Ordinal);
        Assert.Contains("No night ran for Thu 2026-09-10: " + reason + ". Nothing was built and nothing was written", tonight, StringComparison.Ordinal);
        Assert.DoesNotContain("data-resume=", tonight, StringComparison.Ordinal);

        Assert.Contains($"data-state=\"{NightStates.Refused}\" data-tone=\"fail\" data-session=\"2026-09-10\"", run, StringComparison.Ordinal);
        Assert.Contains("Refused before its first step", run, StringComparison.Ordinal);
        Assert.Contains(reason, run, StringComparison.Ordinal);

        // The view the pages are handed, directly.
        var view = RunScreen.Night(none, new DateOnly(2026, 9, 10), DateTimeOffset.Parse("2026-09-11T01:00:00Z", System.Globalization.CultureInfo.InvariantCulture), TimeSpan.FromMinutes(60), null, reason);

        Assert.Equal((NightStates.Refused, reason), (view.State, view.Reason));
        Assert.Equal("fail", MarkRenderer.NightTone(view.State));

        // A refusal that fell on another session's evening is not this session's: the night of 2026-09-10
        // reads as never ran.
        using var another = Logged(none);

        File.WriteAllText(Path.Combine(another.Root, NightBuild.RefusalFileName), "2026-09-11T23:30:05Z " + reason + "\n");

        var (later, laterTonight) = await BothPagesAsync(another, "2026-09-11T01:00:00Z");

        Assert.Contains($"data-state=\"{NightStates.NeverRan}\"", laterTonight, StringComparison.Ordinal);
        Assert.Contains($"data-state=\"{NightStates.NeverRan}\"", later, StringComparison.Ordinal);
        Assert.DoesNotContain(reason, laterTonight, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRunPageShowsTheCommitTheNightWasBuiltFromAsTheNightRecordedIt()
    {
        const string detail = "built from 8ae51b59cb91, 2 commit(s) behind origin/main, built in 94 s";

        using var store = Logged([.. FinishedNight, LogRow(NightRun, NightBuild.Stage, "2026-09-10T23:30:01Z", "2026-09-10T23:30:01Z", detail: detail)]);

        var (run, _) = await BothPagesAsync(store, "2026-09-11T01:00:00Z");

        Assert.Contains($"data-state=\"{NightStates.Finished}\"", run, StringComparison.Ordinal);
        Assert.Contains("<p class=\"ns-built\" data-commit=\"8ae51b59cb91\">Built from 8ae51b59cb91, 2 commit(s) behind origin/main, built in 94 s.</p>", run, StringComparison.Ordinal);

        // A night that recorded no build draws no line, and the view carries none.
        using var bare = Logged(FinishedNight);

        var (plain, _) = await BothPagesAsync(bare, "2026-09-11T01:00:00Z");

        Assert.DoesNotContain("ns-built", plain, StringComparison.Ordinal);
        Assert.Null(RunScreen.Night(FinishedNight, new DateOnly(2026, 9, 10), DateTimeOffset.Parse("2026-09-11T01:00:00Z", System.Globalization.CultureInfo.InvariantCulture), TimeSpan.FromMinutes(60)).BuiltFrom);
    }

    [Fact]
    public void TheDrainAndTheRestOfTheNightStartFromTheNewestNightsBuildWhereItHoldsTheWorkerAndFromTheBuildBesideOtherwise()
    {
        using var root = new TemporaryDirectory();

        var beside = Path.Combine(root.Path, "build");
        var data = Path.Combine(root.Path, "data-root");

        Directory.CreateDirectory(beside);
        Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(beside, WorkerDrainLauncher.Assembly), "the worker beside the surface");

        // The newest night's build, named by the commit file and holding the worker.
        const string commit = "8ae51b59cb91f00ba11ce0123456789abcdef012";
        var made = Path.Combine(data, NightBuild.CopiesFolder, commit[..12], "src", "EquityBrief.Worker", "bin", "Release", Repository.Framework);

        Directory.CreateDirectory(made);
        File.WriteAllText(Path.Combine(made, WorkerDrainLauncher.Assembly), "the night's own worker");
        File.WriteAllText(Path.Combine(data, NightBuild.CommitFileName), commit + "\n");

        var asked = new List<ProcessStartInfo>();
        var launcher = new WorkerDrainLauncher(
            root.Path,
            beside,
            data,
            FixedClock.At(UtcAt("2026-10-01T19:01:00Z"), SessionZones.UnitedStates),
            info =>
            {
                asked.Add(info);

                return true;
            },
            () => NightBuild.NewestWorkerBuild(data));

        Assert.Equal(made, launcher.BuildToStart());

        // The drain and the rest of the night each start from a copy of the night's build.
        Assert.True(launcher.Start().Started);
        Assert.True(launcher.StartTheRestOfTheNight().Started);

        Assert.Equal(2, asked.Count);
        Assert.Equal("the night's own worker", File.ReadAllText(asked[0].ArgumentList[0]));
        Assert.Equal("the night's own worker", File.ReadAllText(asked[1].ArgumentList[0]));
        Assert.Equal([asked[1].ArgumentList[0], .. WorkerDrainLauncher.RestOfTheNight], asked[1].ArgumentList);
        Assert.StartsWith(Path.Combine(data, WorkerDrainLauncher.CopiesFolder), asked[0].ArgumentList[0], StringComparison.Ordinal);

        // With the night's build gone, or no night the script started, the build beside the surface.
        File.Delete(Path.Combine(made, WorkerDrainLauncher.Assembly));

        Assert.Equal(beside, launcher.BuildToStart());
        Assert.True(launcher.Start().Started);
        Assert.Equal("the worker beside the surface", File.ReadAllText(asked[2].ArgumentList[0]));

        File.Delete(Path.Combine(data, NightBuild.CommitFileName));

        Assert.Equal(beside, launcher.BuildToStart());
    }
}
