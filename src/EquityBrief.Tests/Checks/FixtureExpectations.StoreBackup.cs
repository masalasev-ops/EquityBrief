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
// see: The store is copied once the night and every process it started have finished, and the newest three copies are kept after each is opened and read
// see: A store's copy counts and removes only the copies its own rows name, and a test or a rehearsal names a copies' folder of its own
public partial class FixtureExpectations
{
    // The night's step that starts the copy, as section 14 states it.
    internal const string StoreCopyStep =
        "Start the store's copy as the labeller is started, a process of its own that waits until the night, the drain it started and the " +
        "labeller have finished, holding the drain's lock while it copies the store through SQLite's own backup into the copies' folder, " +
        "opens and reads the copy against the store, keeps the newest three after opening and reading each, and writes one row of its own " +
        "and nothing else; a night run again for an earlier session starts one that waits for no labeller (see: The store is copied once the " +
        "night and every process it started have finished, and the newest three copies are kept after each is opened and read).";

    // The part of the run page's worry row the copy adds.
    internal const string StoreCopyLine =
        "Anything to worry about, the store's newest copy beneath them with the time it was made and its folder and the copies kept and the " +
        "newest attempt that made none with why";

    // The rows the copy adds that this check reaches: section 17's row and section 18's two.
    internal static readonly string[] StoreCopyClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Store copies"),
        CheckReach.Key(Scope.FailureTable, "A store copy that does not open and read"),
        CheckReach.Key(Scope.FailureTable, "The night, its drain or its labeller still holding the store after twenty hours"),
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
    static (StoreBackup Backup, CopyClock Clock) Copying(TemporaryStore store, DateTimeOffset start, string folder, Action<DateTimeOffset>? script = null)
    {
        var clock = new CopyClock(start);

        return (new StoreBackup(clock, store.Root, store.DatabaseFile, folder, TimeSpan.FromMinutes(20), (span, _) =>
        {
            clock.UtcNow += span;
            script?.Invoke(clock.UtcNow);

            return Task.CompletedTask;
        }), clock);
    }

    static JsonElement CopyRow(TemporaryStore store, string runId, out string outcome)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT outcome, detail FROM run_log WHERE run_id = $run AND stage = $stage;";
        command.Parameters.AddWithValue("$run", runId);
        command.Parameters.AddWithValue("$stage", StoreCopies.Stage);

        using var reader = command.ExecuteReader();

        Assert.True(reader.Read(), $"no row for {runId}");
        outcome = reader.GetString(0);

        Assert.False(AbsolutePaths.LooksAbsolute(reader.GetString(1)), reader.GetString(1));

        using var document = JsonDocument.Parse(reader.GetString(1));

        return document.RootElement.Clone();
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

        // One row of its own, the folder relative to the data root, the copy's figures and nothing waited for.
        var detail = CopyRow(store, StoreBackup.RunIdAt(CopyStart), out var outcome);

        Assert.Equal(StoreBackup.Copied, outcome);
        Assert.Equal("copies", detail.GetProperty("folder").GetString());
        Assert.Equal(JsonValueKind.Null, detail.GetProperty("elsewhere").ValueKind);
        Assert.Equal(("2026-10-02", 2L), (detail.GetProperty("newest").GetString()!, detail.GetProperty("bars").GetInt64()));
        Assert.Equal([made.Copy!], Names(detail, "kept"));
        Assert.Empty(Names(detail, "waited"));
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
}
