using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Backup;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the operator's ruling of 2026-10-02: the store's copy after every night. The copy is made
// through SQLite's own backup into the copies' folder, named by its instant, opened and read against the store,
// and its row names the folder relative to the data root; the newest three of the store's own copies are kept,
// each opened and read before an older one is removed and none removed where one does not, a copy another store
// made in the folder neither counted nor removed; and the copy waits for the night, the labeller the night
// started and the drain, holding the drain's lock while it copies, and gives up after twenty hours.
// see: The store is copied once the night and every process it started have finished and the newest three copies are kept after each is opened and read, and the copy writes a row as it starts and one as it ends
// see: A store's copy counts and removes only the copies its own rows name, and a test or a rehearsal names a copies' folder of its own
public partial class FixtureExpectations
{
    // The night's step that starts the copy, as section 14 states it.
    internal const string StoreCopyStep =
        "Start the store's copy as the labeller is started, a process of its own that waits until the night, the drain it started and the " +
        "labeller have finished, holding the drain's lock while it copies the store through SQLite's own backup into the copies' folder, " +
        "opens and reads the copy against the store, keeps the newest three after opening and reading each, and writes a row of its own as " +
        "it starts and one as it ends and nothing else; a night run again for an earlier session starts one that waits for no labeller (see: " +
        "The store is copied once the night and every process it started have finished and the newest three copies are kept after each is " +
        "opened and read, and the copy writes a row as it starts and one as it ends).";

    // The part of the run page's worry row the copy adds.
    internal const string StoreCopyLine =
        "Anything to worry about, the store's newest copy beneath them with the time it was made and its folder and the copies kept and the " +
        "newest attempt that made none with why, with a copy started since that has written no end and whether it was ended before it " +
        "finished and the copies the newest copy found gone from its folder that no copy removed";

    // The checklist's two items on the copies, the 13.10 correction of 2026-10-08.
    internal const string StoreCopiedItem =
        "Anything to worry about, the store copied after the night began or a copy started since waiting or copying, and where one was ended " +
        "before it finished or none was started its start or the night's named";

    internal const string StoreCopiesKeptItem =
        "Anything to worry about, every copy the newest copy's row before it kept still in the folder, and where any is gone each named";

