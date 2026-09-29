using EquityBrief.Core.Configuration;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;
using EquityBrief.Worker.Nights;

namespace EquityBrief.Tests.Checks;

// nightly-run: a night that stops before its close tries again from the step that stopped, three more times
// fifteen minutes apart, each try under its own id and deadline and keeping what earlier tries stored; a run of
// the rest of a night starts from the first step its tries have not finished; and one night runs at a time under
// the lock.
// see: A night that stops before its close is tried again from the step that stopped, three more times fifteen minutes apart, each try under a deadline of its own
// see: A night left unfinished is run to its end from the step it stopped at by a press or a command, and one night runs at a time under a lock file
public partial class NightlyRun
{
    const string Tried = "night-tried";

    // The fixture's news feed with its first calls failing, as a provider that did not answer.
    sealed class FailingNews(INewsFeed inner, int failures) : INewsFeed
    {
        int left = failures;

        public int Requests => inner.Requests;

        public Task<IReadOnlyList<NewsArticle>> ArticlesAsync(DateOnly from, DateOnly to, CancellationToken cancellation = default) =>
            left-- > 0 ? throw new HttpRequestException("the provider did not answer") : inner.ArticlesAsync(from, to, cancellation);
    }

    // A night over the fixture whose news fails the times given, trying again under the standard plan with the
    // waits recorded rather than slept.
    static async Task<(int Code, string Output, string Error, IReadOnlyList<TimeSpan> Waits)> TriedNightAsync(
        TemporaryStore store,
        int failures,
        int tryNumber = 1,
        bool resume = false,
        TimeSpan? deadline = null)
    {
        var waits = new List<TimeSpan>();
        var output = new StringWriter();
        var error = new StringWriter();
        var feeds = NightFeeds.FromFixture(FixtureFolder());

        var code = await Nightly.RunAsync(
            new StoreLocation(Path.GetDirectoryName(store.DatabaseFile)!),
            feeds with { News = new FailingNews(feeds.News, failures) },
            NightQueue.FromFixture(FixtureFolder()),
            "GSPC",
            FixedClock.At(Night, SessionZones.UnitedStates),
            output,
            error,
            Tried,
            deadline,
            tries: resume ? Nightly.TryPlan.Once : Nightly.TryPlan.Standard with { Delay = wait => { waits.Add(wait); return Task.CompletedTask; } },
            tryNumber: tryNumber,
            resume: resume);

        return (code, output.ToString(), error.ToString(), waits);
    }

    static IReadOnlyList<string[]> Stages(TemporaryStore store, string runId) =>
        StoreRows(store, $"SELECT stage, outcome, detail FROM run_log WHERE run_id = '{runId}' ORDER BY rowid;");

    [Fact]
    public async Task ANightThatStopsTriesAgainFromTheStepThatStoppedAndKeepsWhatTheEarlierTryStored()
    {
        using var store = new TemporaryStore();

        // The news fails once: try 1 stops at the news, says try 2 starts from it fifteen minutes on, waits, and
        // try 2 runs from the news to the end under the first try's id with the mark and its number.
        var (code, output, error, waits) = await TriedNightAsync(store, failures: 1);

        Assert.True(code == 0, error + output);
        Assert.Equal([TimeSpan.FromMinutes(15)], waits);

        var first = Stages(store, Tried);

        Assert.Equal(["news-pulse", "failed", "step 'news-pulse' failed: the provider did not answer"], first[^2]);
        Assert.Equal([NightClose.TryAgainStage, NightClose.Waiting, "try 2 of 4 starts from step 'news-pulse' at 2026-09-08T21:25:00Z"], first[^1]);
        Assert.Contains(first, row => row[0] == "fetch" && row[1] == "ok");

        // Try 2 begins at the news and writes no step before it, since the fetch and the list try 1 stored stand.
        var second = Stages(store, NightClose.TryId(Tried, 2));

        Assert.Equal("news-pulse", second[0][0]);
        Assert.DoesNotContain(second, row => row[0] is "membership" or "fetch" or "levels" or "listings");
        Assert.Contains(second, row => row[0] == NightClose.Stage && row[1] == "ok");
        Assert.DoesNotContain(second, row => row[0] == NightClose.TryAgainStage);

        var rest = await NightResume.NewestAsync(store.DatabaseFile, FixedClock.At(Night, SessionZones.UnitedStates));

        Assert.Equal((Tried, 3, true), (rest!.FirstTry, rest.NextTry, rest.Finished));
    }

