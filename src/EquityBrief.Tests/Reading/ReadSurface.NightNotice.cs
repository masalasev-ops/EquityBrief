using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the night's state on tonight's page and the Run page: one view handed to both, for every state a
// night can be in, each try that stopped listed with its reason, and the press running the rest of a night left
// unfinished, refused without the page's header, while a night holds the lock, and where the night is not left
// unfinished.
// see: A night's state is read off its own run log rows and its tries, and the pages that state it read that one state
// see: A night left unfinished is run to its end from the step it stopped at by a press or a command, and one night runs at a time under a lock file
public partial class ReadSurface
{
    // The claims the 12.3 correction building the night's tries adds, which this check reaches and the phase's
    // pair names. Declared before the reach that takes them in.
    internal static readonly string[] NightNoticeClaims =
    [
        CheckReach.Key("15.7 Tonight", "The night's state, a notice at the top naming the night's state as the Run page's headline names it"),
        CheckReach.Key("15.7 Tonight", "The night's state, each try so far with the step it stopped at and its reason while the night waits to try again or is left unfinished"),
        CheckReach.Key("15.7 Tonight", "The night's state, a press running the rest of a night left unfinished"),
        CheckReach.Key("15.7 Tonight", "The night's state, a one-line note where the night finished"),
        CheckReach.Key("15.10 Run", "How last night went, each try so far with the step it stopped at and its reason while the night waits to try again or is left unfinished"),
        CheckReach.Key("15.10 Run", "How last night went, a press running the rest of a night left unfinished"),
    ];

    static readonly RunStageRow[] StoppedAtTheLevels =
    [
        LogRow(NightRun, "fetch", "2026-09-10T23:30:10Z", "2026-09-10T23:30:20Z"),
        LogRow(NightRun, "levels", "2026-09-10T23:31:00Z", "2026-09-10T23:45:00Z", "stopped", detail: "step 'levels' passed the night's deadline of 15 minute(s) and was stopped."),
    ];

    static readonly RunStageRow[] WaitingOnTryTwo =
    [
        .. StoppedAtTheLevels,
        LogRow(NightRun, RunScreen.TryAgainStage, "2026-09-10T23:45:00Z", "2026-09-10T23:45:00Z", RunScreen.Waiting, detail: "try 2 of 4 starts from step 'levels' at 2026-09-11T00:00:00Z"),
    ];