    // The rows the copy adds that this check reaches: section 17's row and section 18's three.
    internal static readonly string[] StoreCopyClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Store copies"),
        CheckReach.Key(Scope.FailureTable, "A store copy that does not open and read"),
        CheckReach.Key(Scope.FailureTable, "The night, its drain or its labeller still holding the store after twenty hours"),
    ];

    // The rows the 13.10 correction of 2026-10-08 adds, which no phase's pair counted: the checklist's two items,
    // and section 18's rows on a copy ended before it finished and on copies gone that no copy removed.
    internal static readonly string[] StoreCopyRowsClaims =
    [
        CheckReach.Key("15.10 Run", StoreCopiedItem),
        CheckReach.Key("15.10 Run", StoreCopiesKeptItem),
        CheckReach.Key(Scope.FailureTable, "A store copy ended before it finished"),
        CheckReach.Key(Scope.FailureTable, "Copies of the store gone from their folder that no copy removed"),
    ];

    // Every row the copy adds, whichever check reaches it, which the phase's pair names apart.
    internal static readonly string[] StoreCopyRows =
    [
        CheckReach.Key(Scope.CatalogueTable, "Store backup"),
        CheckReach.Key(Scope.MatrixTable, "Store backup"),
        CheckReach.Key(NightlyRunSteps.Heading, StoreCopyStep),
        CheckReach.Key("15.10 Run", StoreCopyLine),
        .. StoreCopyClaims,
    ];

    sealed class CopyClock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = start;

        public TimeZoneInfo SessionZone { get; } = SessionZones.ResolveSessionZone(SessionZones.UnitedStates);
    }

    static readonly DateTimeOffset CopyStart = new(2026, 10, 3, 0, 5, 0, TimeSpan.Zero);

    // A store holding two sessions of one name's bars, which a copy is read back against.
    static TemporaryStore CopyStore()
    {
        var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES " +
            "('AAA', '2026-10-01', '10', '11', '9', '10.5', 500, 'bulk', '2026-10-01T21:10:00Z', '10.5'), " +
            "('AAA', '2026-10-02', '11', '12', '10', '11.5', 600, 'bulk', '2026-10-02T21:10:00Z', '11.5');");

        return store;
    }

    // A copy whose wait moves the clock a look at a time and runs what the script holds at each instant reached.
    static (StoreBackup Backup, CopyClock Clock) Copying(TemporaryStore store, DateTimeOffset start, string folder, Action<DateTimeOffset>? script = null, EquityBrief.Core.Providers.ResearchPricing? pricing = null)
    {
        var clock = new CopyClock(start);

        return (new StoreBackup(clock, store.Root, store.DatabaseFile, folder, TimeSpan.FromMinutes(20), (span, _) =>
        {
            clock.UtcNow += span;
            script?.Invoke(clock.UtcNow);

            return Task.CompletedTask;
        }, pricing), clock);
    }

    // A copy's ending row, the one written after its start's.
    static JsonElement CopyRow(TemporaryStore store, string runId, out string outcome) => RowOf(store, runId, ended: true, out outcome);

    // A copy's row as it started, written before it waited for anything.
    static JsonElement StartRow(TemporaryStore store, string runId, out string outcome) => RowOf(store, runId, ended: false, out outcome);

    static JsonElement RowOf(TemporaryStore store, string runId, bool ended, out string outcome)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT outcome, detail FROM run_log WHERE run_id = $run AND stage = $stage;";
        command.Parameters.AddWithValue("$run", runId);
        command.Parameters.AddWithValue("$stage", ended ? StoreCopies.Stage : StoreCopies.StartStage);

        using var reader = command.ExecuteReader();

        Assert.True(reader.Read(), $"no {(ended ? "ending" : "starting")} row for {runId}");
        outcome = reader.GetString(0);

        Assert.False(AbsolutePaths.LooksAbsolute(reader.GetString(1)), reader.GetString(1));

        using var document = JsonDocument.Parse(reader.GetString(1));

        Assert.False(reader.Read(), $"two {(ended ? "ending" : "starting")} rows for {runId}");

        return document.RootElement.Clone();
    }

    // How many rows a copy's run wrote, its start and its end.
    static long RowsOf(TemporaryStore store, string runId)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT COUNT(*) FROM run_log WHERE run_id = $run AND stage IN ($stage, $start);";
        command.Parameters.AddWithValue("$run", runId);
        command.Parameters.AddWithValue("$stage", StoreCopies.Stage);
        command.Parameters.AddWithValue("$start", StoreCopies.StartStage);

        return (long)command.ExecuteScalar()!;
    }

    static string[] Names(JsonElement detail, string field) => [.. detail.GetProperty(field).EnumerateArray().Select(one => one.GetString()!)];

    [Fact]
    public async Task TheStoreIsCopiedOpenedAndReadIntoItsFolderAndItsRowNamesTheFolderRelativeToTheDataRoot()
    {
        using var store = CopyStore();
        var folder = Path.Combine(store.Root, "copies");
        var (backup, _) = Copying(store, CopyStart, folder);

        var made = await backup.RunAsync(afterTheLabeller: false);

        Assert.Equal(StoreBackup.Copied, made.Outcome);
        Assert.Equal("equitybrief-20261003T000500Z.db", made.Copy);
        Assert.Equal(CopyStart, StoreCopies.MadeAt(made.Copy!));
        var check = await StoreBackup.CheckAsync(Path.Combine(folder, made.Copy!));

        Assert.Equal((true, "opened and read", "2026-10-02", 2L), (check.Reads, check.Said, check.Newest!, check.Bars));
        Assert.Empty(Directory.EnumerateFiles(folder, "*" + StoreCopies.Unfinished));

        // Two rows of its own and no more: one as it started, stamped with its start at both ends and saying whether
        // it waits for the labeller, and one as it ended, the folder relative to the data root, the copy's figures,
        // nothing waited for and no copy gone.
        Assert.Equal(2, RowsOf(store, StoreBackup.RunIdAt(CopyStart)));

        var start = StartRow(store, StoreBackup.RunIdAt(CopyStart), out var starting);

        Assert.Equal(StoreBackup.Started, starting);
        Assert.False(start.GetProperty("afterTheLabeller").GetBoolean());
        Assert.Equal(
            ("2026-10-03T00:05:00Z", "2026-10-03T00:05:00Z"),
            (Text(store, $"SELECT started_at FROM run_log WHERE stage = '{StoreCopies.StartStage}';"),
                Text(store, $"SELECT ended_at FROM run_log WHERE stage = '{StoreCopies.StartStage}';")));

        var detail = CopyRow(store, StoreBackup.RunIdAt(CopyStart), out var outcome);

        Assert.Equal(StoreBackup.Copied, outcome);
        Assert.Equal("copies", detail.GetProperty("folder").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("elsewhere").ValueKind);
        Assert.Equal(("2026-10-02", 2L), (detail.GetProperty("newest").GetString()!, detail.GetProperty("bars").GetInt64()));
        Assert.Equal([made.Copy!], Names(detail, "kept"));
        Assert.Empty(Names(detail, "waited"));
        Assert.Empty(Names(detail, "missing"));
        Assert.Equal(folder, StoreCopies.FromStored("copies", store.Root));

        // The folder the setting names, absolute or under the data root, and the data root's own where it names none.
        Assert.Equal(Path.Combine(Path.GetFullPath(store.Root), StoreCopies.DefaultFolder), StoreCopies.Folder(null, store.Root));
        Assert.Equal(Path.Combine(Path.GetFullPath(store.Root), "elsewhere"), StoreCopies.Folder("elsewhere", store.Root));
        Assert.Equal(folder, StoreCopies.Folder(folder, Path.GetTempPath()));
        Assert.Equal("../copies", StoreCopies.AsStored(folder, Path.Combine(store.Root, "data")));
    }

    [Fact]
    public async Task TheNewestThreeAreKeptEachOpenedAndReadBeforeAnOlderOneIsRemoved()
    {
        using var store = CopyStore();
        var folder = Path.Combine(store.Root, "copies");
        var names = new List<string>();

        // Four nights' copies: the fourth removes the first, once the second and the third have opened and read.
        for (var night = 0; night < 4; night++)
        {
            var (backup, _) = Copying(store, CopyStart.AddDays(night), folder);
            var made = await backup.RunAsync(afterTheLabeller: false);

            Assert.Equal(StoreBackup.Copied, made.Outcome);
            names.Add(made.Copy!);
        }

        Assert.Equal([names[3], names[2], names[1]], StoreCopies.In(folder).Select(copy => Path.GetFileName(copy.File)));

        var fourth = CopyRow(store, StoreBackup.RunIdAt(CopyStart.AddDays(3)), out _);

        Assert.Equal([names[0]], Names(fourth, "removed"));
        Assert.Empty(Names(fourth, "unread"));
        Assert.Equal([names[3], names[2], names[1]], Names(fourth, "kept"));

        // A kept copy that no longer opens stops every removal: the fifth night's copy is made and read, the third
        // night's does not open, and all four stay.
        using (var damaged = new FileStream(Path.Combine(folder, names[2]), FileMode.Open, FileAccess.Write))
        {
            damaged.Write("this is no database at all"u8);
        }

        var (fifth, _) = Copying(store, CopyStart.AddDays(4), folder);
        var latest = await fifth.RunAsync(afterTheLabeller: false);

        Assert.Equal(StoreBackup.Copied, latest.Outcome);
        Assert.Equal(4, StoreCopies.In(folder).Count);

        var row = CopyRow(store, StoreBackup.RunIdAt(CopyStart.AddDays(4)), out _);

        Assert.Empty(Names(row, "removed"));
        Assert.StartsWith(names[2] + ", it did not open", Assert.Single(Names(row, "unread")), StringComparison.Ordinal);
    }

    // A copy of this store made at an instant and named by a row of its own, as an earlier copy would have left
    // it: the store's file under the copy's name, which opens and reads, and an ending row naming it as kept.
    static string Standing(TemporaryStore store, string folder, DateTimeOffset at)
    {
        var name = StoreCopies.NameAt(at);
        var started = at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var ended = at.AddMinutes(1).UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        Directory.CreateDirectory(folder);
        File.Copy(store.DatabaseFile, Path.Combine(folder, name));
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) " +
            $"VALUES ('{StoreBackup.RunIdAt(at)}', '{StoreCopies.Stage}', '{started}', '{ended}', 'ok', 0, 0, 0, '0', " +
            $"'{{\"copy\":\"{name}\",\"folder\":\"copies\",\"kept\":[\"{name}\"],\"removed\":[],\"unread\":[],\"missing\":[],\"waited\":[]}}');");

        return name;
    }

    [Fact]
    public async Task RotationLeavesNoFewerCopiesThanTheThreeKeptHoweverManyStoodBeforeIt()
    {
        // With none to five of this store's own copies standing, each opening and reading, one more copy leaves the
        // newest three, or every one where fewer than three stood: never fewer than the three kept, and never fewer
        // than stood plus the one made where that is under three. The ones removed are the oldest beyond the three,
        // each named on the row.
        for (var standing = 0; standing <= 5; standing++)
        {
            using var store = CopyStore();
            var folder = Path.Combine(store.Root, "copies");
            var before = Enumerable.Range(0, standing).Select(day => Standing(store, folder, CopyStart.AddDays(day - standing))).ToArray();

            var (backup, _) = Copying(store, CopyStart, folder);
            var made = await backup.RunAsync(afterTheLabeller: false);

            Assert.Equal(StoreBackup.Copied, made.Outcome);

            var expected = Math.Min(standing + 1, StoreCopies.Kept);
            var left = StoreCopies.In(folder).Select(copy => Path.GetFileName(copy.File)).ToArray();

            Assert.Equal(expected, left.Length);
            Assert.True(left.Length >= Math.Min(standing + 1, StoreCopies.Kept), FormattableString.Invariant($"{standing} standing left {left.Length}"));
            Assert.Equal(made.Copy!, left[0]);

            var row = CopyRow(store, StoreBackup.RunIdAt(CopyStart), out _);

            // The removed are the oldest beyond the three, named newest first as the folder lists them.
            Assert.Equal(left, Names(row, "kept"));
            Assert.Equal(before.Take(Math.Max(0, standing + 1 - StoreCopies.Kept)).Reverse().ToArray(), Names(row, "removed"));
            Assert.Empty(Names(row, "missing"));
        }
    }

    [Fact]
    public async Task ACopyEndedBeforeItFinishedLeavesItsStartWithNoEndAndTheNextCopyNamesTheCopiesGone()
    {
        using var store = CopyStore();
        var folder = Path.Combine(store.Root, "copies");

        // The first night's copy is made, two rows.
        var (first, _) = Copying(store, CopyStart, folder);
        var made = await first.RunAsync(afterTheLabeller: false);

        Assert.Equal(StoreBackup.Copied, made.Outcome);

        // The second night's copy is ended while it waits for the night, as a process ended from outside is: its
        // start is on the run log, stamped with its start at both ends, and no end follows it.
        using (var night = NightLock.Take(store.Root, "night-20261003T233000Z")!)
        {
            var (second, _) = Copying(store, CopyStart.AddDays(1), folder, _ => throw new OperationCanceledException("ended from outside"));

            await Assert.ThrowsAsync<OperationCanceledException>(() => second.RunAsync(afterTheLabeller: true));
        }

        var secondRun = StoreBackup.RunIdAt(CopyStart.AddDays(1));

        Assert.Equal(1, RowsOf(store, secondRun));
        Assert.True(StartRow(store, secondRun, out var starting).GetProperty("afterTheLabeller").GetBoolean());
        Assert.Equal(StoreBackup.Started, starting);
        Assert.Equal(
            ("2026-10-04T00:05:00Z", "2026-10-04T00:05:00Z"),
            (Text(store, $"SELECT started_at FROM run_log WHERE run_id = '{secondRun}';"), Text(store, $"SELECT ended_at FROM run_log WHERE run_id = '{secondRun}';")));
        Assert.Equal([made.Copy!], StoreCopies.In(folder).Select(copy => Path.GetFileName(copy.File)));

        // The first copy is removed by another hand. The third night's copy finds it gone, names it as missing on its
        // row, removed by no copy, and counts it among neither the kept nor the removed; the folder holds the third
        // alone.
        File.Delete(Path.Combine(folder, made.Copy!));

        var (third, _) = Copying(store, CopyStart.AddDays(2), folder);
        var latest = await third.RunAsync(afterTheLabeller: false);
        var row = CopyRow(store, StoreBackup.RunIdAt(CopyStart.AddDays(2)), out _);

        Assert.Equal(StoreBackup.Copied, latest.Outcome);
        Assert.Equal([made.Copy!], Names(row, "missing"));
        Assert.Equal([latest.Copy!], Names(row, "kept"));
        Assert.Empty(Names(row, "removed"));
        Assert.Equal([latest.Copy!], StoreCopies.In(folder).Select(copy => Path.GetFileName(copy.File)));

        // The fourth night's copy reads the third's row, whose one kept copy stands, so none is missing: a copy gone
        // is named once, by the first copy after it to look, and not again.
        var (fourth, _) = Copying(store, CopyStart.AddDays(3), folder);

        Assert.Equal(StoreBackup.Copied, (await fourth.RunAsync(afterTheLabeller: false)).Outcome);
        Assert.Empty(Names(CopyRow(store, StoreBackup.RunIdAt(CopyStart.AddDays(3)), out _), "missing"));
        Assert.Equal(2, StoreCopies.In(folder).Count);
    }

    [Fact]
    public async Task ACopyAnotherStoreMadeInTheFolderIsNeitherCountedNorRemovedAndAnUnfinishedOneWaitsOutACopysWait()
    {
        // The folder already holds three files named as copies that no row of this store names, as a night the
        // suite ran left in the operator's folder: two older than any of this store's and one newer, none of them
        // a database. Read by name, the newer would have been kept unread and stopped every removal, and the two
        // older removed. And two unfinished copies, one named twenty hours before the first copy and one a second
        // further back.
        using var store = CopyStore();
        var folder = Path.Combine(store.Root, "copies");

        Directory.CreateDirectory(folder);

        string[] others = [StoreCopies.NameAt(CopyStart.AddDays(-10)), StoreCopies.NameAt(CopyStart.AddDays(-9)), StoreCopies.NameAt(CopyStart.AddDays(30))];

        foreach (var other in others)
        {
            File.WriteAllText(Path.Combine(folder, other), "another store's copy");
        }

        var waited = Path.Combine(folder, StoreCopies.NameAt(CopyStart - StoreCopies.WaitsAtMost) + StoreCopies.Unfinished);
        var past = Path.Combine(folder, StoreCopies.NameAt(CopyStart - StoreCopies.WaitsAtMost - TimeSpan.FromSeconds(1)) + StoreCopies.Unfinished);

        File.WriteAllText(waited, "being written");
        File.WriteAllText(past, "left behind");

        var names = new List<string>();

        for (var night = 0; night < 4; night++)
        {
            var (backup, _) = Copying(store, CopyStart.AddDays(night), folder);
            var made = await backup.RunAsync(afterTheLabeller: false);

            Assert.Equal(StoreBackup.Copied, made.Outcome);
            names.Add(made.Copy!);

            // The first copy removes the unfinished one past a copy's wait and leaves the one at it.
            if (night == 0)
            {
                Assert.False(File.Exists(past));
                Assert.True(File.Exists(waited));
            }
        }

        // The fourth night's copy keeps this store's newest three and removes its first, and the other store's
        // three stand, in the folder and in no row.
        var fourth = CopyRow(store, StoreBackup.RunIdAt(CopyStart.AddDays(3)), out _);

        Assert.Equal([names[3], names[2], names[1]], Names(fourth, "kept"));
        Assert.Equal([names[0]], Names(fourth, "removed"));
        Assert.Empty(Names(fourth, "unread"));
        Assert.All(others, other => Assert.True(File.Exists(Path.Combine(folder, other)), other));
        Assert.Equal(
            [others[2], names[3], names[2], names[1], others[1], others[0]],
            StoreCopies.In(folder).Select(copy => Path.GetFileName(copy.File)));

        // A day on, the unfinished copy at the wait is past it, and the next copy removes it.
        Assert.False(File.Exists(waited));

        // A copy this store made and refused is its own as much as one it kept: named by a failed row and newer
        // than the rest, it is among the newest three, does not open, and stops every removal.
        var refused = StoreCopies.NameAt(CopyStart.AddDays(10));

        File.WriteAllText(Path.Combine(folder, refused), "a copy that did not read");
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) " +
            $"VALUES ('backup-20261013T000500Z', '{StoreCopies.Stage}', '2026-10-13T00:05:00Z', '2026-10-13T00:06:00Z', 'failed', 0, 0, 0, '0', " +
            $"'{{\"copy\":\"{refused}\",\"reason\":\"the copy did not open and read as the store does\"}}');");

        var (fifth, _) = Copying(store, CopyStart.AddDays(4), folder);
        var latest = await fifth.RunAsync(afterTheLabeller: false);
        var row = CopyRow(store, StoreBackup.RunIdAt(CopyStart.AddDays(4)), out _);

        Assert.Equal(StoreBackup.Copied, latest.Outcome);
        Assert.Empty(Names(row, "removed"));
        Assert.StartsWith(refused + ", it did not open", Assert.Single(Names(row, "unread")), StringComparison.Ordinal);
        Assert.Equal([refused, latest.Copy!, names[3], names[2], names[1]], Names(row, "kept"));
    }

    [Fact]
    public async Task TheCopyWaitsForTheNightTheLabellerAndTheDrainAndGivesUpAfterTwentyHours()
    {
        // The night holds its lock for three looks, and the copy is made once it lets go.
        using (var store = CopyStore())
        {
            var night = NightLock.Take(store.Root, "night-20261002T233000Z")!;
            var looks = 0;
            var (backup, clock) = Copying(store, CopyStart, Path.Combine(store.Root, "copies"), _ =>
            {
                if (++looks == 3)
                {
                    night.Dispose();
                }
            });

            Assert.Equal(StoreBackup.Copied, (await backup.RunAsync(afterTheLabeller: false)).Outcome);
            Assert.Equal(CopyStart + (3 * StoreBackup.Between), clock.UtcNow);
            Assert.Equal(["the night"], Names(CopyRow(store, StoreBackup.RunIdAt(CopyStart), out _), "waited"));
        }

        // Started by the night, it waits for the labeller's last row, which the labeller writes on its fourth look.
        using (var store = CopyStore())
        {
            var looks = 0;
            var (backup, clock) = Copying(store, CopyStart, Path.Combine(store.Root, "copies"), _ =>
            {
                if (++looks == 4)
                {
                    store.Execute(
                        "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) " +
                        "VALUES ('label-news-20261003T000400Z', 'news-labels', '2026-10-03T00:04:00Z', '2026-10-03T00:07:00Z', 'ok', 0, 0, 0, '0', '{}');");
                }
            });

            Assert.Equal(StoreBackup.Copied, (await backup.RunAsync(afterTheLabeller: true)).Outcome);
            Assert.Equal(CopyStart + (4 * StoreBackup.Between), clock.UtcNow);
            Assert.Equal(["the news labeller"], Names(CopyRow(store, StoreBackup.RunIdAt(CopyStart), out _), "waited"));
        }

        // A labeller that writes no last row is waited for until its time limit and ten minutes more have passed.
        using (var store = CopyStore())
        {
            var (backup, clock) = Copying(store, CopyStart, Path.Combine(store.Root, "copies"));

            Assert.Equal(StoreBackup.Copied, (await backup.RunAsync(afterTheLabeller: true)).Outcome);
            Assert.Equal(CopyStart + TimeSpan.FromMinutes(20) + StoreBackup.PastTheLabellersLimit, clock.UtcNow);
        }

        // Started at 01:35 on Wednesday 2026-10-07, as the night of 2026-10-06 started it, inside the news profile's window
        // from 01:00 to 04:00, the labeller waits to 04:00 and labels for its twenty minutes, so the copy waits for it until
        // 04:20 and ten minutes more, where it would have copied at 02:05 beneath a labeller still to label; on Saturday
        // 2026-10-03, which names no window, the copy waits as before.
        var waited = new DateTimeOffset(2026, 10, 7, 1, 35, 0, TimeSpan.Zero);

        using (var store = CopyStore())
        {
            var (backup, clock) = Copying(store, waited, Path.Combine(store.Root, "copies"), pricing: Providers.ResearchModelFeedTests.Pinned().Pricing);

            Assert.Equal(StoreBackup.Copied, (await backup.RunAsync(afterTheLabeller: true)).Outcome);
            Assert.Equal(new DateTimeOffset(2026, 10, 7, 4, 30, 0, TimeSpan.Zero), clock.UtcNow);
        }

        using (var store = CopyStore())
        {
            var (backup, clock) = Copying(store, CopyStart, Path.Combine(store.Root, "copies"), pricing: Providers.ResearchModelFeedTests.Pinned().Pricing);

            Assert.Equal(StoreBackup.Copied, (await backup.RunAsync(afterTheLabeller: true)).Outcome);
            Assert.Equal(CopyStart + TimeSpan.FromMinutes(20) + StoreBackup.PastTheLabellersLimit, clock.UtcNow);
        }

        // The drain holds its lock for two looks, and the copy holds the lock itself while it copies.
        using (var store = CopyStore())
        {
            var drains = Path.Combine(store.Root, WorkerDrainLauncher.CopiesFolder);

            Directory.CreateDirectory(drains);

            var drain = new FileStream(Path.Combine(drains, DrainLock.FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var looks = 0;
            var (backup, clock) = Copying(store, CopyStart, Path.Combine(store.Root, "copies"), _ =>
            {
                if (++looks == 2)
                {
                    drain.Dispose();
                }
            });

            Assert.Equal(StoreBackup.Copied, (await backup.RunAsync(afterTheLabeller: false)).Outcome);
            Assert.Equal(CopyStart + (2 * StoreBackup.Between), clock.UtcNow);
            Assert.Equal(["the drain"], Names(CopyRow(store, StoreBackup.RunIdAt(CopyStart), out _), "waited"));
        }

        // A night that never lets go is waited for twenty hours, and no copy is made.
        using (var store = CopyStore())
        {
            using var night = NightLock.Take(store.Root, "night-20261002T233000Z")!;
            var folder = Path.Combine(store.Root, "copies");
            var (backup, clock) = Copying(store, CopyStart, folder);

            var gave = await backup.RunAsync(afterTheLabeller: false);

            Assert.Equal(StoreBackup.NotCopied, gave.Outcome);
            Assert.Null(gave.Copy);
            Assert.Equal(CopyStart + StoreCopies.WaitsAtMost, clock.UtcNow);
            Assert.Empty(StoreCopies.In(folder));
            Assert.Equal(
                "no copy was made, since the night still held the store after 20 hours",
                CopyRow(store, StoreBackup.RunIdAt(CopyStart), out var outcome).GetProperty("reason").GetString());
            Assert.Equal(StoreBackup.NotCopied, outcome);
        }
    }

    // A clock that steps a second at each read and, at each, asks for the drain's lock as the drain asks for it and
    // lets it go at once, so every instant a copy reads says whether the lock was held at that moment.
    sealed class DrainProbe(DateTimeOffset start, string dataRoot) : IClock
    {
        DateTimeOffset next = start;

        public Dictionary<DateTimeOffset, bool> Held { get; } = [];

        public DateTimeOffset UtcNow
        {
            get
            {
                var at = next;

                next += TimeSpan.FromSeconds(1);
                Held[at] = DrainHeld(dataRoot);

                return at;
            }
        }

        public TimeZoneInfo SessionZone { get; } = SessionZones.ResolveSessionZone(SessionZones.UnitedStates);
    }

    // Whether another holds the drain's lock, its file already made: asked for as the drain asks for it and let go.
    static bool DrainHeld(string dataRoot)
    {
        try
        {
            using var asked = new FileStream(Path.Combine(dataRoot, WorkerDrainLauncher.CopiesFolder, DrainLock.FileName), FileMode.Open, FileAccess.ReadWrite, FileShare.None);

            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    [Fact]
    public async Task TheCopyHoldsTheDrainsLockFromTheMomentItNamesTheCopyUntilItWritesItsRow()
    {
        // Nothing holds the store, so the copy takes the drain's lock at once, the lock's file made as a drain leaves
        // it. The read as the copy starts finds the lock free; the read that names the copy and the read that stamps
        // its row's end, after the copy is written and read back, find it held; and once the copy has finished it is
        // free again.
        using var store = CopyStore();
        var drains = Path.Combine(store.Root, WorkerDrainLauncher.CopiesFolder);

        Directory.CreateDirectory(drains);

        using (new FileStream(Path.Combine(drains, DrainLock.FileName), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
        }

        var clock = new DrainProbe(CopyStart, store.Root);
        var made = await new StoreBackup(clock, store.Root, store.DatabaseFile, Path.Combine(store.Root, "copies"), TimeSpan.FromMinutes(20), (_, _) => Task.CompletedTask)
            .RunAsync(afterTheLabeller: false);

        Assert.Equal(StoreBackup.Copied, made.Outcome);

        var stamped = Text(store, $"SELECT ended_at FROM run_log WHERE stage = '{StoreCopies.Stage}';");
        var ended = DateTimeOffset.ParseExact(stamped, "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

        Assert.False(clock.Held[CopyStart]);
        Assert.True(clock.Held[StoreCopies.MadeAt(made.Copy!)!.Value]);
        Assert.True(clock.Held[ended]);
        Assert.False(DrainHeld(store.Root));
    }
}
