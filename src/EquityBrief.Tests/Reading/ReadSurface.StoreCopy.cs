using System.Net;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Configuration;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the operator's ruling of 2026-10-02: the run page draws the store's newest copy beneath anything to
// worry about, its time, its folder read back against the surface's own data root and the copies kept, a newer
// attempt that made none with why, and that none is recorded where the store holds no copy's row; and from the 13.10
// correction of 2026-10-08 a copy started since that has written no end, waiting or copying within a copy's longest
// wait and an hour and ended before it finished past it, the copies the newest copy found gone from its folder that
// no copy removed, and the checklist's two items on the copies; from the operator's word of 2026-11-20 the first item
// held until the copy's own run after the night is due.
// see: The store is copied by a scheduled run of its own holding the drain's lock for the copy alone, and the oldest beyond two is removed once the new copy is opened and read
public partial class ReadSurface
{
    static StoreBackupRow StartedRow(string runId, string startedAt, bool afterTheLabeller = true) =>
        new(runId, RunScreen.CopyStarted, startedAt, startedAt, $"{{\"afterTheLabeller\":{(afterTheLabeller ? "true" : "false")}}}");

    static StoreBackupRow EndedRow(string runId, string outcome, string startedAt, string endedAt, string detail) => new(runId, outcome, startedAt, endedAt, detail);

