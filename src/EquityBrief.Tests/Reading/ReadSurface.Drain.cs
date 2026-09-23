using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Passes;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Reading;

// read-surface: a press starting the worker's drain, from a copy of the worker's build, and
// the drain paying the off-peak rate for every pass it runs.
public partial class ReadSurface
{
    // A launcher that records each start and starts nothing, so a route is hosted without a
    // drain reaching a model.
    internal sealed class RecordingLauncher : IDrainLauncher
    {
        public const string Line = "The worker was asked to start by the suite.";

        public int Started { get; private set; }

        public DrainStart Start()
        {
            Started++;

            return new DrainStart(true, Line);
        }
    }

    // A clock the drain's own wait moves on, so the instant a pass starts at is the one the
    // wait chose and never the machine's.
    sealed class WaitedClock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = start;

        public TimeZoneInfo SessionZone { get; } = SessionZones.ResolveSessionZone(SessionZones.UnitedStates);
    }

    static DateTimeOffset UtcAt(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    static TemporaryStore RequestsFor(params string[] tickers)
    {
        var store = new TemporaryStore().Migrated();

        // One second apart in the order given, which is the order the drain takes them in.
        for (var at = 0; at < tickers.Length; at++)
        {
            store.Execute(
                "INSERT INTO research_request (ticker, asked_at, asked_from, lane, state) VALUES "
                + $"('{tickers[at]}', '2026-09-20T12:00:0{at}Z', 'list', 'paid', 'outstanding');");
        }

        return store;
    }

    [Fact]
    public async Task APressOverEitherRouteWritesOneRequestAndAsksOneLauncherToStartOneDrain()
    {
        using var store = await FixtureReplay.ReplayedAsync();

        var launcher = new RecordingLauncher();

        using var host = new PassHost(store.Root) { Launcher = launcher };
        using var client = host.CreateClient();

        Assert.Contains("KEYS", await client.GetStringAsync("/screens/name/KEYS"), StringComparison.Ordinal);

        // From a row of tonight's list and from a name's own page, the two places a press is made.
        var fromList = await client.SendAsync(Press(SinglePageApp.PassRoute, "KEYS", SinglePageApp.PassHeaderValue, ("from", "list")));

        Assert.Equal(HttpStatusCode.Accepted, fromList.StatusCode);
        Assert.Equal(1, launcher.Started);

        var fromName = await client.SendAsync(Press(SinglePageApp.PassRoute, "AAPL", SinglePageApp.PassHeaderValue, ("from", "name")));

        Assert.Equal(HttpStatusCode.Accepted, fromName.StatusCode);
        Assert.Equal(2, launcher.Started);

        Assert.Equal(
            [["AAPL", "name", "outstanding"], ["KEYS", "list", "outstanding"]],
            Rows(store, "SELECT ticker, asked_from, state FROM research_request ORDER BY ticker;"));

        // The reply says what the launcher came to, after the line saying the name is queued.
        var said = WebUtility.HtmlDecode(await fromList.Content.ReadAsStringAsync());

        Assert.Contains("data-drain=\"true\"", said, StringComparison.Ordinal);
        Assert.Contains("KEYS is in the queue. " + RecordingLauncher.Line, said, StringComparison.Ordinal);

        // A press that wrote nothing starts nothing: a name already waiting, a press without
        // the page's header and a name the index does not hold.
        Assert.Equal(HttpStatusCode.Conflict, (await client.SendAsync(Press(SinglePageApp.PassRoute, "KEYS", SinglePageApp.PassHeaderValue, ("from", "name")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Press(SinglePageApp.PassRoute, "MSFT", null))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.SendAsync(Press(SinglePageApp.PassRoute, "ZZZZ", SinglePageApp.PassHeaderValue))).StatusCode);

        Assert.Equal(2, launcher.Started);
    }

    [Fact]
    public void TheShippedSourceStartsAProcessInOnePlaceTheLauncherAPressAsks()
    {
        // No shipped file started a process from 9.2 until 11.1 made a press start the worker's
        // drain. The launcher the press asks is the one file that starts one now, and it starts
        // it at one call, so a second place starting a process, for a pass or anything else,
        // fails here. It reads the source because what it asserts is where a thing is not.
        var started = Directory
            .EnumerateFiles(Path.Combine(Repository.Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(Path.Combine("src", "EquityBrief.Tests"), StringComparison.Ordinal))
            .Where(file => Regex.IsMatch(File.ReadAllText(file), @"Process\.Start|ProcessStartInfo"))
            .Select(file => Path.GetRelativePath(Repository.Root, file).Replace(Path.DirectorySeparatorChar, '/'))
            .ToArray();

        Assert.Equal([LauncherSource], started);

        var launcher = File.ReadAllText(Path.Combine(Repository.Root, LauncherSource));

        Assert.Single(Regex.Matches(launcher, @"Process\.Start\("));

        // And the page's own script sends the header the route requires.
        var shell = new SinglePageApp().Shell("EquityBrief");

        Assert.Contains($"'{SinglePageApp.PassHeader}': '{SinglePageApp.PassHeaderValue}'", shell, StringComparison.Ordinal);
        Assert.Contains("research-control", shell, StringComparison.Ordinal);
    }

    const string LauncherSource = "src/EquityBrief.Core/Research/DrainLauncher.cs";

    [Fact]
    public void TheDrainIsStartedFromACopyOfTheWorkersBuildAndNeverFromTheBuildItself()
    {
        using var root = new TemporaryDirectory();

        var build = Path.Combine(root.Path, "build");
        var data = Path.Combine(root.Path, "data-root");

        Directory.CreateDirectory(Path.Combine(build, "runtimes"));
        File.WriteAllText(Path.Combine(build, WorkerDrainLauncher.Assembly), "the worker");
        File.WriteAllText(Path.Combine(build, "appsettings.json"), "{}");
        File.WriteAllText(Path.Combine(build, "runtimes", "native.bin"), "native");

        var now = UtcAt("2026-09-23T19:01:00Z");
        var asked = new List<ProcessStartInfo>();
        var launcher = new WorkerDrainLauncher(root.Path, build, data, FixedClock.At(now, SessionZones.UnitedStates), info =>
        {
            asked.Add(info);

            return true;
        });

        // A copy nothing has been started from for longer than a copy is kept, and one inside it.
        var copies = Path.Combine(data, WorkerDrainLauncher.CopiesFolder);
        var old = Path.Combine(copies, "old");
        var recent = Path.Combine(copies, "recent");

        Directory.CreateDirectory(old);
        Directory.CreateDirectory(recent);
        Directory.SetLastWriteTimeUtc(old, (now - WorkerDrainLauncher.KeptFor - TimeSpan.FromMinutes(1)).UtcDateTime);
        Directory.SetLastWriteTimeUtc(recent, (now - WorkerDrainLauncher.KeptFor + TimeSpan.FromMinutes(1)).UtcDateTime);

        var first = launcher.Start();

        Assert.True(first.Started);

        var info = Assert.Single(asked);
        var assembly = info.ArgumentList[0];
        var copy = Path.GetDirectoryName(assembly)!;

        // Started from a copy under the data root, and never from the build it was made from.
        Assert.Equal(copies, Path.GetDirectoryName(copy));
        Assert.False(assembly.StartsWith(build, StringComparison.Ordinal));
        Assert.Equal(Path.Combine(copy, WorkerDrainLauncher.Assembly), assembly);
        Assert.Equal([assembly, WorkerDrainLauncher.Verb], info.ArgumentList);
        Assert.Equal(WorkerDrainLauncher.Executable, info.FileName);
        Assert.Equal(root.Path, info.WorkingDirectory);
        Assert.Equal(data, info.Environment[WorkerDrainLauncher.DataRootVariable]);
        Assert.False(info.UseShellExecute);
        Assert.True(info.CreateNoWindow);

        // The copy holds every file of the build, byte for byte.
        foreach (var file in Directory.EnumerateFiles(build, "*", SearchOption.AllDirectories))
        {
            Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(copy, Path.GetRelativePath(build, file))));
        }

        // The copy nothing started from for longer than a copy is kept is gone, and the one
        // inside it stays, since a drain may still be running from it.
        Assert.False(Directory.Exists(old));
        Assert.True(Directory.Exists(recent));

        // A second press over the same build starts from the same copy, and a build made again
        // is copied again.
        Assert.True(launcher.Start().Started);
        Assert.Equal(assembly, asked[1].ArgumentList[0]);

        File.SetLastWriteTimeUtc(Path.Combine(build, WorkerDrainLauncher.Assembly), (now + TimeSpan.FromHours(4)).UtcDateTime);

        Assert.True(launcher.Start().Started);

        var again = Path.GetDirectoryName(asked[2].ArgumentList[0])!;

        Assert.NotEqual(copy, again);
        Assert.Equal(
            new[] { copy, again, recent }.Order(StringComparer.Ordinal),
            Directory.GetDirectories(copies).Order(StringComparer.Ordinal));

        // A build holding no worker starts nothing and says why.
        File.Delete(Path.Combine(build, WorkerDrainLauncher.Assembly));

        var none = launcher.Start();

        Assert.False(none.Started);
        Assert.Contains("no worker is built", none.Line, StringComparison.Ordinal);
        Assert.Equal(3, asked.Count);
    }

    [Fact]
    public void TheWorkersBuildIsFoundBesideTheSurfacesOwnAndNeverBesideTheSuites()
    {
        var checkout = Repository.Root;
        var surface = Path.Combine(checkout, "src", "EquityBrief.Api", "bin", "Debug", "net10.0");

        Assert.Equal(
            Path.Combine(checkout, "src", "EquityBrief.Worker", "bin", "Debug", "net10.0"),
            WorkerDrainLauncher.WorkerBuildBeside(checkout, surface));

        // The suite hosts the surface from its own build, so a press under test finds no worker
        // beside it, and neither does a surface outside any checkout or at its project's root.
        Assert.Null(WorkerDrainLauncher.WorkerBuildBeside(checkout, AppContext.BaseDirectory));
        Assert.Null(WorkerDrainLauncher.WorkerBuildBeside(checkout, Path.Combine(checkout, "src", "EquityBrief.Api")));
        Assert.Null(WorkerDrainLauncher.WorkerBuildBeside(null, surface));

        // And the shipped launcher handed no build starts nothing.
        var asked = 0;
        var refused = new WorkerDrainLauncher(checkout, null, Path.Combine(checkout, "data-ci"), FixedClock.At(UtcAt("2026-09-23T19:01:00Z"), SessionZones.UnitedStates), _ =>
        {
            asked++;

            return true;
        }).Start();

        Assert.False(refused.Started);
        Assert.Equal(0, asked);
    }

    // The windows the shipped prices state, 01:00 to 04:00 and 06:00 to 10:00 UTC on weekdays,
    // worked by hand at both edges of each, on a Friday, and on a Saturday and a Sunday, which
    // name no window.
    [Theory]
    [InlineData("2026-09-21T00:59:59Z", null)]
    [InlineData("2026-09-21T01:00:00Z", "2026-09-21T04:00:00Z")]
    [InlineData("2026-09-21T03:59:59Z", "2026-09-21T04:00:00Z")]
    [InlineData("2026-09-21T04:00:00Z", null)]
    [InlineData("2026-09-21T05:59:59Z", null)]
    [InlineData("2026-09-21T06:00:00Z", "2026-09-21T10:00:00Z")]
    [InlineData("2026-09-21T09:59:59Z", "2026-09-21T10:00:00Z")]
    [InlineData("2026-09-21T10:00:00Z", null)]
    [InlineData("2026-09-25T06:30:00Z", "2026-09-25T10:00:00Z")]
    [InlineData("2026-09-26T02:00:00Z", null)]
    [InlineData("2026-09-20T07:00:00Z", null)]
    public async Task ADrainStartedInsideAPeakWindowWaitsForItsEndAndOneStartedOutsideBeginsAtOnce(string started, string? waitsUntil)
    {
        using var store = RequestsFor("KEYS");

        var clock = new WaitedClock(UtcAt(started));
        var waits = new List<DateTimeOffset>();
        var passes = new List<DateTimeOffset>();

        await RequestDrain.DrainAsync(
            store.DatabaseFile,
            clock,
            _ =>
            {
                passes.Add(clock.UtcNow);

                return Task.CompletedTask;
            },
            Providers.ResearchModelFeedTests.Shipped().Pricing,
            until =>
            {
                waits.Add(until);
                clock.UtcNow = until;

                return Task.CompletedTask;
            });

        var begins = waitsUntil is null ? UtcAt(started) : UtcAt(waitsUntil);

        Assert.Equal(waitsUntil is null ? [] : [begins], waits);
        Assert.Equal([begins], passes);
    }

    [Fact]
    public async Task ADrainWithNothingOutstandingEndsWithoutWaitingEvenInsideAPeakWindow()
    {
        using var store = RequestsFor();

        // Inside the first window on a Monday, with nothing asked for.
        var clock = new WaitedClock(UtcAt("2026-09-21T02:00:00Z"));
        var waits = new List<DateTimeOffset>();

        var (taken, written) = await RequestDrain.DrainAsync(
            store.DatabaseFile,
            clock,
            _ => throw new InvalidOperationException("nothing is outstanding, so no pass runs"),
            Providers.ResearchModelFeedTests.Shipped().Pricing,
            until =>
            {
                waits.Add(until);
                clock.UtcNow = until;

                return Task.CompletedTask;
            });

        Assert.Equal((0, 0), (taken, written));
        Assert.Empty(waits);
    }

    [Fact]
    public async Task ADrainThatReachesAPeakWindowBetweenPassesWaitsThereBeforeTheNext()
    {
        using var store = RequestsFor("KEYS", "AAPL");

        // Started off-peak at 00:40 on a Monday, and the first pass takes half an hour, so it
        // ends at 01:10, inside the first window.
        var clock = new WaitedClock(UtcAt("2026-09-21T00:40:00Z"));
        var waits = new List<DateTimeOffset>();
        var passes = new List<(string Ticker, DateTimeOffset At)>();

        await RequestDrain.DrainAsync(
            store.DatabaseFile,
            clock,
            verb =>
            {
                passes.Add((verb[2], clock.UtcNow));
                clock.UtcNow += TimeSpan.FromMinutes(30);

                return Task.CompletedTask;
            },
            Providers.ResearchModelFeedTests.Shipped().Pricing,
            until =>
            {
                waits.Add(until);
                clock.UtcNow = until;

                return Task.CompletedTask;
            });

        Assert.Equal([("KEYS", UtcAt("2026-09-21T00:40:00Z")), ("AAPL", UtcAt("2026-09-21T04:00:00Z"))], passes);
        Assert.Equal([UtcAt("2026-09-21T04:00:00Z")], waits);

        // The second request waited as outstanding and was claimed at the window's end, which
        // is the instant its settle is bounded by.
        Assert.Empty(Rows(store, "SELECT ticker FROM research_request WHERE state IN ('outstanding', 'writing');"));
    }

    [Fact]
    public async Task ASecondDrainStartedWhileOneRunsClaimsNoRequestTheFirstHolds()
    {
        using var store = RequestsFor("KEYS", "AAPL");

        // A Sunday, which names no peak window, so neither drain waits.
        var clock = FixedClock.At(UtcAt("2026-09-20T12:00:05Z"), SessionZones.UnitedStates);
        var pricing = Providers.ResearchModelFeedTests.Shipped().Pricing;
        var first = new List<string>();
        var second = new List<string>();
        var held = new List<string>();

        static Task NoWait(DateTimeOffset _) => throw new InvalidOperationException("nothing here is at peak, so nothing waits");

        var (taken, _) = await RequestDrain.DrainAsync(
            store.DatabaseFile,
            clock,
            async verb =>
            {
                first.Add(verb[2]);

                // While the first drain holds its request, a second is started and runs to its end.
                if (first.Count == 1)
                {
                    held.AddRange(Rows(store, "SELECT ticker FROM research_request WHERE state = 'writing';").Select(row => row[0]));

                    await RequestDrain.DrainAsync(
                        store.DatabaseFile,
                        clock,
                        other =>
                        {
                            second.Add(other[2]);

                            return Task.CompletedTask;
                        },
                        pricing,
                        NoWait);
                }
            },
            pricing,
            NoWait);

        Assert.Equal(["KEYS"], held);
        Assert.Equal(["KEYS"], first);
        Assert.Equal(["AAPL"], second);
        Assert.Equal(1, taken);

        // Each request settled once, by the drain that claimed it, and nothing is left waiting.
        Assert.Empty(Rows(store, "SELECT ticker FROM research_request WHERE state IN ('outstanding', 'writing');"));
    }
}