    // The Run page's store with its run log replaced by the rows given.
    static TemporaryStore Logged(IEnumerable<RunStageRow> rows)
    {
        var store = RunTopStore();

        store.Execute("DELETE FROM run_log;");

        foreach (var row in rows)
        {
            store.Execute(
                "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) " +
                $"VALUES ('{row.RunId}', '{row.Stage}', '{Stamped(row.StartedAt)}', '{Stamped(row.EndedAt)}', '{row.Outcome}', 0, 0, {row.NetworkRequests}, '{row.Spend}', '{row.Detail.Replace("'", "''", StringComparison.Ordinal)}');");
        }

        return store;

        static string Stamped(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    static async Task<(string Run, string Tonight)> BothPagesAsync(TemporaryStore store, string at)
    {
        using var host = new PassHost(store.Root) { Clock = FixedClock.At(DateTimeOffset.Parse(at, CultureInfo.InvariantCulture), SessionZones.UnitedStates) };
        using var client = host.CreateClient();

        return (
            WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-09-10")),
            WebUtility.HtmlDecode(await client.GetStringAsync("/screens/tonight/2026-09-10")));
    }

    [Fact]
    public async Task TheRunPagesHeadlineAndTonightsNoticeNameOneStateForEveryStateANightCanBeIn()
    {
        RunStageRow[] partway = [LogRow(NightRun, "fetch", "2026-09-10T23:30:10Z", "2026-09-10T23:30:20Z"), LogRow(NightRun, "levels", "2026-09-10T23:31:00Z", "2026-09-10T23:35:00Z")];
        RunStageRow[] none = [.. FinishedNight.Where(row => !row.RunId.StartsWith("night-", StringComparison.Ordinal))];

        // Each state over its own run log at an instant worked by hand for it.
        foreach (var (state, rows, at) in new (string, RunStageRow[], string)[]
        {
            (NightStates.Finished, FinishedNight, "2026-09-11T01:00:00Z"),
            (NightStates.Running, partway, "2026-09-10T23:40:00Z"),
            (NightStates.Waiting, WaitingOnTryTwo, "2026-09-10T23:50:00Z"),
            (NightStates.Unfinished, StoppedAtTheLevels, "2026-09-11T01:00:00Z"),
            (NightStates.NotYet, none, "2026-09-10T22:00:00Z"),
            (NightStates.NeverRan, none, "2026-09-11T01:00:00Z"),
        })
        {
            using var store = Logged(rows);

            var (run, tonight) = await BothPagesAsync(store, at);

            var headline = Regex.Match(run, "class=\"night-status\" data-state=\"([^\"]+)\"").Groups[1].Value;
            var notice = Regex.Match(tonight, "class=\"night-notice\" data-state=\"([^\"]+)\"").Groups[1].Value;

            // From 19.2 a night not yet run draws no notice on tonight's page, its date line saying when it starts.
            Assert.Equal((state, state == NightStates.NotYet ? string.Empty : state), (headline, notice));
        }
    }

    // 19.2: the night's usual start, read off the newest seven nights' own run ids, the minute the most of them started at,
    // the newest among a tie, a later try's, a queued request's and another run's ids read as no night.
    // see: The date line names tonight's night by the start the most of the newest seven nights share
    [Fact]
    public void TheNightsUsualStartIsTheMinuteTheMostOfTheNewestSevenNightsStartedAt()
    {
        string Night(string day, string time) => $"night-{day}T{time}Z";

        // Six nights at 23:00 UTC, seconds apart, and the newest run again by hand at 01:30: 23:00.
        string[] nights =
        [
            Night("20261006", "230004"), Night("20261007", "230011"), Night("20261008", "230002"), Night("20261009", "230003"),
            Night("20261010", "230007"), Night("20261012", "230016"), Night("20261013", "013029"),
            "night-20261012T230016Z-try-3", "night-20261009T230003Z-queue-ZD", "read-api-20261013T020000.1234567Z", "night-2026101",
        ];

        Assert.Equal(new TimeOnly(23, 0), NightTime.Usual(nights));
        Assert.Null(NightTime.StartOf("night-20261012T230016Z-try-3"));
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 1, 30, 29, TimeSpan.Zero), NightTime.StartOf(Night("20261013", "013029")));

        // The newest seven alone are read: an eighth, older, at 01:30 breaks no tie, and of three and three a tie goes
        // to the newer minute.
        Assert.Equal(new TimeOnly(23, 0), NightTime.Usual([.. nights, Night("20261001", "013000")]));
        Assert.Equal(
            new TimeOnly(1, 30),
            NightTime.Usual([Night("20261001", "230000"), Night("20261002", "230000"), Night("20261003", "230000"), Night("20261004", "013000"), Night("20261005", "013000"), Night("20261006", "013000")]));
        Assert.Null(NightTime.Usual([]));

        // Tonight's start for a session: 23:00 UTC is 19:00 in New York in daylight time and 18:00 after the change of
        // 2026-11-01, on the session's day; a minute before the close, 15:00 UTC, falls on the day after.
        var york = SessionZones.ResolveSessionZone(SessionZones.UnitedStates);

        Assert.Equal(new DateTimeOffset(2026, 10, 12, 23, 0, 0, TimeSpan.Zero), NightTime.On(new DateOnly(2026, 10, 12), new TimeOnly(23, 0), york));
        Assert.Equal(new DateTimeOffset(2026, 11, 2, 23, 0, 0, TimeSpan.Zero), NightTime.On(new DateOnly(2026, 11, 2), new TimeOnly(23, 0), york));
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 15, 0, 0, TimeSpan.Zero), NightTime.On(new DateOnly(2026, 10, 12), new TimeOnly(15, 0), york));
    }

    // 19.2: a night not yet run draws no notice on tonight's page and its date line says when it starts, in New York's
    // time with UTC beneath the pointer; a run log holding no night says nothing of when; every other state keeps its
    // notice and no such words.
    // see: The date line names tonight's night by the start the most of the newest seven nights share
    [Fact]
    public async Task ANightNotYetRunFoldsIntoTheDateLineWithTheTimeItStarts()
    {
        // Tonight's page over the fixture's listed night, asked for on the next weekday the exchange trades, at an instant
        // given, the run log's nights replaced by five at 23:00 UTC before it where they are kept.
        async Task<string> TonightAt(TimeOnly at, bool nights)
        {
            using var store = await FixtureExpectations.WithListings();
            var listed = DateOnly.ParseExact(NightIn(store), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var next = Enumerable.Range(1, 7).Select(listed.AddDays).First(day => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && day != new DateOnly(2026, 9, 7));

            store.Execute("DELETE FROM run_log WHERE run_id LIKE 'night-%';");

            if (nights)
            {
                foreach (var back in Enumerable.Range(1, 5))
                {
                    var day = listed.AddDays(-back);

                    store.Execute(FormattableString.Invariant($"INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES ('night-{day:yyyyMMdd}T230005Z', 'close', '{day:yyyy-MM-dd}T23:40:00Z', '{day:yyyy-MM-dd}T23:41:00Z', 'ok', 0, 0, 0, '0', '');"));
                }
            }

            var instant = new DateTimeOffset(next.ToDateTime(at), TimeSpan.Zero);

            using var host = new PassHost(store.Root) { Clock = FixedClock.At(instant, SessionZones.UnitedStates) };
            using var client = host.CreateClient();

            return WebUtility.HtmlDecode(await client.GetStringAsync("/screens/tonight"));
        }

        // Nights before it at 23:00 UTC, asked at 22:00 UTC before the next night: no notice, and the date line saying
        // tonight's night starts at 19:00 in New York with 23:00 UTC beneath the pointer.
        var due = await TonightAt(new TimeOnly(22, 0), nights: true);

        Assert.DoesNotContain("class=\"night-notice\"", due, StringComparison.Ordinal);
        Assert.Matches(@"computed after the close · tonight's night runs at <time class=""night-due"" datetime=""\d{4}-\d{2}-\d{2}T23:00Z"" title=""23:00 UTC"">19:00 ET</time>", due);

        // No night in the run log: no notice and no time.
        var unknown = await TonightAt(new TimeOnly(22, 0), nights: false);

        Assert.DoesNotContain("class=\"night-notice\"", unknown, StringComparison.Ordinal);
        Assert.DoesNotContain("night-due", unknown, StringComparison.Ordinal);
        Assert.Contains("computed after the close", unknown, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TonightsNoticeListsEachTryThatStoppedOffersThePressOnANightLeftUnfinishedAndSaysInOneLineWhereItFinished()
    {
        // Waiting on try 2: the notice names the state and when the next try starts, lists try 1 with its reason,
        // and offers no press, since the night will try again by itself.
        using (var waiting = Logged(WaitingOnTryTwo))
        {
            var (run, tonight) = await BothPagesAsync(waiting, "2026-09-10T23:50:00Z");

            foreach (var page in new[] { run, tonight })
            {
                Assert.Contains("<li data-try=\"1\" data-step=\"levels\"><b>Try 1</b> stopped at levels: step 'levels' passed the night's deadline of 15 minute(s) and was stopped.</li>", page, StringComparison.Ordinal);
                Assert.DoesNotContain("class=\"night-control\"", page, StringComparison.Ordinal);
            }

            Assert.Contains("The night of Thu 2026-09-10: Waiting to try again", tonight, StringComparison.Ordinal);
            Assert.Contains("try 2 starts from that step at 00:00 UTC", tonight, StringComparison.Ordinal);
        }

        // Left unfinished: both pages offer the press, and the notice names the step and the reason.
        using (var unfinished = Logged(StoppedAtTheLevels))
        {
            var (run, tonight) = await BothPagesAsync(unfinished, "2026-09-11T01:00:00Z");

            foreach (var page in new[] { run, tonight })
            {
                Assert.Contains($"<form class=\"night-control\" method=\"post\" action=\"{SinglePageApp.NightResumeRoute}\"><button type=\"submit\" class=\"btn\">Run the rest of the night</button></form>", page, StringComparison.Ordinal);
                Assert.Single(Regex.Matches(page, "class=\"night-control\""));
            }

            Assert.Contains("The night of Thu 2026-09-10: Left unfinished at levels", tonight, StringComparison.Ordinal);
        }

        // Finished: one line, no tries listed and no press.
        using (var finished = Logged(FinishedNight))
        {
            var (_, tonight) = await BothPagesAsync(finished, "2026-09-11T01:00:00Z");
            var notice = Assert.Single(Blocks(tonight, "<section class=\"night-notice-box\">.*?</section>"));

            Assert.Matches("^<section class=\"night-notice-box\"><p class=\"night-notice\" data-state=\"finished\" data-tone=\"ok\" data-session=\"2026-09-10\">The night of Thu 2026-09-10 finished; its last step was written at 23:41 UTC.</p></section>$", notice);
        }
    }

    [Fact]
    public async Task ThePressRunsTheRestOfANightLeftUnfinishedAndIsRefusedWithoutThePagesHeaderWhileANightHoldsTheLockAndOtherwise()
    {
        using var store = Logged(StoppedAtTheLevels);

        var launcher = new RecordingLauncher();

        using var host = new PassHost(store.Root) { Clock = FixedClock.At(DateTimeOffset.Parse("2026-09-11T01:00:00Z", CultureInfo.InvariantCulture), SessionZones.UnitedStates), Launcher = launcher };
        using var client = host.CreateClient();

        async Task<(HttpStatusCode Status, string Said)> PressAsync(bool header)
        {
            using var press = new HttpRequestMessage(HttpMethod.Post, SinglePageApp.NightResumeRoute);

            if (header)
            {
                press.Headers.Add(SinglePageApp.PassHeader, SinglePageApp.PassHeaderValue);
            }

            var response = await client.SendAsync(press);

            return (response.StatusCode, Regex.Match(await response.Content.ReadAsStringAsync(), "data-resume=\"([^\"]+)\"").Groups[1].Value);
        }

        Assert.Equal((HttpStatusCode.Forbidden, "refused"), await PressAsync(header: false));

        using (NightLock.Take(store.Root, "night-20260911T000000Z"))
        {
            Assert.Equal("night-20260911T000000Z", NightLock.Holder(store.Root));
            Assert.Equal((HttpStatusCode.Conflict, "held"), await PressAsync(header: true));
        }

        Assert.Null(NightLock.Holder(store.Root));
        Assert.Equal(0, launcher.RestStarted);

        Assert.Equal((HttpStatusCode.Accepted, "started"), await PressAsync(header: true));
        Assert.Equal((1, 0), (launcher.RestStarted, launcher.Started));

        // A night that finished is not run again by the press.
        using var finished = Logged(FinishedNight);
        using var other = new PassHost(finished.Root) { Clock = FixedClock.At(DateTimeOffset.Parse("2026-09-11T01:00:00Z", CultureInfo.InvariantCulture), SessionZones.UnitedStates), Launcher = launcher };
        using var second = other.CreateClient();
        using var again = new HttpRequestMessage(HttpMethod.Post, SinglePageApp.NightResumeRoute);

        again.Headers.Add(SinglePageApp.PassHeader, SinglePageApp.PassHeaderValue);

        var said = await (await second.SendAsync(again)).Content.ReadAsStringAsync();

        Assert.Contains("data-resume=\"not-unfinished\"", said, StringComparison.Ordinal);
        Assert.Equal(1, launcher.RestStarted);
    }
}