    [Fact]
    public void TheCopyLineReadsAStartWithNoEndAsWaitingWithinACopysLongestWaitAndAnHourAndAsEndedBeforeItFinishedPastItAndNamesTheCopiesGone()
    {
        // Rows newest first, as the read API hands them: a copy made on the 17th, kept three, two of which the copy on
        // the 18th found gone, and a copy started on the 18th at 01:00 that has written no end.
        const string made = "{\"copy\":\"equitybrief-20260917T004112Z.db\",\"folder\":\"../copies\",\"elsewhere\":null,\"kept\":[\"equitybrief-20260917T004112Z.db\",\"equitybrief-20260916T004000Z.db\",\"equitybrief-20260915T003900Z.db\"],\"removed\":[],\"unread\":[],\"missing\":[],\"waited\":[]}";
        const string gone = "{\"copy\":\"equitybrief-20260918T004500Z.db\",\"folder\":\"../copies\",\"elsewhere\":null,\"kept\":[\"equitybrief-20260918T004500Z.db\"],\"removed\":[],\"unread\":[],\"missing\":[\"equitybrief-20260916T004000Z.db\",\"equitybrief-20260915T003900Z.db\"],\"waited\":[]}";
        var started = new DateTimeOffset(2026, 9, 19, 1, 0, 0, TimeSpan.Zero);

        StoreBackupRow[] rows =
        [
            StartedRow("backup-20260919T010000Z", "2026-09-19T01:00:00Z"),
            EndedRow("backup-20260918T003000Z", "ok", "2026-09-18T00:30:00Z", "2026-09-18T00:50:00Z", gone),
            StartedRow("backup-20260918T003000Z", "2026-09-18T00:30:00Z"),
            EndedRow("backup-20260917T003000Z", "ok", "2026-09-17T00:30:00Z", "2026-09-17T00:45:00Z", made),
            StartedRow("backup-20260917T003000Z", "2026-09-17T00:30:00Z"),
        ];

        // Read within the wait and the hour, the start is a copy waiting or copying; a second past them, one ended
        // before it finished. Either way the newest copy made is the 18th's, with the two it found gone named.
        var waiting = RunScreen.StoreCopy(rows, Path.GetTempPath(), started + StoreCopies.EndsBy).Newest!;
        var ended = RunScreen.StoreCopy(rows, Path.GetTempPath(), started + StoreCopies.EndsBy + TimeSpan.FromSeconds(1)).Newest!;

        Assert.Equal((started, false, "equitybrief-20260918T004500Z.db", 1), (waiting.StartedAt!.Value, waiting.Gone, waiting.Copy!, waiting.Kept));
        Assert.Equal((started, true), (ended.StartedAt!.Value, ended.Gone));
        Assert.Equal(["equitybrief-20260916T004000Z.db", "equitybrief-20260915T003900Z.db"], waiting.Missing!);

        var marks = new MarkRenderer();

        Assert.Contains("A copy started at 01:00 UTC on 2026-09-19 is waiting or copying, and has not written its end.", marks.StoreCopyLine(waiting), StringComparison.Ordinal);
        Assert.Contains("data-unfinished=\"open\"", marks.StoreCopyLine(waiting), StringComparison.Ordinal);
        Assert.Contains("The copy started at 01:00 UTC on 2026-09-19 wrote no end: it was ended before it finished, and made none.", marks.StoreCopyLine(ended), StringComparison.Ordinal);
        Assert.Contains("data-unfinished=\"gone\" data-missing=\"2\"", marks.StoreCopyLine(ended), StringComparison.Ordinal);
        Assert.Contains("2 copies the copy before kept are gone from the folder, removed by no copy: equitybrief-20260916T004000Z.db, equitybrief-20260915T003900Z.db.", marks.StoreCopyLine(ended), StringComparison.Ordinal);

        // A start whose end was written is no open start: the 18th's start alone, with its end before it, draws none.
        var closed = RunScreen.StoreCopy(rows[1..], Path.GetTempPath(), started).Newest!;

        Assert.Null(closed.StartedAt);
        Assert.Contains("data-unfinished=\"none\"", marks.StoreCopyLine(closed), StringComparison.Ordinal);

        // The checklist's two items over the same rows, the night having begun at 23:30 on the 18th: a copy started
        // since it began waiting holds the first with its words, and past the wait fails it; the copies gone fail the
        // second, each named; a store holding no copy's row reads neither; and a copy made since the night began
        // holds the first while none started since fails it naming the night's start.
        var night = new NightView(new DateOnly(2026, 9, 18), "finished", null, null, new DateTimeOffset(2026, 9, 18, 23, 30, 0, TimeSpan.Zero), null, 600, 120, 4, 3, 0m, 0, null, []);

        IReadOnlyList<WorryItem> Items(StoreCopyRead read) => RunScreen.Worries([], night, [], [], [], null, read);

        var open = Items(new StoreCopyRead(waiting));
        var past = Items(new StoreCopyRead(ended));
        var none = Items(new StoreCopyRead(null));

        Assert.Equal(10, open.Count);
        Assert.Equal(("The store was copied after the last night", WorryItem.Held, "a copy started at 01:00 UTC on 2026-09-19 is waiting or copying"), (open[8].Item, open[8].State, open[8].Why));
        Assert.Equal((WorryItem.Failed, "the copy started at 01:00 UTC on 2026-09-19 was ended before it finished, and made none"), (past[8].State, past[8].Why));
        // From the 13.10 correction of 2026-10-26 a copy gone is a worry only where no copy newer than it stands: the
        // two the 18th's copy found gone are older than it, so the second item holds; an attempt on the 20th that made
        // none and found the 18th's copy gone too leaves the 17th's the newest standing, and fails the item naming the
        // 18th's alone with the line saying so.
        Assert.Equal((RunScreen.CopiesStandWorry, WorryItem.Held, (string?)null), (open[9].Item, open[9].State, open[9].Why));

        const string keptTwo = "{\"copy\":\"equitybrief-20260918T004500Z.db\",\"folder\":\"../copies\",\"elsewhere\":null,\"kept\":[\"equitybrief-20260918T004500Z.db\",\"equitybrief-20260917T004112Z.db\"],\"removed\":[],\"unread\":[],\"missing\":[\"equitybrief-20260916T004000Z.db\",\"equitybrief-20260915T003900Z.db\"],\"waited\":[]}";
        const string notMade = "{\"copy\":\"equitybrief-20260920T004500Z.db\",\"reason\":\"the copy did not open and read as the store does\",\"missing\":[\"equitybrief-20260918T004500Z.db\"],\"waited\":[]}";
        var newestGone = Items(RunScreen.StoreCopy(
            [
                EndedRow("backup-20260920T003000Z", "failed", "2026-09-20T00:30:00Z", "2026-09-20T00:50:00Z", notMade),
                StartedRow("backup-20260920T003000Z", "2026-09-20T00:30:00Z"),
                EndedRow("backup-20260918T003000Z", "ok", "2026-09-18T00:30:00Z", "2026-09-18T00:50:00Z", keptTwo),
                StartedRow("backup-20260918T003000Z", "2026-09-18T00:30:00Z"),
            ],
            Path.GetTempPath(),
            started));

        Assert.Equal(
            (WorryItem.Failed, "1 gone from the folder, removed by no copy: equitybrief-20260918T004500Z.db. The store's last copy is from 17 Sep; no copy since."),
            (newestGone[9].State, newestGone[9].Why));
        Assert.Equal((WorryItem.NotRead, "no copy of the store is recorded yet"), (none[8].State, none[8].Why));
        Assert.Equal((WorryItem.NotRead, "no copy of the store is recorded yet"), (none[9].State, none[9].Why));
        Assert.Equal(8, RunScreen.Worries([], night, [], [], []).Count);

        var copiedSince = RunScreen.StoreCopy([EndedRow("backup-20260919T003000Z", "ok", "2026-09-19T00:30:00Z", "2026-09-19T00:45:00Z", made.Replace("20260917T004112Z", "20260919T004112Z", StringComparison.Ordinal))], Path.GetTempPath(), started);
        var nothingSince = RunScreen.StoreCopy(rows[3..], Path.GetTempPath(), started);

        Assert.Equal((WorryItem.Held, (string?)null), (Items(copiedSince)[8].State, Items(copiedSince)[8].Why));
        Assert.Equal((WorryItem.Held, (string?)null), (Items(copiedSince)[9].State, Items(copiedSince)[9].Why));

        // From the operator's word of 2026-11-20 the copy is its own run's, at 07:00 UTC on the morning after a weekday
        // night: the night of Friday the 18th began at 23:30, so its copy's run is Saturday the 19th at 07:00. Read at
        // 01:00 or a second before 07:00 with no copy started since, the item holds naming the run; read at 07:00 or
        // after, it fails naming the run and the night's start.
        var beforeTheRun = RunScreen.StoreCopy(rows[3..], Path.GetTempPath(), new DateTimeOffset(2026, 9, 19, 6, 59, 59, TimeSpan.Zero));
        var atTheRun = RunScreen.StoreCopy(rows[3..], Path.GetTempPath(), new DateTimeOffset(2026, 9, 19, 7, 0, 0, TimeSpan.Zero));

        Assert.Equal((WorryItem.Held, "the copy's own run after the night starts at 07:00 UTC on 2026-09-19"), (Items(nothingSince)[8].State, Items(nothingSince)[8].Why));
        Assert.Equal((WorryItem.Held, "the copy's own run after the night starts at 07:00 UTC on 2026-09-19"), (Items(beforeTheRun)[8].State, Items(beforeTheRun)[8].Why));
        Assert.Equal(
            (WorryItem.Failed, "no copy has started since its run due at 07:00 UTC on 2026-09-19, after the night began at 23:30 UTC on 2026-09-18"),
            (Items(atTheRun)[8].State, Items(atTheRun)[8].Why));
    }

