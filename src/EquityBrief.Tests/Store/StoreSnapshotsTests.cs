using EquityBrief.Data.Migrations;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Store;

// The suite's kept stores: a chain over fixed inputs built once a run, and each test handed a copy of its own.
public class StoreSnapshotsTests
{
    static int built;

    // A folder and a file the chain writes beside its store, as the facts assembler writes its files.
    static readonly string Beside = string.Concat("kept", "-beside");
    static readonly string Probe = string.Concat("probe", ".txt");

    static Task<TemporaryStore> CountedBuilt()
    {
        Interlocked.Increment(ref built);

        var store = new TemporaryStore().Migrated();

        store.Execute("CREATE TABLE probe (said TEXT NOT NULL); INSERT INTO probe VALUES ('kept');");
        Directory.CreateDirectory(Path.Combine(store.Root, Beside));
        File.WriteAllText(Path.Combine(store.Root, Beside, Probe), "kept");

        return Task.FromResult(store);
    }

    static IReadOnlyList<string> Said(TemporaryStore store)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT said FROM probe ORDER BY rowid;";

        using var reader = command.ExecuteReader();
        var said = new List<string>();

        while (reader.Read())
        {
            said.Add(reader.GetString(0));
        }

        return said;
    }

    [Fact]
    public async Task AKeptStoreIsBuiltOnceAndEachCallerWritesIntoACopyOfItsOwn()
    {
        using var first = await StoreSnapshots.CopyOf(CountedBuilt);
        using var second = await StoreSnapshots.CopyOf(CountedBuilt);

        Assert.Equal(1, built);
        Assert.NotEqual(first.Root, second.Root);

        first.Execute("INSERT INTO probe VALUES ('first');");

        Assert.Equal(["kept", "first"], Said(first));
        Assert.Equal(["kept"], Said(second));

        // A copy taken after another's write holds none of it, and every file the chain wrote beside the store.
        using var third = await StoreSnapshots.CopyOf(CountedBuilt);

        Assert.Equal(["kept"], Said(third));
        Assert.Equal("kept", File.ReadAllText(Path.Combine(third.Root, Beside, Probe)));
        Assert.Equal(1, built);
    }

    [Fact]
    public async Task AStoreBuiltByALambdaOrOverAValueIsRefused()
    {
        var value = "a value";

        await Assert.ThrowsAsync<ArgumentException>(() => StoreSnapshots.CopyOf(() => Task.FromResult(new TemporaryStore())));
        await Assert.ThrowsAsync<ArgumentException>(() => StoreSnapshots.CopyOf(() => Task.FromResult(value.Length > 0 ? new TemporaryStore() : new TemporaryStore())));
    }

    [Fact]
    public void ANewStoresMigratedCopyIsTheStoreTheMigrationsBuild()
    {
        using var copied = new TemporaryStore().Migrated();
        using var applied = new TemporaryStore();

        MigrationRunner.Standard().Apply(applied.DatabaseFile);

        Assert.Equal(Schema(applied), Schema(copied));

        using var connection = copied.Open();

        Assert.Equal(SchemaMigrations.LatestVersion, MigrationRunner.AppliedVersion(connection));
    }

    static IReadOnlyList<string> Schema(TemporaryStore store)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");

        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = "SELECT type || ' ' || name || ' ' || COALESCE(sql, '') FROM sqlite_master ORDER BY type, name;";

        using var reader = command.ExecuteReader();
        var rows = new List<string>();

        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        // 303 tables, indexes and triggers when it was set.
        Assert.True(rows.Count >= 300, $"Read {rows.Count} schema rows, expected at least 300.");

        return rows;
    }
}