    [Fact]
    public async Task ANightStopsTryingAfterThreeMoreTriesEachStoppedWithItsReasonAndItsRestRunsFromThatStep()
    {
        using var store = new TemporaryStore();

        // The news never answers: four tries, three waits, each try stopped at the news, and a try again said
        // after each but the last.
        var (code, _, _, waits) = await TriedNightAsync(store, failures: 100);

        Assert.Equal(1, code);
        Assert.Equal(3, waits.Count);

        foreach (var number in new[] { 1, 2, 3, 4 })
        {
            var rows = Stages(store, NightClose.TryId(Tried, number));

            Assert.Contains(rows, row => row[0] == "news-pulse" && row[1] == NightClose.Failed);
            Assert.Equal(number < 4, rows.Any(row => row[0] == NightClose.TryAgainStage));
        }

        Assert.Empty(Stages(store, NightClose.TryId(Tried, 5)));

        // The rest of it, once the news answers: try 5, from the news after the migration, which writes no row of
        // its own, and no step between.
        var rest = await NightResume.NewestAsync(store.DatabaseFile, FixedClock.At(Night, SessionZones.UnitedStates));

        Assert.Equal((Tried, 5, false), (rest!.FirstTry, rest.NextTry, rest.Finished));
        Assert.Contains("listings", rest.Done);
        Assert.DoesNotContain("news-pulse", rest.Done);

        var (resumed, output, error, none) = await TriedNightAsync(store, failures: 0, tryNumber: rest.NextTry, resume: true);

        Assert.True(resumed == 0, error + output);
        Assert.Empty(none);

        var fifth = Stages(store, NightClose.TryId(Tried, 5));

        Assert.Equal("news-pulse", fifth[0][0]);

        // Asked again once it finished, it runs nothing and writes no try.
        var (after, said, _, _) = await TriedNightAsync(store, failures: 0, tryNumber: 6, resume: true);

        Assert.Equal(0, after);
        Assert.Contains($"every step of {Tried} has finished, so there is nothing to run", said, StringComparison.Ordinal);
        Assert.Empty(Stages(store, NightClose.TryId(Tried, 6)));
        Assert.DoesNotContain(fifth, row => row[0] is "fetch" or "levels" or "listings" or "facts");
        Assert.True((await NightResume.NewestAsync(store.DatabaseFile, FixedClock.At(Night, SessionZones.UnitedStates)))!.Finished);
    }

    [Fact]
    public async Task ATryPastItsDeadlineIsTriedAgainUnderADeadlineOfItsOwn()
    {
        using var store = new TemporaryStore();

        // A deadline no step can meet: each try passes it at its first cancellable step and is tried again, so
        // four tries each stop on their own deadline rather than on one spent before them.
        var (code, _, _, waits) = await TriedNightAsync(store, failures: 0, deadline: TimeSpan.FromTicks(1));

        Assert.Equal(1, code);
        Assert.Equal(3, waits.Count);

        foreach (var number in new[] { 1, 2, 3, 4 })
        {
            Assert.Contains(Stages(store, NightClose.TryId(Tried, number)), row => row[1] == NightClose.Stopped && row[2].Contains("passed the night's deadline", StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task ASecondNightIsRefusedWhileOneHoldsTheLockAndWritesNothing()
    {
        using var store = new TemporaryStore();
        var root = Path.GetDirectoryName(store.DatabaseFile)!;

        using (NightLock.Take(root, "night-holding"))
        {
            Assert.Equal("night-holding", NightLock.Holder(root));
            Assert.Null(NightLock.Take(root, "night-second"));

            var (code, _, error) = await NightAsync(store, runId: "night-refused");

            Assert.Equal(1, code);
            Assert.Contains("night-holding holds the night's lock", error, StringComparison.Ordinal);
            var logged = File.Exists(store.DatabaseFile) && StoreRows(store, "SELECT name FROM sqlite_master WHERE name = 'run_log';").Count > 0
                ? StoreRows(store, "SELECT run_id FROM run_log;").Count
                : 0;

            Assert.Equal(0, logged);
        }

        Assert.Null(NightLock.Holder(root));

        var (clean, _, cleanError) = await NightAsync(store, runId: "night-after");

        Assert.True(clean == 0, cleanError);
        Assert.Null(NightLock.Holder(root));
    }
}
