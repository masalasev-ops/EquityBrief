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

    internal TemporaryStore Migrated()
    {
        MigrationRunner.Standard().Apply(DatabaseFile);
        return this;
    }

    internal SqliteConnection Open()
    {
        var connection = new SqliteConnection(MigrationRunner.ConnectionStringFor(DatabaseFile));
        connection.Open();
        return connection;
    }

    internal void Execute(string sql)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
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
        //
        // Under each string the store is opened with, since a pool is kept per string: the migration
        // runner's, the one the components share, and the bare one the read surface and a test open it
        // with. A pool left under any of them holds the file, the delete below is refused, and the
        // directory is left behind in the temporary folder.
        foreach (var opened in new[] { MigrationRunner.ConnectionStringFor(DatabaseFile), StoreConnection.For(DatabaseFile), $"Data Source={DatabaseFile}" })
        {
            using var connection = new SqliteConnection(opened);

            SqliteConnection.ClearPool(connection);
        }

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