    [Fact]
    public void TheCopysOwnRunIsAtSevenUtcOnTheMorningsAfterTheWeekdayNights()
    {
        // Worked by hand over the week of Monday 2026-11-16: a weekday night at 23:00 is copied at 07:00 the next morning,
        // Friday's on the Saturday; a night run by hand on Saturday waits for Tuesday, since no run starts on a Sunday or
        // a Monday; an instant at the run's own second is past it, and one a second before is not.
        static DateTimeOffset Utc(int day, int hour, int minute, int second = 0) => new(2026, 11, day, hour, minute, second, TimeSpan.Zero);

        Assert.Equal(Utc(17, 7, 0), StoreCopies.RunAfter(Utc(16, 23, 0)));
        Assert.Equal(Utc(21, 7, 0), StoreCopies.RunAfter(Utc(20, 23, 0)));
        Assert.Equal(Utc(24, 7, 0), StoreCopies.RunAfter(Utc(21, 23, 30)));
        Assert.Equal(Utc(24, 7, 0), StoreCopies.RunAfter(Utc(23, 6, 59)));
        Assert.Equal(Utc(17, 7, 0), StoreCopies.RunAfter(Utc(17, 6, 59, 59)));
        Assert.Equal(Utc(18, 7, 0), StoreCopies.RunAfter(Utc(17, 7, 0)));
    }
    // A copy's row as the worker writes it: its end under the copies' stage, or its start under the start's stage.
    static void CopyRowOf(TemporaryStore store, string runId, string outcome, string endedAt, string detail) =>
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) " +
            $"VALUES ('{runId}', '{(outcome == RunScreen.CopyStarted ? StoreCopies.StartStage : StoreCopies.Stage)}', '{endedAt}', '{endedAt}', '{outcome}', 0, 0, 0, '0', '{detail}');");

    [Fact]
    public async Task TheRunPageDrawsTheStoresNewestCopyWithItsTimeAndFolderBeneathAnythingToWorryAbout()
    {
        using var store = await FixtureExpectations.FamilyStore(new DateTimeOffset(2026, 9, 6, 22, 0, 0, TimeSpan.Zero));
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<string> Line() =>
            Assert.Single(Blocks(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-09-16")), "<p class=\"store-copy\".*?</p>"));

        // No copy's row yet.
        var none = await Line();

        Assert.Contains("data-copies=\"none\"", none, StringComparison.Ordinal);
        Assert.Contains("No copy of the store is recorded yet. Its own run copies the store at 07:00 UTC the morning after a weekday night.", none, StringComparison.Ordinal);

        // A copy made at 00:41:12 on the 17th into a folder beside the data root, three kept, read back against
        // this surface's own data root.
        CopyRowOf(store, "backup-20260917T003000Z", "ok", "2026-09-17T00:45:00Z",
            "{\"copy\":\"equitybrief-20260917T004112Z.db\",\"folder\":\"../copies\",\"elsewhere\":null,\"kept\":[\"equitybrief-20260917T004112Z.db\",\"equitybrief-20260916T004000Z.db\",\"equitybrief-20260915T003900Z.db\"],\"removed\":[],\"unread\":[],\"waited\":[\"the drain\"]}");

        var folder = Path.GetFullPath(Path.Combine(store.Root, "..", "copies"));
        var made = await Line();

        Assert.Contains("data-made=\"2026-09-17T00:41:12Z\" data-kept=\"3\" data-failed=\"false\"", made, StringComparison.Ordinal);
        Assert.Contains($"The newest copy of the store was made at 00:41 UTC on 2026-09-17, in {folder} as equitybrief-20260917T004112Z.db, and opened and read; 3 copies are kept.", made, StringComparison.Ordinal);

        // A newer attempt that made none is drawn beside it with why, and an older one is not.
        CopyRowOf(store, "backup-20260916T003000Z", "failed", "2026-09-16T20:30:00Z", "{\"reason\":\"an older attempt\",\"waited\":[]}");
        CopyRowOf(store, "backup-20260918T003000Z", "failed", "2026-09-18T20:30:00Z", "{\"reason\":\"no copy was made, since the drain still held the queue after 20 hours\",\"waited\":[\"the drain\"]}");

        var failed = await Line();

        Assert.Contains("data-failed=\"true\"", failed, StringComparison.Ordinal);
        Assert.Contains($"in {folder} as equitybrief-20260917T004112Z.db", failed, StringComparison.Ordinal);
        Assert.Contains("The copy tried at 20:30 UTC on 2026-09-18 was not made: no copy was made, since the drain still held the queue after 20 hours.", failed, StringComparison.Ordinal);
        Assert.DoesNotContain("an older attempt", failed, StringComparison.Ordinal);

        // A copy started on the 19th that wrote no end, read on a day long after, is drawn as ended before it
        // finished; and the checklist's two items read the copies' rows off the page: the first held, since the 17th's
        // copy was made after the night of the 16th began, and the second held, since the 17th's row names no copy gone.
        CopyRowOf(store, "backup-20260919T010000Z", RunScreen.CopyStarted, "2026-09-19T01:00:00Z", "{\"afterTheLabeller\":true}");

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-09-16"));

        Assert.Contains("The copy started at 01:00 UTC on 2026-09-19 wrote no end: it was ended before it finished, and made none.", page, StringComparison.Ordinal);
        Assert.Contains("data-unfinished=\"gone\"", page, StringComparison.Ordinal);
        Assert.Contains("<li data-item=\"The store was copied after the last night\" data-state=\"held\">", page, StringComparison.Ordinal);
        Assert.Contains($"<li data-item=\"{RunScreen.CopiesStandWorry}\" data-state=\"held\">", page, StringComparison.Ordinal);
    }
}
