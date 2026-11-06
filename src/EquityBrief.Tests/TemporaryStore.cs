using EquityBrief.Core.Configuration;
using EquityBrief.Data;
using EquityBrief.Data.Migrations;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests;

// A store in a throwaway directory.
//
// Nothing in the suite reaches data/. A check that reads the live store is a
// check whose result depends on last night, which is a different instrument
// from the one this corpus builds.
internal sealed class TemporaryStore : IDisposable
{
    internal TemporaryStore()
    {
        Root = Path.Combine(Path.GetTempPath(), "equitybrief-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Root);
    }

    internal string Root { get; }

    internal string DatabaseFile => Path.Combine(Root, StoreLocation.DatabaseFileName);

    // A store with no file yet starts as a copy of one the migrations built once this run, and the runner then finds
    // nothing pending; one holding a file is migrated from where it stands.
    internal TemporaryStore Migrated()
    {
        if (!File.Exists(DatabaseFile))
        {
            File.Copy(StoreSnapshots.MigratedFile(), DatabaseFile);
        }

        MigrationRunner.Standard().Apply(DatabaseFile);
        return this;
    }

    // This store's file once its pools are released, so a copy of it is the file as last written.
    internal string Released()
    {
        ReleasePools();
        return DatabaseFile;
    }

    internal SqliteConnection Open()
    {
        var connection = new SqliteConnection(MigrationRunner.ConnectionStringFor(DatabaseFile));
        connection.Open();
        return connection;
    }

    // What a worker the suite starts runs under: this store's root as its data root, and a copies' folder under
    // it, since the worker reads the machine's settings and the copies' folder they name keeps the operator's
    // copies, where a night the suite runs would otherwise copy this store.
    // see: A store's copy counts and removes only the copies its own rows name, and a test or a rehearsal names a copies' folder of its own
    internal Dictionary<string, string> WorkerEnvironment() => new(StringComparer.Ordinal)
    {
        ["EquityBrief__DataRoot"] = Root,
        ["EquityBrief__Backup__Folder"] = Path.Combine(Root, StoreCopies.DefaultFolder),
    };

    internal void Execute(string sql)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    // A store of its own holding every file this one holds, read once its pools are released so the copy is the
    // file as last written.
    internal TemporaryStore Copy()
    {
        ReleasePools();

        var copy = new TemporaryStore();

        foreach (var folder in Directory.EnumerateDirectories(Root, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(copy.Root, Path.GetRelativePath(Root, folder)));
        }

        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(copy.Root, Path.GetRelativePath(Root, file)));
        }

        return copy;
    }

    void ReleasePools()
    {
        // Under each string the store is opened with, since a pool is kept per string: the migration
        // runner's, the one the components share, and the bare one the read surface and a test open it
        // with. A pool left under any of them holds the file, the delete below is refused, and the
        // directory is left behind in the temporary folder.
        foreach (var opened in new[] { MigrationRunner.ConnectionStringFor(DatabaseFile), StoreConnection.For(DatabaseFile), $"Data Source={DatabaseFile}" })
        {
            using var connection = new SqliteConnection(opened);

            SqliteConnection.ClearPool(connection);
        }
    }

    public void Dispose()
    {
        // This store's pool, not every pool in the process.
        //
        // ClearAllPools is process-wide and xUnit runs test classes in parallel,
        // so a store disposing here pulled pooled connections out from under
        // tests in other classes that were part-way through reading. Measured at
        // one failure in fifteen runs before the change and always in the test
        // that makes four sequential queries, which is the widest window rather
        // than a different fault.
        //
        // An intermittent red that goes green on a re-run is worse than a
        // reliable one. It is the failure that teaches a person to press the
        // button again instead of reading the result, and on CI it would have
        // arrived as one run in fifteen going red for no reason anyone could
        // reproduce.
        ReleasePools();

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not a reason to fail a run.
        }
    }
}
